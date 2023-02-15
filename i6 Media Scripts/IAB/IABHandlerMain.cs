using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using JetBrains.Annotations;
using SimpleJSON;
using UnityEngine.Networking;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;

public class IABHandlerMain : MonoBehaviour, IABHandler, IStoreListener {
    [Serializable]
    private struct AmazonReceipt {
        public string receiptId;
        public string userId;
        public bool isSandbox;
        public ProductJson productJson;
        public ReceiptJson receiptJson;
    }

    [Serializable]
    private struct ProductJson {
        public string sku;
        public string productType;
        public string description;
        public string smallIconUrl;
        public string title;
        public int coinsRewardAmount; // How many bonus amazon coins the user got for this purchase
    }
    
    [Serializable]
    private struct ReceiptJson {
        public string receiptId;
        public string sku;
        public string itemType;
        public string purchaseDate;
    }
    
    private IABManager IAB;
    
    private IStoreController controller; // Unity purchasing system
    private IExtensionProvider extensions; // Store specific purchasing system
    
    private StandardPurchasingModule purchaseModule;
    
    private IGooglePlayStoreExtensions googleExtensions;
    private IAppleExtensions appleExtensions;
    private IAmazonExtensions amazonExtensions;
    private IMicrosoftExtensions microsoftExtensions;
    
    private HashSet<ProductDefinition> productDefinitions;

    public enum ItemInventoryState { Pending, Purchased, Cancelled, Refunded }
    
    public void Init(IABManager manager) {
        IAB = manager;
    }
    
    public void InitializePurchasing() {
        if (!CrossPlatformManager.instance.hasInitialized) {
            Debug.LogError("Failed to initialize billing! (Store initialization has not completed)");
            return;
        }
        
        if (IAB.isInitializing) {
            if (FirebaseManager.instance.debugMode)
                Debug.Log("Failed to initialize billing! (Already initializing)");
            return;
        }

        IAB.isInitializing = true;

        purchaseModule = StandardPurchasingModule.Instance();
        ConfigurationBuilder builder = ConfigurationBuilder.Instance(purchaseModule);

        // Populate the product list with the products configured in unity
        for (int i = 0; i < IAB.itemList.Count; i++) {
            IABItem localItem = IAB.itemList[i];

            IDs ids = new IDs();

            // Add all store skus for all appstores, Unity IAP will use the correct ids for the current store
            foreach (IABItemStoreInfo storeSKUItem in localItem.storeSKUs) {
                ids.Add(storeSKUItem.sku, CrossPlatformManager.AppStoreToStoreName(storeSKUItem.store));
            }

            builder.AddProduct(localItem.productId, localItem.type, ids);
        }

        // Setup some store specific configurations and callbacks
        switch (CrossPlatformManager.GetActiveStore()) {
            case AppStore.GooglePlay:
                // Setup a deferred purchase listener on android
                // This allows purchases to be made in the app and marked as pending to be paid for later in person at a shop etc
                builder.Configure<IGooglePlayConfiguration>().SetDeferredPurchaseListener(OnPurchaseDeferred);
                break;

            case AppStore.AppleAppStore:
                // Setup a callback for apples special promotional purchases (this just lets us be notified that it was a promo purchase, without this it would go through silently as a normal purchase)
                builder.Configure<IAppleConfiguration>().SetApplePromotionalPurchaseInterceptorCallback(OnIOSPromoPurchase);
                break;

            case AppStore.AmazonAppStore:
                builder.Configure<IAmazonConfiguration>().WriteSandboxJSON(builder.products);
                break;

            case AppStore.fake:
                purchaseModule.useFakeStoreAlways = true;
                purchaseModule.useFakeStoreUIMode = FakeStoreUIMode.DeveloperUser;
                break;
        }

        // Store the product definitions as they're needed if we want to reload the inventory during the session
        productDefinitions = builder.products;

        UnityPurchasing.Initialize(this, builder);
    }
    
    // IStoreListener callback
    public void OnInitialized(IStoreController initController, IExtensionProvider initExtensions) {
        // If for some reason initialization was a success but the controller or extensions or null just call initialization failed instead and return
        if (initController == null || initExtensions == null) {
            OnInitializeFailed(InitializationFailureReason.PurchasingUnavailable);
            return;
        }
        
        controller = initController;
        extensions = initExtensions;
        
        // Setup the store specific extensions and setup some callback listeners for events setup via the extensions
        switch (CrossPlatformManager.GetActiveStore()) {
            case AppStore.GooglePlay:
                // Setup a deferred purchase listener on android
                // This allows purchases to be made in the app and marked as pending to be paid for later in person at a shop etc
                googleExtensions = extensions.GetExtension<IGooglePlayStoreExtensions>();
                
                // Note: Google Play has their deferred listener setup as part of the builder setup so you can find the callback setup in InitializePurchasing()
                break;
            
            case AppStore.MacAppStore:
            case AppStore.AppleAppStore:
                appleExtensions = extensions.GetExtension<IAppleExtensions>();
        
                // Setup a deferred purchase listener on iOS
                // On iOS a purchase can be deferred when parental settings forward the purchase prompt to a parents phone to approve the purchase
                appleExtensions?.RegisterPurchaseDeferredListener(OnPurchaseDeferred);
                break;
            
            case AppStore.AmazonAppStore:
                amazonExtensions = extensions.GetExtension<IAmazonExtensions>();
                break;
            
            case AppStore.WinRT:
                microsoftExtensions = extensions.GetExtension<IMicrosoftExtensions>();
                break;
        }
        
        IAB.isInitialized = true;
        IAB.isInitializing = false;

#if !UNITY_EDITOR
        Debug.Log("Successfully initialized billing!");
#endif

        // Sync the local inventory with the store inventory
        OnInventoryReloaded();
    }

    private void OnPurchaseDeferred(Product product) {
        IAB.OnPurchaseDeferred(IAB.GetItemBySKU(product.definition.storeSpecificId));
    }

    // IStoreListener callback
    public void OnInitializeFailed(InitializationFailureReason error, string errorString) {
        OnInitializeFailed(error);
        
        Debug.LogError("Initialize failed with error string: " + errorString);
    }

    // IStoreListener callback
    public void OnInitializeFailed(InitializationFailureReason error) {
        IAB.isInitialized = false;
        IAB.isInitializing = false;
        IAB.isPurchaseActive = false;

        switch (error) {
            case InitializationFailureReason.PurchasingUnavailable:
                Debug.LogError("Failed to initialize billing! (Purchasing unavailable)");
                break;
            case InitializationFailureReason.AppNotKnown:
                Debug.LogError("Failed to initialize billing! (App not configured for this store)");
                break;
            case InitializationFailureReason.NoProductsAvailable:
                Debug.LogError("Failed to initialize billing! (No purchasable products have been setup)");
                break;
            default:
                Debug.LogError("Failed to initialize billing! (Unknown error occured)");
                break;
        }

        FirebaseAnalyticsManager.LogEvent("iab_not_supported", "error", error.ToString());
    }

