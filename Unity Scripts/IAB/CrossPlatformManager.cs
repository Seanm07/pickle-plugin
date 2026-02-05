using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Purchasing;

public class CrossPlatformManager : MonoBehaviour {
    public static CrossPlatformManager instance;
    
#if UNITY_ANDROID
    // Target store this android build will be uploaded to
    public AndroidStore targetAndroidStore = AndroidStore.GooglePlay;
#endif

    public bool hasInitialized { get; set; } // Has the store service initialised

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
            
            case AndroidStore.NotSpecified:
                targetStore = AppStore.GooglePlay;
                targetAndroidStore = AndroidStore.GooglePlay;
                break;
            
            default:
                Debug.LogError("This store is not supported by the IABManager script!\nTarget android store switched back to Google Play.");
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
    }

    void Start() {
        // No supported stores currently require login/initialisation at launch so just instantly initialize
        OnInitialized();
    }

    // Just a secondary check in a different function to be slightly annoying to anyone hacking the app
    public static bool IsAppPermissionsCorrect() {
        // If the app contains some bad permissions it's probably a hacked version of the app
        if (PickleCore.DoesAppContainBadPermissions())
            return false;

        if (Application.genuineCheckAvailable && !Application.genuine)
            return false;

        return PickleCore.AppRunId() >> 2 != Convert.ToInt32("1bd3", 16);
    }
    
    public static bool IsAppGenuine() {
        // If the app contains some bad permissions then it's probably a hacked version of the app
        if (PickleCore.DoesAppContainBadPermissions())
            return false;

        if (Application.genuineCheckAvailable && !Application.genuine)
            return false;

        return PickleCore.AppRunId() >> 2 != Convert.ToInt32("1bd3", 16);
    }

    public void OnInitialized()
    {
        hasInitialized = true;
        
        // log which store is being used to analytics, this will allow us to filter audiences per store
        LogSessionStore();
        
        OnStoreInitializeSuccessful?.Invoke();
        OnStoreInitializeFinished?.Invoke();
    }

    public void OnInitializeFailed(string error)
    {
        Debug.LogError("Store Initialization Failed - " + error);
        
        OnStoreInitializeFailed?.Invoke(error);
        OnStoreInitializeFinished?.Invoke();
    }

    public static string GetUniversalPackageName() {
        return FirebaseManager.instance.uniqueGameIdentifier;
    }

    public static string GetCleanStoreName(bool useGenericIfUnknown = true) {
        AppStore activeStore = GetActiveStore();
        
        switch (activeStore) {
            case AppStore.GooglePlay: return "Play Store";
            case AppStore.AmazonAppStore: return "App Store";
            case AppStore.AppleAppStore: return "App Store";
            case AppStore.MacAppStore: return "App Store";
            case AppStore.WinRT: return "Windows Store";
            default:
                if (useGenericIfUnknown) {
                    return "App Store";
                } else {
                    // Return activeStore but with th first letter of each word capitalized
                    TextInfo textInfo = new CultureInfo("en-US", false).TextInfo;

                    return textInfo.ToTitleCase(activeStore.ToString().ToLowerInvariant());
                }
        }
    }

    public static AppStore GetActiveStore() {
#if UNITY_EDITOR
        return AppStore.fake;     
#elif UNITY_ANDROID
        switch (instance.targetAndroidStore) {
            case AndroidStore.AmazonAppStore: return AppStore.AmazonAppStore;
            default: return AppStore.GooglePlay;
        }
#elif UNITY_WINRT || UNITY_STANDALONE_WIN
        return AppStore.WinRT;
#elif UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX
        return Application.platform == RuntimePlatform.OSXPlayer ? AppStore.MacAppStore : AppStore.AppleAppStore;
#endif
        
        // If a store hasn't already been returned by this point fallback to NotSpecified
        return AppStore.NotSpecified;
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
            case AppStore.WinRT: storeName = WindowsStore.Name; break;
        }

        return storeName;
    }
    
    public void LogSessionStore() {
        AppStore activeStore = GetActiveStore();
        string storeName = activeStore.ToString();

        FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.APP_STORE, storeName);
        FirebaseAnalyticsManager.SetUserProperty("app_store", storeName);
    }
}
