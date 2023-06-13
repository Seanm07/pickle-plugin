using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Xml;
using SimpleJSON;
using UnityEngine.Networking;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Security;
using UnityEngine.UDP;

#if UNITY_IOS
    using UnityEngine.iOS;
#endif

[Serializable]
public class IABItemStoreInfo {
    public AppStore store;
    public string sku;
}

[Serializable]
public class IABItem {
    public string productId;
    public ProductType type;

    public List<IABItemStoreInfo> storeSKUs = new List<IABItemStoreInfo>();

    public string title { get; set; } // Item title (up to 55 characters, can be localized)
    public string description { get; set; } // Item description (up to 80 characters, can be localized)
    public string priceValue { get; set; } // Price excluding currency symbol e.g 1.23
    public string formattedPrice { get; set; } // Includes currency symbol e.g £1.23 or $1.23
    public string currencyCode { get; set; } // ISO currency code e.g GBP, EUR, DZD, AMD.. ETC

    public string subscriptionPeriod { get; set; } // Billing period of the subscription in ISO-8601 format can be P1W, P1M, P3M, P6M or P1Y
    public string freeTrialPeriod { get; set; } // Free trial period of the subscription in ISO-8601 format e.g P1D, P2D, P3D, P4D, P5D, P6D, P7D.. ETC

    // Note: The introductory price is a one time first purchase offer on subscriptions, when that ends the user will be charged the normal priceValue/formattedPrice value
    public string introductoryFormattedPrice { get; set; } // Introductory subscription price with currency symbol included e.g £1.23, $1.23, €1.23.. ETC
    public string introductoryPriceValue { get; set; } // Introductory subscription price excluding currency symbol e.g 1.23

    // Price period is used if the subscription has its introductory price set to a period of time rather than a billing cycle, otherwise price cycles is used
    public string introductoryPricePeriod { get; set; } // Introductory price period in ISO-8601 format e.g P1D, P2D, P1W, P2W, P1M, P2M.. ETC
    public int introductoryPriceCycles { get; set; } // Introductory price cycles e.g 1, 2, 3 (Multiply this by the subscriptionPeriod to find length of time the introductory period lasts)

    public PurchaseState purchaseState { get; set; }
    public bool isProcessingValidation { get; set; }

    public IPurchaseReceipt lastPurchaseReceipt { get; set; }

    public bool isProductId(string inProductId) {
        return (inProductId == productId);
    }
}

// Receipt used for other android stores (not used for Google Play, see GooglePlayReceipt)
[Serializable]
public class AmazonAppsReceipt : IPurchaseReceipt {
    public string productID { get; private set; }
    public string transactionID { get; private set; }
    public DateTime purchaseDate { get; private set; }
    
    public string userId { get; private set; }
    public bool isSandbox { get; private set; }
    public ProductType itemType { get; private set; }

    public AmazonAppsReceipt(string productID, string transactionID, DateTime purchaseDate, string userId, bool isSandbox, ProductType itemType) {
        this.productID = productID;
        this.transactionID = transactionID;
        this.purchaseDate = purchaseDate;
        this.userId = userId;
        this.isSandbox = isSandbox;
        this.itemType = itemType;
    }
}

// Consumable items switch between NotOwned when unowned and Pending when it's in limbo between purchase and being consumed then when consumed its set back to NotOwned
// Non-consumable items switch between NotOwned when unowned, Pending when it's in limbo between purchase and finalisation then when finalised the status becomes Owned
public enum PurchaseState { NotOwned, Owned, Pending }

public class IABManager : MonoBehaviour {
    public static IABManager instance;
    
    public List<IABItem> itemList = new List<IABItem>();

    [Header("iOS Only (Only required in apps using subscriptions)")]
    public string iosSharedSecret = "";

    private bool isCachedDataReady = false;

    public bool isInitializing { get; set; }
    public bool isInitialized { get; set; }
    public bool itemDataReady { get; set; }
    public bool isPurchaseActive { get; set; }

    public string lastPurchaseAttemptProductId { get; set; }

    public static event Action OnIABInventoryReady;
    public static event Action OnIABPricesUpdated;
    public static event Action<IABItem> OnOwnedItemLoaded;

