using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

public class AmazonCustomStore : Store
{
    private readonly AmazonPurchasingCustom _amazonPurchasingCustom;

    public const string Name = "AmazonStore";

    private ConnectionState _mConnectionState;

    // Stores each pending ICart keyed by SKU, so we can retrieve it when Amazon responds
    private readonly Dictionary<string, ICart> _pendingCarts = new();

    // Stores the Amazon ReceiptId keyed by SKU, so FinishTransaction can fulfill it
    private readonly Dictionary<string, string> _pendingReceiptIds = new();

    // Minimal IOrderInfo implementation for Amazon — keeps AmazonCustomStore free of platform-specific types.
    // Public (not nested-private) so callers elsewhere (e.g. server-side receipt verification code) can
    // pattern-match on it via `order.Info is AmazonCustomStore.AmazonOrderInfo` to reach UserId/ProductType/
    // PurchaseDateEpochMs — none of which IOrderInfo itself exposes.
    public class AmazonOrderInfo : IOrderInfo
    {
        public IAppleOrderInfo? Apple => null;   // Not Apple
        public IGoogleOrderInfo? Google => null; // Not Google
        public IPaymentProvidersOrderInfo? PaymentProviders => null; // Not a Payment Providers order
        public List<IPurchasedProductInfo> PurchasedProductInfo { get; set; } = new();
        public string Receipt { get; }        // Amazon ReceiptId
        public string TransactionID { get; }  // Amazon ReceiptId (must be unique per purchase)
        public string UserId { get; }         // Amazon user ID — needed for server-side receipt verification
        public string ProductType { get; }    // "CONSUMABLE" / "ENTITLED" / "SUBSCRIPTION"
        public long PurchaseDateEpochMs { get; }

        public AmazonOrderInfo(string receipt, string transactionId, string userId = null, string productType = null, long purchaseDateEpochMs = 0)
        {
            Receipt = receipt;
            TransactionID = transactionId;
            UserId = userId;
            ProductType = productType;
            PurchaseDateEpochMs = purchaseDateEpochMs;
        }
    }

    // ==========================================================
    // STARTUP
    public AmazonCustomStore()
    {
        Debug.Log("[IAP] AmazonCustomStore v5: Constructor called");

        _amazonPurchasingCustom = new AmazonPurchasingCustom();
        _amazonPurchasingCustom.Init();
        Debug.Log("[IAP] AmazonCustomStore v5: Initialized");

        _amazonPurchasingCustom.OnProductsFetched += HandleProductsFetchedFromAmazon;
        _amazonPurchasingCustom.OnProductsFetchedFailed += HandleProductsFetchFailedFromAmazon;

        _amazonPurchasingCustom.OnPurchasesFetched += HandlePurchasesFetched;
        _amazonPurchasingCustom.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;
        _amazonPurchasingCustom.OnPurchaseSucceeded += HandlePurchaseSucceeded;
        _amazonPurchasingCustom.OnPurchaseFailed += HandlePurchaseFailed;

        _mConnectionState = ConnectionState.Disconnected;
    }


    // ==========================================================
    // CONNECTION
    #region Connection
    internal ConnectionState GetStoreConnectionState()
    {
        return _mConnectionState;
    }

    public override void Connect()
    {
        Debug.Log("[IAP] AmazonCustomStore v5: Connecting...");
        _ = ConnectInternalAsync();
    }

