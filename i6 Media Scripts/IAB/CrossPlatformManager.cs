using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.UDP;

// See: https://docs.unity3d.com/2022.1/Documentation/Manual/udp-service-interoperability.html under "udpStore values" for possible values (21/01/2022)
// Some additional stores found at https://docs.unity3d.com/Packages/com.unity.purchasing.udp@2.0/manual/service-interoperability.html
public enum UDPStore {
    UNKNOWN, // Future added app stores will be marked as unknown until the app properly supports them
    UDPSANDBOX, // UDP Sandbox (for the generic UDP builds)
    SAMSUNGGALAXYSTORE, // Samsung Galaxy Store
    QOOAPP, // QooApp Game Store
    ONESTORE, // ONE store
    APPTUTTI, // APPTUTTi
    HTC, // Viveport
    XIAOMI, // Mi GetApps
    XIAOMISTORE, // Mi GetApps
    XIAOMISTORECN, // Mi Game Center
    HUAWEI, // HUAWEI AppGallery
    TPAY, // TPAY MOBILE Stores (I think this has merged with OneStore)
    UPTODOWN, // Uptodown
    LEGIONREALM, // Just a guess at what the name for Legion Realm Game Store will be
    SHAREIT, // SHAREit
    JIOGAMESSTORE, // JioGames
    JIO // JioGames
}

public class CrossPlatformManager : MonoBehaviour, IInitListener {
    public static CrossPlatformManager instance;
    
#if UNITY_ANDROID
    // Target store this android build will be uploaded to
    public AndroidStore targetAndroidStore = AndroidStore.GooglePlay;
#endif
    
    private struct AppAttributes {
        public string key;
        public string udpStore;
        public string udpClientId;
        public string cloudProjectId;
    }

    private AppAttributes udpStoreInfo;

    public bool hasInitialized { get; set; } // Has the store service initialised (UDP the player need to sign into the app store etc)

    // Generic finished, success or fail
    public static event Action OnStoreInitializeFinished;
    
    public static event Action OnStoreInitializeSuccessful;
    public static event Action<string> OnStoreInitializeFailed;
    
#if UNITY_EDITOR && UNITY_ANDROID
    [HideInInspector] public AppStore lastSetAppStore;
    
    private void OnValidate() {
        AppStore targetStore = AppStore.NotSpecified;
        
        switch (targetAndroidStore) {
            case AndroidStore.GooglePlay:
                targetStore = AppStore.GooglePlay;
                break;
            
            case AndroidStore.AmazonAppStore: 
                targetStore = AppStore.AmazonAppStore;
                break;
            
            case AndroidStore.UDP:
                targetStore = AppStore.UDP;
                break;
            
            case AndroidStore.NotSpecified:
                targetStore = AppStore.GooglePlay;
                targetAndroidStore = AndroidStore.GooglePlay;
                break;
            
            default:
                Debug.LogError("This store is not yet supported by the IABManager script!\nTarget android store switch back to Google Play.");
                targetAndroidStore = AndroidStore.GooglePlay;
                break;
        }

        if (!Application.isPlaying && targetStore != lastSetAppStore) {
            lastSetAppStore = targetStore;
            
            // If this gets called by the OnValidate which is ran when leaving play mode in the editor it can cause the editor to crash
            UnityEditor.Purchasing.UnityPurchasingEditor.TargetAndroidStore(targetStore);
        }
    }
#endif
    
    void Awake() {
        instance = instance ?? this;
        
        // Initial UDP store check, this will be checked again at initialization success/fail
        CheckUDPStore();
    }

    void Start() {
        if (GetActiveStore() == AppStore.UDP) {
            InitialiseStoreService();
        } else {
            // No need to do any store initialization in non-UDP builds, just instantly call initialized
            OnInitialized(default);
        }
    }
    
    public static bool IsAppGenuine() {
        // If the app contains some bad permissions and it's not a UDP build then it's probably a hacked version of the app
        // Note: Some UDP stores add these permissions to install their appstore at app launch (Huawei App Gallery) so we don't do this check for UDP builds
        if (GetActiveStore() != AppStore.UDP && JarLoader.DoesAppContainBadPermissions())
            return false;

        if (Application.genuineCheckAvailable && !Application.genuine)
            return false;

        return JarLoader.AppRunId() >> 2 != Convert.ToInt32("1bd3", 16);
    }

    public void InitialiseStoreService() {
        // Initialises the store (this will show the store login screen on most stores and may also prompt runtime store permissions)
        StoreService.Initialize(this);
    }

    // Called when UDP builds have successfully initialised (called on Start on non-UDP builds)
    public void OnInitialized(UserInfo userInfo)
    {
        if (GetActiveStore() == AppStore.UDP) {
            Debug.Log("Store Initialization Successful");
            CheckUDPStore();
        }

        hasInitialized = true;
        
        // log which store is being used to analytics, this will allow us to filter audiences per store
        LogSessionStore();
        
        OnStoreInitializeSuccessful?.Invoke();
        OnStoreInitializeFinished?.Invoke();
    }

    // Called when UDP builds have failed to initialise
    public void OnInitializeFailed(string error)
    {
        Debug.LogError("Store Initialization Failed - " + error);

        CheckUDPStore();
        
        OnStoreInitializeFailed?.Invoke(error);
        OnStoreInitializeFinished?.Invoke();
    }

    private void CheckUDPStore() {
        if (GetActiveStore() == AppStore.UDP)
            FetchUDPStoreInfo();
    }

    public static string GetUniversalPackageName() {
        return FirebaseManager.instance.uniqueGameIdentifier;
    }