    public static event Action<IABItem, string> OnIABPurchaseComplete;
    public static event Action<IABItem> OnIABPurchaseDeferred; // A deferred purchase means the user selected to purchase something but selected to pay later such as at a shop, the play store will give the user more info
    public static event Action<string> OnIABPurchaseFailed;
    public static event Action<IABItem> OnIABPurchaseRefunded;

    public static event Action<IABItem> OnIOSPromoPurchaseComplete;

    // Legacy restore callback events, to be removed in future
    [Obsolete("Use OnRestoreComplete instead")] public static event Action OnIOSRestoreComplete;
    [Obsolete("Use OnRestoreFailed instead")] public static event Action OnIOSRestoreFailed;
    
    public static event Action OnRestoreComplete;
    public static event Action OnRestoreFailed;

    private Dictionary<string, string[]> isoToCultureMappings = new Dictionary<string, string[]>() {
        {"AED",new []{"ar-AE"}},{"AFN",new []{"ps-AF","prs-AF"}},{"ALL",new []{"sq-AL"}},{"AMD",new []{"hy-AM"}},{"ARS",new []{"es-AR"}},{"AUD",new []{"en-AU"}},{"AZN",new []{"az-Latn-AZ","az-Cyrl-AZ"}},{"BAM",new []{"sr-Latn-BA","bs-Cyrl-BA","bs-Latn-BA","hr-BA","sr-Cyrl-BA"}},{"BDT",new []{"bn-BD"}},{"BGN",new []{"bg-BG"}},{"BHD",new []{"ar-BH"}},{"BND",new []{"ms-BN"}},{"BOB",new []{"quz-BO","es-BO"}},{"BRL",new []{"pt-BR"}},{"BYR",new []{"be-BY"}},{"BZD",new []{"en-BZ"}},{"CAD",new []{"moh-CA","en-CA","fr-CA","iu-Cans-CA","iu-Latn-CA"}},{"CHF",new []{"rm-CH","de-CH","de-LI","fr-CH","it-CH"}},{"CLP",new []{"es-CL","arn-CL"}},{"CNY",new []{"zh-CN","bo-CN","ii-CN","mn-Mong-CN","ug-CN"}},{"COP",new []{"es-CO"}},{"CRC",new []{"es-CR"}},{"CSD",new []{"sr-Latn-CS","sr-Cyrl-CS"}},{"CZK",new []{"cs-CZ"}},{"DKK",new []{"kl-GL","da-DK","fo-FO"}},{"DOP",new []{"es-DO"}},{"DZD",new []{"tzm-Latn-DZ","ar-DZ"}},{"EGP",new []{"ar-EG"}},{"ETB",new []{"am-ET"}},{"EUR",new []{"sv-FI","br-FR","ca-ES","co-FR","de-AT","de-DE","de-LU","dsb-DE","el-GR","en-IE","es-ES","et-EE","et-EE","eu-ES","fi-FI","fr-BE","fr-FR","fr-FR","fr-LU","fr-MC","fy-NL","ga-IE","ga-IE","gl-ES","gsw-FR","hsb-DE","it-IT","lb-LU","mt-MT","nl-BE","nl-NL","oc-FR","pt-PT","se-FI","sk-SK","sl-SI","smn-FI","sr-Cyrl-ME","sr-Latn-ME"}},{"GBP",new []{"gd-GB","cy-GB","en-GB"}},{"GEL",new []{"ka-GE"}},{"GTQ",new []{"qut-GT","es-GT"}},{"HKD",new []{"zh-HK"}},{"HNL",new []{"es-HN"}},{"HRK",new []{"hr-HR"}},{"HUF",new []{"hu-HU"}},{"IDR",new []{"id-ID"}},{"ILS",new []{"he-IL"}},{"INR",new []{"te-IN","as-IN","bn-IN","bn-IN","en-IN","gu-IN","hi-IN","kn-IN","kok-IN","ml-IN","mr-IN","or-IN","pa-IN","sa-IN","ta-IN"}},{"IQD",new []{"ar-IQ"}},{"IRR",new []{"fa-IR"}},{"ISK",new []{"is-IS"}},{"JMD",new []{"en-JM"}},{"JOD",new []{"ar-JO"}},{"JPY",new []{"ja-JP"}},{"KES",new []{"sw-KE"}},{"KGS",new []{"ky-KG"}},{"KHR",new []{"km-KH"}},{"KRW",new []{"ko-KR"}},{"KWD",new []{"ar-KW"}},{"KZT",new []{"kk-KZ"}},{"LAK",new []{"lo-LA"}},{"LBP",new []{"ar-LB"}},{"LKR",new []{"si-LK"}},{"LTL",new []{"lt-LT"}},{"LVL",new []{"lv-LV"}},{"LYD",new []{"ar-LY"}},{"MAD",new []{"ar-MA"}},{"MKD",new []{"mk-MK"}},{"MNT",new []{"mn-MN"}},{"MOP",new []{"zh-MO"}},{"MVR",new []{"dv-MV"}},{"MXN",new []{"es-MX"}},{"MYR",new []{"ms-MY","en-MY"}},{"NIO",new []{"yo-NG","es-NI","ha-Latn-NG","ig-NG","ig-NG"}},{"NOK",new []{"smj-NO","nb-NO","nn-NO","se-NO","sma-NO"}},{"NPR",new []{"ne-NP"}},{"NZD",new []{"mi-NZ","en-NZ"}},{"OMR",new []{"ar-OM"}},{"PAB",new []{"es-PA"}},{"PEN",new []{"quz-PE","es-PE"}},{"PHP",new []{"fil-PH","en-PH"}},{"PKR",new []{"ur-PK"}},{"PLN",new []{"pl-PL"}},{"PYG",new []{"es-PY"}},{"QAR",new []{"ar-QA"}},{"RON",new []{"ro-RO"}},{"RSD",new []{"sr-Latn-RS","sr-Cyrl-RS"}},{"RUB",new []{"tt-RU","ba-RU","ba-RU","ru-RU","sah-RU"}},{"RWF",new []{"rw-RW"}},{"SAR",new []{"ar-SA"}},{"SEK",new []{"sv-SE","se-SE","sma-SE","smj-SE"}},{"SGD",new []{"zh-SG","en-SG"}},{"SYP",new []{"syr-SY","ar-SY"}},{"THB",new []{"th-TH"}},{"TJS",new []{"tg-Cyrl-TJ"}},{"TMT",new []{"tk-TM","tk-TM"}},{"TND",new []{"ar-TN"}},{"TRY",new []{"tr-TR","tr-TR"}},{"TTD",new []{"en-TT"}},{"TWD",new []{"zh-TW"}},{"UAH",new []{"uk-UA"}},{"USD",new []{"quz-EC","en-029","en-US","es-EC","es-PR","es-SV","es-US"}},{"UYU",new []{"es-UY"}},{"UZS",new []{"uz-Cyrl-UZ"}},{"VEF",new []{"es-VE"}},{"VND",new []{"vi-VN"}},{"XOF",new []{"wo-SN"}},{"YER",new []{"ar-YE"}},{"ZAR",new []{"zu-ZA","af-ZA","af-ZA","en-ZA","nso-ZA","tn-ZA","xh-ZA"}},{"ZWL",new []{"en-ZW"}}
    };