    // IStoreListener callback
    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e) {
        Product product = e.purchasedProduct;
        
        if (FirebaseManager.instance.debugMode)
            Debug.Log("ProcessPurchase called for SKU: " + product.definition.storeSpecificId + " has receipt? " + product.hasReceipt);

        IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

        if (localItem != null && !localItem.isProcessingValidation)
            OnPurchaseComplete(product);
        
        // We return pending here to ensure consumable items aren't instantly consumed, we want to manually consume them with this script when done validating the purchase
        // This also mimics the OpenIAB behaviour we had where purchases could be consumed manually via a consume function
        return PurchaseProcessingResult.Pending;
    }
    
    // IStoreListener callback
    public void OnPurchaseFailed(Product product, PurchaseFailureReason errorType) {
        bool hasProductData = product?.definition != null;

        string errorMessage = "An unknown error occured!\nTry again later. (" + errorType + ")";
        Debug.LogError("Purchase Failed [Item: " + (hasProductData ? product.definition.storeSpecificId : "unknown") + "] [Reason: " + errorType + "]");
        
        if (hasProductData) {
            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            switch (errorType) {
                case PurchaseFailureReason.PurchasingUnavailable:
                    errorMessage = "Purchase failed!\nCheck your connection and make sure parental controls aren't blocking purchases.";
                    break;

                case PurchaseFailureReason.ExistingPurchasePending:
                    // If the purchase is consumable try consume it (may be a previous purchase which didn't finish)
                    if (product.definition.type == ProductType.Consumable) {
                        // Call the purchase complete function again for this owned consumable item to re-attempt serverside validation & consume this item
                        if (localItem != null && !localItem.isProcessingValidation) {
                            OnPurchaseComplete(product);
                            
                            // Exit early, this isn't actually a purchase failed error, we're now attempting to re-validate the purchase
                            return;
                        }
                        
                        errorMessage = "Another device on your account is currently processing a transaction of this item, try again soon!";
                    } else if (product.definition.type == ProductType.Subscription) {
                        // If a subscription is already owned (e.g on another device on the same account) then count this purchase as a success
                        OnPurchaseComplete(product);
                        return;
                    } else {
                        errorMessage = "You already own " + product.metadata.localizedTitle + ".";
                    }

                    break;

                case PurchaseFailureReason.ProductUnavailable:
                    // The title can be blank in cases where initialisation was successful but the connection was lost before getting the information for this product
                    bool isTitleBlank = string.IsNullOrEmpty(product?.metadata?.localizedTitle);
                    
                    errorMessage = (isTitleBlank ? "This item" : product.metadata.localizedTitle) + " is unavailable for purchase!\nCheck your connection and try again later.";
                    break;

                case PurchaseFailureReason.SignatureInvalid:
                    // If this error is being called then it's most likely due to the public key not matching the store key
                    // Window > Unity IAP > Receipt Validation Obfuscator to set the key to match the store
                    errorMessage = "Local purchase signature validation failed!\nA 3rd party app may be interfering with purchases or this app has been modified.";
                    break;

                case PurchaseFailureReason.UserCancelled:
                    // Note: Amazon and windows platforms do not specify cancelled as a failure reason (they instead call PurchaseFailureReason.Unknown)
                    errorMessage = "Purchase cancelled, you have not been charged.";

                    if (CrossPlatformManager.GetActiveStore() == AppStore.GooglePlay) {
                        // BUGFIX: Android billing forever has had an issue where minimizing after tapping  buy then tapping the app icon from the drawer to launch again does the cancel action
                        // I'm not sure if it's just an issue both OpenIAB and Unity IAP have but this workaround just re-checks for pending purchases when the cancel event happens
                        googleExtensions.RestoreTransactions((result, error) => { }); // We don't care about the result, but this will force trigger ProcessPurchase if a new purchase is found
                    }
                    break;

                case PurchaseFailureReason.PaymentDeclined:
                    // Only triggered on apple, other platforms don't end the purchase prompt until payment has been made
                    // or completes successfully but then gets rejected later in cases of card fraud etc
                    errorMessage = "Payment declined!\nCheck your account payment methods and try again.";
                    break;

                case PurchaseFailureReason.DuplicateTransaction:
                    if (product.definition.type == ProductType.Consumable) {
                        // Call the purchase complete function again for this owned consumable item to re-attempt serverside validation & consume this item
                        if (localItem != null && !localItem.isProcessingValidation) {
                            OnPurchaseComplete(product);
                            
                            // Exit early, this isn't actually a purchase failed error, we're now attempting to re-validate the purchase
                            return;
                        }

                        errorMessage = "Another device on your account is currently processing a transaction of this item, try again soon!";
                    } else if (product.definition.type == ProductType.Subscription) {
                        // If a subscription is already owned (e.g on another device on the same account) then count this purchase as a success
                        OnPurchaseComplete(product);
                        return;
                    } else {
                        errorMessage = "You already own " + product.metadata.localizedTitle + ".";
                    }

                    break;

                case PurchaseFailureReason.Unknown:
                    // On android this is triggered for card declined etc so just give a generic message
                    // The play store error popup before returning to the game should have explained the error cause in detail
                    // Note: amazon and windows platforms return this failure reason for cancelling the purchase
                    errorMessage = "Purchase failed, you have not been charged.";
                    break;
            }

            if(localItem != null)
                localItem.isProcessingValidation = false;
        } else {
            errorMessage = "Failed to get item info!\nCheck your connection and try again later.";
        }

        // We only reach this point if the switch statement above didn't exit early
        IAB.OnPurchaseFailed(errorMessage);

        FirebaseAnalyticsManager.LogEvent("iab_purchase_error", "error", errorType.ToString());

        IAB.isPurchaseActive = false;
    }
    
    public Product GetProductByProductId(string productId) {
        return controller?.products?.WithID(productId);
    }

    public Product GetProductBySKU(string sku) {
        return controller?.products?.WithStoreSpecificID(sku);
    }

    public bool HasRawReceipt(string productId) {
        Product product = GetProductByProductId(productId);

        return product != null ? product.hasReceipt : false;
    }

    public string GetRawReceipt(string productId) {
        Product product = GetProductByProductId(productId);

        return product != null ? product.receipt : null;
    }
    
    public IPurchaseReceipt GetPurchaseReceipt(string productId) {
        Product product = GetProductByProductId(productId);

        return product != null ? GetPurchaseReceipt(product) : null;
    }

	public string GetAppleAppReceipt() {
#if UNITY_IOS
        ConfigurationBuilder configBuilder = ConfigurationBuilder.Instance(purchaseModule);
        IAppleConfiguration appleConfig = configBuilder.Configure<IAppleConfiguration>();

        return appleConfig.appReceipt;
#else
        return "";
#endif
    }
	
    private IPurchaseReceipt GetPurchaseReceipt(Product product) {
        if (IAB.StoreSupportsLocalReceipts() && GetStoreReceipts(product.receipt, out IPurchaseReceipt[] receipts)) {
            foreach (IPurchaseReceipt receipt in receipts) {
                if (FirebaseManager.instance.debugMode) {
                    Debug.Log("IAB searching receipts - Receipt Transaction ID: " + receipt.transactionID + " / Product Transaction ID: " + product.transactionID);
                    Debug.Log("IAB additional receipt info: Product ID: " + receipt.productID + ", Purchase Date: " + receipt.purchaseDate);
                }

                if (receipt.productID == product.definition.storeSpecificId && !string.IsNullOrEmpty(receipt.transactionID)) {
                    // Return once we hit the target product which doesn't have a blank transactionID
                    return receipt;
                }
            }
        }

        // No receipt found
        return null;
    }

    private bool GetStoreReceipt(string productReceipt, out IPurchaseReceipt receipt)
    {
        bool receiptStatus = GetStoreReceipts(productReceipt, out IPurchaseReceipt[] receipts);

        if (receipts.Length > 0)
        {
            receipt = receipts[0];
            return receiptStatus;
        }
        
        receipt = null;
        return false;
    }
    
    private bool GetStoreReceipts(string productReceipt, out IPurchaseReceipt[] receipts) {
        switch(CrossPlatformManager.GetActiveStore())
        {
            // Standard local validation flow for GooglePlay / Apple
            case AppStore.GooglePlay:
            case AppStore.AppleAppStore:
            case AppStore.MacAppStore:
                // This scripting define symbol is used because the GooglePlayTangle and AppleTangle scripts do not exist until they have been generated
                // Setting UNITY_PURCHASING scripting define symbol just shows that the developer is following the guide and has setup the tangle files
#if UNITY_PURCHASING
                try {
                    CrossPlatformValidator validator = new CrossPlatformValidator(GooglePlayTangle.Data(), AppleTangle.Data(), Application.identifier);
                    receipts = validator.Validate(productReceipt);

                    return true;
                } catch (IAPSecurityException e) {
                    // Purchase validation failed
                    IAB.OnPurchaseFailed("Local purchase receipt validation failed!");
                    FirebaseAnalyticsManager.LogEvent("iab_purchase_error", "error", "LocalValidationFailed");
                    Debug.LogError("Purchase failed! Local validation error: " + e.Message);
                }
#else
                OnIABPurchaseFailed?.Invoke("Developer error! UNITY_PURCHASING scripting define symbol not set, could not run local purchase validation!");
                Debug.LogError("UNITY_PURCHASING scripting define symbol must be set to use local validation on Google and Apple stores!");
#endif
                break;
        
            // Unity's documentation regarding amazon for Unity IAP is terrible, there's basically no documentation and I couldn't even find info on what version of amazon IAP it was using
            // Manually looking inside AmazonAppStore.aar I found that it's using version 2.0 which means basically all information online about amazon with Unity IAP is invalid and outdated
            // However finding this info is good as amazon had remove their docs for amazon IAP 1.0 and example receipts and info on unity forums wasn't making any sense with the 2.0 docs
            case AppStore.AmazonAppStore:
                // This app store does not support local purchase validation, just return the receipts
                UnifiedReceipt decodedReceipt = JsonUtility.FromJson<UnifiedReceipt>(productReceipt);

                if (decodedReceipt != null && !string.IsNullOrEmpty(decodedReceipt.Payload)) {
                    if(FirebaseManager.instance.debugMode)
                        Debug.Log("Amazon receipt payload: " + decodedReceipt.Payload);
                    
                    AmazonReceipt receiptPayload = JsonUtility.FromJson<AmazonReceipt>(decodedReceipt.Payload);
                    
                    // https://i.imgur.com/BTuVyRw.png (note: receiptJson here is from amazon IAB v1.0 which is pretty different from v2.0

                    // An example receipt can be found here: https://i.imgur.com/Fo1VF4D.png
                    // Amazon's receipt use a weird custom date format which we need to manually parse (ddd MMM dd HH:mm:ss GMTzzz yyyy)
                    if (!DateTime.TryParseExact(receiptPayload.receiptJson.purchaseDate, "ddd MMM dd HH:mm:ss GMTzzz yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime purchaseDate)) {
                        // If the receipt date fails to parse fallback to just using DateTime.Now
                        purchaseDate = DateTime.Now;
                    }

                    ProductType itemType = default;
                    
                    // https://developer.amazon.com/docs/in-app-purchasing/iap-api-for-webapps-ref.html#itemtype
                    switch (receiptPayload.receiptJson.itemType) {
                        case "CONSUMABLE": itemType = ProductType.Consumable; break;
                        case "ENTITLEMENT": itemType = ProductType.NonConsumable; break;
                        case "SUBSCRIPTION": itemType = ProductType.Subscription; break;
                    }
                    
                    // There's more receipt data we can use for subscriptions if needed, see https://developer.amazon.com/docs/in-app-purchasing/iap-rvs-examples.html

                    receipts = new IPurchaseReceipt[1] { new AmazonAppsReceipt(receiptPayload.receiptJson.sku, receiptPayload.receiptId, purchaseDate, receiptPayload.userId, receiptPayload.isSandbox, itemType) };
                    return true;
                } else {
                    IAB.OnPurchaseFailed("Invalid amazon purchase receipt - Failed to decode receipt.");
                    FirebaseAnalyticsManager.LogEvent("iab_purchase_error", "error", "UnifiedReceipt failed");
                    Debug.LogError("Purchase failed! UnifiedReceipt decode failed: " + productReceipt);
                }
                break;
        }

        receipts = new IPurchaseReceipt[0];
        return false;
    }
    
    // For cases when we really want something removed from the google inventory, the old OpenIAB way
    public void ConsumeItem(string productId) {
        // Get the item from our local inventory
        IABItem localItem = IAB.GetItemByProductId(productId);

        if (localItem != null) {
            // Get the item from the store inventory
            Product product = controller?.products?.WithID(productId);

            // Make sure this product has a receipt otherwise we don't own it
            if (product != null && product.hasReceipt) {
                // Force consume the purchase 
                controller.ConsumePurchase(product);
            }

            // Change the local inventory purchase state of the item
            localItem.purchaseState = PurchaseState.NotOwned;
        } else {
            Debug.LogError("Failed to consume! Invalid localItem for productId: " + productId);
        }
    }
    
    private void EndPendingTransaction(Product product) {
        IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);
        
        if (localItem != null) {
            // For consumable items this will consume the item from the store inventory, non-consumable items will just have their pending transactions ended
            if (CrossPlatformManager.GetActiveStore() == AppStore.GooglePlay) {
#if UNITY_ANDROID && UNITY_PURCHASING && !UNITY_EDITOR
                googleExtensions?.FinishTransaction(product.definition, product.transactionID);
#endif
            }

            // Tell the store that we've acknowledged the purchase (if not acknowledged for 3 days on Google Play then the purchase will be refunded)
            controller.ConfirmPendingPurchase(product);

            // Consumable items are marked NotOwned again once the transaction ends
            // Non-consumables/subscriptions are marked Owned once the transaction ends
            localItem.purchaseState = localItem.type == ProductType.Consumable ? PurchaseState.NotOwned : PurchaseState.Owned;
        } else {
            Debug.LogError("Failed to consume! Invalid localItem for SKU: " + product.definition.storeSpecificId);
        }
    }
    
    public void RestorePurchases() {
        switch (CrossPlatformManager.GetActiveStore()) {
            case AppStore.MacAppStore:
            case AppStore.AppleAppStore:
                appleExtensions?.RestoreTransactions((result, error) => {
                    if (result) {
                        // OnPurchaseSuccess will be called for each item restored as if its the first purchase
                        IAB.OnPurchaseRestoreComplete();
                    } else {
                        IAB.OnPurchaseRestoreFailed();
                    }
                });
                break;

            case AppStore.WinRT:
                microsoftExtensions?.RestoreTransactions();

                // WINRT has no callback so just assume it always completed successfully
                IAB.OnPurchaseRestoreComplete();
                break;
            
            default:
                controller.FetchAdditionalProducts(productDefinitions, RestorePurchasesReload, IAB.OnPurchaseRestoreFailed);
                break;
        }
    }

    private void RestorePurchasesReload() {
        OnInventoryReloaded();
        
        IAB.OnPurchaseRestoreComplete();
    }

    private void OnInventoryReloaded() {
        if (FirebaseManager.instance.debugMode)
            Debug.Log("IAB OnInventoryReloaded()");

        OnItemDataReady(controller.products);
    }
    
    private void OnItemDataReady(ProductCollection products) {
        if (FirebaseManager.instance.debugMode)
            Debug.Log("IAB Item data is ready!");

        AppStore activeAppStore = CrossPlatformManager.GetActiveStore();
        Dictionary<string, string> appleProductData = null;

        if (activeAppStore == AppStore.AppleAppStore || activeAppStore == AppStore.MacAppStore) {
            // This isn't just the intro price, it grabs all the subscription info
            appleProductData = appleExtensions?.GetProductDetails();
        }

        foreach (Product product in products.all) {
// This logging constantly in the editor was annoying
#if !UNITY_EDITOR
            if (FirebaseManager.instance.debugMode)
                Debug.Log("Loaded item from inventory: " + product.definition.storeSpecificId);
#endif

            // Find the item matching the SKU in the itemData list
            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            // If itemLocalData is null then this is either a purchase in a later version of the game than the current app version or the developer forgot to add it
            if (localItem != null) {
                localItem.priceValue = product.metadata.localizedPrice.ToString(); // e.g "12.99"
                localItem.formattedPrice = product.metadata.localizedPriceString; // e.g "$12.99"
                localItem.description = product.metadata.localizedDescription;
                localItem.title = product.metadata.localizedTitle;
                localItem.currencyCode = product.metadata.isoCurrencyCode;
                
                if (localItem.type == ProductType.Subscription && IAB.SubscriptionsSupported()) {
                    string introJSON = null;
                    
                    // Set the default values to just behave as if there's no free trial or intro price
                    localItem.subscriptionPeriod = "P1W";
                    localItem.freeTrialPeriod = (activeAppStore == AppStore.AmazonAppStore ? "P1W" : "");
                    localItem.introductoryFormattedPrice = "";
                    localItem.introductoryPricePeriod = "";
                    localItem.introductoryPriceCycles = 0;
                    
                    // Google Play uses the custom product.metadata.GetGoogleProductMetadata() function, other stores use the SubscriptionManager
                    if (activeAppStore != AppStore.GooglePlay && activeAppStore != AppStore.fake && product.hasReceipt) {
                        // iOS needs to pass some introJSON, on android we just pass null as the 2nd parameter
                        SubscriptionManager subscriptionManager = new SubscriptionManager(product, introJSON);
                        SubscriptionInfo subscriptionInfo = subscriptionManager.getSubscriptionInfo();

                        localItem.subscriptionPeriod = IAB.TimeSpanToISO8601String(subscriptionInfo.getSubscriptionPeriod());

                        // These values won't be set if the store subscription isn't setup to use them
                        localItem.freeTrialPeriod = IAB.TimeSpanToISO8601String(subscriptionInfo.getFreeTrialPeriod());
                        localItem.introductoryFormattedPrice = subscriptionInfo.getIntroductoryPrice();

                        localItem.introductoryPricePeriod = IAB.TimeSpanToISO8601String(subscriptionInfo.getIntroductoryPricePeriod());
                        localItem.introductoryPriceCycles = Mathf.RoundToInt(subscriptionInfo.getIntroductoryPricePeriodCycles());
                    }
                    
                    // If a receipt is available then read the data from that
                    if (product.receipt != null) {
                        if (IAB.StoreSupportsLocalReceipts() && GetStoreReceipts(product.receipt, out IPurchaseReceipt[] receipts)) {
                            // Some stores return all owned receipts and others return just the receipt for the purchase, because of this we need to check through them to compare transactionID
                            foreach (IPurchaseReceipt receipt in receipts) {
                                if (FirebaseManager.instance.debugMode) {
                                    Debug.Log("IAB searching receipts - Receipt Transaction ID: " + receipt.transactionID + " / Product Transaction ID: " + product.transactionID);
                                    Debug.Log("IAB additional receipt info: Product ID: " + receipt.productID + ", Purchase Date: " + receipt.purchaseDate);
                                }

																					 
                                localItem.lastPurchaseReceipt = receipt;
								 
                            }
                        }
                    } else {
                        switch (activeAppStore) {
                            case AppStore.GooglePlay:
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
                            
                            case AppStore.MacAppStore:
                            case AppStore.AppleAppStore:
                                if (appleProductData != null && appleProductData.ContainsKey(product.definition.storeSpecificId)){ 
                                    introJSON = appleProductData[product.definition.storeSpecificId];
                                    
                                    JSONNode introJSONNode = JSONNode.Parse(introJSON);
                                    // Example introJSON value:
                                    // {"subscriptionNumberOfUnits":"7","subscriptionPeriodUnit":"0","localizedPrice":"3.99","isoCurrencyCode":"GBP","localizedPriceString":"\u00a33.99","localizedTitle":"Special Ops Membership","localizedDescription":"Daily rewards & membership area access!","introductoryPrice":"0","introductoryPriceLocale":"GBP","introductoryPriceNumberOfPeriods":"1","numberOfUnits":"3","unit":"0"}

                                    if (introJSONNode != null)
                                    {
                                        // 1 week / 1,2,3,6 months / 1 year
                                        // Apple reports the 1 week subscription as 7 days, so we should do some conversion on basic periods to show proper units
                                        if(introJSONNode["subscriptionNumberOfUnits"].Value == "7" && introJSONNode["subscriptionPeriodUnit"].Value == "0"){
                                            localItem.subscriptionPeriod = "P1W";
                                        }
                                        else
                                        {
                                            string subPeriodUnit = "";

                                            switch (introJSONNode["subscriptionPeriodUnit"].Value)
                                            {
                                                case "0": subPeriodUnit = "D"; break; // day unit
                                                case "1": subPeriodUnit = "W"; break; // week unit
                                                case "2": subPeriodUnit = "M"; break; // Month unit
                                                case "3": subPeriodUnit = "Y"; break; // Year unit
                                            }
                                            
                                            localItem.subscriptionPeriod = "P" + introJSONNode["subscriptionNumberOfUnits"].Value + subPeriodUnit;
                                        }
                                        
                                        
                                        string periodUnit = "";

                                        switch (introJSONNode["unit"].Value)
                                        {
                                            case "0": periodUnit = "D"; break; // day unit
                                            case "1": periodUnit = "W"; break; // week unit
                                            case "2": periodUnit = "M"; break; // Month unit
                                            case "3": periodUnit = "Y"; break; // Year unit
                                        }
                                        
                                        // Does the intro price look like a free trial (aka free for the first billing period only)
                                        if (introJSONNode["introductoryPrice"].Value == "0" && introJSONNode["introductoryPriceNumberOfPeriods"].Value == "1")
                                        {
                                            // This is a free trial (iOS doesn't allow intro prices AND free trials together, their free trials are just intro prices set to FREE)
                                            localItem.freeTrialPeriod = "P" + introJSONNode["numberOfUnits"].Value + periodUnit;
                                        }
                                        else
                                        {
                                            localItem.introductoryFormattedPrice = introJSONNode["introductoryPrice"].Value; // The currency symbol is added to this later in code
                                            localItem.introductoryPricePeriod = "P" + introJSONNode["numberOfUnits"].Value + periodUnit;
                                            localItem.introductoryPriceCycles = int.TryParse(introJSONNode["introductoryPriceNumberOfPeriods"].Value, out int introPriceCycles) ? introPriceCycles : 0;
                                        }
                                    }
                                }
                                else
                                {
                                    Debug.LogError("Failed to get subscription product data for " + product.definition.storeSpecificId);
                                }
                                break;
                            
                            case AppStore.fake:
                                // Set the values to some dummy values for the developer to use for testing
                                localItem.subscriptionPeriod = "P1W";
                                localItem.freeTrialPeriod = "P2D";
                                localItem.introductoryFormattedPrice = "$1.23";
                                localItem.introductoryPricePeriod = "P1W";
                                localItem.introductoryPriceCycles = 2;
                                break;

                            // All other android stores shouldn't need to do anything extra, the SubscriptionManager should've handled everything
                        }
                    }

                    // Try convert the intro price using the store iso currency code, if that fails fallback to local device culture
                    if (!float.TryParse(localItem.introductoryFormattedPrice, NumberStyles.Currency, IAB.GetCultureForISOCurrency(localItem.currencyCode), out float introPriceVal))
                        float.TryParse(localItem.introductoryFormattedPrice, NumberStyles.Currency, CultureInfo.CurrentCulture, out introPriceVal);
                    
                    localItem.introductoryPriceValue = introPriceVal.ToString();
                }
                
                // If a consumable item is still in the inventory when loaded then purchase verification must have failed when trying to consume the item
                // Re-attempt to verify this purchase then we can consume it if successful
                if (product.hasReceipt && !localItem.isProcessingValidation) {
                    // Default to owned (iOS this is currently the only supported state)
                    ItemInventoryState itemInventoryState = ItemInventoryState.Purchased;
                    
                    if (localItem.lastPurchaseReceipt != null) {
                        switch (CrossPlatformManager.GetActiveStore()) {
                            case AppStore.GooglePlay:
                                GooglePlayReceipt googleReceipt = (GooglePlayReceipt) localItem.lastPurchaseReceipt;

                                switch (googleReceipt.purchaseState) {
                                    case GooglePurchaseState.Cancelled:
                                        itemInventoryState = ItemInventoryState.Cancelled;
                                        break;
                                    case GooglePurchaseState.Purchased:
                                        itemInventoryState = ItemInventoryState.Purchased;
                                        break;
                                    case GooglePurchaseState.Refunded:
                                        itemInventoryState = ItemInventoryState.Refunded;
                                        break;
                                    default:
                                        itemInventoryState = ItemInventoryState.Pending;
                                        break;
                                }
                                break;
                            
                            case AppStore.AmazonAppStore:
                                // Receipt has no purchase state https://i.imgur.com/Fo1VF4D.png
                                break;
                            
                            case AppStore.AppleAppStore:
                            case AppStore.MacAppStore:
                                // iOS doesn't give a purchase state, if a receipt is in the inventory then it is purchased
                                break;
                        }
                    } else {
                        Debug.LogError("No existing receipt found for " + product.definition.storeSpecificId + " this purchase may still be pending..");
                        itemInventoryState = ItemInventoryState.Pending;
                    }
                    
                    // This is an item the player owns
                    switch (localItem.type) {
                        case ProductType.Consumable:
                            if (itemInventoryState == ItemInventoryState.Purchased) {
                                // This item should have been consumed, attempt to consume it again
                                // Call the purchase complete function again for this owned consumable item to re-attempt serverside validation & consume this item

                                // If a Consumable item has a receipt it was purchased atleast once this session so we can't rely on this for purchase status
                                // So to double check if a consumable purchase is active the PurchaseState will be Pending
                                if (localItem.purchaseState == PurchaseState.Pending)
                                    OnPurchaseComplete(product);
                            }
                            break;

                        case ProductType.NonConsumable:
                            if (itemInventoryState == ItemInventoryState.Refunded) {
                                // This purchase was refunded! We'll trigger the refunded callback then consume the purchase to clear it from the inventory
                                OnItemRefunded(product);
                            } else if(itemInventoryState == ItemInventoryState.Purchased) {
                                // If a NonConsumable item has a receipt and is marked as NotOwned then call OnOwnedItemLoaded as it's a newly purchased item
                                IAB.OnOwnedItemLoad(localItem);
                            }
                            break;

                        case ProductType.Subscription:
                            if (itemInventoryState == ItemInventoryState.Purchased) {
                                // If a Subscription item has a receipt and is marked as NotOwned then call OnOwnedItemLoaded as it's a newly purchased subscription
                                IAB.OnOwnedItemLoad(localItem);
                            }
                            break;
                    }
                }
            }
        }

        IAB.itemDataReady = true;
        
        IAB.OnInventoryReady();
    }
    
    private void OnItemRefunded(Product product) {
        Debug.Log("Purchase refunded [Item: " + product.definition.storeSpecificId + "]");

        IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

        localItem.purchaseState = PurchaseState.NotOwned;
        localItem.isProcessingValidation = false;

        // Consume the item to force remove it from the inventory
        ConsumeItem(product.definition.storeSpecificId);

        IAB.OnPurchaseRefunded(localItem);

        FirebaseAnalyticsManager.LogEvent("iab_purchase_refund", "id", product.definition.storeSpecificId);
    }
    
    public void PurchaseItem(string productId, bool skipInitialisingCheck) {
        IAB.isPurchaseActive = true;

        IABItem localItem = IAB.GetItemByProductId(productId);

        // Check if the item is setup within the app (internet connection not required)
        if (localItem != null) {
            // Store a reference to the last product we attempted to purchase incase it's needed later in the purchase process
            IAB.lastPurchaseAttemptProductId = Application.isEditor ? productId : localItem.productId;

            if (controller != null) {
                if(controller.products != null){
                    Product product = controller.products.WithID(productId);

                    if (product != null && product.availableToPurchase) {
                        if (product.definition.type == ProductType.Subscription) {
                            if (IAB.SubscriptionsSupported()) {
                                try {
                                    // Purchase the subscription
                                    controller.InitiatePurchase(product);
                                } catch (InvalidOperationException e) {
                                    // Failed because the store disconnected and broke
                                    OnPurchaseFailed(product, PurchaseFailureReason.PurchasingUnavailable);

                                    Debug.LogError("Purchase Failed: " + e.Message);
                                } catch (Exception e) {
                                    // Unknown exception
                                    OnPurchaseFailed(product, PurchaseFailureReason.Unknown);

                                    Debug.LogError("Purchase Failed: " + e.Message);
#if UNITY_EDITOR
                                    Debug.LogError("Subscriptions are not yet supported in the editor by Unity IAP");
#endif
                                }
                            } else {
                                switch (CrossPlatformManager.GetActiveStore()) {
                                    case AppStore.GooglePlay:
                                    case AppStore.AmazonAppStore:
                                        IAB.OnPurchaseFailed("Subscriptions are not currently supported on this version of android!");
                                        break;
                                    
                                    case AppStore.AppleAppStore:
                                    case AppStore.MacAppStore:
                                        IAB.OnPurchaseFailed("Subscriptions are not currently supported on this version of iOS!");
                                        break;
                                    
                                    default:
                                        IAB.OnPurchaseFailed("Subscriptions are not currently supported on your device!");
                                        break;
                                }
                            }
                        } else {
                            try {
                                // Purchase the item
                                controller.InitiatePurchase(product);
                            } catch (InvalidOperationException e) {
                                // Failed because the store disconnected and broke
                                OnPurchaseFailed(product, PurchaseFailureReason.PurchasingUnavailable);

                                Debug.LogError("Purchase Failed: " + e.Message);
                            } catch (Exception e) {
                                // Unknown exception
                                OnPurchaseFailed(product, PurchaseFailureReason.Unknown);

                                Debug.LogError("Purchase Failed: " + e.Message);
                            }
                        }
                    } else {
                        OnPurchaseFailed(product, PurchaseFailureReason.ProductUnavailable);
                    }
                } else {
                    OnPurchaseFailed(null, PurchaseFailureReason.ProductUnavailable);
                }
            } else {
                Debug.LogError("Product list was null when trying to purchase!");
                
                OnPurchaseFailed(null, PurchaseFailureReason.ProductUnavailable);
            }
        } else {
            Debug.LogError("Controller was null when trying to purchase!");
            
            OnPurchaseFailed(null, PurchaseFailureReason.ProductUnavailable);
        }
    }
    
    public void OnPurchaseComplete(Product product) {
        OnPurchaseComplete(product, false);
    }
    
    public void ClearTransactionLog() {
        UnityPurchasing.ClearTransactionLog();
    }
    
    private IEnumerator WaitForInitialisationThenResumePurchaseComplete(Product product) {
        float waitTime = 0f;

        // If billing isn't initialising and billing isn't supported then the previous initialisation attempt failed - try again
        if (!IAB.isInitializing)
            InitializePurchasing();

        yield return null;

        // Wait up to 5 seconds for billing to finish initialising (ends early if initilisation finished, success or failure)
        while (waitTime < 5f && IAB.isInitializing) {
            yield return null;

            waitTime += Time.unscaledDeltaTime;
        }

        OnPurchaseComplete(product, true);
    }
    
    private void OnPurchaseComplete(Product product, bool skipInitialisingCheck) {
        if (!skipInitialisingCheck && IAB.isInitializing) {
            IAB.StartCoroutine(WaitForInitialisationThenResumePurchaseComplete(product));

            return;
        }

        if(FirebaseManager.instance.debugMode)
            Debug.Log("Purchase ready to be processed [Item: " + product.definition.storeSpecificId + "]");

        // If the product receipt is blank then something went wrong, fail the purchase
        if (string.IsNullOrEmpty(product.receipt)) {
            Debug.Log("Blank product.receipt, purchase failed");
            OnPurchaseFailed(product, PurchaseFailureReason.Unknown);
            return;
        }

        if (product.definition.type == ProductType.Consumable) {
            IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

            if (localItem != null) {
                localItem.purchaseState = PurchaseState.Pending;
                localItem.isProcessingValidation = true;
            }
        }

        // Something's happening, refresh the timer for the hold on screen to show the skip button
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ResetTimer();
        
        if(IAB.StoreSupportsLocalReceipts()){
            // LOCAL purchase validation is only supported on Google Play and Apple App Stores, we skip this step on other stores
            // Returns true if the local receipt looks valid, false if it either looks invalid or the developer has not setup the tangle correctly
            if (GetStoreReceipts(product.receipt, out IPurchaseReceipt[] receipts)) {
                // For some reason this returns all current receipts even though we just made 1 purchase
                foreach (IPurchaseReceipt receipt in receipts) {
                    if (FirebaseManager.instance.debugMode) {
                        Debug.Log("IAB searching receipts - Receipt Transaction ID: " + receipt.transactionID + " / Product Transaction ID: " + product.transactionID);
                        Debug.Log("IAB additional receipt info: Product ID: " + receipt.productID + ", Purchase Date: " + receipt.purchaseDate);
                    }

                    if (!string.IsNullOrEmpty(receipt.transactionID) && !string.IsNullOrEmpty(product.transactionID)) {
                        switch (CrossPlatformManager.GetActiveStore()) {
                            case AppStore.GooglePlay:
                                GooglePlayReceipt googleReceipt = receipt as GooglePlayReceipt;

                                if (googleReceipt != null)
                                {
                                    switch (googleReceipt.purchaseState)
                                    {
                                        case GooglePurchaseState.Purchased:
                                            SendPurchaseVerificationRequest(product, googleReceipt);
                                            break;

                                        // This will never happen, the purchase state on Google Play is always Purchased for some reason
                                        // Need to actually query the list of refunds and store purchased transaction ids
                                        case GooglePurchaseState.Refunded:
                                            OnItemRefunded(product);
                                            break;
                                    }
                                }
                                break;

                            case AppStore.AppleAppStore:
                            case AppStore.MacAppStore:
                                AppleInAppPurchaseReceipt appleReceipt = receipt as AppleInAppPurchaseReceipt;

                                if (appleReceipt != null)
                                    SendPurchaseVerificationRequest(product, appleReceipt);
                                break;
                            
                            case AppStore.AmazonAppStore:
                                AmazonAppsReceipt amazonAppsReceipt = receipt as AmazonAppsReceipt;
                                
                                if(amazonAppsReceipt != null)
                                    SendPurchaseVerificationRequest(product, amazonAppsReceipt);
                                break;
                        }

                        // Return once we hit the target transactionID
                        return;
                    }
                }
                
                if (FirebaseManager.instance.debugMode) {
                    Debug.Log("Purchase didn't match existing transactions, this looks like a promo code purchase");
                    Debug.Log("Raw Receipt: " + product.receipt);
                }
                
                // All receipts have been searched but none matched the transaction id of the purchase, this either means:
                // 1: This is a promotional purchase (these don't have transaction ids)
                // 2: This is a fake purchase (doubtful though as purchase spoofing apps generally also manipulate the receipts list too)
                // 3: This is a store returning a purchase success when it actually failed (amazon does this when an item is already owned, it's a successful purchase response with blank receipt data)
                switch (CrossPlatformManager.GetActiveStore()) {
                    case AppStore.GooglePlay:
                    case AppStore.AppleAppStore:
                    case AppStore.MacAppStore:
                        CheckForPromotionalPurchase(product);
                        break;

                    default:
                        // On amazon this is probably item already owned.. however we can't know for sure so just return an unknown failure reason
                        OnPurchaseFailed(product, PurchaseFailureReason.Unknown);
                        break;
                }
            }
        } else {
            // If we're on a store which doesn't support receipts or validation hasn't been setup, just fallback to skipping purchase verification
            OnPurchaseVerificationCompleted(product);
        }
    }
    
    private void CheckForPromotionalPurchase(Product product) {
        if (product.hasReceipt && !string.IsNullOrEmpty(product.receipt)) {
            // https://docs.unity3d.com/Manual/UnityIAPPurchaseReceipts.html
            UnifiedReceipt decodedReceipt = JsonUtility.FromJson<UnifiedReceipt>(product.receipt);

            if (decodedReceipt != null && !string.IsNullOrEmpty(decodedReceipt.Payload)) {
                JSONNode receiptPayload = JSONNode.Parse(decodedReceipt.Payload);

                if (receiptPayload != null) {
                    switch (CrossPlatformManager.GetActiveStore()) {
                        case AppStore.GooglePlay:
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

                                        // If we get to this point then this seems to be a promo code purchase
                                        GooglePlayReceipt promoReceipt = new GooglePlayReceipt(product.definition.storeSpecificId, product.transactionID, Application.identifier, purchaseToken, purchaseDateTime, GooglePurchaseState.Purchased);

                                        // Try validate something, if it fails then it'll fall back to purchase validation failed
                                        SendPurchaseVerificationRequest(product, promoReceipt);

                                        return;
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
                        
                        case AppStore.AppleAppStore:
                        case AppStore.MacAppStore:
                            // iOS sets the transaction id as normal for promotional offers so promo offers should never get here
                            break;
                    }
                } else {
                    Debug.LogError("Purchase failed! Failed to parse receipt payload");
                }
            } else {
                Debug.LogError("Purchase failed! Failed to decode UnifiedReceipt");
            }
        }
    }
    
    private void OnIOSPromoPurchase(Product product) {
        Debug.Log("Promo Purchase [Item: " + product.definition.storeSpecificId + "]");

        IAB.OnPromoPurchaseComplete(IAB.GetItemBySKU(product.definition.storeSpecificId));
        
        // Continue the normal purchase flow
        appleExtensions?.ContinuePromotionalPurchases();
    }
    
    private void SendPurchaseVerificationRequest(Product product, GooglePlayReceipt googleReceipt) {
        // Something's happening, refresh the timer for the hold on screen to show the skip button
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ResetTimer();

        IAB.StartCoroutine(DoPurchaseVerificationRequest(product, googleReceipt));
    }
    
    private void SendPurchaseVerificationRequest(Product product, AppleInAppPurchaseReceipt appleReceipt, bool verifyWithSandbox = false) {
        // Something's happening, refresh the timer for the hold on screen to show the skip button
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ResetTimer();

        IAB.StartCoroutine(DoPurchaseVerificationRequest(product, appleReceipt, verifyWithSandbox));
    }

    private void SendPurchaseVerificationRequest(Product product, AmazonAppsReceipt amazonAppsReceipt, bool verifyWithSandbox = false) {
        // Something's happening, refresh the timer for the hold on screen to show the skip button
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ResetTimer();

        IAB.StartCoroutine(DoPurchaseVerificationRequest(product, amazonAppsReceipt, verifyWithSandbox));
    }

    private IEnumerator DoPurchaseVerificationRequest(Product product, GooglePlayReceipt googleReceipt) {
        IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

        if (localItem != null)
            localItem.lastPurchaseReceipt = googleReceipt;
        
        UnityWebRequest verificationRequest = new UnityWebRequest();

        try {
            List<IMultipartFormSection> verificationPostData = new List<IMultipartFormSection>();

            verificationPostData.Add(new MultipartFormDataSection("store_type", "android", Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("bundle", googleReceipt.packageName, Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("sku", googleReceipt.productID, Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("token", googleReceipt.purchaseToken, Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("item_type", product.definition.type == ProductType.Subscription ? "subs" : "inapp", Encoding.UTF8, "multipart/form-data"));

            verificationRequest = UnityWebRequest.Post("https://data.i6.com/google_iab_validation.php", verificationPostData);
        } catch (ArgumentException e) {
            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Invalid purchase validation request!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
            Debug.LogError("Purchase failed - missing parameter!");

            if (FirebaseManager.instance.debugMode) {
                Debug.LogError("Store type - android");
                Debug.LogError("Bundle - " + googleReceipt.packageName);
                Debug.LogError("SKU - " + googleReceipt.productID);
                Debug.LogError("Token - " + googleReceipt.purchaseToken);
                Debug.LogError("Item Type - " + (product.definition.type == ProductType.Subscription ? "subs" : "inapp"));
            } else {
                Debug.Log("Enable debug mode on the FirebaseManager for a more detailed output!");
            }

            yield break;
        }

        if (verificationRequest != null) {
            DownloadHandler verificationRequestDownloadHandler = verificationRequest.downloadHandler;

            // Wait for the request to complete
            yield return verificationRequest.SendWebRequest();

            if (verificationRequest.isHttpError || verificationRequest.isNetworkError || string.IsNullOrEmpty(verificationRequestDownloadHandler.text)) {
                IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Unable to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
                
                Debug.Log("Validation isHttpError: " + verificationRequest.isHttpError + " / isNetworkError: " + verificationRequest.isNetworkError);
                Debug.LogError("Validation response: " + verificationRequestDownloadHandler.text);
                
                yield break;
            }

            try {
                if (FirebaseManager.instance.debugMode)
                    Debug.Log(verificationRequestDownloadHandler.text);

                JSONNode verificationResponse = SimpleJSON.JSON.Parse(verificationRequestDownloadHandler.text);

                if (verificationResponse != null) {
                    // Note: Not comparing .Value to the strings results in a non match as the JSONNode children are JSONNodes, not strings
                    // Note2: developerPayload is being phased out, it's in the v3 API but Unity IAP doesn't support it and forum unity devs suggest not using it as it'll eventually be api removed
                    // if the server response had a valid matching orderId though we can assume it's legit and there isn't anything extra we could get from checking a developerPayload too
                    if (verificationResponse["orderId"].Value == googleReceipt.transactionID) {
                        OnPurchaseVerificationCompleted(product);
                    } else {
                        // Check if this purchase was made via alternative purchase methods e.g promo code, rewarded or test purchase
                        if (int.TryParse(verificationResponse["purchaseType"]?.Value, out int purchaseType)) {
                            OnPurchaseVerificationCompleted(product);
                        } else {
                            // Invalid purchase..
                            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Purchase validation failed!\nRestart the app or try the purchase again to retry (you won't be re-charged)");

                            Debug.LogError("Purchase failed - Invalid payload or order id!");

                            if (FirebaseManager.instance.debugMode) {
                                Debug.LogError("Verification transaction id: " + verificationResponse["orderId"]);
                                Debug.LogError("Client orderId: " + googleReceipt.orderID);

                                Debug.LogError("Store type - android");
                                Debug.LogError("Bundle - " + googleReceipt.packageName);
                                Debug.LogError("SKU - " + googleReceipt.productID);
                                Debug.LogError("Token - " + googleReceipt.purchaseToken);
                                Debug.LogError("Item Type - " + (product.definition.type == ProductType.Subscription ? "subs" : "inapp"));
                            } else {
                                Debug.Log("Enable debug mode on the FirebaseManager for a more detailed output!");
                            }
                        }
                    }
                } else {
                    IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Blank response from Google's purchase validation service!");
                    Debug.LogError("Purchase failed - Null SimpleJSON parse");
                }
            } catch (ArgumentException e) {
                IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Exception while parsing response from Google's purchase validation service!");

                if (e is IndexOutOfRangeException || e is ArgumentOutOfRangeException) {
                    Debug.LogError("Purchase failed - developerPayload and/or orderId missing from the verification response!");
                } else {
                    Debug.LogError("Purchase failed - " + e.Message);
                }
            }
        } else {
            IAB.OnPurchaseVerificationFailed(googleReceipt.productID, "Failed to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
        }
    }

    private IEnumerator DoPurchaseVerificationRequest(Product product, AmazonAppsReceipt amazonAppsReceipt, bool verifyWithSandbox = false) {
        IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

        if (localItem != null)
            localItem.lastPurchaseReceipt = amazonAppsReceipt;
        
        UnityWebRequest verificationRequest = new UnityWebRequest();

        try {
            List<IMultipartFormSection> verificationPostData = new List<IMultipartFormSection>();

            verificationPostData.Add(new MultipartFormDataSection("store_type", "amazon", Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("bundle", Application.identifier, Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("sku", amazonAppsReceipt.productID, Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("item_type", amazonAppsReceipt.itemType.ToString(), Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("user_id", amazonAppsReceipt.userId, Encoding.UTF8, "multipart/form-data"));
            verificationPostData.Add(new MultipartFormDataSection("receipt_id", amazonAppsReceipt.transactionID, Encoding.UTF8, "multipart/form-data"));

            verificationRequest = UnityWebRequest.Post("https://data.i6.com/google_iab_validation.php", verificationPostData);
        } catch (ArgumentException e) {
            IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Invalid purchase validation request!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
            Debug.LogError("Purchase failed - missing parameter!");

            if (FirebaseManager.instance.debugMode) {
                Debug.LogError("Store type - amazon");
                Debug.LogError("Bundle - " + Application.identifier);
                Debug.LogError("SKU - " + amazonAppsReceipt.productID);
                Debug.LogError("Item Type - " + amazonAppsReceipt.itemType.ToString());
                Debug.LogError("User ID - " + amazonAppsReceipt.userId);
                Debug.LogError("Receipt ID- " + amazonAppsReceipt.transactionID);
            } else {
                Debug.Log("Enable debug mode on the FirebaseManager for more detailed output!");
            }

            yield break;
        }

        if (verificationRequest != null) {
            DownloadHandler verificationRequestDownloadHandler = verificationRequest.downloadHandler;
            
            // Wait for the request to complete
            yield return verificationRequest.SendWebRequest();

            if (verificationRequest.isHttpError || verificationRequest.isNetworkError || string.IsNullOrEmpty(verificationRequestDownloadHandler.text)) {
                IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Unable to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
                
                Debug.Log("Validation isHttpError: " + verificationRequest.isHttpError + " / isNetworkError: " + verificationRequest.isNetworkError);
                Debug.LogError("Validation response: " + verificationRequestDownloadHandler.text);
                yield break;
            }

            try {
                if (FirebaseManager.instance.debugMode)
                    Debug.Log(verificationRequestDownloadHandler.text);

                JSONNode verificationResponse = SimpleJSON.JSON.Parse(verificationRequestDownloadHandler.text);

                if (verificationResponse != null) {
                    // Note: Not comparing .Value to the strings results in a non match as the JSONNode children are JSONNodes, not strings
                    // Note2: developerPayload is being phased out, it's in the v3 API but Unity IAP doesn't support it and forum unity devs suggest not using it as it'll eventually be api removed
                    // if the server response had a valid matching receiptId though we can assume it's legit and there isn't anything extra we could get from checking a developerPayload too
                    if (verificationResponse["receiptId"].Value == amazonAppsReceipt.transactionID) {
                        OnPurchaseVerificationCompleted(product);
                    } else {
                        // Invalid purchase
                        IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Purchase validation failed!\nRestart the app or try the purchase again to retry (you don't be re-charged)");
                        
                        Debug.LogError("Purchase failed - Invalid receipt id!");

                        if (FirebaseManager.instance.debugMode) {
                            Debug.LogError("Verification transaction id: " + verificationResponse["receiptId"]);
                            Debug.LogError("Client transaction id: " + amazonAppsReceipt.transactionID);
                            
                            Debug.LogError("Store type - amazon");
                            Debug.LogError("Bundle - " + Application.identifier);
                            Debug.LogError("SKU - " + amazonAppsReceipt.productID);
                            Debug.LogError("Item Type - " + amazonAppsReceipt.itemType.ToString());
                            Debug.LogError("User ID - " + amazonAppsReceipt.userId);
                            Debug.LogError("Receipt ID- " + amazonAppsReceipt.transactionID);
                        } else {
                            Debug.Log("Enable debug mode on the FirebaseManager for a more detailed output!");
                        }
                    }
                } else {
                    IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Blank response from Amazon's purchase validation service!");
                    Debug.LogError("Purchase failed - Null SimpleJSON parse");
                }
            } catch (ArgumentException e) {
                IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Exception while parsing response from Amazon's purchase validation service!");

                if (e is IndexOutOfRangeException || e is ArgumentOutOfRangeException) {
                    Debug.LogError("Purchase failed - developerPayload and/or orderId missing from the verification response!");
                } else {
                    Debug.LogError("Purchase failed - " + e.Message);
                }
            }
        } else {
            IAB.OnPurchaseVerificationFailed(amazonAppsReceipt.productID, "Failed to contact the purchase validation service!\nTry the purchase again or restart the app to retry (you won't be re-charged)");
        }
    }
    
    private IEnumerator DoPurchaseVerificationRequest(Product product, AppleInAppPurchaseReceipt appleReceipt, bool verifyWithSandbox = false) {
        IABItem localItem = IAB.GetItemBySKU(product.definition.storeSpecificId);

        if (localItem != null)
            localItem.lastPurchaseReceipt = appleReceipt;

        UnityWebRequest verificationRequest = new UnityWebRequest();
        
		try {
			List<IMultipartFormSection> verificationPostData = new List<IMultipartFormSection>();
			
			verificationPostData.Add(new MultipartFormDataSection("store_type", "ios", Encoding.UTF8, "multipart/form-data"));

            if(FirebaseManager.instance.debugMode)
                Debug.Log("product.receipt: " + product.receipt);
            
            UnifiedReceipt decodedReceipt = JsonUtility.FromJson<UnifiedReceipt>(product.receipt);
            
            if (decodedReceipt != null && !string.IsNullOrEmpty(decodedReceipt.Payload))
            {
                if(FirebaseManager.instance.debugMode)
                    Debug.Log("decodedReceipt.Payload: " + decodedReceipt.Payload);
                
                /*JSONNode receiptPayload = JSONNode.Parse(decodedReceipt.Payload);

                if (receiptPayload != null)
                {
                    // Payload is JSON format
                    if(FirebaseManager.instance.debugMode)
                        Debug.Log("Payload is JSON format");

                    try {
                        // iOS 7 and later the payload is a base64 encoded App Receipt and as we don't support below iOS 7 I'm only supported that payload type here
                        JSONNode iOSDecodedReceipt = JSONNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(receiptPayload)));
                        
                        if (iOSDecodedReceipt != null)
                        {
                            string iOSReceipt = iOSDecodedReceipt.Value;

                            if (!string.IsNullOrEmpty(iOSReceipt))
                                verificationPostData.Add(new MultipartFormDataSection("receipt", iOSReceipt, Encoding.UTF8, "multipart/form-data"));
                        } else {
                            //IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Decoded iOS receipt was invalid!");
                            Debug.LogError("Null iOSDecodedPayload parse");
                        }
                    } catch(Exception e){
                        // Fallback to ASN.1 format
                        if (FirebaseManager.instance.debugMode)
                            Debug.Log("Falling back to ASN.1 format due to JSON parse exception - " + e.Message);

                        verificationPostData.Add(new MultipartFormDataSection("receipt", decodedReceipt.Payload, Encoding.UTF8, "multipart/form-data"));
                    }
                }
                else
                {*/
                    // Payload is ASN.1 format
                    if(FirebaseManager.instance.debugMode)
                        Debug.Log("Payload is ASN.1 format");

                    verificationPostData.Add(new MultipartFormDataSection("receipt", decodedReceipt.Payload, Encoding.UTF8, "multipart/form-data"));
                //}

                // Currently unused on iOS for validation, but no harm sending the data for future proofing :)
                verificationPostData.Add(new MultipartFormDataSection("item_type", product.definition.type == ProductType.Subscription ? "subs" : "inapp", Encoding.UTF8, "multipart/form-data"));

                // The shared secret is only required when verifying subscriptions, so throw an error if it's not set and this is a subscription
                if (!string.IsNullOrEmpty(IAB.iosSharedSecret)) {
                    verificationPostData.Add(new MultipartFormDataSection("password", IAB.iosSharedSecret, Encoding.UTF8, "multipart/form-data"));
                } else if (product.definition.type == ProductType.Subscription) {
                    Debug.LogError("iOS shared secret must be set when verifying subscriptions!");
                }

                verificationPostData.Add(new MultipartFormDataSection("exclude_old_transactions", "false", Encoding.UTF8, "multipart/form-data"));
                verificationPostData.Add(new MultipartFormDataSection("use_sandbox", verifyWithSandbox ? "1" : "0", Encoding.UTF8, "multipart/form-data"));

                verificationRequest = UnityWebRequest.Post("https://data.i6.com/google_iab_validation.php", verificationPostData);
            } else {
                IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Failed to parse receipt!");
                Debug.LogError("Purchase failed - Null UnifiedReceipt parse");
            }
        } catch (ArgumentException) {
            IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Invalid purchase validation request! Try the purchase again or restart the app to retry (you won't be re-charged)");
            yield break;
        }

		if (verificationRequest != null) {
			DownloadHandler verificationRequestDownloadHandler = verificationRequest.downloadHandler;
				
			// Wait for the request to complete
			yield return verificationRequest.SendWebRequest();

			if (verificationRequest.isHttpError || verificationRequest.isNetworkError || string.IsNullOrEmpty(verificationRequestDownloadHandler.text)) {
				IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Unable to contact the purchase validation service! Try the purchase again or restart the app to retry (you won't be re-charged)");
                
                Debug.Log("Validation isHttpError: " + verificationRequest.isHttpError + " / isNetworkError: " + verificationRequest.isNetworkError);
                Debug.LogError("Validation response: " + verificationRequestDownloadHandler.text);
				yield break;
			}

            try {
                JSONNode verificationResponse = SimpleJSON.JSON.Parse(verificationRequestDownloadHandler.text);

                if (verificationResponse != null) {
                        if(verificationResponse["status"] != null){
                        if (int.TryParse(verificationResponse["status"].Value, out int responseStatus)) {
                            switch (responseStatus) {
                                case 0: // Success
                                    Debug.Log("Raw response: " + verificationRequestDownloadHandler.text);
                                    Debug.Log("Verification response: " + verificationResponse.ToString());
                                    Debug.Log("Last receipt info: " + verificationResponse["latest_receipt_info"].ToString());
                                    
                                    JSONNode latestReceiptInfo = verificationResponse.HasKey("latest_receipt_info") ? SimpleJSON.JSON.Parse(verificationResponse["latest_receipt_info"].ToString())[0] : null;

                                    if (latestReceiptInfo != null) {
                                        // Unity doesn't handle getting the original transaction id properly so we need to do it ourselves.. (unity returns the normal transaction id sometimes when it shouldn't)
                                        string originalTransactionId = latestReceiptInfo.HasKey("original_transaction_id") ? latestReceiptInfo["original_transaction_id"].Value : "";
                                        
                                        Debug.Log("originalTransactionId: " + originalTransactionId);
                                        
                                        OnPurchaseVerificationCompleted(product, originalTransactionId);
                                    } else
                                    {
                                        Debug.Log("latestReceiptInfo was null");
                                        OnPurchaseVerificationCompleted(product);
                                    }
                                    break;

                                case 21004: // Shared secret key was invalid
                                    IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Shared secret key was invalid!");
                                    break;

                                case 21007: // Receipt from the sandbox test environment being used in the production environment
                                    if (verifyWithSandbox) {
                                        IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Purchase made in the sandbox environment but iosVerifyWithSandbox was not enabled!");
                                    } else {
                                        Debug.Log("Sandbox receipt detected! Re-running verification via the sandbox verification server!");

                                        // Looks like the app is running in a sandbox environment, re-run the verification with the sandbox verification server
                                        SendPurchaseVerificationRequest(product, appleReceipt, true);
                                    }
                                    break;

                                case 21008: // Receipt from the production environment being used in the sandbox test environment
                                    IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Purchase made in the production environment but iosVerifyWithSandbox was enabled!");
                                    break;

                                default: // Other invalid responses which we don't need to log specific messages about
                                    IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Apple's receipt validation service returned status code " + responseStatus + "!");
                                    break;
                            }
                        } else {
                            IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Invalid response from Apple's purchase validation service!");
                            Debug.LogError("Purchase failed - Could not parse status as int");
                        }
                    } else {
                        IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Missing purchase status from Apple's purchase validation service!");
                        Debug.LogError("Purchase failed - JSON response missing status");
                    }
                } else {
                    IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Blank response from Apple's purchase validation service!");
                    Debug.LogError("Purchase failed - Null SimpleJSON parse");
                }
            }
            catch (IndexOutOfRangeException e)
            {
                IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Exception while parsing response from Apple's purchase validation service!");
                Debug.LogError("Purchase failed - " + e.Message);
            } catch (ArgumentOutOfRangeException e) {
                IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Exception while parsing response from Apple's purchase validation service!");
                Debug.LogError("Purchase failed - " + e.Message);
            }
		} else {
			IAB.OnPurchaseVerificationFailed(appleReceipt.productID, "Unable to contact Apple's receipt validation service! Try the purchase again or restart the app to retry (you won't be re-charged)");
		}
    }

    private void OnPurchaseVerificationCompleted(Product product, string originalTransactionId = "") {
        IAB.OnPurchaseVerificationCompleted(IAB.GetItemBySKU(product.definition.storeSpecificId), originalTransactionId);
        
        // For consumable items this will consume the purchase, everything else it just finalises the transaction
        EndPendingTransaction(product);
    }
}