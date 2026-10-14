using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AmazonIap;
using UnityEngine;

// ==========================================================
// DTOs (DATA TRANSFER OBJECTS)
// Unchanged from the previous CPT-based version - AmazonCustomStore.cs depends on these
// exact shapes and does not need to change.

// SDK-agnostic product info. Price is a locale-formatted display string (e.g. "$1.99",
// "1,99 €"); numeric/currency fields are not carried - source those from a server-side
// catalog if needed.
public class GenericProductData
{
    public string Sku { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Price { get; set; }
    public string SmallIconUrl { get; set; }
    public string ProductType { get; set; }
    // Derived from the connected user's marketplace (Amazon's product data has no currency
    // field of its own - Price is a pre-formatted display string only). Null if no marketplace
    // is known yet or it doesn't map to a valid ISO region.
    public string IsoCurrencyCode { get; set; }
}

// SDK-agnostic purchase result.
public class GenericPurchaseResult
{
    public string Sku { get; set; }
    // For subscriptions, Amazon's PurchaseUpdatesResponse reports the subscription GROUP sku in
    // Sku (e.g. "srcd_platinum_membership_group") rather than the specific term sku that's
    // actually configured as the product's storeSpecificId in Unity IAP's catalog (e.g.
    // "srcd_platinum_membership"). TermSku carries that real term sku - null for non-subscriptions.
    public string TermSku { get; set; }
    // Non-null when Amazon's own receipt already reports this purchase as cancelled (mirrors
    // Receipt.getCancelDate()) - lets HandlePurchasesFetched skip re-validating permanently
    // dead history instead of round-tripping to the backend for it on every fetch.
    public long? CancelDateEpochMs { get; set; }
    public string ReceiptId { get; set; }   // Amazon ReceiptId - needed for fulfillment.
    public string RequestId { get; set; }   // Amazon RequestId (per API call - not unique per purchase).
    public string UserId { get; set; }      // Amazon user ID - needed for server-side receipt verification.
    public string ProductType { get; set; } // "CONSUMABLE" / "ENTITLED" / "SUBSCRIPTION"
    public long PurchaseDateEpochMs { get; set; }
}

// ==========================================================
// This version calls the Amazon Appstore SDK directly via AndroidJavaObject/AndroidJavaClass
// (see AmazonIapBridge.cs) instead of going through Amazon's CPT layer (IAmazonIapV2 /
// AmazonIapV2Impl) and its native libAmazonIapV2Bridge.so, which only ever shipped
// armeabi-v7a. AmazonIapBridge has no native binary at all, so it works on arm64-v8a and
// armeabi-v7a identically.
public class AmazonPurchasingCustom : IPurchaseServiceCustom
{
    private bool _initialized;
    private bool _listenersBound;

    // Completes when GetUserData returns. Resolved from OnUserDataResponse.
    private TaskCompletionSource<bool> _connectTcs;

    // Amazon's PurchaseResponse carries no SKU on failure/cancellation (no receipt is
    // attached), so OnPurchaseResponse falls back to whichever SKU Purchase() most recently
    // submitted in order to correlate the failure back to the right pending cart.
    private string _lastAttemptedSku;

    // Two-letter marketplace country code from the connected user's UserData (e.g. "GB",
    // "US"). Amazon's product data carries no currency of its own, so product fetch derives
    // an ISO currency code from this via RegionInfo.
    private string _marketplace;

    // ==========================================================
    // Events - names and signatures unchanged from the CPT-based version.
    public event Action<string> OnConnected;
    public event Action<string> OnDisconnected;

    public event Action<Dictionary<string, GenericProductData>> OnProductsFetched;
    public event Action<string> OnProductsFetchedFailed;

    public event Action<List<GenericPurchaseResult>> OnPurchasesFetched;
    public event Action<string> OnPurchasesFetchFailed;

    public event Action<string> OnPurchaseDeferred;
    public event Action<GenericPurchaseResult> OnPurchaseSucceeded;
    public event Action<string, string> OnPurchaseFailed;
    public event Action<string, string, long, long> OnPurchaseUpdated;


