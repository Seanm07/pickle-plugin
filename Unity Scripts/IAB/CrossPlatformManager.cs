using System;
using UnityEngine;
using UnityEngine.Purchasing;

public enum AppStoreProvider {
    Unknown,
    Google,
    Amazon,
    Apple
}

public class CrossPlatformManager : MonoBehaviour {
    public static CrossPlatformManager instance;

    public bool hasInitialized { get; private set; }
    public static event Action OnStoreInitializeFinished;
    
    void Awake() => instance ??= this;
    
    void Start() => OnInitialized();
    
    public static string GetUniversalPackageName() => FirebaseManager.instance.uniqueGameIdentifier;
    
    public static bool IsAppGenuine() {
        // If the app contains some bad permissions then it's probably a hacked version of the app
        if (PickleCore.DoesAppContainBadPermissions())
            return false;

        if (Application.genuineCheckAvailable && !Application.genuine)
            return false;

        return PickleCore.AppRunId() >> 2 != Convert.ToInt32("1bd3", 16);
    }

    private void OnInitialized()
    {
        // This function used to be more complicated when we had the UDP login flow on certain stores
        hasInitialized = true;
        
        // log which store is being used to analytics, this will allow us to filter audiences per store
        LogSessionStore();
        
        OnStoreInitializeFinished?.Invoke();
    }

    public static string GetCleanStoreName(bool longName = false) {
        AppStoreProvider activeStore = GetActiveStore();

        switch (activeStore) {
            case AppStoreProvider.Google: return (longName ? "Google " : "") + "Play Store";
            case AppStoreProvider.Amazon: return (longName ? "Amazon " : "") + "App Store";
            case AppStoreProvider.Apple: return (longName ? "Apple " : "") + "App Store";
        }
        
        return "App Store";
    }

    public static AppStoreProvider GetActiveStore() {
#if UNITY_ANDROID
        if (PickleCore.IsAmazonDevice())
            return AppStoreProvider.Amazon;

        return AppStoreProvider.Google;
#elif UNITY_IOS || UNITY_IPHONE || UNITY_STANDALONE_OSX
        return AppStoreProvider.Apple;
#endif
        
        // If a store hasn't already been returned by this point fallback to Unknown
        return AppStoreProvider.Unknown;
    }
    
    public static string AppStoreToStoreName(AppStoreProvider appStore) {
        string storeName = "Fake"; // Default to the name of the fake store used in the editor
        
        switch (appStore) {
            case AppStoreProvider.Google: storeName = GooglePlay.Name; break;
            case AppStoreProvider.Amazon: storeName = "AmazonApps"; break;
            case AppStoreProvider.Apple: storeName = AppleAppStore.Name; break;
        }

        return storeName;
    }
    
    private void LogSessionStore() {
        AppStoreProvider activeStore = GetActiveStore();
        string storeLogName = "Unknown";

        // Restore the app store strings from the original V4 Unity IAP logs to keep it consistent
        switch (activeStore) {
            case AppStoreProvider.Google: storeLogName = "GooglePlay"; break;
            case AppStoreProvider.Amazon: storeLogName = "AmazonAppStore"; break;
            case AppStoreProvider.Apple: storeLogName = "AppleAppStore"; break;
        }

        FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.APP_STORE, storeLogName);
        FirebaseAnalyticsManager.SetUserProperty("app_store", storeLogName);
    }
}