    private IABHandler iabHandler;

#if UNITY_EDITOR
    [ContextMenu("Import Item List From CSV")]
    private void ImportItemListFromCSV() {
        string csvPath = UnityEditor.EditorUtility.OpenFilePanel("Import IAP CSV", "", "csv");

        if (csvPath.Length > 0) {
            itemList.Clear();

            string fileData = System.IO.File.ReadAllText(csvPath);
            string[] fileLines = fileData.Split("\r"[0]);

            // Skip the first line as it's headers
            for (int lineId = 1; lineId < fileLines.Length; lineId++) {
                string[] lineData = fileLines[lineId].Trim().Split(","[0]);
                
                IABItem newIABItem = new IABItem();
                
                newIABItem.storeSKUs = new List<IABItemStoreInfo>();

                IABItemStoreInfo googlePlayStoreInfo = new IABItemStoreInfo();
                googlePlayStoreInfo.store = AppStore.GooglePlay;
                googlePlayStoreInfo.sku = lineData[0];
                newIABItem.storeSKUs.Add(googlePlayStoreInfo);
                
                IABItemStoreInfo appStoreStoreInfo = new IABItemStoreInfo();
                appStoreStoreInfo.store = AppStore.AppleAppStore;
                appStoreStoreInfo.sku = lineData[1];
                newIABItem.storeSKUs.Add(appStoreStoreInfo);
                
                IABItemStoreInfo amazonStoreInfo = new IABItemStoreInfo();
                amazonStoreInfo.store = AppStore.AmazonAppStore;
                amazonStoreInfo.sku = lineData[2];
                newIABItem.storeSKUs.Add(amazonStoreInfo);
                
                IABItemStoreInfo udpStoreInfo = new IABItemStoreInfo();
                udpStoreInfo.store = AppStore.UDP;
                udpStoreInfo.sku = lineData[3];
                newIABItem.storeSKUs.Add(udpStoreInfo);

                newIABItem.productId = googlePlayStoreInfo.sku;
                switch (lineData[4]) {
                    case "Consumable": newIABItem.type = ProductType.Consumable; break;
                    case "Nonconsumable": newIABItem.type = ProductType.NonConsumable; break;
                    case "Subscription": newIABItem.type = ProductType.Subscription; break;
                }
                
                itemList.Add(newIABItem);
            }
        }

    }
#endif
    
