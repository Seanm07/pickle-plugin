using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using SimpleJSON;
using UnityEngine.Networking;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using System.Threading.Tasks;

public class IABHandlerMain : MonoBehaviour, IABHandler {
    private IABManager IAB;

    private StoreController controller; // Unity purchasing system
    private List<ProductDefinition> productDefinitions = new List<ProductDefinition>();

    private IAppleStoreExtendedProductService appleProductService;

    private AmazonCustomStore amazonCustomStore;
    
    private bool debugPendingConsumeAllPurchases;
    
    #region Initialisation
    
    public void Init(IABManager manager) {
        IAB = manager;
    }

    private void InitializeInDebugConsumeMode() {
        debugPendingConsumeAllPurchases = true;

        if (!IAB.isInitializing)
            InitializePurchasing();
    }

    public void InitializePurchasing() {
        // Call initialisation, we don't care about the task results
        _ = InitializeUnityIAP();
    }

    private async Task InitializeUnityIAP() {
        if (!CrossPlatformManager.instance.hasInitialized) {
            Debug.LogError("Failed to initialize billing! (Store initialization has not completed)");
            debugPendingConsumeAllPurchases = false;
            return;
        }

        if (IAB.isInitializing) {
            if (FirebaseManager.instance.debugMode)
                Debug.Log("Failed to initialize billing! (Already initializing)");

            debugPendingConsumeAllPurchases = false;
            return;
        }

        productDefinitions.Clear();
        IAB.isInitializing = true;

        // Populate the product list with the products configured in unity
        AppStoreProvider activeStore = CrossPlatformManager.GetActiveStore();

        for (int i = 0; i < IAB.itemList.Count; i++) {
            IABItem localItem = IAB.itemList[i];
            IABItemStoreInfo activeStoreSKU = localItem.storeSKUs.Find(s => s.store == activeStore);

            if (activeStoreSKU != null) {
                productDefinitions.Add(new ProductDefinition(
                    localItem.productId,
                    activeStoreSKU.sku,
                    debugPendingConsumeAllPurchases ? ProductType.Consumable : localItem.type
                ));
            } else {
                Debug.LogError("No store SKU configured for " + localItem.productId + " on store " + activeStore);
            }
        }

        #if UNITY_ANDROID
            // Register the Amazon custom store before obtaining the controller, so Unity IAP routes to it instead of Google Play when this is the active store
            // Re-running InitializeUnityIAP() (e.g. via ConsumeAllItems) won't re-register the same store
            if (CrossPlatformManager.GetActiveStore() == AppStoreProvider.Amazon && amazonCustomStore == null) {
                amazonCustomStore = new AmazonCustomStore();
                UnityIAPServices.AddNewCustomStore(new AmazonStoreWrapper(AmazonCustomStore.Name, amazonCustomStore));
                UnityIAPServices.SetStoreAsDefault(AmazonCustomStore.Name);
            }
        #endif

        controller = UnityIAPServices.StoreController();

        // Fetching list of available products
        controller.OnProductsFetched += OnProductsFetched;
        controller.OnProductsFetchFailed += OnProductsFetchFailed;

        // Purchase complete/failed events
        controller.OnPurchaseConfirmed += OnPurchaseConfirmed;
        controller.OnPurchaseFailed += OnPurchaseFailed;

        // Purchase deferred/pending events
        controller.OnPurchaseDeferred += OnPurchaseDeferred;
        controller.OnPurchasePending += OnPurchasePending;

        // Fetching list of owned purchases
        controller.OnPurchasesFetched += OnPurchasesFetched;
        controller.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
        
        // Initialisation and disconnect callbacks
        controller.OnStoreConnected += OnStoreConnected;
        controller.OnStoreDisconnected += OnStoreDisconnected;
        
        // Define that we want pending orders to be included in the OnPurchasesFetched callback
        controller.ProcessPendingOrdersOnPurchasesFetched(true);
        
        try {
            await controller.Connect();
        } catch (Exception e) {
            IAB.isInitializing = false;
            IAB.isInitialized = false;
            debugPendingConsumeAllPurchases = false;

            Debug.LogError("IAP connection failed! " + e.Message);
        }
    }
    
    #endregion initialisation

    void OnDestroy() {
        controller.OnProductsFetched -= OnProductsFetched;
        controller.OnProductsFetchFailed -= OnProductsFetchFailed;

        controller.OnPurchaseConfirmed -= OnPurchaseConfirmed;
        controller.OnPurchaseFailed -= OnPurchaseFailed;

        controller.OnPurchaseDeferred -= OnPurchaseDeferred;
        controller.OnPurchasePending -= OnPurchasePending;

        controller.OnPurchasesFetched -= OnPurchasesFetched;
        controller.OnPurchasesFetchFailed -= OnPurchasesFetchFailed;
        
        controller.OnStoreConnected -= OnStoreConnected;
        controller.OnStoreDisconnected -= OnStoreDisconnected;

        // Releases the Amazon SDK listener bindings and any in-flight ConnectAsync.
        amazonCustomStore?.Dispose();
    }
    
    #region Callbacks

    // Called when the store initialisation has completed and billing is now available
    private void OnStoreConnected() {
        // If for some reason initialization was a success but the controller is null just call initialization failed instead and return
        if (controller == null) {
            OnStoreDisconnected(new StoreConnectionFailureDescription("Connected but controller was null!", true));
            return;
        }

        IAB.isInitialized = true;
        IAB.isInitializing = false;
        
        #if !UNITY_EDITOR
            Debug.Log("Successfully initialized billing!");
        #endif
        
        #if UNITY_IOS && !UNITY_EDITOR
            appleProductService = UnityIAPServices.DefaultProduct().Apple;
        
            if (appleProductService == null)
                Debug.LogError("Failed to get Apple product service!");
        #endif
        
        // Fetch products from the store (OnProductsFetched/OnProductsFetchFailed callbacks)
        controller.FetchProducts(productDefinitions);
    }
    