    // ==========================================================
    // STARTUP
    public void Init()
    {
        if (_initialized) return; // safe to call multiple times

        Debug.Log("[IAP] AmazonPurchasingCustom Init");
        try
        {
            AmazonIapBridge.Initialize();
            _initialized = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[IAP] AmazonPurchasingCustom Init unexpected error: {e.Message}");
            _initialized = false;
        }
    }

    private void BindListeners()
    {
        if (_listenersBound || !_initialized) return;
        _listenersBound = true;

        try
        {
            // Marshal every Amazon SDK callback to the Unity main thread. AndroidJavaProxy
            // callbacks arrive on Android's UI thread, which is not the same thread Unity
            // runs its update loop on, so touching Unity APIs directly here would be unsafe.
            AmazonIapBridge.OnUserDataResponse += HandleUserDataResponse;
            AmazonIapBridge.OnProductDataResponse += HandleProductDataResponse;
            AmazonIapBridge.OnPurchaseResponse += HandlePurchaseResponse;
            AmazonIapBridge.OnPurchaseUpdatesResponse += HandlePurchaseUpdatesResponse;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IAP] BindListeners error: {ex.Message}");
        }
    }

    private void UnbindListeners()
    {
        if (!_listenersBound) return;
        _listenersBound = false;

        try
        {
            AmazonIapBridge.OnUserDataResponse -= HandleUserDataResponse;
            AmazonIapBridge.OnProductDataResponse -= HandleProductDataResponse;
            AmazonIapBridge.OnPurchaseResponse -= HandlePurchaseResponse;
            AmazonIapBridge.OnPurchaseUpdatesResponse -= HandlePurchaseUpdatesResponse;
        }
        catch (Exception)
        {
            // Defensive - static event unsubscribe shouldn't throw, but don't let cleanup fail loudly.
        }
    }

    private void HandleUserDataResponse(AmazonUserDataResult args) =>
        UnityMainThreadDispatcher.instance.Enqueue(() => OnUserDataResponse(args));

    private void HandleProductDataResponse(AmazonProductDataResult args) =>
        UnityMainThreadDispatcher.instance.Enqueue(() => OnProductDataResponse(args));

    private void HandlePurchaseResponse(AmazonPurchaseResult args) =>
        UnityMainThreadDispatcher.instance.Enqueue(() => OnPurchaseResponse(args));

    private void HandlePurchaseUpdatesResponse(AmazonPurchaseUpdatesResult args) =>
        UnityMainThreadDispatcher.instance.Enqueue(() => OnPurchaseUpdatesResponse(args));