    private void Awake() {
        instance = instance ?? this;

        // Setup the specific IAB handler as we need to do things differently for UDP builds
        if (CrossPlatformManager.GetActiveStore() == AppStore.UDP) {
            iabHandler = GetComponent<IABHandlerUDP>() ?? gameObject.AddComponent<IABHandlerUDP>();
            iabHandler.Init(this);
        } else {
            iabHandler = GetComponent<IABHandlerMain>() ?? gameObject.AddComponent<IABHandlerMain>();
            iabHandler.Init(this);
        }
        
        CrossPlatformManager.OnStoreInitializeSuccessful += OnStoreInitializeSuccessful;
    }

    // Add a few frames of delay so we're not initializing alongside other scripts
    private void OnStoreInitializeSuccessful() {
        StartCoroutine(DoStoreInitialization());
    }

    private IEnumerator DoStoreInitialization() {
        for(int i=0;i < 10;i++)
            yield return null;
        
        iabHandler.InitializePurchasing();
    }

    // Removed as this was causing a lot of confusion for developers where some were passing productId and some were passing store SKU
    /*public IABItem GetItem(string ProductID) {
        return GetItemByProductId(ProductID);
    }*/

    // Returns true if subscriptions are supported on the current device
    // (Trying to initiate a subscription on a unsupported device crashes the app)
    public bool SubscriptionsSupported() {
        // On android subscriptions have been supported since android 2.2 so if unity runs on the device then it supports subscription :)
        switch (CrossPlatformManager.GetActiveStore()) {
            case AppStore.fake: // Enables the fake editor billing to have subscription support
            case AppStore.GooglePlay:
            case AppStore.AmazonAppStore:
                return true;
                
            // iOS 11.2 changed handling of subscriptions adding features like introductory prices, changing subscription plan, free trials etc
            // OpenIAB previously didn't easily support lower iOS versions but Unity IAP supports all supported versions above the device supported minimum
            case AppStore.AppleAppStore: 
            case AppStore.MacAppStore: 
                return true;
            
            // Other app stores may support subscriptions, but we haven't setup support within the app or firebase functions for handling them
            default:
                return false;
        }
    }

    public IABItem GetItemBySKU(string wantedSKU) {
        for (int i = 0; i < itemList.Count; i++) {
            IABItem item = itemList[i];

            foreach (IABItemStoreInfo storeInfo in item.storeSKUs)
                if (storeInfo.sku == wantedSKU)
                    return item;
        }
        
        // If this is the amazon app store if this product ends in _group then it's a subscription parent, remove _group and try search for the IABItem again
        if (CrossPlatformManager.GetActiveStore() == AppStore.AmazonAppStore && wantedSKU.Contains("_group"))
            return GetItemBySKU(wantedSKU.Replace("_group", ""));

        return null;
    }

    public IABItem GetItemByProductId(string productId) {
        for (int i = 0; i < itemList.Count; i++) {
            IABItem localItem = itemList[i];

            if (localItem.isProductId(productId))
                return localItem;
        }

        return null;
    }