    // Called when the connection to the store has been lost, reinitialise to use billing
    private void OnStoreDisconnected(StoreConnectionFailureDescription error) {
        IAB.isInitialized = false;
        IAB.isInitializing = false;
        IAB.isPurchaseActive = false;
        debugPendingConsumeAllPurchases = false;

        Debug.LogError(error.Message + (error.isRetryable ? " (Retryable)" : ""));
        FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.IAP_ERROR, error.ToString());
    }

    // Success callback when fetching available products from store
    private void OnProductsFetched(List<Product> products) {
        if (FirebaseManager.instance.debugMode)
            Debug.Log("IAB item data is ready!");

        AppStoreProvider activeAppStore = CrossPlatformManager.GetActiveStore();
        Dictionary<string, string> appleProductData = null;

        if (!Application.isEditor && activeAppStore == AppStoreProvider.Apple) {
            // This isn't just the intro price, it grabs all the subscription info
            appleProductData = appleProductService?.GetProductDetails();
        }

        foreach (Product product in products) {
            #if !UNITY_EDITOR
            if (FirebaseManager.instance.debugMode)
                Debug.Log("IAB product fetched: " + product.definition.storeSpecificId);
            #endif

            // Find the item matching the SKU in the itemData list
            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            // If itemLocalData is null then this is either a purchase in a later version of the game than the current app version or the developer forgot to add it
            if (localItem != null) {
                localItem.priceValue = (double)product.metadata.localizedPrice; // e.g 12.99
                localItem.formattedPrice = product.metadata.localizedPriceString; // e.g "$12.99"
                localItem.description = product.metadata.localizedDescription;
                localItem.title = product.metadata.localizedTitle;
                localItem.currencyCode = product.metadata.isoCurrencyCode;

                if (localItem.type == ProductType.Subscription) {
                    // Set the default values to just behave as if there's no free trial or intro price
                    localItem.subscriptionPeriod = "P1W";
                    localItem.freeTrialPeriod = (activeAppStore == AppStoreProvider.Amazon ? "P1W" : "");
                    localItem.introductoryFormattedPrice = "";
                    localItem.introductoryPricePeriod = "";
                    localItem.introductoryPriceCycles = 0;

                    if (!Application.isEditor) {
                        switch (activeAppStore) {
                            case AppStoreProvider.Google:
                                GoogleProductMetadata googleProductData = product.metadata.GetGoogleProductMetadata();
                                if (googleProductData != null) {
                                    localItem.subscriptionPeriod = googleProductData.subscriptionPeriod;

                                    // These values won't be set if the store subscription isn't setup to use them
                                    localItem.freeTrialPeriod = googleProductData.freeTrialPeriod;
                                    localItem.introductoryFormattedPrice = googleProductData.introductoryPrice;

                                    localItem.introductoryPricePeriod = googleProductData.introductoryPricePeriod;
                                    localItem.introductoryPriceCycles = googleProductData.introductoryPriceCycles;
                                } else {
                                    Debug.LogError("Failed to get subscription product data for " + product.definition.storeSpecificId);
                                }

                                break;

                            case AppStoreProvider.Apple:
                                if (appleProductData != null && appleProductData.TryGetValue(product.definition.storeSpecificId, out string introJSON)) {
                                    JSONNode introJSONNode = JSONNode.Parse(introJSON);
                                    // Example introJSON value:
                                    // {"subscriptionNumberOfUnits":"7","subscriptionPeriodUnit":"0","localizedPrice":"3.99","isoCurrencyCode":"GBP","localizedPriceString":"\u00a33.99","localizedTitle":"Special Ops Membership","localizedDescription":"Daily rewards & membership area access!","introductoryPrice":"0","introductoryPriceLocale":"GBP","introductoryPriceNumberOfPeriods":"1","numberOfUnits":"3","unit":"0"}

                                    if (introJSONNode != null) {
                                        // 1 week / 1,2,3,6 months / 1 year
                                        // Apple reports the 1 week subscription as 7 days, so we should do some conversion on basic periods to show proper units
                                        if (introJSONNode["subscriptionNumberOfUnits"].Value == "7" && introJSONNode["subscriptionPeriodUnit"].Value == "0") {
                                            localItem.subscriptionPeriod = "P1W";
                                        } else {
                                            string subPeriodUnit = "";

                                            switch (introJSONNode["subscriptionPeriodUnit"].Value) {
                                                case "0":
                                                    subPeriodUnit = "D";
                                                    break; // day unit
                                                case "1":
                                                    subPeriodUnit = "W";
                                                    break; // week unit
                                                case "2":
                                                    subPeriodUnit = "M";
                                                    break; // Month unit
                                                case "3":
                                                    subPeriodUnit = "Y";
                                                    break; // Year unit
                                            }

                                            localItem.subscriptionPeriod = "P" + introJSONNode["subscriptionNumberOfUnits"].Value + subPeriodUnit;
                                        }

                                        string periodUnit = "";

                                        switch (introJSONNode["unit"].Value) {
                                            case "0":
                                                periodUnit = "D";
                                                break; // day unit
                                            case "1":
                                                periodUnit = "W";
                                                break; // week unit
                                            case "2":
                                                periodUnit = "M";
                                                break; // Month unit
                                            case "3":
                                                periodUnit = "Y";
                                                break; // Year unit
                                        }

                                        // Does the intro price look like a free trial (aka free for the first billing period only)
                                        if (introJSONNode["introductoryPrice"].Value == "0" && introJSONNode["introductoryPriceNumberOfPeriods"].Value == "1") {
                                            // This is a free trial (iOS doesn't allow intro prices AND free trials together, their free trials are just intro prices set to FREE)
                                            localItem.freeTrialPeriod = "P" + introJSONNode["numberOfUnits"].Value + periodUnit;
                                        } else {
                                            localItem.introductoryFormattedPrice = introJSONNode["introductoryPrice"].Value; // The currency symbol is added to this later in code
                                            localItem.introductoryPricePeriod = "P" + introJSONNode["numberOfUnits"].Value + periodUnit;
                                            localItem.introductoryPriceCycles = int.TryParse(introJSONNode["introductoryPriceNumberOfPeriods"].Value, out int introPriceCycles) ? introPriceCycles : 0;
                                        }
                                    }
                                } else {
                                    Debug.LogError("Failed to get subscription product data for " + product.definition.storeSpecificId);
                                }

                                break;
                        }
                    } else {
                        // Set the values to some dummy values for the developer to use for testing
                        localItem.subscriptionPeriod = "P1W";
                        localItem.freeTrialPeriod = "P2D";
                        localItem.introductoryFormattedPrice = "$1.23";
                        localItem.introductoryPricePeriod = "P1W";
                        localItem.introductoryPriceCycles = 2;
                    }

                    // Try convert the intro price using the store iso currency code, if that fails fallback to local device culture
                    if (!float.TryParse(localItem.introductoryFormattedPrice, NumberStyles.Currency, IAB.GetCultureForISOCurrency(localItem.currencyCode), out float introPriceVal))
                        float.TryParse(localItem.introductoryFormattedPrice, NumberStyles.Currency, CultureInfo.CurrentCulture, out introPriceVal);

                    localItem.introductoryPriceValue = introPriceVal;
                }
            }
        }

        IAB.itemDataReady = true;

        IAB.OnInventoryReady();
        
        // Fetch purchases owned by the player
        controller.FetchPurchases();
    }
    
    // Failure callback when fetching available products from store
    private void OnProductsFetchFailed(ProductFetchFailed error) {
        IAB.isInitializing = false;

        Debug.LogError("Failed to fetch products: " + error);
    }

    private void OnPurchasesFetched(Orders orders) {
        List<PendingOrder> debugConsumePendingOrders = new List<PendingOrder>();
        
        foreach (PendingOrder order in orders.PendingOrders) {
            if (debugPendingConsumeAllPurchases) {
                debugConsumePendingOrders.Add(new PendingOrder(order.CartOrdered, order.Info));
            } else {
                // Restored orders (e.g. from the app-launch FetchPurchases call, or the
                // already-owned retry in OnPurchaseFailed) need to go through the same
                // validation pipeline a live purchase would - otherwise nothing ever resolves
                // them and any caller waiting on a purchase result (like a stuck overlay) never
                // gets a success or failure.
                ValidatePurchase(order, false);
            }
        }
        
        foreach (DeferredOrder order in orders.DeferredOrders) {
            if(debugPendingConsumeAllPurchases)
                debugConsumePendingOrders.Add(new PendingOrder(order.CartOrdered, order.Info));
        }

        foreach (ConfirmedOrder order in orders.ConfirmedOrders) {
            // Trigger OnOwnedItemLoad for any owned items when fetching purchases
            foreach (CartItem cartItem in order.CartOrdered.Items()) {
                Product product = cartItem.Product;
                IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);
                
                if(localItem != null && localItem.type != ProductType.Consumable)
                    IAB.OnOwnedItemLoad(localItem, cartItem.Quantity);
            }

            if(debugPendingConsumeAllPurchases)
                debugConsumePendingOrders.Add(new PendingOrder(order.CartOrdered, order.Info));
        }

        if (debugPendingConsumeAllPurchases) {
            Debug.Log("Debug consuming all purchases!");

            foreach (PendingOrder pendingOrder in debugConsumePendingOrders)
                controller.ConfirmPurchase(pendingOrder);

            debugPendingConsumeAllPurchases = false;

            // Notify that consume operation is complete
            IAB.OnConsumeAllComplete();

            Debug.Log("All purchases consumed successfully!");
        }
    }

    private void OnPurchasesFetchFailed(PurchasesFetchFailureDescription error) {
        IAB.isInitializing = false;
        Debug.Log("Failed to fetch purchases: " + error);
    }

    // Called when a purchase will complete later, such as selected to be paid for at an in-person shop or requires parental consent to complete
    private void OnPurchaseDeferred(DeferredOrder order) {
        foreach (CartItem item in order.CartOrdered.Items())
            IAB.OnPurchaseDeferred(IAB.GetItemBySKU(item.Product.definition.storeSpecificId));
    }

    // Payment completed, we need to validate the purchase > give the item > consume/acknowledge purchase
    private void OnPurchasePending(PendingOrder order) {
        ValidatePurchase(order, false);
    }

    // Called after the purchase has been validated, will consume consumable items and just mark non-consumable items as acknowledged
    private void EndPendingPurchase(PendingOrder order) {
        foreach (CartItem item in order.CartOrdered.Items()) {
            Product product = item.Product;
            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            if (localItem != null) {
                // Consumable items are marked NotOwned again once the transaction ends
                // Non-consumables/subscriptions are marked Owned once the transaction ends
                localItem.purchaseState = localItem.type == ProductType.Consumable ? PurchaseState.NotOwned : PurchaseState.Owned;
            } else {
                Debug.LogError("Failed to consume! Invalid localItem for SKU: " + product.definition.storeSpecificId);
            }
        }
        
        // Tell the store that we've acknowledged the purchase (if not acknowledged for 3 days on Google Play then the purchase will be refunded)
        controller.ConfirmPurchase(order);
    }
    
    // Purchase has passed our validation checks and controller.ConfirmPurchase was called
    private void OnPurchaseConfirmed(Order order) {
        // Items are credited to the player upon validation completion before awknowledgement/consumption
        // this callback is currently not used but it could be used as a possible final stage of purchasing if needed
    }

    // Called when a purchase failed
    private void OnPurchaseFailed(FailedOrder order) {
        Product product;
        
        foreach (CartItem cartItem in order.CartOrdered.Items()) {
            product = cartItem.Product;
            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            // Some failure statuses require special actions
            switch (order.FailureReason) {
                case PurchaseFailureReason.ExistingPurchasePending:
                case PurchaseFailureReason.DuplicateTransaction:
                    if (product.definition.type == ProductType.Consumable) {
                        // If this error triggered for a consumable purchase, reload the purchases
                        controller.FetchPurchases();
                    } else if (product.definition.type == ProductType.Subscription) {
                        // If a subscription is already owned (e.g on another device on the same account) reload the purchases
                        controller.FetchPurchases();
                        return;
                    }
                    break;
                
                case PurchaseFailureReason.UserCancelled:
                    // Note: Amazon platform does not specify cancelled as a failure reason (they instead call PurchaseFailureReason.Unknown)
                    
                    if (CrossPlatformManager.GetActiveStore() == AppStoreProvider.Google) {
                        // BUGFIX: Android billing forever has had an issue where minimizing after tapping  buy then tapping the app icon from the drawer to launch again does the cancel action
                        // I'm not sure if it's just an issue both OpenIAB and Unity IAP have but this workaround just re-checks for pending purchases when the cancel event happens
                        controller.FetchPurchases();
                    }
                    break;
            }
            
            if (localItem != null)
                localItem.isProcessingValidation = false;
        }
        
        // If there were no special actions just generate the error message from the first product
        product = order.CartOrdered.Items()[0].Product;

        string errorMessage = "An unknown error occured!\nTry again later. (" + order.Details + ")";

        // The title can be blank in cases where initialisation was successful but the connection was lost before getting the information for this product
        bool isTitleBlank = string.IsNullOrEmpty(product.metadata?.localizedTitle);

        switch (order.FailureReason) {
            case PurchaseFailureReason.PurchasingUnavailable:
                errorMessage = "Purchase failed!\nCheck your connection and make sure parental controls aren't blocking purchases.";
                break;

            case PurchaseFailureReason.ExistingPurchasePending:
                // If the purchase is consumable try consume it (may be a previous purchase which didn't finish)
                if (product.definition.type == ProductType.Consumable) {
                    errorMessage = "Another device on your account is currently processing a transaction of this item, try again soon!";
                } else {
                    errorMessage = "You already own " + (isTitleBlank ? "This item" : product.metadata.localizedTitle) + ".";
                }
                break;

            case PurchaseFailureReason.ProductUnavailable:
                errorMessage = (isTitleBlank ? "This item" : product.metadata.localizedTitle) + " is unavailable for purchase!\nCheck your connection and try again later.";
                break;

            case PurchaseFailureReason.SignatureInvalid:
                // If this error is being called then it's most likely due to the public key not matching the store key
                // Window > Unity IAP > Receipt Validation Obfuscator to set the key to match the store
                // Note: this is Unity IAP's OWN internal failure reason (fired before our code ever runs),
                // distinct from the INVALID_JWS_SIGNATURE our server can return later in DoPurchaseVerificationRequest
                // (Apple overload) - both currently show the same text to the player, so log which one actually fired here to tell them apart.
                Debug.LogError("Unity IAP reported PurchaseFailureReason.SignatureInvalid (this is Unity IAP's own local check, separate from our server-side JWS verification) - order.Details: " + order.Details);
                errorMessage = "Local purchase signature validation failed!\nA 3rd party app may be interfering with purchases or this app has been modified.";
                break;

            case PurchaseFailureReason.UserCancelled:
                // Note: Amazon and windows platforms do not specify cancelled as a failure reason (they instead call PurchaseFailureReason.Unknown)
                errorMessage = "Purchase cancelled, you have not been charged.";
                break;

            case PurchaseFailureReason.PaymentDeclined:
                // Only triggered on apple, other platforms don't end the purchase prompt until payment has been made
                // or completes successfully but then gets rejected later in cases of card fraud etc
                errorMessage = "Payment declined!\nCheck your account payment methods and try again.";
                break;

            case PurchaseFailureReason.DuplicateTransaction:
                if (product.definition.type == ProductType.Consumable) {
                    errorMessage = "Another device on your account is currently processing a transaction of this item, try again soon!";
                } else {
                    errorMessage = "You already own " + (isTitleBlank ? "This item" : product.metadata.localizedTitle) + ".";
                }
                break;

            case PurchaseFailureReason.Unknown:
                // On android this is triggered for card declined etc so just give a generic message
                // The play store error popup before returning to the game should have explained the error cause in detail
                // Note: amazon and windows platforms return this failure reason for cancelling the purchase
                errorMessage = "Purchase failed, you have not been charged.";
                break;
        }

        Debug.LogError("Purchase Failed [Item: " + product.definition.storeSpecificId + "] [Reason: " + order.FailureReason + "]");

        InvokePurchaseFailureResponse(order.FailureReason, errorMessage);
    }
    
    #endregion

    

    #region Info Lookup

    public Product GetProductByProductId(string productId) {
        return controller?.GetProductById(productId);
    }

    // Returns purchased orders which contain productId in the cart
    public Order[] GetOrdersContainingProductId(string productId) {
        List<Order> matchingOrders = new List<Order>();
        ReadOnlyObservableCollection<Order> orders = controller.GetPurchases();
        
        foreach (Order order in orders) {
            foreach (IPurchasedProductInfo productInfo in order.Info.PurchasedProductInfo) {
                if (productId != productInfo.productId) continue;
                
                matchingOrders.Add(order);
                break; // Move to next order
            }
        }

        return matchingOrders.ToArray();
    }

    public string GetRawReceipt(string productId) {
        Order[] orders = GetOrdersContainingProductId(productId);
        
        if (orders.Length > 0) {
            // Just use the latest order if multiple exist
            return orders[^1].Info.Receipt;
        }

        return null;
    }

    public IPurchaseReceipt GetPurchaseReceipt(string productId) {
        Order[] orders = GetOrdersContainingProductId(productId);

        if (orders.Length > 0) {
            // Just use the latest order if multiple exist
            return GetPurchaseReceipt(orders[^1]);
        }

        return null;
    }

    private IPurchaseReceipt GetPurchaseReceipt(Order order) {
        if (LocalReceiptValidation(order, out IPurchaseReceipt[] receipts)) {
            foreach (IPurchaseReceipt receipt in receipts) {
                if (FirebaseManager.instance.debugMode) {
                    Debug.Log("IAB searching receipts - Receipt Transaction ID: " + receipt.transactionID + " / Product Transaction ID: " + order.Info.TransactionID);
                    Debug.Log("IAB additional receipt info: Product ID: " + receipt.productID + ", Purchase Date: " + receipt.purchaseDate);
                }

                foreach (CartItem cartItem in order.CartOrdered.Items()) {
                    Product product = cartItem.Product;
                    
                    if (receipt.productID == product.definition.storeSpecificId && !string.IsNullOrEmpty(receipt.transactionID)) {
                        // Return once we hit the target product which doesn't have a blank transactionID
                        return receipt;
                    }
                }
            }
        }

        // No receipt found
        return null;
    }
    
    #endregion
    
    #region Actions

    private void InvokePurchaseFailureResponse(PurchaseFailureReason failureReason, string errorMessage) {
        IAB.OnPurchaseFailed(errorMessage);
        
        FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.IAP_PURCHASE_FAILED, failureReason.ToString());
        
        Debug.LogError(errorMessage);

        IAB.isPurchaseActive = false;
    }

    public void RestorePurchases() {
        // Unity IAP 5.x's Store extension point has no generic RestoreTransactions override
        // (RestoreTransactions was an Apple-only concept back in the old IAppleExtensions API),
        // so it's unclear whether controller.RestoreTransactions does anything meaningful for a
        // custom store like Amazon's. Call the Amazon-specific restore directly instead - a full
        // reset:true history fetch, with HandlePurchasesFetched still filtering out anything
        // already reported as cancelled.
        if (CrossPlatformManager.GetActiveStore() == AppStoreProvider.Amazon) {
            if (amazonCustomStore != null) {
                amazonCustomStore.RestorePurchases();

                // FetchPurchases has no completion callback of its own - it just kicks off the
                // fetch; OnPurchasesFetched (already routed through ValidatePurchase) is what
                // eventually resolves each restored order. Matches the "started, not necessarily
                // finished" semantics of the controller.RestoreTransactions path below.
                IAB.OnPurchaseRestoreComplete();
            } else {
                IAB.OnPurchaseRestoreFailed("Amazon store not initialized.");
            }

            return;
        }

        controller.RestoreTransactions((success, error) => {
            if (success) {
                // Restore process started, does not mean restoration has fully completed successfully
                // OnPurchasesFetched will be triggered to restore the purchases
                IAB.OnPurchaseRestoreComplete();
            } else {
                // Restore process failed to start
                IAB.OnPurchaseRestoreFailed(error);
            }
        });
    }
    
    public void ConsumeAllItems() {
        if (!IAB.isInitializing && IAB.isInitialized) {
            InitializeInDebugConsumeMode();
        } else {
            Debug.LogError("Could not consume all items, billing isn't ready!");
        }
    }

    public void PurchaseItem(string productId, bool skipInitialisingCheck) {
        IAB.isPurchaseActive = true;
        
        // Store a reference to the last product purchase attempt in case it's needed later in the purchase process
        IAB.lastPurchaseAttemptProductId = productId;

        IABItem localItem = IAB.GetItemByProductId(productId);

        if (localItem == null) {
            InvokePurchaseFailureResponse(PurchaseFailureReason.ProductUnavailable, "Developer error!\nItem " + productId + " was called to be purchased but was not setup in the IABManager item list!");
            return;
        }

        if (controller == null || !IAB.isInitialized) {
            InvokePurchaseFailureResponse(PurchaseFailureReason.PurchasingUnavailable, "Purchasing not initialised!\nCheck your connection and try again later.");
            return;
        }

        if (productDefinitions.Count == 0) {
            InvokePurchaseFailureResponse(PurchaseFailureReason.ProductUnavailable, "Products not available!\nNo products have been setup for purchasing!");
            return;
        }

        Product product = GetProductByProductId(productId);

        if (product == null || !product.availableToPurchase) {
            InvokePurchaseFailureResponse(PurchaseFailureReason.ProductUnavailable, "Item not available!\nItem " + productId + " is not currently available for purchase!");
            return;
        }

        try {
            // Purchase the item
            controller.PurchaseProduct(product);
        } catch (InvalidOperationException e) {
            // Failed because the store disconnected and broke
            InvokePurchaseFailureResponse(PurchaseFailureReason.PurchasingUnavailable, CrossPlatformManager.GetCleanStoreName() + " connection failed!\nCheck your connection and try again later!");
        } catch (Exception e) {
            // Unknown exception
            InvokePurchaseFailureResponse(PurchaseFailureReason.Unknown, "Item " + productId + " failed with error: " + e.Message);
        }
    }
    
    #endregion
    
    #region Validation

    private void CheckForPromotionalPurchase(PendingOrder order) {
        IReadOnlyList<CartItem> cartItems = order.CartOrdered.Items();

        if (cartItems.Count == 0 || string.IsNullOrEmpty(order.Info.Receipt))
            return;

        // The receipt belongs to the whole order and the server side validation request already includes every item in the cart
        // so the receipt only needs decoding once and the verification request only needs sending once
        // https://docs.unity3d.com/Manual/UnityIAPPurchaseReceipts.html
        UnifiedReceipt decodedReceipt = JsonUtility.FromJson<UnifiedReceipt>(order.Info.Receipt);

        if (decodedReceipt == null || string.IsNullOrEmpty(decodedReceipt.Payload)) {
            Debug.LogError("Purchase failed! Failed to decode UnifiedReceipt");
            return;
        }

        JSONNode receiptPayload = JSONNode.Parse(decodedReceipt.Payload);

        if (receiptPayload == null) {
            Debug.LogError("Purchase failed! Failed to parse receipt payload");
            return;
        }

        switch (CrossPlatformManager.GetActiveStore()) {
            case AppStoreProvider.Google:
                // https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.products#ProductPurchase
                JSONNode gPlayPurchaseData = JSONNode.Parse(receiptPayload["json"].Value);

                if (gPlayPurchaseData != null) {
                    // purchaseState can sometimes just not exist when purchased or it can be one of 3 values: 0 = purchased / 1 = cancelled / 2 = pending
                    // Google also has an undocumented purchaseState 4 for deferred purchase, this should just be treated the same as state 2 pending
                    if (gPlayPurchaseData["purchaseState"] == null || int.Parse(gPlayPurchaseData["purchaseState"].Value) == 0) {
                        string purchaseToken = gPlayPurchaseData["purchaseToken"].Value;

                        if (gPlayPurchaseData["purchaseTime"] != null && double.TryParse(gPlayPurchaseData["purchaseTime"].Value, out double purchaseTime)) {
                            DateTime purchaseDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                            purchaseDateTime = purchaseDateTime.AddMilliseconds(purchaseTime);

                            // The purchase data lists the product id the promo code was redeemed for, use the cart item which matches it
                            // if for some reason nothing matches fall back to the first item in the cart (promo codes are redeemed for a single product)
                            Product promoProduct = cartItems[0].Product;

                            foreach (CartItem cartItem in cartItems) {
                                if (cartItem.Product.definition.storeSpecificId == gPlayPurchaseData["productId"].Value) {
                                    promoProduct = cartItem.Product;
                                    break;
                                }
                            }

                            // If we get to this point then this seems to be a promo code purchase
                            GooglePlayReceipt promoReceipt = new GooglePlayReceipt(promoProduct.definition.storeSpecificId, order.Info.TransactionID, Application.identifier, purchaseToken, purchaseDateTime, GooglePurchaseState.Purchased);

                            // Try validate something, if it fails then it'll fall back to purchase validation failed
                            SendPurchaseVerificationRequest(order, promoReceipt);
                        } else {
                            Debug.LogError("Purchase failed! Failed to parse purchaseTime in promo purchase!");
                        }
                    } else {
                        if (gPlayPurchaseData["purchaseState"] == null) {
                            Debug.LogError("Active purchase purchaseState did not exist in the purchase info");
                        } else {
                            Debug.Log("Active purchase purchaseState was " + int.Parse(gPlayPurchaseData["purchaseState"].Value));
                        }
                    }
                } else {
                    Debug.LogError("Purchase failed! Invalid purchaseInfoJson");
                }

                break;
        }
    }
    
    private bool LocalReceiptValidation(Order order, out IPurchaseReceipt[] receipts) {
        switch (CrossPlatformManager.GetActiveStore()) {
            // Standard local validation flow for GooglePlay. Apple (StoreKit2) never reaches this
            // method - see the early Apple branch in ValidatePurchase.
            case AppStoreProvider.Google:
                try {
                    CrossPlatformValidator validator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
                    receipts = validator.Validate(order.Info.Receipt);

                    return true;
                } catch (IAPSecurityException e) {
                    // Purchase validation failed
                    IAB.OnPurchaseFailed("Local purchase receipt validation failed!");
                    FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.IAP_PURCHASE_FAILED, "LocalValidationFailed");
                    Debug.LogError("Purchase failed! Local validation error: " + e.Message);
                }
                break;

            // AmazonCustomStore (our custom store wrapping the Amazon Appstore SDK v3.0.9 directly)
            // hands back the ReceiptId as a plain string via order.Info - there's no JSON payload to
            // decode here (unlike Unity's old built-in Amazon store integration this used to target).
            case AppStoreProvider.Amazon:
                if (order.Info is AmazonCustomStore.AmazonOrderInfo amazonOrderInfo) {
                    string receiptId = amazonOrderInfo.TransactionID;

                    if (string.IsNullOrEmpty(receiptId)) {
                        IAB.OnPurchaseFailed("Invalid amazon purchase receipt - missing receipt id.");
                        FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.IAP_PURCHASE_FAILED, "AmazonReceiptMissing");
                        break;
                    }

                    IReadOnlyList<CartItem> amazonCartItems = order.CartOrdered.Items();
                    Product amazonProduct = amazonCartItems.Count > 0 ? amazonCartItems[0].Product : null;
                    string sku = amazonProduct?.definition.storeSpecificId ?? string.Empty;

                    ProductType itemType = default;

                    // Amazon SDK v3.0.9's ProductType strings (CONSUMABLE/ENTITLED/SUBSCRIPTION) differ
                    // from the old built-in Amazon store's strings - note ENTITLED, not ENTITLEMENT.
                    switch (amazonOrderInfo.ProductType) {
                        case "CONSUMABLE": itemType = ProductType.Consumable; break;
                        case "ENTITLED": itemType = ProductType.NonConsumable; break;
                        case "SUBSCRIPTION": itemType = ProductType.Subscription; break;
                    }

                    DateTime purchaseDate = amazonOrderInfo.PurchaseDateEpochMs > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(amazonOrderInfo.PurchaseDateEpochMs).UtcDateTime
                        : DateTime.UtcNow;

                    // The Amazon Appstore SDK (v3.0.9) does not expose a sandbox flag on the receipt or
                    // user data the way the old legacy JSON payload did. Defaulting to false here - if
                    // you need sandbox detection, query LicensingService.getAppstoreSDKMode() separately.
                    receipts = new IPurchaseReceipt[1] { new AmazonAppsReceipt(sku, receiptId, purchaseDate, amazonOrderInfo.UserId, false, itemType) };
                    return true;
                } else {
                    IAB.OnPurchaseFailed("Invalid amazon purchase receipt - unexpected order info type.");
                    Debug.LogError("Purchase failed! order.Info was not AmazonCustomStore.AmazonOrderInfo");
                }

                break;
        }

        receipts = new IPurchaseReceipt[0];
        return false;
    }
    
    private IEnumerator WaitForInitThenValidatePurchase(PendingOrder order) {
        float waitTime = 0f;

        // If billing isn't initialising and billing isn't supported then the previous initialisation attempt failed - try again
        if (!IAB.isInitializing)
            InitializePurchasing();

        yield return null;

        // Wait up to 5 seconds for billing to finish initialising (ends early if init finished, success or failure)
        while (waitTime < 5f && IAB.isInitializing) {
            yield return null;

            waitTime += Time.unscaledDeltaTime;
        }

        if (!IAB.isInitialized) {
            InvokePurchaseFailureResponse(PurchaseFailureReason.PurchasingUnavailable, "Purchasing failed to initialise!");
            yield break;
        }
        
        ValidatePurchase(order, true);
    }

    private void ValidatePurchase(PendingOrder order, bool skipInitialisingCheck) {
        if (!skipInitialisingCheck && IAB.isInitializing) {
            IAB.StartCoroutine(WaitForInitThenValidatePurchase(order));

            return;
        }

        if (Application.isEditor) {
            // Editor: skip all local and serverside verification, just run the final success actions
            OnPurchaseVerificationCompleted(order, "EDITOR_TRANSACTION", "", true);
            return;
        }

        // StoreKit2 (Unity IAP's current Apple flow) doesn't populate order.Info.Receipt with a
        // legacy base64 App Store receipt the way the old StoreKit1 flow did - Payload comes back
        // empty. Proof of purchase is the signed JWS transaction on order.Info.Apple instead, which
        // BuildServersideVerificationRequest already reads directly. So for Apple, skip the legacy
        // CrossPlatformValidator/receipt-matching path entirely (it can never find a match against
        // an empty Payload) and go straight to server-side JWS verification.
        if (CrossPlatformManager.GetActiveStore() == AppStoreProvider.Apple) {
            foreach (CartItem cartItem in order.CartOrdered.Items()) {
                Product product = cartItem.Product;

                if (product.definition.type == ProductType.Consumable) {
                    IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

                    if (localItem != null) {
                        localItem.purchaseState = PurchaseState.Pending;
                        localItem.isProcessingValidation = true;
                    }
                }
            }

            SendPurchaseVerificationRequest(order);
            return;
        }

        // If the product receipt is blank then something went wrong, fail the purchase
        if (string.IsNullOrEmpty(order.Info.Receipt)) {
            InvokePurchaseFailureResponse(PurchaseFailureReason.Unknown, "Order receipt missing!");
            return;
        }

        // Mark consumable local items as pending
        foreach (CartItem cartItem in order.CartOrdered.Items()) {
            Product product = cartItem.Product;
            
            if (product.definition.type == ProductType.Consumable) {
                IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

                if (localItem != null) {
                    localItem.purchaseState = PurchaseState.Pending;
                    localItem.isProcessingValidation = true;
                }
            }
        }

        // Something's happening, refresh the timer for the hold on screen to show the skip button
        if (ClickLockManager.Instance != null)
            ClickLockManager.Instance.ResetTimer();

        // Returns true if the local receipt looks valid, false if it either looks invalid or the developer has not setup the tangle correctly
        if (LocalReceiptValidation(order, out IPurchaseReceipt[] receipts)) {
            // For some reason this returns all current receipts even though we just made 1 purchase
            foreach (IPurchaseReceipt receipt in receipts) {
                if (FirebaseManager.instance.debugMode) {
                    Debug.Log("IAB searching receipts - Receipt Transaction ID: " + receipt.transactionID + " / Product Transaction ID: " + order.Info.TransactionID);
                    Debug.Log("IAB additional receipt info: Product ID: " + receipt.productID + ", Purchase Date: " + receipt.purchaseDate);
                }

                if (!string.IsNullOrEmpty(receipt.transactionID) && !string.IsNullOrEmpty(order.Info.TransactionID)) {
                    switch (CrossPlatformManager.GetActiveStore()) {
                        case AppStoreProvider.Google:
                            if (receipt is GooglePlayReceipt googleReceipt) {
                                switch (googleReceipt.purchaseState) {
                                    case GooglePurchaseState.Purchased:
                                        SendPurchaseVerificationRequest(order, googleReceipt);
                                        break;
                                }
                            }
                            break;

                        case AppStoreProvider.Amazon:
                            if (receipt is AmazonAppsReceipt amazonAppsReceipt)
                                SendPurchaseVerificationRequest(order, amazonAppsReceipt);
                            break;
                    }

                    // Return once we hit the target transactionID
                    return;
                }
            }

            if (FirebaseManager.instance.debugMode) {
                Debug.Log("Purchase didn't match existing transactions, this looks like a promo code purchase");
                Debug.Log("Raw Receipt: " + order.Info.Receipt);
            }

            // All receipts have been searched but none matched the transaction id of the purchase, this either means:
            // 1: This is a promotional purchase (these don't have transaction ids)
            // 2: This is a fake purchase (doubtful though as purchase spoofing apps generally also manipulate the receipts list too)
            // 3: This is a store returning a purchase success when it actually failed (amazon does this when an item is already owned, it's a successful purchase response with blank receipt data)
            switch (CrossPlatformManager.GetActiveStore()) {
                case AppStoreProvider.Google:
                    CheckForPromotionalPurchase(order);
                    break;

                default:
                    // On amazon this is probably item already owned.. however we can't know for sure so just return an unknown failure reason
                    InvokePurchaseFailureResponse(PurchaseFailureReason.Unknown, "Something went wrong!\nTry again later.");
                    break;
            }
        }
    }
    
    private void SendPurchaseVerificationRequest(PendingOrder order, GooglePlayReceipt googleReceipt) {
        // Something's happening, refresh the timer for the hold on screen to show the skip button
        if (ClickLockManager.Instance != null)
            ClickLockManager.Instance.ResetTimer();

        IAB.StartCoroutine(DoPurchaseVerificationRequest(order, googleReceipt));
    }

    private void SendPurchaseVerificationRequest(PendingOrder order, bool verifyWithSandbox = false) {
        // Something's happening, refresh the timer for the hold on screen to show the skip button
        if (ClickLockManager.Instance != null)
            ClickLockManager.Instance.ResetTimer();

        IAB.StartCoroutine(DoPurchaseVerificationRequest(order, verifyWithSandbox));
    }

    private void SendPurchaseVerificationRequest(PendingOrder order, AmazonAppsReceipt amazonAppsReceipt) {
        // Something's happening, refresh the timer for the hold on screen to show the skip button
        if (ClickLockManager.Instance != null)
            ClickLockManager.Instance.ResetTimer();

        IAB.StartCoroutine(DoPurchaseVerificationRequest(order, amazonAppsReceipt));
    }

    private void SyncLocalItemWithCartItems(PendingOrder order, IPurchaseReceipt receipt) {
        IReadOnlyList<CartItem> cartItems = order.CartOrdered.Items();
        
        foreach (CartItem cartItem in cartItems) {
            Product product = cartItem.Product;

            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            if (localItem != null) {
                localItem.lastPurchaseReceipt = receipt;
                localItem.lastOrder = order;
            }
        }
    }

    private const string VALIDATION_URL = "https://data.i6.com/google_iab_validation.php";
    private const int VALIDATION_VERSION = 2;

    private UnityWebRequest BuildServersideVerificationRequest(AppStoreProvider appstore, PendingOrder order, IPurchaseReceipt receipt, bool verifyWithSandbox = false) {
        List<IMultipartFormSection> verificationPostData = new List<IMultipartFormSection>();
        IReadOnlyList<CartItem> cartItems = order.CartOrdered.Items();

        verificationPostData.Add(new MultipartFormDataSection("version", VALIDATION_VERSION.ToString(), Encoding.UTF8, "multipart/form-data"));
        verificationPostData.Add(new MultipartFormDataSection("bundle", Application.identifier, Encoding.UTF8, "multipart/form-data"));

        switch (appstore) {
            case AppStoreProvider.Google:
                GooglePlayReceipt googleReceipt = (GooglePlayReceipt)receipt;

                verificationPostData.Add(new MultipartFormDataSection("store_type", "android", Encoding.UTF8, "multipart/form-data"));
                verificationPostData.Add(new MultipartFormDataSection("token", googleReceipt.purchaseToken, Encoding.UTF8, "multipart/form-data"));
                verificationPostData.Add(new MultipartFormDataSection("transaction_id", googleReceipt.transactionID, Encoding.UTF8, "multipart/form-data"));
                break;

            case AppStoreProvider.Amazon:
                AmazonAppsReceipt amazonReceipt = (AmazonAppsReceipt)receipt;

                verificationPostData.Add(new MultipartFormDataSection("store_type", "amazon", Encoding.UTF8, "multipart/form-data"));
                verificationPostData.Add(new MultipartFormDataSection("user_id", amazonReceipt.userId, Encoding.UTF8, "multipart/form-data"));
                verificationPostData.Add(new MultipartFormDataSection("receipt_id", amazonReceipt.transactionID, Encoding.UTF8, "multipart/form-data"));
                break;

            case AppStoreProvider.Apple:
                IAppleOrderInfo appleOrderInfo = order.Info.Apple;

                verificationPostData.Add(new MultipartFormDataSection("store_type", "ios", Encoding.UTF8, "multipart/form-data"));

                if (appleOrderInfo != null && !string.IsNullOrEmpty(appleOrderInfo.jwsRepresentation)) {
                    verificationPostData.Add(new MultipartFormDataSection("jws", appleOrderInfo.jwsRepresentation, Encoding.UTF8, "multipart/form-data"));
                } else {
                    Debug.LogError("Apple JWS representation is missing! Server-side verification will fail (bundle/jws are required).");
                }

                verificationPostData.Add(new MultipartFormDataSection("exclude_old_transactions", "false", Encoding.UTF8, "multipart/form-data"));
                verificationPostData.Add(new MultipartFormDataSection("use_sandbox", verifyWithSandbox ? "1" : "0", Encoding.UTF8, "multipart/form-data"));
                break;
        }

        foreach (CartItem cartItem in cartItems) {
            Product product = cartItem.Product;

            verificationPostData.Add(new MultipartFormDataSection("products[]", product.definition.storeSpecificId, Encoding.UTF8, "multipart/form-data"));

            // Type string will be either: "Consumable", "NonConsumable", "Subscription" or "Unknown" if something went wrong
            verificationPostData.Add(new MultipartFormDataSection("product_types[]", product.definition.type.ToString(), Encoding.UTF8, "multipart/form-data"));
        }
        
        if (FirebaseManager.instance.debugMode) {
            // Log the VALIDATION_URL with verificationPostData added in a GET URL format so we can manually visit the URL in browser
            StringBuilder debugUrl = new StringBuilder(VALIDATION_URL);

            for (int i = 0; i < verificationPostData.Count; i++) {
                IMultipartFormSection section = verificationPostData[i];

                debugUrl.Append(i == 0 ? "?" : "&");
                debugUrl.Append(UnityWebRequest.EscapeURL(section.sectionName));
                debugUrl.Append("=");
                debugUrl.Append(UnityWebRequest.EscapeURL(Encoding.UTF8.GetString(section.sectionData)));
            }

            Debug.Log("Validation URL: " + debugUrl);
        }

        return UnityWebRequest.Post(VALIDATION_URL, verificationPostData);
    }

    private IEnumerator DoPurchaseVerificationRequest(PendingOrder order, GooglePlayReceipt googleReceipt) {
        SyncLocalItemWithCartItems(order, googleReceipt);

        UnifiedReceipt decodedReceipt = JsonUtility.FromJson<UnifiedReceipt>(order.Info.Receipt);
        UnityWebRequest verificationRequest = BuildServersideVerificationRequest(AppStoreProvider.Google, order, googleReceipt);

        if (verificationRequest == null) {
            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Failed to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }

        DownloadHandler verificationRequestDownloadHandler = verificationRequest.downloadHandler;

        // Wait for the request to complete
        yield return verificationRequest.SendWebRequest();
        
        if (FirebaseManager.instance.debugMode)
            Debug.Log("Verification response: " + verificationRequestDownloadHandler.text);

        if (verificationRequest.result == UnityWebRequest.Result.ProtocolError || string.IsNullOrEmpty(verificationRequestDownloadHandler.text)) {
            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Unable to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }

        JSONNode verificationResponse = JSON.Parse(verificationRequestDownloadHandler.text);

        if (verificationResponse == null) {
            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Blank response from Google's purchase validation service!");
            yield break;
        }
        
        bool purchaseValid = false;

        // Standard consumable/non-consumable purchases
        if (verificationResponse.HasKey("orderId") && verificationResponse["orderId"]?.Value == googleReceipt.transactionID) {
            purchaseValid = true;
        } else if (verificationResponse.HasKey("lineItems")) {
            // Subscription purchases use lineItems > latestSuccessfulOrderId instead of orderId
            JSONNode subscriptionLineItems = verificationResponse["lineItems"];

            if (subscriptionLineItems != null && subscriptionLineItems.IsArray) {
                foreach (JSONNode lineItem in subscriptionLineItems) {
                    if (lineItem.HasKey("latestSuccessfulOrderId") && lineItem["latestSuccessfulOrderId"]?.Value == googleReceipt.transactionID) {
                        purchaseValid = true;
                        break;
                    }
                }
            }
        }

        // Check if this purchase was made via alternative purchase methods e.g promo code, rewarded or test purchase
        if (!purchaseValid && verificationResponse.HasKey("purchaseType") && int.TryParse(verificationResponse["purchaseType"]?.Value, out int purchaseType)) {
            purchaseValid = true;
        }

        if (purchaseValid) {
            OnPurchaseVerificationCompleted(order, googleReceipt.transactionID, decodedReceipt?.Payload);
        } else {
            // Invalid purchase..
            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Purchase validation failed!\nRestart the app or try the purchase again to retry (you won't be re-charged)");
            Debug.LogError("Purchase failed - Invalid purchase type or order id!");
        }
    }

    private IEnumerator DoPurchaseVerificationRequest(PendingOrder order, AmazonAppsReceipt amazonAppsReceipt) {
        SyncLocalItemWithCartItems(order, amazonAppsReceipt);

        UnityWebRequest verificationRequest = BuildServersideVerificationRequest(AppStoreProvider.Amazon, order, amazonAppsReceipt);

        if (verificationRequest == null) {
            IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Failed to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }
        
        DownloadHandler verificationRequestDownloadHandler = verificationRequest.downloadHandler;
        
        // Wait for the request to complete
        yield return verificationRequest.SendWebRequest();
	
        if (FirebaseManager.instance.debugMode)
            Debug.Log("Verification response: " + verificationRequestDownloadHandler.text);

        if (verificationRequest.result == UnityWebRequest.Result.ProtocolError || string.IsNullOrEmpty(verificationRequestDownloadHandler.text)) {
            IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Unable to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }
        
        JSONNode verificationResponse = JSON.Parse(verificationRequestDownloadHandler.text);
        
        if (verificationResponse == null) {
            IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Blank response from Amazon's purchase validation service!");
            yield break;
        }

        // Note: Not comparing .Value to the strings results in a non match as the JSONNode children are JSONNodes, not strings
        // if the server response had a valid matching receiptId though we can assume it's legit and there isn't anything extra we could get from checking a developerPayload too
        if (verificationResponse["receiptId"].Value == amazonAppsReceipt.transactionID) {
            OnPurchaseVerificationCompleted(order, amazonAppsReceipt.transactionID, null);
        } else {
            // Invalid purchase
            IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Purchase validation failed!\nRestart the app or try the purchase again to retry (you won't be re-charged)");
            Debug.LogError("Purchase failed - Invalid receipt id!");
        }
    }

    private IEnumerator DoPurchaseVerificationRequest(PendingOrder order, bool verifyWithSandbox = false) {
        // No legacy Apple receipt object to key off anymore (see ValidatePurchase) - derive the
        // product id straight from the order for error messages and local caching.
        string productId = order.CartOrdered.Items().FirstOrDefault()?.Product.definition.storeSpecificId ?? string.Empty;

        SyncLocalItemWithCartItems(order, null);

        UnityWebRequest verificationRequest = BuildServersideVerificationRequest(AppStoreProvider.Apple, order, null, verifyWithSandbox);

        if (verificationRequest == null) {
            IAB.OnPurchaseVerificationFailed(productId, "Unable to contact Apple's receipt validation service! Try the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }

        DownloadHandler verificationRequestDownloadHandler = verificationRequest.downloadHandler;

        // Wait for the request to complete
        yield return verificationRequest.SendWebRequest();

        if (FirebaseManager.instance.debugMode)
            Debug.Log("Apple verification HTTP status: " + verificationRequest.responseCode + ", result: " + verificationRequest.result + ", response: " + verificationRequestDownloadHandler.text);

        if (verificationRequest.result == UnityWebRequest.Result.ProtocolError || string.IsNullOrEmpty(verificationRequestDownloadHandler.text)) {
            IAB.OnPurchaseVerificationFailed(productId, "Unable to contact the purchase validation service! Try the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }

        JSONNode verificationResponse = SimpleJSON.JSON.Parse(verificationRequestDownloadHandler.text);

        if (verificationResponse == null) {
            IAB.OnPurchaseVerificationFailed(productId, "Blank response from Apple's purchase validation service!");
            yield break;
        }

        // Our backend verifies Apple's signed StoreKit2 transaction (JWS) itself and returns either
        // the decoded transaction payload directly on success, or {"error": "..."} on failure - there
        // is no legacy verifyReceipt-style numeric "status" field to check here (that was v1 only).
        if (verificationResponse.HasKey("error")) {
            string errorCode = verificationResponse["error"].Value;

            Debug.LogError("Apple purchase verification failed - errorCode: " + errorCode + ", productId: " + productId + ", verifyWithSandbox: " + verifyWithSandbox);

            // The server independently verifies which environment (sandbox/production) the signed
            // transaction actually belongs to. If our client-side guess was wrong, retry once with
            // the opposite - mirrors the old v1 21007/21008 sandbox-retry behaviour.
            if (errorCode == "ENVIRONMENT_MISMATCH" && !verifyWithSandbox) {
                Debug.Log("Environment mismatch, retrying Apple purchase verification against sandbox!");
                SendPurchaseVerificationRequest(order, true);
                yield break;
            }

            string errorMessage;

            switch (errorCode) {
                case "MISSING_PARAM":
                case "MISSING_PRODUCTS":
                case "INVALID_PRODUCTS":
                    errorMessage = "Developer error!\nPurchase validation request was malformed.";
                    break;

                case "INVALID_JWS":
                case "INVALID_JWS_SIGNATURE":
                    errorMessage = "Local purchase signature validation failed!\nA 3rd party app may be interfering with purchases or this app has been modified.";
                    break;

                case "BUNDLE_MISMATCH":
                case "MISSING_PRODUCT":
                case "PRODUCT_MISMATCH":
                case "PRODUCT_TYPE_MISMATCH":
                    errorMessage = "Purchase validation failed!\nRestart the app or try the purchase again to retry (you won't be re-charged)";
                    break;

                case "PURCHASE_REVOKED":
                    errorMessage = "This purchase has been revoked or refunded.";
                    break;

                default: // Includes a genuine ENVIRONMENT_MISMATCH on the sandbox retry itself
                    errorMessage = "Purchase validation failed!\nRestart the app or try the purchase again to retry (you won't be re-charged)";
                    break;
            }

            IAB.OnPurchaseVerificationFailed(productId, errorMessage);
            Debug.LogError("Purchase failed - Apple verification error: " + errorCode);
            yield break;
        }

        // Success: verificationResponse is the decoded, server-verified StoreKit2 transaction payload.
        IAppleOrderInfo appleOrderInfo = order.Info.Apple;

        string originalTransactionId = verificationResponse.HasKey("originalTransactionId") && !string.IsNullOrEmpty(verificationResponse["originalTransactionId"].Value)
            ? verificationResponse["originalTransactionId"].Value
            : (appleOrderInfo != null && !string.IsNullOrEmpty(appleOrderInfo.OriginalTransactionID) ? appleOrderInfo.OriginalTransactionID : order.Info.TransactionID);

        bool isSandboxTransaction = verificationResponse.HasKey("environment") && verificationResponse["environment"].Value == "Sandbox";

        OnPurchaseVerificationCompleted(order, originalTransactionId, null, isSandboxTransaction);
    }

    private void OnPurchaseVerificationCompleted(PendingOrder order, string originalTransactionId = "", string payload = "", bool isTestPurchase = false) {
        bool testPurchase = isTestPurchase;

        if (!string.IsNullOrEmpty(payload)) {
            JSONNode payloadData = JSON.Parse(payload);
            
            switch (CrossPlatformManager.GetActiveStore()) {
                case AppStoreProvider.Google:
                    // "json", "signature" https://docs.unity3d.com/Packages/com.unity.purchasing@4.13/manual/GoogleReceipt.html
                    if (payloadData != null && payloadData.HasKey("json")) {
                        JSONNode payloadJSONData = JSON.Parse(payloadData["json"].Value);

                        if (payloadJSONData != null) {
                            // https://developers.google.com/android-publisher/api-ref/rest/v3/purchases.products
                            if (payloadJSONData.HasKey("purchaseType"))
                                if (payloadJSONData["purchaseType"].AsInt == 0)
                                    testPurchase = true;
                        }
                    }
                break;

                case AppStoreProvider.Amazon:
                    // AmazonCustomStore passes payload as null (see DoPurchaseVerificationRequest overload
                    // for AmazonAppsReceipt) since order.Info.Receipt is a plain ReceiptId string, not JSON -
                    // so this branch is currently unreachable. Amazon Appstore SDK v3.0.9 doesn't expose a
                    // sandbox flag the way the old legacy payload did; query LicensingService.getAppstoreSDKMode()
                    // separately if you need that.
                break;
                
                // iOS it's passed as the isTestPurchase parameter
            }
        }

        foreach (CartItem cartItem in order.CartOrdered.Items()) {
            Product product = cartItem.Product;
            
            IAB.OnPurchaseVerificationCompleted(IAB.GetItemBySKU(product.definition.storeSpecificId), cartItem.Quantity, originalTransactionId, payload, testPurchase);
        }
        
        // For consumable items this will consume the purchase, everything else it will acknowledge the transaction
        EndPendingPurchase(order);
    }
    
    #endregion
}