    // ==========================================================
    // CONNECTION
    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            Debug.LogError("[IAP] ConnectAsync called before Init or bridge unavailable.");
            return false;
        }

        BindListeners();

        // Fresh TCS per attempt so stale callbacks from a prior Connect cannot resolve this one.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _connectTcs = tcs;

        try
        {
            AmazonIapBridge.GetUserData();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IAP] Connect/GetUserData failed: {ex.Message}");
            OnDisconnected?.Invoke($"Connect/GetUserData failed: {ex.Message}");
            _connectTcs = null;
            return false;
        }

        using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
        {
            // No ConfigureAwait(false): Unity IAP's StoreController callbacks are not
            // thread-safe, so the continuation must stay on the main thread.
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout, cancellationToken));
            _connectTcs = null;

            if (completed != tcs.Task)
            {
                Debug.LogWarning($"[IAP] ConnectAsync timed out after {timeout.TotalSeconds}s waiting for GetUserData response.");
                OnDisconnected?.Invoke("Connect/GetUserData timed out");
                return false;
            }

            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                OnDisconnected?.Invoke("Connect/GetUserData cancelled");
                return false;
            }
        }
    }

    public void Disconnect()
    {
        Debug.Log("[IAP] AmazonPurchasingCustom Disconnect");
        UnbindListeners();
        _initialized = false; // allow Init to run again
    }


    // ==========================================================
    // PRODUCTS
    public void FetchProducts(List<string> productSkus)
    {
        var count = productSkus?.Count ?? 0;
        Debug.Log($"[IAP] AmazonPurchasingCustom: FetchProducts called for {count} product{(count == 1 ? string.Empty : "s")}");

        if (!_initialized)
        {
            Debug.LogError("[IAP] AmazonPurchasingCustom: FetchProducts called before Init or bridge unavailable.");
            OnProductsFetchedFailed?.Invoke("IAP service not initialized.");
            return;
        }

        if (productSkus == null || productSkus.Count == 0)
        {
            OnProductsFetchedFailed?.Invoke("No SKUs provided to FetchProducts.");
            return;
        }

        try
        {
            AmazonIapBridge.GetProductData(productSkus);
            Debug.Log($"[IAP] AmazonPurchasingCustom: GetProductData submitted for {productSkus.Count} SKU(s).");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonPurchasingCustom: FetchProducts error: {ex.Message}");
            OnProductsFetchedFailed?.Invoke(ex.Message);
        }
    }


    // ==========================================================
    // PURCHASING
    // reset:false (default) for routine automatic calls (app launch, already-owned retry) -
    // only what's new/changed since the last reset:false call. reset:true for an explicit,
    // user-initiated "Restore Purchases" action - returns the customer's entire purchase
    // history, including old cancelled/refunded receipts (HandlePurchasesFetched in
    // AmazonCustomStore filters those out via CancelDateEpochMs regardless of which was used).
    public void FetchPurchases(bool reset = false)
    {
        if (!_initialized)
        {
            Debug.LogError("[IAP] AmazonPurchasingCustom: FetchPurchases called before Init or bridge unavailable.");
            OnPurchasesFetchFailed?.Invoke("IAP service not initialized.");
            return;
        }

        try
        {
            Debug.Log($"[IAP] AmazonPurchasingCustom: FetchPurchases - calling GetPurchaseUpdates (reset: {reset})...");
            AmazonIapBridge.GetPurchaseUpdates(reset: reset);
            Debug.Log($"[IAP] AmazonPurchasingCustom: GetPurchaseUpdates submitted.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonPurchasingCustom: FetchPurchases error: {ex.Message}");
            OnPurchasesFetchFailed?.Invoke(ex.Message);
        }
    }

    public void Purchase(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            OnPurchaseFailed?.Invoke(productId, "ProductId is null or empty.");
            return;
        }

        if (!_initialized)
        {
            Debug.LogError("[IAP] Purchase requested before Init.");
            OnPurchaseFailed?.Invoke(productId, "IAP service not initialized.");
            return;
        }

        try
        {
            _lastAttemptedSku = productId;
            AmazonIapBridge.Purchase(productId);
            Debug.Log($"[IAP] Purchase request submitted for {productId}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[IAP] Purchase failed for {productId}. {e.Message}");
            OnPurchaseFailed?.Invoke(productId, e.Message);
        }
    }

    public void NotifyFulfillment(string receiptId)
    {
        if (string.IsNullOrEmpty(receiptId) || !_initialized) return;

        try
        {
            Debug.Log($"[IAP] AmazonPurchasingCustom: Notifying fulfillment for receiptId: {receiptId}");
            AmazonIapBridge.NotifyFulfillment(receiptId, AmazonFulfillmentResult.FULFILLED);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonPurchasingCustom: NotifyFulfillment error: {ex.Message}");
        }
    }


    // ==========================================================
    // AMAZON SDK CALLBACKS
    private void OnUserDataResponse(AmazonUserDataResult args)
    {
        try
        {
            Debug.Log($"[IAP] UserDataResponse: Status: {args?.Status}");
            bool ok = args?.Status == AmazonRequestStatus.SUCCESSFUL;

            if (ok)
                _marketplace = args?.UserData?.Marketplace;

            // Unblock any in-flight ConnectAsync.
            _connectTcs?.TrySetResult(ok);

            if (ok)
                OnConnected?.Invoke("[IAP] UserDataResponse indicates success; OnConnected callback invoked.");
            else
                OnDisconnected?.Invoke($"[IAP] UserDataResponse indicates failure; OnDisconnected callback invoked. {args?.Status}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IAP] OnUserDataResponse error: {ex.Message}");
            _connectTcs?.TrySetResult(false);
            OnDisconnected?.Invoke($"[IAP] UserDataResponse error {ex.Message}");
        }
    }

    // Amazon locks each marketplace to a single currency, so the two-letter country code from
    // UserData maps onto an ISO currency code via RegionInfo. Returns null if no marketplace
    // is known yet or the code isn't a valid ISO 3166-1 region (RegionInfo throws in that case).
    private static string IsoCurrencyCodeForMarketplace(string marketplace)
    {
        if (string.IsNullOrEmpty(marketplace)) return null;

        try
        {
            return new RegionInfo(marketplace).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            Debug.LogWarning($"[IAP] AmazonPurchasingCustom: Unrecognized marketplace '{marketplace}', cannot derive ISO currency code.");
            return null;
        }
    }

    private void OnProductDataResponse(AmazonProductDataResult args)
    {
        try
        {
            string requestId = args?.RequestId;
            Dictionary<string, AmazonProduct> productDataMap = args?.ProductData;
            string isoCurrencyCode = IsoCurrencyCodeForMarketplace(_marketplace);

            var mapped = new Dictionary<string, GenericProductData>();
            if (productDataMap != null)
            {
                foreach (var kvp in productDataMap)
                {
                    var sku = kvp.Key;
                    var pd = kvp.Value;
                    if (pd == null) continue;

                    Debug.Log($"[IAP] AmazonPurchasingCustom : OnProductDataResponse - ProductMapping for KEY: {sku} TITLE: {pd.Title} PRICE: {pd.Price}");

                    mapped[sku] = new GenericProductData
                    {
                        Sku = sku,
                        Title = pd.Title,
                        Description = pd.Description,
                        Price = pd.Price,
                        SmallIconUrl = pd.SmallIconUrl,
                        ProductType = pd.ProductType.ToString(),
                        IsoCurrencyCode = isoCurrencyCode,
                    };
                }
            }

            OnProductsFetched?.Invoke(mapped);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonPurchasingCustom OnProductDataResponse exception: {ex}");
            OnProductsFetchedFailed?.Invoke($"[IAP] AmazonPurchasingCustom: OnProductDataResponse Exception: {ex.Message}");
        }
    }

    private void OnPurchaseResponse(AmazonPurchaseResult args)
    {
        if (args == null)
        {
            Debug.LogError("[IAP] PurchaseResponse is null");
            return;
        }

        AmazonRequestStatus status = args.Status;
        Debug.Log($"[IAP] Purchase Response received. Status: {status}, Sku: {args.Receipt?.Sku}");

        // _pendingCarts in AmazonCustomStore is keyed by whatever SKU Purchase() actually
        // submitted (_lastAttemptedSku). Amazon's own reported receipt SKU isn't reliable for
        // this lookup: on failure/cancellation there's no receipt at all, and for subscriptions
        // the receipt reports the subscription GROUP sku (e.g. "srcd_platinum_membership_group")
        // rather than the specific term sku that was actually submitted and purchased (e.g.
        // "srcd_platinum_membership" - Amazon puts that in a separate termSku field instead). So
        // prefer _lastAttemptedSku always, falling back to the receipt's own sku only if we
        // somehow don't have one tracked.
        var sku = _lastAttemptedSku ?? args.Receipt?.Sku ?? string.Empty;

        if (status == AmazonRequestStatus.SUCCESSFUL)
        {
            var result = new GenericPurchaseResult
            {
                Sku = sku,
                ReceiptId = args.Receipt?.ReceiptId,
                RequestId = args.RequestId,
                UserId = args.UserData?.UserId,
                ProductType = args.Receipt?.ProductType.ToString(),
                PurchaseDateEpochMs = args.Receipt?.PurchaseDateEpochMs ?? 0
            };
            // Fulfillment is deferred to AmazonCustomStore.FinishTransaction, which runs
            // after the player has been rewarded (via Unity IAP's ConfirmPurchase).
            OnPurchaseSucceeded?.Invoke(result);
            return;
        }

        switch (status)
        {
            case AmazonRequestStatus.PENDING:
                // PENDING handles ask-to-buy / deferred child purchases. On this SDK version
                // it's reachable; OnPurchaseDeferred surfaces it so the app waits rather than
                // granting immediately - the eventual outcome arrives via GetPurchaseUpdates.
                Debug.Log($"[IAP] AmazonPurchasingCustom: Purchase PENDING for {sku} - awaiting follow-up response.");
                OnPurchaseDeferred?.Invoke(sku);
                break;

            case AmazonRequestStatus.INVALID_SKU:
            case AmazonRequestStatus.ALREADY_PURCHASED:
            case AmazonRequestStatus.NOT_SUPPORTED:
            case AmazonRequestStatus.FAILED:
            default:
                OnPurchaseFailed?.Invoke(sku, status.ToString());
                break;
        }
    }

    private void OnPurchaseUpdatesResponse(AmazonPurchaseUpdatesResult args)
    {
        Debug.Log($"[IAP] PurchaseUpdatesResponse received. Status: {args?.Status}, " +
                  $"Receipts: {args?.Receipts?.Count ?? 0}, HasMore: {args?.HasMore}");

        if (args == null)
        {
            OnPurchasesFetchFailed?.Invoke("PurchaseUpdatesResponse was null.");
            return;
        }

        var results = new List<GenericPurchaseResult>();

        foreach (var receipt in args.Receipts ?? new List<AmazonReceipt>())
        {
            Debug.Log($"[IAP] PurchaseReceipt Sku: {receipt?.Sku}, " +
                      $"ProductType: {receipt?.ProductType}, " +
                      $"ReceiptId: {receipt?.ReceiptId}");

            if (string.IsNullOrEmpty(receipt?.Sku)) continue;

            OnPurchaseUpdated?.Invoke(
                receipt.Sku,
                receipt.ProductType.ToString(),
                receipt.PurchaseDateEpochMs,
                receipt.CancelDateEpochMs ?? 0L);

            results.Add(new GenericPurchaseResult
            {
                Sku = receipt.Sku,
                TermSku = receipt.TermSku,
                ReceiptId = receipt.ReceiptId,
                RequestId = args.RequestId,
                UserId = args.UserData?.UserId,
                ProductType = receipt.ProductType.ToString(),
                PurchaseDateEpochMs = receipt.PurchaseDateEpochMs,
                CancelDateEpochMs = receipt.CancelDateEpochMs
            });
        }

        OnPurchasesFetched?.Invoke(results);

        // GetPurchaseUpdates is paginated; keep paging with Reset=false until HasMore
        // is false, otherwise receipts on dropped pages go silently missing.
        if (args.HasMore)
        {
            try
            {
                Debug.Log("[IAP] AmazonPurchasingCustom: HasMore=true, paging next batch via GetPurchaseUpdates(Reset=false)...");
                AmazonIapBridge.GetPurchaseUpdates(reset: false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[IAP] AmazonPurchasingCustom: GetPurchaseUpdates (paging) error: {ex.Message}");
                OnPurchasesFetchFailed?.Invoke($"Paging error: {ex.Message}");
            }
        }
    }


    // ==========================================================
    // SHUTDOWN & CLEANUP
    public void Dispose()
    {
        // Unblock any in-flight ConnectAsync so callers don't hang on shutdown.
        _connectTcs?.TrySetResult(false);
        _connectTcs = null;
        Disconnect();
    }
}