    // Converts a product id to the SKU for the active store
    public string ProductIdToSKU(string productId) {
        List<IABItemStoreInfo> skuInfo = GetItemByProductId(productId).storeSKUs;

        AppStore activeStore = CrossPlatformManager.GetActiveStore();
        
        foreach (IABItemStoreInfo curSkuInfo in skuInfo) {
            if (curSkuInfo.store == activeStore) {
                return curSkuInfo.sku;
            }
        }
        
        Debug.LogError("ProductIdToSKUForActiveStore could not find active store id for " + productId + " on " + activeStore + " falling back to productId (this may cause issues)");
        return productId;
    }

    public IPurchaseReceipt GetPurchaseReceiptBySKU(string sku) {
        return iabHandler.GetPurchaseReceipt(GetItemBySKU(sku).productId);
    }
    
    public IPurchaseReceipt GetPurchaseReceiptByProductId(string productId) {
        return iabHandler.GetPurchaseReceipt(productId);
    }

    public bool HasRawReceiptBySKU(string sku) {
        return iabHandler.HasRawReceipt(GetItemBySKU(sku).productId);
    }

    public bool HasRawReceiptByProductId(string productId) {
        return iabHandler.HasRawReceipt(productId);
    }
    
    public string GetRawReceiptBySKU(string sku) {
        return iabHandler.GetRawReceipt(GetItemBySKU(sku).productId);
    }

    public string GetRawReceiptByProductId(string productId) {
        return iabHandler.GetRawReceipt(productId);
    }

    public string GetAppleAppReceipt() {
        return iabHandler.GetAppleAppReceipt();
    }
    
    private bool hasActivateAnyPurchaseDebug = false;
    private TouchScreenKeyboard keyboard;
    
