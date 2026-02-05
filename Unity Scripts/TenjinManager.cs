using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.MiniJSON;

public class TenjinManager : MonoBehaviour {
    [System.Serializable]
    public class SDKKeys {
        public string google = "";
        public string amazon = "";
        public string apple = "";
    }
    
    public SDKKeys sdkKeys;

    private bool activeUseTenjin;
    private string activeSDKKey;

    public static TenjinManager instance;
    
    #if tenjin_admob_enabled
        private AppStoreType activeStoreType;
    #endif
    
    void Awake() {
        instance ??= this;
    }
    
    #if tenjin_admob_enabled
        void Start() {
            switch (CrossPlatformManager.GetActiveStore()) {
                case AppStore.GooglePlay: 
                    activeSDKKey = sdkKeys.google;
                    activeStoreType = AppStoreType.googleplay;
                    break;
                
                case AppStore.AmazonAppStore:
                    activeSDKKey = sdkKeys.amazon;
                    activeStoreType = AppStoreType.amazon;
                    break;
                
                case AppStore.AppleAppStore:
                case AppStore.MacAppStore:
                    activeSDKKey = sdkKeys.apple;
                    activeStoreType = AppStoreType.other;
                    break;
            }

            if (string.IsNullOrEmpty(activeSDKKey)) {
                activeUseTenjin = false;
            } else {
                Debug.Log("Tenjin ready to connect!");
                
                activeUseTenjin = true;

                IABManager.OnIABPurchaseCompleteExtended += PurchaseComplete;
            }
            
            // Only connect to Tenjin once the personalisation auth flow has been completed and admob has been called to initialize
            if (AdMob_Manager.instance.hasAdMobInitializeBeenCalled) {
                TenjinConnect();
            } else {
                PersonalisationManager.OnAuthRequestsComplete += TenjinConnect;
            }
        }
        
        void OnApplicationPause(bool pauseState) {
            if(!pauseState)
                TenjinConnect();
        }

        // I'm not sure why but the Tenjin guide grabbed a new instance every single request so I'm just doing the same..
        private BaseTenjin GetTenjinInstance() {
            BaseTenjin tenjinInstance = Tenjin.getInstance(activeSDKKey);

            // Set the default settings
            tenjinInstance.SetAppStoreType(activeStoreType); // Set the app store type for the current platform
            
            // BUGFIX: Tenjin will crash the app on iOS if SetCustomerUserId is called with a null string
            if(!string.IsNullOrEmpty(FirebaseManager.instance.instanceId))
                tenjinInstance.SetCustomerUserId(FirebaseManager.instance.instanceId); // Allows us to link users to their firebase instance id for data removal requests
            
            tenjinInstance.SetCacheEventSetting(true); // Enables automatically resending events when internet connection is restored
            
            return tenjinInstance;
        }
        
        private void TenjinConnect() {
            if (!activeUseTenjin) return;
            
            BaseTenjin tenjinInstance = GetTenjinInstance();
            
            // Only initialise tenjin once the personalisation auth flow has been completed and admob has been called to initialize
            if (AdMob_Manager.instance.hasAdMobInitializeBeenCalled) {
                if(AdMob_Manager.instance.debugLogging)
                    Debug.Log("Tenjin connect called!");
                
                tenjinInstance.Connect();
            } else {
                if(AdMob_Manager.instance.debugLogging)
                    Debug.Log("Could not connect to tenjin because admob has not yet initialized");
            }
        }

        private void PurchaseComplete(IABItem item, int quantity, string originalTransactionId, string payload) {
            if (!activeUseTenjin) return;
            
            // Ignore subscriptions, they're handled via server-to-server realtime events as Tenjin does not want trial events recorded
            if (item.type == ProductType.Subscription)
                return;
            
            BaseTenjin tenjinInstance = GetTenjinInstance();
            
            switch (activeStoreType) {
                case AppStoreType.googleplay:
                    // "json", "signature" https://docs.unity3d.com/Packages/com.unity.purchasing@4.13/manual/GoogleReceipt.html
                    Dictionary<string, object> googlePayload = Json.Deserialize(payload) as Dictionary<string, object>;

                    tenjinInstance.Transaction(item.productId, item.currencyCode, quantity, double.Parse(item.priceValue), originalTransactionId, (string)googlePayload?["json"], (string)googlePayload?["signature"]);
                    break;
                
                case AppStoreType.amazon: 
                    // "receiptId", "userId", "isSandbox", "receiptJson"
                    Dictionary<string, object> amazonPayload = Json.Deserialize(payload) as Dictionary<string, object>;
                    
                    tenjinInstance.TransactionAmazon(item.productId, item.currencyCode, quantity, double.Parse(item.priceValue), (string)amazonPayload?["receiptId"], (string)amazonPayload?["userId"]);
                    break;
                
                default: 
                    // The payload on iOS is the base64 encoded ASN.1 receipt
                    tenjinInstance.Transaction(item.productId, item.currencyCode, quantity, double.Parse(item.priceValue), originalTransactionId, payload, null);
                    break;
            }
        }
    #endif

    public string GetInstallationId() {
        #if tenjin_admob_enabled
            if (!activeUseTenjin) return "";
                
            BaseTenjin tenjinInstance = GetTenjinInstance();

            return tenjinInstance.GetAnalyticsInstallationId();
        #else
            return "";
        #endif
    }
    
    // Custom event log
    public void LogEvent(string eventName) {
        #if tenjin_admob_enabled
            if (!activeUseTenjin) return;
            
            BaseTenjin tenjinInstance = GetTenjinInstance();

            tenjinInstance.SendEvent(eventName);
        #endif
    }

    // Custom event log with an integer value
    public void LogEvent(string eventName, int eventValue) {
        #if tenjin_admob_enabled
            if (!activeUseTenjin) return;
            
            BaseTenjin tenjinInstance = GetTenjinInstance();

            tenjinInstance.SendEvent(eventName, eventValue.ToString());
        #endif
    }

    // Ad Mediation Impression logging
    public void LogAdMobImpressionFromJSON(string json) {
        #if tenjin_admob_enabled
            if (!activeUseTenjin) return;
            
            BaseTenjin tenjinInstance = GetTenjinInstance();
            
            tenjinInstance.AdMobImpressionFromJSON(json);
        #endif
    }
}