    public static string GetCleanStoreName(bool useGenericIfUnknown = true) {
        AppStore activeStore = GetActiveStore();
        
        if (activeStore == AppStore.UDP) {
            UDPStore activeUDPStore = GetActiveUDPStore();
            
            switch (activeUDPStore) {
                case UDPStore.HTC: return "Viveport Store";
                case UDPStore.JIOGAMESSTORE: case UDPStore.JIO: return "Jio Games Store";
                case UDPStore.TPAY: return "TPay Mobile Store";
                case UDPStore.HUAWEI: return "Huawei AppGallery Store";
                case UDPStore.QOOAPP: return "QooApp Game Store";
                case UDPStore.XIAOMISTORECN: case UDPStore.XIAOMISTORE: case UDPStore.XIAOMI: return "Mi GetApps Store";
                case UDPStore.APPTUTTI: return "APPTUTTi Store";
                case UDPStore.ONESTORE: return "OneStore";
                case UDPStore.UPTODOWN: return "Uptodown Store";
                case UDPStore.UDPSANDBOX: return "UDP Sandbox";
                case UDPStore.LEGIONREALM: return "Legion Realm Store";
                case UDPStore.SAMSUNGGALAXYSTORE: return "Galaxy Store";
                
                // SHAREit republishes to many stores so we won't know which store it was republished to
                case UDPStore.SHAREIT: default:
                    if (useGenericIfUnknown) {
                        return "app store";
                    } else {
                        // Return activeUDPStore but with th first letter of each word capitalized
                        TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

                        return textInfo.ToTitleCase(activeUDPStore.ToString().ToLowerInvariant());
                    }
            }
        } else {
            switch (activeStore) {
                case AppStore.GooglePlay: return "Google Play Store";
                case AppStore.AmazonAppStore: return "Amazon App Store";
                case AppStore.AppleAppStore: return "Apple App Store";
                case AppStore.MacAppStore: return "Mac App Store";
                case AppStore.WinRT: return "Windows Store";
                default:
                    if (useGenericIfUnknown) {
                        return "app store";
                    } else {
                        // Return activeStore but with th first letter of each word capitalized
                        TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

                        return textInfo.ToTitleCase(activeStore.ToString().ToLowerInvariant());
                    }
            }
        }
    }

    public static AppStore GetActiveStore() {
#if UNITY_EDITOR
        return AppStore.fake;     
#elif UNITY_ANDROID
        switch (instance.targetAndroidStore) {
            case AndroidStore.AmazonAppStore: return AppStore.AmazonAppStore;
            case AndroidStore.UDP: return AppStore.UDP;
            default: case AndroidStore.GooglePlay: return AppStore.GooglePlay;
        }
#elif UNITY_WINRT || UNITY_STANDALONE_WIN
        return AppStore.WinRT;
#elif UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX
        return Application.platform == RuntimePlatform.OSXPlayer ? AppStore.MacAppStore : AppStore.AppleAppStore;
#endif
        
        // If a store hasn't already been returned by this point fallback to NotSpecified
        return AppStore.NotSpecified;
    }
    
    public static UDPStore GetActiveUDPStore() {
        if (instance == null || instance.udpStoreInfo.udpStore == null)
            return UDPStore.UNKNOWN;
        
        return Enum.TryParse(instance.udpStoreInfo.udpStore.ToUpper(), out UDPStore udpStore) ? udpStore : UDPStore.UNKNOWN;
    }
    
    public static string AppStoreToStoreName(AppStore appStore) {
        string storeName = "Fake"; // Default to the name of the fake store used in the editor
        
        // We only need to do this because the amazon enum doesn't match the expected store name T_T
        // enum is "AmazonAppStore" but it expects "AmazonApps"
        switch (appStore) {
            case AppStore.GooglePlay: storeName = GooglePlay.Name; break;
            case AppStore.AmazonAppStore: storeName = AmazonApps.Name; break;
            case AppStore.AppleAppStore: storeName = AppleAppStore.Name; break;
            case AppStore.MacAppStore: storeName = MacAppStore.Name; break;
            case AppStore.UDP: storeName = UDP.Name; break;
            case AppStore.WinRT: storeName = WindowsStore.Name; break;
        }

        return storeName;
    }
    
    private void FetchUDPStoreInfo() {
        // We need to read the UDP config file unity created to know which store we're on
        string udpJsonPath = System.IO.Path.Combine(Application.persistentDataPath, "Unity", Application.cloudProjectId, "udp", "udp.json");

        if (System.IO.File.Exists(udpJsonPath)) {
            string udpJsonContents = System.IO.File.ReadAllText(udpJsonPath);
            
            udpStoreInfo = JsonUtility.FromJson<AppAttributes>(udpJsonContents);

            Debug.Log("udp.json loaded successfully");
            
            if (FirebaseManager.instance.debugMode)
                Debug.Log("udp.json loaded with contents: " + udpJsonContents);
        } else {
            Debug.LogError("Could not find " + udpJsonPath + " - This is a UDP build and must be rebuilt via Unity's Distribution Portal!");
        }
    }
    
    public void LogSessionStore() {
        string storeName = "";

        AppStore activeStore = GetActiveStore();

        if (activeStore != AppStore.UDP) {
            storeName = activeStore.ToString();
        } else {
            // Find the specific store we were published to after UDP
            // (not using GetActiveUDPStore() because we want a string and I want to include all future values too, not just ones the app is setup for)
            storeName = udpStoreInfo.udpStore;
        }

        FirebaseAnalyticsManager.LogEvent("app_store", "name", storeName);
        FirebaseAnalyticsManager.SetUserProperty("app_store", storeName);
    }
}