    public void DebugAnyPurchase() {
        hasActivateAnyPurchaseDebug = true;
        keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, false, false, false, true, "Enter the product id of the purchase you want to test");
    }

    private void Update() {
        if (hasActivateAnyPurchaseDebug && keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Done) {
            if(ClickLockManager.Instance != null)
                ClickLockManager.Instance.ShowIABClickLock();

            PurchaseItem(keyboard.text);

            hasActivateAnyPurchaseDebug = false;
            keyboard = null;
        }
    }
    
    public void DebugAllPurchases() {
        StartCoroutine(DoDebugAllPurchases());
    }

    private IEnumerator DoDebugAllPurchases() {
        for (int i = 0; i < itemList.Count; i++) {
            IABItem item = itemList[i];

            // Skip subscriptions if the store doesn't support them
            if (item.type == ProductType.Subscription && !SubscriptionsSupported())
                continue;
            
            // Pre-show the message just incase the purchase handling freezes the app until it shows the prompt
            if (ClickLockManager.Instance != null) {
                ClickLockManager.Instance.ShowClickLock("Purchasing: " + item.productId + "\n[sup][FFFF00]Moving to next item in 5[-][/sup]\n\n[sup][0000FF]Item " + i + " / " + itemList.Count + "[-][/sup]");
                yield return null;
            }

            PurchaseItem(item.productId);
            
            // Move to next item after 5 seconds
            for (int timeUntilMove = 5; timeUntilMove >= 0f; timeUntilMove--) {
                if (ClickLockManager.Instance != null)
                    ClickLockManager.Instance.ShowClickLock("Purchasing: " + item.productId + "\n[sup][FFFF00]Moving to next item in " + timeUntilMove + "[-][/sup]\n\n[sup][0000FF]Item " + i + " / " + itemList.Count + "[-][/sup]");

                yield return new WaitForSecondsRealtime(1f);
            }

            yield return null;
            
            if (ClickLockManager.Instance != null)
                ClickLockManager.Instance.HideClickLock();
        }
    }
    
    public void PurchaseItem(string productId) {
        PurchaseItem(productId, false);
    }

    private void PurchaseItem(string productId, bool skipInitializingCheck) {
        // If billing is still pending initialisation or failed initialisation, wait a few more seconds for it to initialise then try make the purchase again
        if (!skipInitializingCheck && isInitializing) {
            StartCoroutine(WaitForInitialisationThenResumePurchaseItem(productId));

            return;
        }

        iabHandler.PurchaseItem(productId, skipInitializingCheck);
    }

    private IEnumerator WaitForInitialisationThenResumePurchaseItem(string productId) {
        float waitTime = 0f;

        // If billing isn't initialising and billing isn't supported then the previous initialisation attempt failed - try again
        if (!isInitializing)
            iabHandler.InitializePurchasing();

        yield return null;

        // Wait up to 5 seconds for billing to finish initialising (ends early if initilisation finished, success or failure)
        while (waitTime < 5f && isInitializing) {
            yield return null;

            waitTime += Time.unscaledDeltaTime;
        }

        // Attempt the purchase again, even if we're still trying to initialise just so we resume the normal purchased failed flow
        PurchaseItem(productId, true);
    }

    public void ConsumeItem(string productId) {
        iabHandler.ConsumeItem(productId);
    }

    // Legacy restore function, to be removed in future
    [Obsolete("Use RestorePurchases instead")]
    public void RestoreIOSPurchases() {
        // Support for additional stores has been added so restoring purchases is no longer iOS specific anymore
        iabHandler.RestorePurchases();
    }

    public void RestorePurchases() {
        iabHandler.RestorePurchases();
    }

    private void OnApplicationFocus(bool hasFocus) {
        // If the store has not initialized yet, do nothing
        if (CrossPlatformManager.instance == null || !CrossPlatformManager.instance.hasInitialized) return;
        
        // If the user switches back to the app and billing is not supported, attempt to initialise the billing service again
        if (hasFocus && !isInitialized && !isInitializing)
            iabHandler.InitializePurchasing();

        // Check for any new purchases on application focus
        // For example the player may have entered a promo code to redeem a purchase outside the game
        if (hasFocus && isInitialized && !isPurchaseActive)
            iabHandler.InitializePurchasing(); // Unity IAB disconnects when minimzing the app
    }

    public bool StoreSupportsLocalReceipts() {
        switch (CrossPlatformManager.GetActiveStore()) {
            // Standard local validation flow for GooglePlay / Apple
            case AppStore.GooglePlay:
            case AppStore.AppleAppStore:
            case AppStore.MacAppStore:
            case AppStore.AmazonAppStore:
                return true;
        }

        return false;
    }
    
     public string GenerateRandomToken(int length = 16) {
        UnityEngine.Random.InitState((int) DateTime.Now.Ticks);

        // Just keeping the tokens simple because I'm scared of encoding issues
        const string glpyhs = "abcdefghijklmnopqrstuvwxyz0123456789";
        StringBuilder token = new StringBuilder(length);

        for (int i = 0; i < length; i++)
            token.Insert(i, glpyhs[UnityEngine.Random.Range(0, glpyhs.Length - 1)]);

        return token.ToString();
    }
    
    // Whether the current store supports a restore purchase button so UI can be adjusted accordingly
    public bool StoreSupportsPurchaseRestore() {
        bool useRestorePurchase = false;
        
        switch (CrossPlatformManager.GetActiveStore()) {
            case AppStore.AppleAppStore:
            case AppStore.MacAppStore:
            case AppStore.WinRT:
                useRestorePurchase = true;
                break;
            
            case AppStore.UDP:
                switch (CrossPlatformManager.GetActiveUDPStore()) {
                    case UDPStore.SAMSUNGGALAXYSTORE: // Not required but supported so might as well use
                    case UDPStore.QOOAPP: // Not required but supported so might as well use
                        // Unity IAP used to support restoring purchases on Samsung but since switching over to UDP they seem to no longer bother supporting the functionality
                        // For now we return false for these stores until UDP adds support for restoring purchases on supported stores
                        useRestorePurchase = false;
                        break;
                    
                    // Some of the stores in this list I just couldn't find more info about
                    case UDPStore.UDPSANDBOX:
                    case UDPStore.ONESTORE:
                    case UDPStore.APPTUTTI:
                    case UDPStore.XIAOMI:
                    case UDPStore.XIAOMISTORE: 
                    case UDPStore.XIAOMISTORECN:
                    case UDPStore.HUAWEI:
                    case UDPStore.TPAY: 
                    case UDPStore.UPTODOWN:
                    case UDPStore.SHAREIT:
                    case UDPStore.JIO:
                    case UDPStore.JIOGAMESSTORE:
                    case UDPStore.UNKNOWN:
                    default:
                        useRestorePurchase = false;
                        break;
                }
                break;
            
            default:
                useRestorePurchase = false;
                break;
        }

        return useRestorePurchase;
    }
    
    public void OnPurchaseRestoreComplete() {
        OnIOSRestoreComplete?.Invoke(); // This callback will be removed in future
        OnRestoreComplete?.Invoke();
    }

    public void OnPurchaseRestoreFailed(InitializationFailureReason failureReason, string errorMessage) {
        Debug.LogError("Purchase restore failed - " + failureReason + " - " + errorMessage);
        
        OnPurchaseRestoreFailed();
    }

    public void OnPurchaseRestoreFailed() {
        OnIOSRestoreFailed?.Invoke(); // This callback will be removed in future
        OnRestoreFailed?.Invoke();
    }

    public void OnOwnedItemLoad(IABItem item) {
        OnOwnedItemLoaded?.Invoke(item);
    }

    public void OnInventoryReady() {
        OnIABInventoryReady?.Invoke();
        OnIABPricesUpdated?.Invoke();
    }

    public void OnPurchaseRefunded(IABItem item) {
        OnIABPurchaseRefunded?.Invoke(item);
    }

    // When making a promo purchase on iOS this callback is triggered before the normal purchase complete one
    public void OnPromoPurchaseComplete(IABItem item) {
        OnIOSPromoPurchaseComplete?.Invoke(item);
    }

    
    
    // An item has been selected for deferred purchase meaning the user has not paid yet
    // This callback should be just used to notify the user that they need to complete the purchase to get the item
    public void OnPurchaseDeferred(IABItem item) {
        //Debug.Log("Purchase Deferred  [Item: " + product.definition.storeSpecificId + "]");

        OnIABPurchaseDeferred?.Invoke(item);
    }

    public void OnPurchaseFailed(string error) {
        OnIABPurchaseFailed?.Invoke(error);
    }

    public void OnPurchaseVerificationCompleted(IABItem item, string originalTransactionId) {
        //IABItem localItem = GetItemBySKU(product.definition.storeSpecificId);

        FirebaseAnalyticsManager.LogEvent("iab_purchase_complete", "id", item.productId);

        OnIABPurchaseComplete?.Invoke(item, originalTransactionId);

        item.isProcessingValidation = false;

        isPurchaseActive = false;
    }

    public void OnPurchaseVerificationFailed(string sku, string error) {
        IABItem localItem = GetItemBySKU(sku);
        
        OnIABPurchaseFailed?.Invoke("Purchase validation failed! " + error);

        Debug.LogError("Purchase validation failed! " + error);

        FirebaseAnalyticsManager.LogEvent("iab_validation_failed", "id", sku);

        if(localItem != null)
            localItem.isProcessingValidation = false;

        isPurchaseActive = false;
    }
    
    // //////////////////////////////
    // STRING CONVERSION FUNCTIONS //
    // //////////////////////////////

    public string TimeSpanToISO8601String(TimeSpan timeSpan) {
        return XmlConvert.ToString(timeSpan);
    }

    public string ISO8601ToReadableString(string input, bool appendS = true, bool noNumberOne = true, bool noNumbers = false) {
        string output = "";

        if (!string.IsNullOrEmpty(input)) {
            int inputLength = input.Length;

            for (int charId = 0; charId < inputLength; charId++) {
                char curChar = input[charId];

                switch (curChar) {
                    case 'P': // Duration
                        // If a number does not appear after P then the duration looks blank and we leave the duration block
                        bool isDurationBlock = input.Length < charId + 1 ? false : char.IsNumber(input[charId + 1]);

                        while (isDurationBlock) {
                            string intChars = "";
                            int intValue = 0;

                            curChar = input[++charId];

                            // Iterate through characters until we no longer hit a number
                            for (; char.IsNumber(curChar); curChar = input[++charId])
                                intChars += curChar;

                            // Parse the string as a number (if this fails then there was no number after P which is an invalid ISO-8601 format)
                            if (int.TryParse(intChars, out intValue)) {
                                string prepend = !string.IsNullOrEmpty(output) ? ", " : "";
                                string append = appendS ? intValue != 1 ? "s" : "" : "";

                                switch (curChar) {
                                    case 'D':
                                        output += prepend + (noNumbers ? "day" : (noNumberOne && intValue == 1 ? "day" : (intValue + " day"))) + append;
                                        break;
                                    case 'W':
                                        output += prepend + (noNumbers ? "week" : (noNumberOne && intValue == 1 ? "week" : (intValue + " week"))) + append;
                                        break;
                                    case 'M':
                                        output += prepend + (noNumbers ? "month" : (noNumberOne && intValue == 1 ? "month" : (intValue + " month"))) + append;
                                        break;
                                    case 'Y':
                                        output += prepend + (noNumbers ? "year" : (noNumberOne && intValue == 1 ? "year" : (intValue + " year"))) + append;
                                        break;
                                }
                            } else {
                                Debug.LogError("Invalid fomat: " + input);
                                return "";
                            }

                            // If the next character is a number then we're still in a duration block
                            isDurationBlock = charId + 1 < inputLength ? char.IsNumber(input[charId + 1]) : false;
                        }

                        break;

                    case 'T': // Time
                        bool isTimeBlock = true;

                        while (isTimeBlock) {
                            string intChars = "";
                            int intValue = 0;

                            curChar = input[++charId];

                            // Iterate through characters until we no longer hit a number
                            for (; char.IsNumber(curChar); curChar = input[++charId])
                                intChars += curChar;

                            // Parse the string as a number (if this fails then there was no number after P which is an invalid ISO-8601 format)
                            if (int.TryParse(intChars, out intValue)) {
                                string prepend = !string.IsNullOrEmpty(output) ? ", " : "";
                                string append = appendS ? intValue != 1 ? "s" : "" : "";

                                switch (curChar) {
                                    case 'S':
                                        output += prepend + (noNumbers ? (appendS ? "second" : "every second") : (noNumberOne && intValue == 1 ? "day" : (intValue + " second"))) + append;
                                        break;
                                    case 'M':
                                        output += prepend + (noNumbers ? (appendS ? "minute" : "every minute") : (noNumberOne && intValue == 1 ? "day" : (intValue + " minute"))) + append;
                                        break;
                                    case 'H':
                                        output += prepend + (noNumbers ? (appendS ? "hour" : "hourly") : (noNumberOne && intValue == 1 ? "day" : (intValue + " hour"))) + append;
                                        break;
                                }
                            } else {
                                Debug.LogError("Invalid fomat: " + input);
                                return "";
                            }

                            // If the next character is a number then we're still in a time block
                            isTimeBlock = charId + 1 < inputLength ? char.IsNumber(input[charId + 1]) : false;
                        }

                        break;

                    default:
                        Debug.LogError("Invalid fomat: " + input);
                        return "";
                }
            }
        }

        return output;
    }

    public string DurationStringToShortened(string input) {
        switch (input) {
            case "second": return "s";
            case "minute": return "m";
            case "hour": return "hr";
            case "day": return "d";
            case "week": return "wk";
            case "month": return "mo";
            case "year": return "yr";
        }

        return "";
    }

    public string DurationStringToPeriod(string input) {
        switch (input) {
            case "second": return "every second";
            case "minute": return "every minute";
            case "hour": return "hourly";
            case "day": return "daily";
            case "week": return "weekly";
            case "month": return "monthly";
            case "year": return "yearly";
        }

        return "";
    }
    
    // Checks if any of the cultures for this iso currency are installed
    public CultureInfo GetCultureForISOCurrency(string isoCurrency) {
        if (!string.IsNullOrEmpty(isoCurrency) && isoToCultureMappings.ContainsKey(isoCurrency)) {
            string[] matchingCultures = isoToCultureMappings[isoCurrency];
            
            // Lookup each culture until we find an available culture for this device
            foreach (string cultureString in matchingCultures) {
                try {
                    CultureInfo culture = CultureInfo.GetCultureInfo(cultureString);

                    return culture;
                } catch (CultureNotFoundException) {}
            }
            
            Debug.LogError("No valid cultures found for currency " + isoCurrency + " falling back to current culture!");
        } else {
            Debug.LogError("Unknown iso currency " + isoCurrency + " falling back to current culture!");
        }
        
        return CultureInfo.CurrentCulture;
    }
}