    private async Task ConnectInternalAsync()
    {
        _mConnectionState = ConnectionState.Connecting;

        if (_amazonPurchasingCustom == null)
        {
            HandleAmazonStoreConnectionFailed("No purchase service");
            return;
        }
        try
        {
            // Attempt with a 15 second timeout, if it fails retry
            bool connected = await _amazonPurchasingCustom.ConnectAsync(TimeSpan.FromSeconds(15));
            if (!connected)
            {
                Debug.LogWarning("[IAP] AmazonCustomStore v5: First connect attempt timed out, retrying...");
                connected = await _amazonPurchasingCustom.ConnectAsync(TimeSpan.FromSeconds(15));
            }

            if (connected)
            {
                Debug.Log("[IAP] AmazonCustomStore v5: Connection succeeded");
                HandleAmazonStoreConnectionSucceeded();
            }
            else
            {
                Debug.LogWarning("[IAP] AmazonCustomStore v5: Connection probe failed or timed out");
                HandleAmazonStoreConnectionFailed("Connection probe failed or timed out");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonCustomStore v5: Connection exception: {ex.Message}");
            HandleAmazonStoreConnectionFailed($"Connection exception: {ex.Message}");
        }
    }
    #endregion


    #region Connection Callbacks
    void HandleAmazonStoreConnectionSucceeded()
    {
        _mConnectionState = ConnectionState.Connected;
        ConnectCallback?.OnStoreConnectionSucceeded();
    }

    void HandleAmazonStoreConnectionFailed(string message)
    {
        _mConnectionState = ConnectionState.Disconnected;
        var failureDescription = new StoreConnectionFailureDescription(message, false);
        ConnectCallback?.OnStoreConnectionFailed(failureDescription);
    }
    #endregion


    // ==========================================================
    // PRODUCTS
    #region Products
    public override void FetchProducts(IReadOnlyCollection<ProductDefinition> products)
    {
        Debug.Log($"[IAP] AmazonCustomStore v5: Fetching {products.Count} products...");

        if (_amazonPurchasingCustom == null)
        {
            HandleAmazonStoreConnectionFailed("No purchase service");
            return;
        }

        if (products == null || products.Count == 0)
        {
            HandleProductsFetchFailedFromAmazon("No products to fetch");
            return;
        }

        var productsList = products.Select(pd => pd.storeSpecificId).ToList();
        _amazonPurchasingCustom.FetchProducts(productsList);
    }
    #endregion


    #region Products Callbacks
    private void HandleProductsFetchedFromAmazon(Dictionary<string, GenericProductData> productDataMap)
    {
        if (productDataMap == null || productDataMap.Count == 0)
        {
            HandleProductsFetchFailedFromAmazon("No product data returned");
            return;
        }

        var productDescriptions = new List<ProductDescription>(productDataMap.Count);

        foreach (var kv in productDataMap)
        {
            var sku = kv.Key;
            var gpd = kv.Value;

            if (gpd == null)
            {
                Debug.LogWarning($"[IAP] AmazonCustomStore: Skipping null GenericProductData for SKU: {sku}");
                continue;
            }

            // Amazon returns a locale-formatted display string (e.g. "$1.99", "1,99 €");
            // the numeric value is a placeholder — production should source it from a
            // server-side catalog if needed.
            var localizedPriceString = gpd.Price ?? string.Empty;
            const decimal localizedPrice = 0m;

            var metadata = new ProductMetadata(
                localizedPriceString,
                gpd.Title ?? string.Empty,
                gpd.Description ?? string.Empty,
                gpd.IsoCurrencyCode,
                localizedPrice
            );

            var productDescription = new ProductDescription(sku, metadata, null, transactionId: null);
            productDescriptions.Add(productDescription);

            Debug.Log($"[IAP] AmazonCustomStore: HandleProductsFetchedFromAmazon - Added ProductDescription for SKU: {sku}");
        }

        Debug.Log($"[IAP] AmazonCustomStore - HandleProductsFetchedFromAmazon: Fetched {productDescriptions.Count} products. calling OnProductsFetched callback");

        try
        {
            if (ProductsCallback == null)
            {
                Debug.LogWarning("[IAP] AmazonCustomStore: ProductsCallback is null, cannot forward products to Unity IAP.");
                return;
            }

            ProductsCallback?.OnProductsFetched(productDescriptions);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonCustomStore: Exception while forwarding products to ProductsCallback: {ex}");
            ProductsCallback?.OnProductsFetchFailed(new ProductFetchFailureDescription(ProductFetchFailureReason.Unknown, $"Forwarding products failed: {ex.Message}"));
        }
    }

    private void HandleProductsFetchFailedFromAmazon(string message)
    {
        Debug.LogWarning($"[IAP] AmazonCustomStore v5: OnProductsFetch failed: {message}");
        ProductsCallback?.OnProductsFetchFailed(new ProductFetchFailureDescription(ProductFetchFailureReason.Unknown, message));
    }
    #endregion


    // ==========================================================
    // PURCHASING
    #region Purchasing

    public override void FetchPurchases()
    {
        Debug.Log("[IAP] AmazonCustomStore v5: FetchPurchases called.");

        if (_amazonPurchasingCustom == null)
        {
            PurchaseFetchCallback?.OnPurchasesRetrievalFailed(
                new PurchasesFetchFailureDescription(PurchasesFetchFailureReason.Unknown, "No purchase service."));
            return;
        }
        _amazonPurchasingCustom.FetchPurchases(reset: false);
    }

    // For an explicit, user-initiated "Restore Purchases" action only - not part of the Store
    // override surface (Unity IAP 5.x doesn't expose a generic RestoreTransactions extension
    // point the way the old Apple-only IAppleExtensions API did), so IABHandlerMain calls this
    // directly on its amazonCustomStore reference rather than through controller.RestoreTransactions.
    // reset:true here returns the customer's full purchase history per Amazon's own guidance;
    // HandlePurchasesFetched still filters out anything with a CancelDateEpochMs regardless.
    public void RestorePurchases()
    {
        Debug.Log("[IAP] AmazonCustomStore v5: RestorePurchases called.");

        if (_amazonPurchasingCustom == null)
        {
            PurchaseFetchCallback?.OnPurchasesRetrievalFailed(
                new PurchasesFetchFailureDescription(PurchasesFetchFailureReason.Unknown, "No purchase service."));
            return;
        }
        _amazonPurchasingCustom.FetchPurchases(reset: true);
    }

    public override void CheckEntitlement(ProductDefinition product)
    {
        // Not implemented in this tutorial sample. A production build would maintain an
        // entitlement cache populated from HandlePurchasesFetched and answer from there;
        // throwing here makes the gap visible rather than hanging the caller forever.
        throw new NotImplementedException(
            $"[IAP] AmazonCustomStore.CheckEntitlement is not implemented for '{product?.id}'.");
    }

    public override void Purchase(ICart cart)
    {
        var cartItem = cart.Items().FirstOrDefault();
        var sku = cartItem?.Product.definition.storeSpecificId;

        if (string.IsNullOrEmpty(sku))
        {
            // Notify the caller — otherwise apps that disabled UI on purchase start are stuck.
            Debug.LogError("[IAP] AmazonCustomStore: Purchase called with empty SKU in cart.");
            PurchaseCallback?.OnPurchaseFailed(
                new FailedOrder(cart, PurchaseFailureReason.ProductUnavailable, "Cart contained no valid SKU."));
            return;
        }

        Debug.Log($"[IAP] AmazonCustomStore v5: Processing purchase for SKU: {sku}");
        _pendingCarts[sku] = cart;

        _amazonPurchasingCustom.Purchase(sku);
    }

    public override void FinishTransaction(PendingOrder pendingOrder)
    {
        // Called by Unity IAP after ConfirmPurchase — this is where Amazon gets NotifyFulfillment.
        var sku = pendingOrder.CartOrdered?.Items().FirstOrDefault()?.Product.definition.storeSpecificId;
        Debug.Log($"[IAP] AmazonCustomStore: FinishTransaction for SKU: {sku}");

        if (!string.IsNullOrEmpty(sku) && _pendingReceiptIds.TryGetValue(sku, out var receiptId))
        {
            _pendingReceiptIds.Remove(sku);
            _amazonPurchasingCustom.NotifyFulfillment(receiptId);
        }
        else
        {
            Debug.LogWarning($"[IAP] AmazonCustomStore: FinishTransaction - no receiptId found for SKU: {sku}");
        }
    }
    #endregion


    #region Purchasing Callbacks
    private void HandlePurchasesFetched(List<GenericPurchaseResult> results)
    {
        Debug.Log($"[IAP] AmazonCustomStore: HandlePurchasesFetched - {results.Count} receipt(s).");

        var pendingOrders = new List<Order>();

        foreach (var result in results)
        {
            // Amazon's purchase history is permanent - GetPurchaseUpdates keeps returning
            // cancelled/refunded receipts forever, not just current entitlements. Amazon's own
            // receipt already reports cancellation client-side (CancelDateEpochMs), so skip these
            // before they ever become a PendingOrder - otherwise they'd get re-validated against
            // the backend on every single fetch (app launch, restore, etc.) forever, for no benefit.
            if (result.CancelDateEpochMs.HasValue)
            {
                Debug.Log($"[IAP] AmazonCustomStore: FetchPurchases - skipping cancelled receipt for SKU: {result.Sku}.");
                continue;
            }

            // Amazon reports the subscription GROUP sku here (e.g.
            // "srcd_platinum_membership_group"), not the specific term sku that's actually
            // configured as the product's storeSpecificId in Unity IAP's catalog (e.g.
            // "srcd_platinum_membership") - so the group sku alone won't resolve via
            // ReadOnlyProductCache for subscriptions. Fall back to TermSku (Amazon's own
            // receipt field for this) when the group sku doesn't match anything cached.
            var product = ReadOnlyProductCache.Find(result.Sku);
            var catalogSku = result.Sku;

            if ((product == null || product.definition == null) && !string.IsNullOrEmpty(result.TermSku))
            {
                product = ReadOnlyProductCache.Find(result.TermSku);
                catalogSku = result.TermSku;
            }

            if (product == null || product.definition == null)
            {
                Debug.LogWarning($"[IAP] AmazonCustomStore: FetchPurchases - no cached product for SKU: {result.Sku}. Skipping.");
                continue;
            }

            // Keyed by catalogSku (matches product.definition.storeSpecificId) so
            // FinishTransaction's later lookup by that same field succeeds.
            _pendingReceiptIds[catalogSku] = result.ReceiptId;

            var cart = new Cart(new CartItem(product));
            var orderInfo = new AmazonOrderInfo(result.ReceiptId ?? string.Empty, result.ReceiptId ?? string.Empty,
                result.UserId, result.ProductType, result.PurchaseDateEpochMs);
            var order = new PendingOrder(cart, orderInfo);

            pendingOrders.Add(order);
            Debug.Log($"[IAP] AmazonCustomStore: FetchPurchases - PendingOrder queued for SKU: {catalogSku}");
        }

        try
        {
            // Unity IAP will fire OnPurchasePending for each PendingOrder, handled in IAPManager.
            PurchaseFetchCallback?.OnAllPurchasesRetrieved(pendingOrders);
            Debug.Log($"[IAP] AmazonCustomStore: OnAllPurchasesRetrieved called with {pendingOrders.Count} order(s).");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonCustomStore: HandlePurchasesFetched exception: {ex}");
        }
    }

    private void HandlePurchasesFetchFailed(string message)
    {
        Debug.LogWarning($"[IAP] AmazonCustomStore: FetchPurchases failed: {message}");
        PurchaseFetchCallback?.OnPurchasesRetrievalFailed(
            new PurchasesFetchFailureDescription(PurchasesFetchFailureReason.Unknown, message));
    }

    private void HandlePurchaseSucceeded(GenericPurchaseResult result)
    {
        var sku = result.Sku;
        Debug.Log($"[IAP] AmazonCustomStore: HandlePurchaseSucceeded for SKU: {sku}");

        if (!_pendingCarts.TryGetValue(sku, out var cart))
        {
            Debug.LogError($"[IAP] AmazonCustomStore: No pending cart found for SKU: {sku}. Cannot complete purchase.");
            return;
        }

        // ReceiptId stashed so FinishTransaction can fulfill later.
        _pendingReceiptIds[sku] = result.ReceiptId;
        _pendingCarts.Remove(sku);

        try
        {
            // TransactionID must be unique per purchase. Amazon's ReceiptId is unique per
            // purchase; RequestId is per API call (multiple receipts can share one in
            // GetPurchaseUpdates), so it must NOT be used here. Receipt is mirrored from
            // ReceiptId for this tutorial; production code might instead serialize the
            // full Amazon PurchaseReceipt into Receipt for server-side validation.
            var orderInfo = new AmazonOrderInfo(result.ReceiptId ?? string.Empty, result.ReceiptId ?? string.Empty,
                result.UserId, result.ProductType, result.PurchaseDateEpochMs);
            var pendingOrder = new PendingOrder(cart, orderInfo);
            PurchaseCallback?.OnPurchaseSucceeded(pendingOrder);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonCustomStore: Exception in HandlePurchaseSucceeded: {ex}");
        }
    }

    private void HandlePurchaseFailed(string sku, string failureReason)
    {
        Debug.LogWarning($"[IAP] AmazonCustomStore: HandlePurchaseFailed for SKU: {sku}, Reason: {failureReason}");

        if (!_pendingCarts.TryGetValue(sku, out var cart))
        {
            Debug.LogError($"[IAP] AmazonCustomStore: No pending cart found for failed SKU: {sku}");
            return;
        }
        _pendingCarts.Remove(sku);

        var reason = failureReason switch
        {
            "INVALID_SKU"       => PurchaseFailureReason.ProductUnavailable,
            "ALREADY_PURCHASED" => PurchaseFailureReason.DuplicateTransaction,
            "NOT_SUPPORTED"     => PurchaseFailureReason.ProductUnavailable,
            _                   => PurchaseFailureReason.Unknown
        };

        try
        {
            var failedOrder = new FailedOrder(cart, reason, $"Amazon status: {failureReason}");
            PurchaseCallback?.OnPurchaseFailed(failedOrder);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IAP] AmazonCustomStore: Exception in HandlePurchaseFailed: {ex}");
        }
    }
    #endregion


    // ==========================================================
    // SHUTDOWN & CLEANUP
    #region Shutdown & Cleanup
    public void Dispose()
    {
        // Releases Amazon listener bindings and any in-flight ConnectAsync on shutdown.
        _amazonPurchasingCustom?.Dispose();
    }
    #endregion
}
