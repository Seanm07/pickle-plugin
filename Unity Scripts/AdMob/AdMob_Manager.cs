using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using GoogleMobileAds.Mediation.Chartboost.Api;
using GoogleMobileAds.Mediation.Mintegral.Api;
using GoogleMobileAds.Mediation.UnityAds.Api;
using GoogleMobileAds.Ump.Api;
using UnityEngine.Purchasing;

#if IVMETRICS_EVENTS_ENABLED
    using IVMetrics;
#endif

public class AdMob_Manager : MonoBehaviour {
    #if UNITY_IOS
        // Function for getting the advertising identifier of the current device on iOS for testing ads
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string GetIOSAdvertisingIdentifier();
    #endif
    
    [Serializable]
    public class PlatformData {
        public PlatformAdData AndroidAdData;
        public PlatformAdData ThirdPartyAndroidAdData;
        public PlatformAdData IosAdData;

        public PlatformAdData GetActive() {
            switch (Application.platform) {
                case RuntimePlatform.Android: return CrossPlatformManager.GetActiveStore() == AppStore.GooglePlay ? AndroidAdData : ThirdPartyAndroidAdData;
                case RuntimePlatform.IPhonePlayer: return IosAdData;

                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.OSXEditor:
                    #if UNITY_ANDROID
						return AndroidAdData;
					#elif UNITY_IOS
						return IosAdData;
					#else
						return AndroidAdData;
					#endif
				
				case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.OSXPlayer:
                    return AndroidAdData;
            }

            Debug.LogError("Unknown runtime platform! We don't know which admob ids to use!");
            return null;
        }
    }

    [Serializable]
    public class AdFloorIDs {
        [Header("Set multiple ids to use floors where element 0 is highest paying")]
        public string[] floorId = new string[0];
    }

    [Serializable]
    public class PlatformAdData {
        [Header("AdMob Ad Ids")] public string appId = "ca-app-pub-xxxxxxxxxxxxxxxx~xxxxxxxxxx";

        // Each ad id is an array, allowing us to load multiple ads into memory at once (for example we might want 2 screens right after each other with reward ads or might want to keep smart banners and square dynamic banners ready at all times for switching between screens)
        // Using different ids is optional, if you just want to use multiple ads with the same id just add the same id again in the array

        [Header("Multiple ids per slot supported for prioritising higher value ads first")]
        public AdFloorIDs[] interstitialFloorData = new AdFloorIDs[0];

        public AdFloorIDs[] bannerFloorData = new AdFloorIDs[0];
        public AdFloorIDs[] rewardFloorData = new AdFloorIDs[0];
        public AdFloorIDs[] rewardedInterstitialFloorData = new AdFloorIDs[0];

        [Header("Additional Ad Settings")]
        // TagForChildDirectedTreatment will stop admob tracking this user and will not deliver interest based ads
        // This will reduce revenue for the game so don't set this unless specifically asked to!
        public bool tagForChildDirectedTreatment = false;
    }

    public static AdMob_Manager instance;

    [Header("AdMob Script Settings")] public bool enableAdMob = true; // Is admob enabled? Will any banners or interstitials be triggered?
    public bool debugLogging = false; // Should debug messages be logged?
    public bool enableTestMode = false; // Test mode will display test ads which we are allowed to click

    [Header("Platform Ad Configuration")] public PlatformData platformAdData = new PlatformData();

    public bool hasAdMobInitializeBeenCalled { get; set; }
    public bool isAdMobInitialized { get; set; }

    public enum BannerSizeType { NONE, SMARTBANNER, BANNER, LEADERBOARD, MEDIUMRECTANGLE }

    [Header("Preloaded Banner Settings")] public BannerSizeType mainPreloadBannerType = BannerSizeType.SMARTBANNER;
    public bool mainDisplayPreloadBannerImmediately = false;

    [Header("Call ShowBannerAd(..) with x and y at runtime to manually position this preloaded ad!")]
    public AdPosition mainPreloadBannerPosition;

    public BannerSizeType[] preloadBannerType { get; private set; } // Note: Banner ads need to be destroyed when changing type so make sure the first banner ad will actually be the first ad type used or we'll just end up loading then destroying it for no reason
    public bool[] displayPreloadBannerImmediately { get; private set; }
    public AdPosition[] preloadBannerPosition { get; private set; }


    [Header("Pre-Interstitial Settings")] public bool useInterstitialWaitScreen = true;
    public GameObject interstitialWaitScreen; // Screen to show before an interstitial pops
    public float interstitialWaitTime = 1f; // Time to wait before displaying interstitial after InterstitialWaitScreen has appeared

    // WARNING: As of early 2019 setting this value too low can make the ads start returning no fill without even trying to check for ads (temporary blacklist)
    [Header("Misc Settings")] public float timeBetweenAdLoadRetry = 10f;

    // How many attempts will be made to reload ads which fail to load (a manual ad load request will always happen regardless of retries made)
    // But note that the retry count only resets when an ad request returns a valid response
    public int maxAutomaticAdLoadRetries = 5;

    // Smart banners are deprecated, when this is false any smart banner requests automatically get upgraded to dynamic adaptive banners
    public bool useLegacySmartBanners = false;

    // Information about the interstitial state
    public bool[] intIsReady { get; private set; }
    public bool[] intIsLoading { get; private set; }
    public bool[] intIsVisible { get; private set; }
    public bool[] intWantedVisible { get; private set; }
    public int[] intFloorGroupAttemptedRetries { get; private set; }

    // Information about the banner state
    public bool[] bannerIsReady { get; private set; }
    public bool[] bannerIsLoading { get; private set; }
    public bool[] bannerIsVisible { get; private set; }
    public bool[] bannerWantedVisible { get; private set; }
    private int[] bannerFloorGroupAttemptedRetries;

    public bool[] rewardIsReady { get; private set; }
    public bool[] rewardIsLoading { get; private set; }
    public bool[] rewardIsVisible { get; private set; }
    public bool[] rewardWantedVisible { get; private set; }
    public int[] rewardFloorGroupAttemptedRetries { get; private set; }

    public bool[] rewardedIntIsReady { get; private set; }
    public bool[] rewardedIntIsLoading { get; private set; }
    public bool[] rewardedIntIsVisible { get; private set; }
    public bool[] rewardedIntWantedVisible { get; private set; }
    public int[] rewardedIntFloorGroupAttemptedRetries { get; private set; }

    public int totalIntSlots { get; private set; }
    public int totalBannerSlots { get; private set; }
    public int totalRewardSlots { get; private set; }
    public int totalRewardedIntSlots { get; private set; }

    public int[] totalIntFloors { get; private set; }
    public int[] totalBannerFloors { get; private set; }
    public int[] totalRewardFloors { get; private set; }
    public int[] totalRewardedIntFloors { get; private set; }

    public int intWantedFloorId { get; private set; }
    public int bannerWantedFloorId { get; private set; }
    public int rewardWantedFloorId { get; private set; }
    public int rewardedIntWantedFloorId { get; private set; }

    // If we're hiding a banner due to an overlay (popup box or backscreen) then we want to remember the ad state when that is closed
    public bool bannerPrevState { get; private set; }

    // Sometimes we like to overlay our overlays but still want to remember our original banner state
    public int bannerOverlayDepth { get; set; }

    private bool hasPurchasedAdRemoval;

    private const string PREF_PERSONALISATION = "personalisation_granted";
    private const string PREF_AD_REMOVAL = "adremoval_purchased";
    private const string PREF_MATURE = "mature_user";

    // Ads loaded into memory
    private InterstitialAd[] adMobInterstitial;
    private BannerView[] adMobBanner;
    private RewardedAd[] adMobRewardedAd;
    private RewardedInterstitialAd[] adMobRewardedInterstitialAd;

    private struct IntVector2 {
        public int x { get; private set; }
        public int y { get; private set; }

        public IntVector2(int x, int y) {
            this.x = x;
            this.y = y;
        }
    }

    // Cache the type of the current banner in memory so we can process calls to LoadBanner again to change certain things without needing to actually request another banner
    private AdSize[] bannerInMemorySize;
    private AdPosition[] bannerInMemoryPosition;
    private IntVector2[] bannerInMemoryPositionXY;

    private bool[] bannerInMemorySizeSet;
    private bool[] bannerInMemoryPositionSet;
    private bool[] bannerInMemoryUseXYPosition;

    private float cachedPointScaleFactor;
    private bool pointScaleFactorSet;

    private PlatformAdData cachedPlatformDataRef;

    // Used to keep track of first loaded ad times for analytics
    private bool hasLoadedAnyInt = false;
    private bool hasLoadedAnyBanner = false;
    private bool hasLoadedAnyReward = false;
    private bool hasLoadedAnyRewardedInt = false;

    // Universal interstitial ad callbacks
    public static event Action<InterstitialAd> OnInterstitialAdReady;
    public static event Action OnInterstitialAdShown;
    public static event Action OnInterstitialAdClosed;
    public static event Action<LoadAdError> OnInterstitialAdFailedToLoad;

    // Universal banner ad callbacks
    public static event Action<BannerView> OnBannerAdReady;
    public static event Action OnBannerAdShown;
    public static event Action OnBannerAdClosed;
    public static event Action<LoadAdError> OnBannerAdFailedToLoad;

    // EventHandlers which allow us to send custom banner callbacks which include the bannerRefId
    private EventHandler<EventArgs>[] bannerOnAdLoadedCallback;
    private EventHandler<EventArgs>[] bannerOnAdOpeningCallback;
    private EventHandler<EventArgs>[] bannerOnAdClosedCallback;
    private EventHandler<LoadAdError>[] bannerOnAdFailedToLoadCallback;

    // Universal reward ad callbacks
    public static event Action<RewardedAd> OnRewardAdReady;
    public static event Action OnRewardAdShown;
    public static event Action<Reward> OnRewardAdRewarded;
    public static event Action OnRewardAdClosed;
    public static event Action OnRewardAdFailedToShow;
    public static event Action<LoadAdError> OnRewardAdFailedToLoad;

    // Universal rewarded interstitial ad callbacks
    public static event Action<RewardedInterstitialAd> OnRewardedInterstitialAdReady;
    public static event Action OnRewardedInterstitialAdShown;
    public static event Action<Reward> OnRewardedInterstitialAdRewarded;
    public static event Action OnRewardedInterstitialAdClosed;
    public static event Action<AdError> OnRewardedInterstitialAdFailedToShow;
    public static event Action<LoadAdError> OnRewardedInterstitialAdFailedToLoad;

    // Private actual admob plugin initialisation complete (we don't announce initialization to be done until waiting a few extra frames for the mediation to be ready too)
    private static event Action<InitializationStatus> OnInitializationComplete;

    // Public callback to say admob is ready, this is called after initialisation AND mediation is ready
    // (if we don't wait for mediation to be ready before making ad calls we risk some mediators causing app crashes!)
    public static event Action OnAdMobReady;

    public float scrDPI { get; private set; }

    private PlatformAdData GetPlatformAdData() {
        if (CrossPlatformManager.instance.hasInitialized) {
            cachedPlatformDataRef = cachedPlatformDataRef ?? platformAdData.GetActive();

            return cachedPlatformDataRef;
        } else {
            return platformAdData.GetActive();
        }
    }

    public void DebugSetRewardReadyEditor(bool wantReady, int rewardRefId = 0) {
        #if UNITY_EDITOR
            rewardIsReady[rewardRefId] = wantReady;
        #endif
    }

    private void Awake() {
        instance = instance ?? this;

        // If a previous session set the admob_test_mode key or the device name contains i6apptester force admob into test mode and enable logging
        if (PlayerPrefs.GetInt("admob_test_mode", 0) == 1 || SystemInfo.deviceName.Contains("i6apptester")) {
            enableTestMode = true;
            debugLogging = true;
        }

        // Load the ad removal status and maturity status
        hasPurchasedAdRemoval = PlayerPrefs.GetInt(PREF_AD_REMOVAL, 0) == 1;
    }

    private void Start() {
        // We support loading from multiple ad ids to load different types of ads without needing to destroy them to change type and letting us display ads back to back if needed
        // Here we initialise banner related arrays for each bannerRefId type
        PlatformAdData adData = GetPlatformAdData();

        totalIntSlots = adData.interstitialFloorData.Length;
        totalBannerSlots = adData.bannerFloorData.Length;
        totalRewardSlots = adData.rewardFloorData.Length;
        totalRewardedIntSlots = adData.rewardedInterstitialFloorData.Length;

        totalIntFloors = new int[totalIntSlots];
        totalBannerFloors = new int[totalBannerSlots];
        totalRewardFloors = new int[totalRewardSlots];
        totalRewardedIntFloors = new int[totalRewardedIntSlots];

        for (int i = 0; i < totalIntSlots; i++)
            totalIntFloors[i] = adData.interstitialFloorData[i].floorId.Length;
        for (int i = 0; i < totalBannerSlots; i++)
            totalBannerFloors[i] = adData.bannerFloorData[i].floorId.Length;
        for (int i = 0; i < totalRewardSlots; i++)
            totalRewardFloors[i] = adData.rewardFloorData[i].floorId.Length;
        for (int i = 0; i < totalRewardedIntSlots; i++)
            totalRewardedIntFloors[i] = adData.rewardedInterstitialFloorData[i].floorId.Length;

        preloadBannerType = new BannerSizeType[totalBannerSlots];
        displayPreloadBannerImmediately = new bool[totalBannerSlots];
        preloadBannerPosition = new AdPosition[totalBannerSlots];

        preloadBannerType[0] = mainPreloadBannerType;
        displayPreloadBannerImmediately[0] = mainDisplayPreloadBannerImmediately;
        preloadBannerPosition[0] = mainPreloadBannerPosition;

        adMobInterstitial = new InterstitialAd[totalIntSlots];
        intIsReady = new bool[totalIntSlots];
        intIsLoading = new bool[totalIntSlots];
        intIsVisible = new bool[totalIntSlots];
        intWantedVisible = new bool[totalIntSlots];
        intFloorGroupAttemptedRetries = new int[totalIntSlots];

        adMobBanner = new BannerView[totalBannerSlots];
        bannerIsReady = new bool[totalBannerSlots];
        bannerIsLoading = new bool[totalBannerSlots];
        bannerIsVisible = new bool[totalBannerSlots];
        bannerWantedVisible = new bool[totalBannerSlots];
        bannerFloorGroupAttemptedRetries = new int[totalBannerSlots];

        bannerInMemorySize = new AdSize[totalBannerSlots];
        bannerInMemoryPosition = new AdPosition[totalBannerSlots];
        bannerInMemorySizeSet = new bool[totalBannerSlots];
        bannerInMemoryPositionSet = new bool[totalBannerSlots];

        bannerInMemoryUseXYPosition = new bool[totalBannerSlots];
        bannerInMemoryPositionXY = new IntVector2[totalBannerSlots];

        adMobRewardedAd = new RewardedAd[totalRewardSlots];
        rewardIsReady = new bool[totalRewardSlots];
        rewardIsLoading = new bool[totalRewardSlots];
        rewardIsVisible = new bool[totalRewardSlots];
        rewardWantedVisible = new bool[totalRewardSlots];
        rewardFloorGroupAttemptedRetries = new int[totalRewardSlots];

        adMobRewardedInterstitialAd = new RewardedInterstitialAd[totalRewardedIntSlots];
        rewardedIntIsReady = new bool[totalRewardedIntSlots];
        rewardedIntIsLoading = new bool[totalRewardedIntSlots];
        rewardedIntIsVisible = new bool [totalRewardedIntSlots];
        rewardedIntWantedVisible = new bool[totalRewardedIntSlots];
        rewardedIntFloorGroupAttemptedRetries = new int[totalRewardedIntSlots];

        // Get the screen dots per inch
        scrDPI = PickleCore.GetDensity();

        // If the screen DPI can't be calculated then fallback to default and hope the ad won't overlap
        // Really this should never happen as we're checking the raw device dpi via Java and if for some reason that fails we then fallback to Unity's Screen.dpi
        // Unity 5 and earlier had issues where Screen.dpi would fail on a bunch of devices which is why switched over to grabbing the system dpi manually via Java
        if (scrDPI <= 0) {
            Debug.LogError("DPI checks failed! Falling back to default!");
            scrDPI = 160f;
        }
    }

    public void InitializeAdMob() {
        if (enableAdMob) {
            hasAdMobInitializeBeenCalled = true;

            if (debugLogging) {
                Debug.Log("AdMob Debug - App ID: " + GetPlatformAdData().appId);
                Debug.Log("AdMob Debug - Child Directed Treatment: " + GetPlatformAdData().tagForChildDirectedTreatment);
                Debug.Log("AdMob Debug - Ad Removal Purchased: " + hasPurchasedAdRemoval);
            }

            #if UNITY_EDITOR
                // Fake an initialisation complete call while in the editor
                StartCoroutine(EditorScheduleFakeInitializationFinished());
            #else
                // Trying to initialize in the editor causes a NuLLReferenceException due to the initialisation status being null
                OnInitializationComplete += OnInitializationFinished;

                // Force pauses the app while interstitials are open on iOS, this also mutes game audio
                MobileAds.SetiOSAppPauseOnBackground(true);

                RequestConfiguration adRequestConfiguration = new RequestConfiguration();

                if (enableTestMode) {
                    // All simulators are marked as test devices
                    adRequestConfiguration.TestDeviceIds.Add(AdRequest.TestDeviceSimulator);
                    
                    // Add the current device as a tester
                    adRequestConfiguration.TestDeviceIds.Add(GetTestDeviceId());
                }

                bool childDirectedTreatment = GetPlatformAdData().tagForChildDirectedTreatment;
                
                adRequestConfiguration.TagForUnderAgeOfConsent = childDirectedTreatment ? TagForUnderAgeOfConsent.True : TagForUnderAgeOfConsent.False;
                adRequestConfiguration.TagForChildDirectedTreatment = childDirectedTreatment ? TagForChildDirectedTreatment.True : TagForChildDirectedTreatment.False;

                MobileAds.SetRequestConfiguration(adRequestConfiguration);

                // Manually initialise admob with an ad manager
                // Initializing AdMob can cause ANRs so make sure the app is doing as little as possible when initializing
                MobileAds.Initialize(OnInitializationComplete);
            #endif
        } else {
            Debug.Log("AdMob is not enabled, no adverts will be triggered!");
        }
    }

    public string GetAdvertisingId() {
        #if UNITY_EDITOR
            return "EDITOR_ADVERTISING_ID";
        #endif
        
        #if UNITY_ANDROID
            using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject contentResolver = currentActivity.Call<AndroidJavaObject>("getContentResolver");
            using AndroidJavaClass settingsSecure = new AndroidJavaClass("android.provider.Settings$Secure");
            
            if (CrossPlatformManager.GetActiveStore() == AppStore.AmazonAppStore) {
                // Amazon uses a different string in Settings.Secure to store the advertising id (OAID)
                return settingsSecure.CallStatic<string>("getString", contentResolver, "advertising_id");
            } else {
                // Google Play uses the android_id string in Settings.Secure to store the advertising id (GAID)
                return settingsSecure.CallStatic<string>("getString", contentResolver, "android_id");
            }
        #elif UNITY_IOS
            // Get the iOS device id (IDFA) via ASIdentifierManager > advertisingIdentifier
            return GetIOSAdvertisingIdentifier();
        #endif
    }
    
    public string GetTestDeviceId() {
        try {
            string deviceId = GetAdvertisingId();
            
            #if UNITY_ANDROID
                // Android uses MD5 hashing converted to uppercase
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create()) {
                    byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(deviceId);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach(byte hashByte in hashBytes)
                        sb.Append(hashByte.ToString("X2"));

                    return sb.ToString().ToUpperInvariant();
                }
            #else
                // iOS uses MD5 hashing converted to lowercase
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create()) {
                    byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(deviceId);
                    byte[] hashBytes = md5.ComputeHash(inputBytes);

                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach(byte hashByte in hashBytes)
                        sb.Append(hashByte.ToString("X2"));

                    return sb.ToString().ToLowerInvariant();
                }
            #endif
        } catch (Exception e) {
            Debug.LogError("Failed to get admob device test id: " + e.Message);
        }

        return string.Empty;
    }

    // AdMob banners on android use dp scaling
    public float PxToDp(float pixels) {
        return 160f * pixels / scrDPI;
    }

    public float DpToPx(float dp) {
        return dp * scrDPI / 160f;
    }

    // AdMob banners on iOS use pt scaling
    public float PxToPt(float pixels) {
        return pixels * GetPixelToPointFactor();
    }
    
    public float PtToPx(float pt) {
        return pt / GetPixelToPointFactor();
    }

    #if UNITY_EDITOR
    private IEnumerator EditorScheduleFakeInitializationFinished() {
        yield return new WaitForSecondsRealtime(1f);

        OnInitializationFinished(default);
    }
    #endif

    // AdMob callback - !! not guaranteed to be called on main thread !!
    private void OnInitializationFinished(InitializationStatus status) {
        if (enableAdMob)
            MobileAdsEventExecutor.ExecuteInUpdate(() => AdMobInitializedMainThread(status));
    }

    private void AdMobInitializedMainThread(InitializationStatus status) {
        FirebaseAnalyticsManager.LogCustomEvent(PickleEventCategory.PickleScripts.ADMOB_TTL.ToString(), "init_partial", Time.realtimeSinceStartup);

        StartCoroutine(DelayedInitialisationCompletion(status));
    }

    // Extra waiting for mediation plugins to become ready to fix crashes caused by trying to load ads before mediation finished initializing
    private IEnumerator DelayedInitialisationCompletion(InitializationStatus status) {
        if (debugLogging)
            Debug.Log("AdMob Debug - AdMob Initialization Complete!");

        if (status != null) {
            Dictionary<string, AdapterStatus> adapterStatuses = status.getAdapterStatusMap();

            if (adapterStatuses != null) {
                foreach (KeyValuePair<string, AdapterStatus> adapterStatus in adapterStatuses) {
                    if (debugLogging) {
                        Debug.Log("AdMob Debug - =======================================");
                        Debug.Log("AdMob Debug - AdMob Adapter: " + adapterStatus.Key);
                        Debug.Log("AdMob Debug - " + adapterStatus.Value.Description);
                    }

                    int mediationWaitFrames = 0;

                    // Wait up to 5 frames for the mediation adapter to be ready
                    while (mediationWaitFrames < 5 && adapterStatus.Value.InitializationState == AdapterState.NotReady) {
                        mediationWaitFrames++;
                        yield return null;
                    }

                    if (adapterStatus.Value.InitializationState == AdapterState.Ready) {
                        if (debugLogging)
                            Debug.Log("AdMob Debug - Waited " + mediationWaitFrames + " frames until " + adapterStatus.Key + " was ready!");
                    } else {
                        // Always logs
                        Debug.LogError("Waited " + mediationWaitFrames + " frames but " + adapterStatus.Key + " never became ready..");
                    }

                    if (debugLogging)
                        Debug.Log("=======================================");
                }
            }
        }

        yield return null;

        // Mark admob as ready to load adverts
        isAdMobInitialized = true;

        FirebaseAnalyticsManager.LogCustomEvent(PickleEventCategory.PickleScripts.ADMOB_TTL.ToString(), "init_full", Time.realtimeSinceStartup);

        if (OnAdMobReady != null)
            OnAdMobReady.Invoke();

        // Reward and rewarded interstitials always preload regardless of whether ad removal is purchased
        if (totalRewardSlots > 0)
            PreloadRewardAd();

        if (totalRewardedIntSlots > 0)
            PreloadRewardedInterstitialAd();

        if (!hasPurchasedAdRemoval) {
            // If we've already sent a manual request to load ads before initialisation finished, don't bother calling the preload banner function
            if (!bannerIsLoading[activeBannerRefId])
                PreloadBannerAd();

            // There's no harm in attempting to load interstitials again even if one is already pending
            PreloadInterstitialAd();
        }
    }

    private void PreloadRewardedInterstitialAd() {
        for (int i = 0; i < totalRewardedIntSlots; i++)
            LoadRewardedInterstitialAd(false, i);
    }

    private void PreloadRewardAd() {
        for (int i = 0; i < totalRewardSlots; i++)
            LoadRewardAd(false, i);
    }

    private void PreloadBannerAd() {
        for (int i = 0; i < totalBannerSlots; i++) {
            if (preloadBannerType[i] != BannerSizeType.NONE) {
                AdSize adSize = AdSize.SmartBanner;

                switch (preloadBannerType[i]) {
                    case BannerSizeType.BANNER:
                        adSize = AdSize.Banner;
                        break;
                    case BannerSizeType.LEADERBOARD:
                        adSize = AdSize.Leaderboard;
                        break;
                    case BannerSizeType.MEDIUMRECTANGLE:
                        adSize = AdSize.MediumRectangle;
                        break;

                    default:
                    case BannerSizeType.SMARTBANNER:
                        adSize = AdSize.SmartBanner;
                        break;
                }

                // Future calls to load or show banner even in a different AdPosition will be ready as this banner will be displayed
                // If the position is different then it'll be moved with .RepositionBannerAd without needing to reload the ad
                LoadBannerAd(adSize, preloadBannerPosition[i], displayPreloadBannerImmediately[i] ? true : bannerWantedVisible[i], i);
            }
        }
    }

    private void PreloadInterstitialAd() {
        for (int i = 0; i < totalIntSlots; i++)
            LoadInterstitialAd(false, true, i);
    }

    private void SendRewardAdLoadRequest(int rewardRefId = 0) {
        if (rewardRefId >= adMobRewardedAd.Length) {
            Debug.LogError(rewardRefId + " is an invalid reward ad ref id, increase the size of the rewardId array to increase how many reward ads can be loaded at once.");
            return;
        }

        if (adMobRewardedAd[rewardRefId] != null)
            adMobRewardedAd[rewardRefId].Destroy();

        string adUnitId = GetPlatformAdData().rewardFloorData[rewardRefId].floorId[rewardWantedFloorId];
        
        RewardedAd.Load(adUnitId, GenerateAdRequest(),
            (RewardedAd ad, LoadAdError loadError) => {
                if (loadError != null || ad == null) {
                    MobileAdsEventExecutor.ExecuteInUpdate(() => RewardAdFailedToLoad(loadError, rewardRefId)); // Ad failed to load callback
                    return;
                }

                adMobRewardedAd[rewardRefId] = ad;
                
                MobileAdsEventExecutor.ExecuteInUpdate(() => RewardAdLoaded(rewardRefId)); // Ad loaded callback
                ad.OnAdFullScreenContentOpened += () => MobileAdsEventExecutor.ExecuteInUpdate(() => RewardAdVisible(rewardRefId)); // Ad opening callback
                ad.OnAdFullScreenContentClosed += () => MobileAdsEventExecutor.ExecuteInUpdate(() => RewardAdClosed(rewardRefId)); // Ad closed callback
                // Note: Reward ad rewarded and failed to show callbacks are now handled when calling to show the reward ad
                ad.OnAdPaid += (adValue) =>  MobileAdsEventExecutor.ExecuteInUpdate(() => AdPaidEvent(adUnitId, ad.GetResponseInfo(), adValue));
            }
        );
    }

    private void SendRewardedInterstitialLoadRequest(int rewardedIntRefId = 0) {
        if (rewardedIntRefId >= adMobRewardedInterstitialAd.Length) {
            Debug.LogError(rewardedIntRefId + " is an invalid rewarded interstitial ad ref id, increase the size of the rewardedIntId array to increase how many rewarded interstitial ads can be loaded at once.");
            return;
        }

        if (adMobRewardedInterstitialAd[rewardedIntRefId] != null)
            adMobRewardedInterstitialAd[rewardedIntRefId].Destroy();

        string adUnitId = GetPlatformAdData().rewardedInterstitialFloorData[rewardedIntRefId].floorId[rewardedIntWantedFloorId];
        
        RewardedInterstitialAd.Load(adUnitId, GenerateAdRequest(),
            (RewardedInterstitialAd ad, LoadAdError loadError) => {
                if (loadError != null || ad == null) {
                    MobileAdsEventExecutor.ExecuteInUpdate(() => RewardedInterstitialAdFailedToLoad(loadError, rewardedIntRefId)); // Ad failed to load callback
                    return;
                }

                adMobRewardedInterstitialAd[rewardedIntRefId] = ad;
                
                MobileAdsEventExecutor.ExecuteInUpdate(() => RewardedInterstitialAdLoaded(rewardedIntRefId)); // Ad loaded callback
                ad.OnAdFullScreenContentOpened += () => MobileAdsEventExecutor.ExecuteInUpdate(() => RewardedInterstitialAdVisible(rewardedIntRefId));
                ad.OnAdFullScreenContentClosed += () => MobileAdsEventExecutor.ExecuteInUpdate(() => RewardedInterstitialAdClosed(rewardedIntRefId));
                // Note: Rewarded interstitial rewarded and failed to show callbacks are now handled when calling to show the reward ad
                ad.OnAdPaid  += (adValue) => MobileAdsEventExecutor.ExecuteInUpdate(() => AdPaidEvent(adUnitId, ad.GetResponseInfo(), adValue));
            }
        );
    }

    private bool IsStandardBannerSize(AdSize adSize) {
        return (adSize == AdSize.Banner || adSize == AdSize.Leaderboard || adSize == AdSize.MediumRectangle || adSize == AdSize.SmartBanner || adSize == AdSize.IABBanner);
    }

    private void SendBannerAdLoadRequest(AdSize adSize, int xPos, int yPos, int bannerRefId = 0) {
        SendBannerAdLoadRequest(adSize, AdPosition.TopLeft, xPos, yPos, bannerRefId);
    }

    private void SendBannerAdLoadRequest(AdSize adSize, AdPosition adPosition, int bannerRefId = 0) {
        SendBannerAdLoadRequest(adSize, adPosition, 0, 0, bannerRefId);
    }

    private void SendBannerAdLoadRequest(AdSize adSize, AdPosition adPosition, int xPos, int yPos, int bannerRefId) {
        if (bannerRefId >= adMobBanner.Length) {
            Debug.LogError(bannerRefId + " is an invalid banner ad ref id, increase the size of the bannerId array to increase how many banner ads can be loaded at once.");
            return;
        }

        AdSize actualAdSize = adSize;

        // If useLegacySmartBanners is not enabled then SmartBanners will be converted into adaptive banner sizes
        if (!useLegacySmartBanners && adSize == AdSize.SmartBanner)
            actualAdSize = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);

        #if !UNITY_EDITOR
            // Loading a new banner size on mobile requires destroying the previous banner
            if(adMobBanner[bannerRefId] != null)
                adMobBanner[bannerRefId].Destroy();
        #else
            // In the editor admob preview we can just hide the old banner when switching banner size
            if (adMobBanner[bannerRefId] != null)
                adMobBanner[bannerRefId].Hide();
        #endif

        string adUnitId = GetPlatformAdData().bannerFloorData[bannerRefId].floorId[bannerWantedFloorId];
        
        // If the adPosition is TopLeft use the x and y positions to set the ad offset
        if (adPosition == AdPosition.TopLeft) {
            adMobBanner[bannerRefId] = new BannerView(adUnitId, actualAdSize, xPos, yPos);
        } else {
            adMobBanner[bannerRefId] = new BannerView(adUnitId, actualAdSize, adPosition);
        }
        
        adMobBanner[bannerRefId].LoadAd(GenerateAdRequest());

        // Fixes a bug where banners would flash on the screen for a frame when being loaded and creating a dummy banner in the editor..
        adMobBanner[bannerRefId].Hide();

        bannerIsLoading[bannerRefId] = true;
        bannerIsVisible[bannerRefId] = false;
        bannerIsReady[bannerRefId] = false;

        bannerInMemorySize[bannerRefId] = adSize; // Keeps reference to initial requested adSize
        bannerInMemorySizeSet[bannerRefId] = true;

        bannerInMemoryPosition[bannerRefId] = adPosition;
        bannerInMemoryPositionSet[bannerRefId] = true;

        bannerInMemoryPositionXY[bannerRefId] = new IntVector2(xPos, yPos);
        bannerInMemoryUseXYPosition[bannerRefId] = adPosition == AdPosition.TopLeft;

        adMobBanner[bannerRefId].OnAdPaid += (adValue) => MobileAdsEventExecutor.ExecuteInUpdate(() => AdPaidEvent(adUnitId, adMobBanner[bannerRefId].GetResponseInfo(), adValue));
        
        #if !UNITY_EDITOR
            adMobBanner[bannerRefId].OnBannerAdLoaded += () => MobileAdsEventExecutor.ExecuteInUpdate(() => BannerAdLoaded(bannerRefId));
            adMobBanner[bannerRefId].OnBannerAdLoadFailed += (LoadAdError error) => MobileAdsEventExecutor.ExecuteInUpdate(() => BannerAdFailedToLoad(error, bannerRefId));
        #else
            // Callbacks don't trigger in the editor so just trigger a success response now
            MobileAdsEventExecutor.ExecuteInUpdate(() => BannerAdLoaded(bannerRefId));
        #endif
    }

    // Send the request to load the interstitial ad and setup callbacks related to loading the ad
    private void SendInterstitialLoadRequest(int interstitialRefId = 0) {
        if (interstitialRefId >= adMobInterstitial.Length) {
            Debug.LogError(interstitialRefId + " is an invalid interstitial ref id, increase the size of the interstitialId array to increase how many interstitials can be loaded at once.");
            return;
        }

        // Cleanup the previous interstitial (if exists)
        if (adMobInterstitial[interstitialRefId] != null)
            adMobInterstitial[interstitialRefId].Destroy();

        string adUnitId = GetPlatformAdData().interstitialFloorData[interstitialRefId].floorId[intWantedFloorId];
        
        InterstitialAd.Load(adUnitId, GenerateAdRequest(),
            (InterstitialAd ad, LoadAdError loadError) => {
                if (loadError != null || ad == null) {
                    MobileAdsEventExecutor.ExecuteInUpdate(() => InterstitialAdFailedToLoad(loadError, interstitialRefId)); // Ad failed to load callback
                    return;
                }

                adMobInterstitial[interstitialRefId] = ad;
                MobileAdsEventExecutor.ExecuteInUpdate(() => InterstitialAdLoaded(interstitialRefId)); // Ad loaded callback
                ad.OnAdFullScreenContentOpened += () => MobileAdsEventExecutor.ExecuteInUpdate(() => InterstitialAdVisible(interstitialRefId)); // Ad opening callback
                ad.OnAdFullScreenContentClosed += () => MobileAdsEventExecutor.ExecuteInUpdate(() => InterstitialAdClosed(interstitialRefId)); // Ad closed callback
                ad.OnAdPaid += (adValue) => MobileAdsEventExecutor.ExecuteInUpdate(() => AdPaidEvent(adUnitId, ad.GetResponseInfo(), adValue));
            }
        );
    }

    private AdRequest GenerateAdRequest() {
        AdRequest adBuilder = new AdRequest();

        // Editor mediation consent calls do a lot of debug logging which we don't need to see
        // Not using editor define so we get editor script errors when plugins missing
        if (!Application.isEditor) {
            bool isGrantedPersonalisation = IsPersonalisationGranted();
            
            Chartboost.AddDataUseConsent(isGrantedPersonalisation ? CBGDPRDataUseConsent.Behavioral : CBGDPRDataUseConsent.NonBehavioral);

            if (isGrantedPersonalisation)
                Chartboost.AddDataUseConsent(CBCCPADataUseConsent.OptInSale);

            UnityAds.SetConsentMetaData("gdpr.consent", isGrantedPersonalisation);

            Mintegral.SetConsentStatus(isGrantedPersonalisation);
        }

        return adBuilder;
    }

    public void SetAdRemovalPurchased(bool isPurchased) {
        PlayerPrefs.SetInt(PREF_AD_REMOVAL, isPurchased ? 1 : 0);
        hasPurchasedAdRemoval = isPurchased;
        PlayerPrefs.Save();

        if (FirebaseManager.instance.debugMode)
            Debug.Log("AdMob Debug - Ad removal purchase state set to: " + isPurchased);
    }

    public bool IsAdRemovalPurchased() {
        return hasPurchasedAdRemoval;
    }

    public bool IsPersonalisationGranted() {
        ConsentStatus consentStatus = ConsentInformation.ConsentStatus;

        if(debugLogging)
            Debug.Log("Consent status: " + consentStatus);
        
        // If the consent status is unknown, granted or not required return true
        return consentStatus != ConsentStatus.Required;
    }

    public bool IsTaggedForChildDirectedTreatment() {
        return GetPlatformAdData().tagForChildDirectedTreatment;
    }

    public void LoadRewardedInterstitialAd(bool displayImmediately = false, int rewardIntRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || totalRewardedIntSlots <= 0)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - LoadRewardedInterstitialAd(" + displayImmediately + ")");

        if (!rewardedIntIsLoading[rewardIntRefId] && !rewardedIntIsReady[rewardIntRefId] && !rewardedIntIsVisible[rewardIntRefId]) {
            if (debugLogging)
                Debug.Log("AdMob Debug - Rewarded Interstitial ad loading!");

            rewardedIntIsLoading[rewardIntRefId] = true;
            rewardedIntWantedVisible[rewardIntRefId] = displayImmediately;

            // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
            MobileAdsEventExecutor.ExecuteInUpdate(() => SendRewardedInterstitialLoadRequest(rewardIntRefId));
        } else {
            if (debugLogging)
                Debug.Log("AdMob Debug - Rewarded Interstitial ad already pending/ready!");

            if (displayImmediately) {
                if (debugLogging)
                    Debug.Log("AdMob Debug - Rewarded interstitial ad wanted force show, so showing rewarded interstitial ad now!");

                ShowRewardedInterstitialAd(rewardIntRefId);
            }
        }
    }

    private void LoadRewardedInterstitialAd(bool displayImmediately, int rewardIntRefId, bool forcedInternalCall) {
        if (!enableAdMob || !isAdMobInitialized || string.IsNullOrEmpty(GetPlatformAdData().rewardedInterstitialFloorData[rewardIntRefId].floorId[rewardedIntWantedFloorId]) || totalRewardedIntSlots <= 0)
            return;

        if (forcedInternalCall) {
            LoadRewardedInterstitialAd(false, rewardIntRefId);
            rewardedIntWantedVisible[rewardIntRefId] = displayImmediately;
        } else {
            LoadRewardedInterstitialAd(displayImmediately, rewardIntRefId);
        }
    }

    public void ShowRewardedInterstitialAd(int rewardIntRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || totalRewardedIntSlots <= 0)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - ShowRewardedInterstitialAd()");

        rewardedIntWantedVisible[rewardIntRefId] = true;

        if (!rewardedIntIsVisible[rewardIntRefId]) {
            if (rewardedIntIsReady[rewardIntRefId]) {
                if (adMobRewardedInterstitialAd[rewardIntRefId] != null) {
                    // We're ready to show the rewarded interstitial ad
                    // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
                    MobileAdsEventExecutor.ExecuteInUpdate(() => adMobRewardedInterstitialAd[rewardIntRefId].Show(
                        reward => MobileAdsEventExecutor.ExecuteInUpdate(() => RewardedInterstitialAdRewarded(reward, rewardIntRefId)))
                    );
                }
            } else {
                LoadRewardedInterstitialAd(true, rewardIntRefId, true);
            }
        }
    }

    public void DestroyRewardedInterstitialAd(int rewardIntRefId = 0) {
        rewardedIntWantedVisible[rewardIntRefId] = false;
        rewardedIntIsVisible[rewardIntRefId] = false;
        rewardedIntIsLoading[rewardIntRefId] = false;
        rewardedIntIsReady[rewardIntRefId] = false;

        if (debugLogging)
            Debug.Log("AdMob Debug - DestroyRewardedInterstitialAd()");

        if (adMobRewardedInterstitialAd[rewardIntRefId] != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
            MobileAdsEventExecutor.ExecuteInUpdate(() => adMobRewardedInterstitialAd[rewardIntRefId].Destroy());
        }
    }

    public void DestroyRewardedInterstitialAd() {
        if (rewardedIntWantedVisible != null)
            for (int i = 0; i < rewardedIntWantedVisible.Length; i++)
                DestroyRewardedInterstitialAd(i);
    }

    public void LoadRewardAd(bool displayImmediately = false) {
        if (!displayImmediately) {
            for (int i = 0; i < totalRewardSlots; i++)
                LoadRewardAd(false, i);
        } else {
            LoadRewardAd(displayImmediately, 0);
        }
    }

    /// <summary>
    /// Loads a reward advert into memory.
    ///</summary>
    /// <param name="displayImmediately">If set to <c>true</c> display the reward ad immediately when it has finished loading.</param>
    public void LoadRewardAd(bool displayImmediately, int rewardRefId) {
        if (!enableAdMob || !isAdMobInitialized || totalRewardSlots <= 0)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - LoadRewardAd(" + displayImmediately + ")");

        if (!rewardIsLoading[rewardRefId] && !rewardIsReady[rewardRefId] && !rewardIsVisible[rewardRefId]) {
            if (debugLogging)
                Debug.Log("AdMob Debug - Reward ad loading!");

            rewardIsLoading[rewardRefId] = true;
            rewardWantedVisible[rewardRefId] = displayImmediately;

            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
            MobileAdsEventExecutor.ExecuteInUpdate(() => SendRewardAdLoadRequest(rewardRefId));
        } else {
            if (debugLogging)
                Debug.Log("AdMob Debug - Reward ad already pending/ready!");

            if (displayImmediately) {
                if (debugLogging)
                    Debug.Log("AdMob Debug - Reward ad load wanted force show, so showing reward ad now!");

                ShowRewardAd(rewardRefId);
            }
        }
    }

    private void LoadRewardAd(bool displayImmediately, int rewardRefId, bool forcedInternalCall) {
        if (!enableAdMob || !isAdMobInitialized || string.IsNullOrEmpty(GetPlatformAdData().rewardFloorData[rewardRefId].floorId[rewardWantedFloorId]) || totalRewardSlots <= 0)
            return;

        if (forcedInternalCall) {
            LoadRewardAd(false, rewardRefId);
            rewardWantedVisible[rewardRefId] = displayImmediately;
        } else {
            LoadRewardAd(displayImmediately, rewardRefId);
        }
    }

    /// <summary>
    /// Shows a reward ad if one is loaded in memory.
    /// </summary>
    public void ShowRewardAd(int rewardRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || totalRewardSlots <= 0)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - ShowRewardAd()");

        rewardWantedVisible[rewardRefId] = true;

        // Check if we can perform the action for the current method
        if (!rewardIsVisible[rewardRefId]) {
            if (rewardIsReady[rewardRefId]) {
                if (adMobRewardedAd[rewardRefId] != null) {
                    // We're ready to show the reward ad
                    // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                    MobileAdsEventExecutor.ExecuteInUpdate(() => adMobRewardedAd[rewardRefId].Show((Reward reward) => {
                        if (reward == null) {
                            MobileAdsEventExecutor.ExecuteInUpdate(() => RewardAdFailedToShow(rewardRefId));
                            return;
                        }

                        MobileAdsEventExecutor.ExecuteInUpdate(() => RewardAdRewarded(reward, rewardRefId));
                    }));
                }
            } else {
                LoadRewardAd(true, rewardRefId, true);
            }
        }
    }

    /// <summary>
    /// Remove the banner from memory. (Required if you want to load a new banner ad type, however it's automatic when calling to load a new banner)
    /// </summary>
    public void DestroyRewardAd(int rewardRefId) {
        rewardWantedVisible[rewardRefId] = false;
        rewardIsVisible[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;
        rewardIsReady[rewardRefId] = false;

        if (debugLogging)
            Debug.Log("AdMob Debug - DestroyRewardAd()");

        if (adMobRewardedAd[rewardRefId] != null)
            adMobRewardedAd[rewardRefId].Destroy();
    }

    public void DestroyRewardAd() {
        if (adMobRewardedAd != null)
            for (int i = 0; i < adMobRewardedAd.Length; i++)
                DestroyRewardAd(i);
    }

    public void LoadInterstitialAd(bool displayImmediately = false, bool useWaitScreen = true) {
        if (!displayImmediately) {
            for (int i = 0; i < totalIntSlots; i++)
                LoadInterstitialAd(false, true, i);
        } else {
            LoadInterstitialAd(displayImmediately, useWaitScreen, 0);
        }
    }

    /// <summary>
    /// Loads an interstitial advert into memory.
    /// </summary>
    /// <param name="displayImmediately">If set to <c>true</c> display the interstitial immediately when it has finished loading.</param>
    public void LoadInterstitialAd(bool displayImmediately, bool useWaitScreen, int interstitialRefId) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - LoadInterstitialAd(" + displayImmediately + ", " + useWaitScreen + ")");

        // Check if we can perform the action for the current method
        if (!intIsLoading[interstitialRefId] && !intIsReady[interstitialRefId] && !intIsVisible[interstitialRefId]) {
            intIsLoading[interstitialRefId] = true;
            intWantedVisible[interstitialRefId] = displayImmediately;

            if (adMobInterstitial != null) {
                // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                MobileAdsEventExecutor.ExecuteInUpdate(() => SendInterstitialLoadRequest(interstitialRefId));
            }
        } else {
            if (displayImmediately)
                ShowInterstitialAd(useWaitScreen, interstitialRefId);
        }
    }

    private void LoadInterstitialAd(bool displayImmediately, bool useWaitScreen, int interstitialRefId, bool forcedInternalCall) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            return;

        if (forcedInternalCall) {
            LoadInterstitialAd(false, useWaitScreen, interstitialRefId);
            intWantedVisible[interstitialRefId] = displayImmediately;
        } else {
            LoadInterstitialAd(displayImmediately, useWaitScreen, interstitialRefId);
        }
    }

    /// <summary>
    /// Shows an interstitial if one is loaded in memory.
    /// </summary>
    /// <param name="useWaitScreen">The wait screen will enable the InterstitialWaitScreen prefab and wait InterstitialWaitTime seconds before showing the interstitial</param>
    public void ShowInterstitialAd(bool useWaitScreen = true, int interstitialRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - ShowInterstitialAd(" + useWaitScreen + ")");

        intWantedVisible[interstitialRefId] = true;

        // Check if we can perform the action for the current method
        if (!intIsVisible[interstitialRefId]) {
            if (intIsReady[interstitialRefId]) {
                if (useWaitScreen && useInterstitialWaitScreen) {
                    // We're ready to show the interstitial but first a message from our sponsors err I mean a black screen wait wait text on it
                    if (interstitialWaitScreen != null) {
                        StartCoroutine(ShowInterstitialAdAfterDelay(interstitialRefId));
                    } else {
                        Debug.LogError("Wait screen enabled but no gameobject was set! Interstitial will not be delayed..");

                        if (adMobInterstitial[interstitialRefId] != null) {
                            // Show the interstitial
                            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                            ConsentAndShowInterstitialInternal(interstitialRefId);
                        }
                    }
                } else {
                    if (adMobInterstitial[interstitialRefId] != null) {
                        // Show the interstitial
                        // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                        ConsentAndShowInterstitialInternal(interstitialRefId);
                    }
                }
            } else {
                LoadInterstitialAd(true, useWaitScreen, interstitialRefId, true);
            }
        }
    }

    private void ConsentAndShowInterstitialInternal(int interstitialRefId) {
        if (!ConsentInformation.CanRequestAds()) {
            PersonalisationManager.instance.DoAuthFlow(() => {
                // Make sure the interstitial request hasn't been cancelled since we requested the form
                if (intWantedVisible[interstitialRefId])
                    adMobInterstitial[interstitialRefId].Show();
                
                if(ClickLockManager.Instance != null)
                    ClickLockManager.Instance.HideClickLock();
            }, true);
        } else {
            MobileAdsEventExecutor.ExecuteInUpdate(() => adMobInterstitial[interstitialRefId].Show());
        }
    }

    public bool isIntLoadingScreenActive { get; private set; }

    private IEnumerator ShowInterstitialAdAfterDelay(int interstitialRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            yield break;

        // Temp hide banner ad if one is visible
        HideBannerAd(true);

        yield return null;

        if (interstitialWaitScreen != null)
            interstitialWaitScreen.SetActive(true);

        isIntLoadingScreenActive = true;

        yield return new WaitForSecondsRealtime(interstitialWaitTime);

        OnInterstitialAdClosed += HideInterstitialLoadingScreen;

        // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
        ConsentAndShowInterstitialInternal(interstitialRefId);

        // Give up if the ad doesn't show within 3 seconds
        for (float waitTime = 0f; isIntLoadingScreenActive && waitTime < 3f; waitTime += Time.unscaledDeltaTime)
            yield return null;

        if (isIntLoadingScreenActive) {
            CancelInterstitialAd(interstitialRefId);

            HideInterstitialLoadingScreen();
        }
    }

    private void HideInterstitialLoadingScreen() {
        OnInterstitialAdClosed -= HideInterstitialLoadingScreen;

        // Hide the wait screen
        if (interstitialWaitScreen != null)
            interstitialWaitScreen.SetActive(false);

        // Show banner ad again if one was visible previously
        ShowBannerAd(activeBannerRefId);

        isIntLoadingScreenActive = false;
    }

    /// <summary>
    /// Cancels an interstitial from loading, useful if you wanted to show an interstitial on menu x but it didn't load in time, 
    /// you might want to cancel the interstitial from showing once the player enters the main game for example.
    /// </summary>
    public void CancelInterstitialAd(int interstitialRefId) {
        intWantedVisible[interstitialRefId] = false;

        if (debugLogging)
            Debug.Log("AdMob Debug - Got request to cancel interstitial..");
    }

    public void CancelInterstitialAd() {
        if (adMobInterstitial != null)
            for (int i = 0; i < adMobInterstitial.Length; i++)
                CancelInterstitialAd(i);
    }

    /// <summary>
    /// Clears an interstitial from memory and sets all interstitial pending values to false.
    /// </summary>
    public void DestroyInterstitialAd(int interstitialRefId) {
        if (!isAdMobInitialized)
            return;

        intWantedVisible[interstitialRefId] = false;
        intIsReady[interstitialRefId] = false;
        intIsVisible[interstitialRefId] = false;
        intIsLoading[interstitialRefId] = false;

        if (debugLogging)
            Debug.Log("AdMob Debug - DestroyInterstitialAd()");

        if (adMobInterstitial != null && adMobInterstitial[interstitialRefId] != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
            MobileAdsEventExecutor.ExecuteInUpdate(() => adMobInterstitial[interstitialRefId].Destroy());
        }
    }

    public void DestroyInterstitialAd() {
        for (int i = 0; i < adMobInterstitial.Length; i++)
            DestroyInterstitialAd(i);
    }

    /// <summary>
    /// Loads a banner advert into memory.
    /// </summary>
    /// <param name="width">Width of the admob banner</param>
    /// <param name="height">Height of the admob banner</param>
    /// <param name="adLayout">Admob ad position</param>
    /// <param name="displayImmediately">If set to <c>true</c> display immediately when it has finished loading.</param>
    public void LoadBannerAd(int width, int height, AdPosition adLayout, bool displayImmediately = false) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        LoadBannerAd(new AdSize(width, height), adLayout, displayImmediately);
    }

    /// <summary>
    /// Loads a banner advert into memory.
    /// </summary>
    /// <param name="adType">Admob banner ad type.</param>
    /// <param name="adLayout">Admob ad position.</param>
    /// <param name="displayImmediately">If set to <c>true</c> display the banner immediately when it has finished loading.</param>
    public void LoadBannerAd(AdSize adSize, AdPosition adPosition, bool displayImmediately = false, int bannerRefId = 0) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - LoadBannerAd(" + adSize + ", " + adPosition + ", " + displayImmediately + ", " + bannerRefId + ")");

        if (bannerRefId != activeBannerRefId && displayImmediately)
            SetActiveBannerRefId(bannerRefId);

        bannerWantedVisible[bannerRefId] = displayImmediately;

        if (displayImmediately)
            bannerOverlayDepth = 0;

        // Check if we can perform the action for the current method
        if (!bannerIsLoading[bannerRefId] && !bannerIsReady[bannerRefId] && !bannerIsVisible[bannerRefId]) {
            if (isAdMobInitialized) {
                bannerIsLoading[bannerRefId] = true;

                // Ensure the admob request is sent on the main thread, otherwise it causes issues on iOS duplicating ads
                MobileAdsEventExecutor.ExecuteInUpdate(() => SendBannerAdLoadRequest(adSize, adPosition, bannerRefId));
            } else {
                // Override what banner will load when initialisation completes
                if (adSize != AdSize.SmartBanner) {
                    // Disables banner ad preloading while a non-smart banner type is wanted to be displayed
                    preloadBannerType[bannerRefId] = BannerSizeType.NONE;
                } else {
                    preloadBannerPosition[bannerRefId] = adPosition;
                }
            }
        } else {
            // If this was just a call to set the banner to the same type and position as it is already in then just ignore it
            if (bannerInMemorySizeSet[bannerRefId] && adSize == bannerInMemorySize[bannerRefId]) {
                if (!bannerInMemoryPositionSet[bannerRefId] || adPosition != bannerInMemoryPosition[bannerRefId]) {
                    bannerInMemoryPosition[bannerRefId] = adPosition;

                    if (isAdMobInitialized) {
                        if (debugLogging)
                            Debug.Log("AdMob Debug - Repositioning AdMob Banner to " + adPosition);

                        #if !UNITY_EDITOR
                        adMobBanner[bannerRefId].SetPosition(adPosition);
                        
                        if (displayImmediately && !bannerIsVisible[bannerRefId])
                             ShowBannerAd(bannerRefId, true);
                        #else
                        // AdMob banner editor previews currently do not support SetPosition so we need to destroy and remake the ad in the editor
                        StartCoroutine(ReloadBannerAd(adSize, adPosition, displayImmediately, bannerRefId));
                        #endif
                    }
                } else {
                    if (displayImmediately && !bannerIsVisible[bannerRefId])
                        ShowBannerAd(bannerRefId, true);
                }
            } else {
                if (debugLogging) {
                    if (bannerInMemorySizeSet[bannerRefId]) {
                        Debug.Log("AdMob Debug - Loaded banner size: " + bannerInMemorySize[bannerRefId].Height + "x" + bannerInMemorySize[bannerRefId].Width + " does not match wanted: " + adSize.Height + "x" + adSize.Width);
                    } else {
                        Debug.Log("AdMob Debug - Banner in memory size not set..");
                    }
                }

                StartCoroutine(ReloadBannerAd(adSize, adPosition, displayImmediately, bannerRefId));
            }
        }
    }

    private IEnumerator ReloadBannerAd(AdSize adSize, AdPosition adPosition, bool displayImmediately = false, int bannerRefId = 0) {
        if (debugLogging)
            Debug.Log("AdMob Debug - Reloading banner ad..");

        DestroyBannerAd(true);

        for (int i = 0; i < 10; i++)
            yield return null;

        LoadBannerAd(adSize, adPosition, displayImmediately, bannerRefId);
    }

    private IEnumerator ReloadBannerAd(AdSize adSize, int xPos, int yPos, bool displayImmediately = false, int bannerRefId = 0) {
        if (debugLogging)
            Debug.Log("AdMob Debug - Reloading banner ad..");

        DestroyBannerAd(true);

        for (int i = 0; i < 10; i++)
            yield return null;

        LoadBannerAd(adSize, xPos, yPos, displayImmediately, bannerRefId);
    }

    /// <summary>
    /// Loads a banner advert into memory.
    /// </summary>
    /// <param name="adType">Admob banner ad type.</param>
    /// <param name="adLayout">Admob ad position.</param>
    /// <param name="displayImmediately">If set to <c>true</c> display the banner immediately when it has finished loading.</param>
    public void LoadBannerAd(AdSize adSize, int xPos, int yPos, bool displayImmediately = false, int bannerRefId = 0) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - LoadBannerAd(" + adSize + ", " + xPos + ", " + yPos + ", " + displayImmediately + ", " + bannerRefId + ")");

        if (bannerRefId != activeBannerRefId && displayImmediately)
            SetActiveBannerRefId(bannerRefId);

        bannerWantedVisible[bannerRefId] = displayImmediately;

        if (displayImmediately)
            bannerOverlayDepth = 0;

        // Check if we can perform the action for the current method
        if (!bannerIsLoading[bannerRefId] && !bannerIsReady[bannerRefId] && !bannerIsVisible[bannerRefId]) {
            if (isAdMobInitialized) {
                bannerIsLoading[bannerRefId] = true;

                // Ensure the admob request is sent on the main thread, otherwise it causes issues on iOS duplicating ads
                MobileAdsEventExecutor.ExecuteInUpdate(() => SendBannerAdLoadRequest(adSize, xPos, yPos, bannerRefId));
            } else {
                // Override what banner will load when initialisation completes
                if (adSize != AdSize.SmartBanner) {
                    // Disables banner ad preloading while a non-smart banner type is wanted to be displayed
                    preloadBannerType[bannerRefId] = BannerSizeType.NONE;
                } else {
                    // The X and Y LoadBannerCall is probably not being called for non-smart banner ads anyway..
                    // Just fallback to top which is yPos 0 equiv
                    preloadBannerPosition[bannerRefId] = AdPosition.Top;
                }
            }
        } else {
            // If this was just a call to set the banner to the same type and position as it is already in then just ignore it
            if (bannerInMemorySizeSet[bannerRefId] && adSize == bannerInMemorySize[bannerRefId]) {
                if (isAdMobInitialized) {
                    if (debugLogging)
                        Debug.Log("AdMob Debug - Repositioning AdMob Banner to " + xPos + ", " + yPos);

                    #if !UNITY_EDITOR
                    InternalSetBannerPosition(bannerRefId, xPos, yPos);
                    
                    if (displayImmediately && !bannerIsVisible[bannerRefId])
                        ShowBannerAd(bannerRefId, true);
                    #else
                    // AdMob banner editor previews currently do not support SetPosition so we need to destroy and remake the ad in the editor
                    StartCoroutine(ReloadBannerAd(adSize, xPos, yPos, displayImmediately, bannerRefId));
                    #endif
                }
            } else {
                StartCoroutine(ReloadBannerAd(adSize, xPos, yPos, displayImmediately, bannerRefId));
            }
        }
    }

    private void InternalSetBannerPosition(int bannerRefId, int xPos, int yPos) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
            MobileAdsEventExecutor.ExecuteInUpdate(() => {
                // Set the new position of the admob banner
                adMobBanner[bannerRefId].SetPosition(xPos, yPos);
            });
        }
    }

    /// <summary>
    /// Loads a banner advert into memory.
    /// </summary>
    /// <param name="width">Width of the admob banner</param>
    /// <param name="height">Height of the admob banner</param>
    /// <param name="xPos">X placement position relative to top left</param>
    /// <param name="yPos">Y placement position relative to top left</param>
    /// <param name="displayImmediately">If set to <c>true</c> display immediately when it has finished loading.</param>
    public void LoadBannerAd(int width, int height, int xPos, int yPos, bool displayImmediately = false, int bannerRefId = 0) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        LoadBannerAd(new AdSize(width, height), xPos, yPos, displayImmediately, bannerRefId);
    }

    private void LoadBannerAd(bool displayImmediately, bool forcedInternalCall, int bannerRefId) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        if (!bannerInMemorySizeSet[bannerRefId])
            return;

        if (forcedInternalCall) {
            if (bannerInMemoryUseXYPosition[bannerRefId]) {
                LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPositionXY[bannerRefId].x, bannerInMemoryPositionXY[bannerRefId].y, false, bannerRefId);
            } else {
                LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPosition[bannerRefId], false, bannerRefId);
            }

            bannerWantedVisible[bannerRefId] = displayImmediately;
        } else {
            if (bannerInMemoryUseXYPosition[bannerRefId]) {
                LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPositionXY[bannerRefId].x, bannerInMemoryPositionXY[bannerRefId].y, displayImmediately, bannerRefId);
            } else {
                LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPosition[bannerRefId], displayImmediately, bannerRefId);
            }
        }
    }

    public int activeBannerRefId { get; set; }

    public void SetActiveBannerRefId(int bannerRefId) {
        // Force hide all other banners and reset their overlay depths
        for (int i = 0; i < GetPlatformAdData().bannerFloorData.Length; i++)
            if (i != bannerRefId && (bannerIsVisible[i] || bannerWantedVisible[i]))
                HideBannerAd(i, false);

        activeBannerRefId = bannerRefId;
    }

    /// <summary>
    /// Shows a banner advert if one is loaded in memory.
    /// </summary>
    public void ShowBannerAd(int bannerRefId = 0, bool forceShow = false) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - ShowBannerAd(" + forceShow + ")");

        SetActiveBannerRefId(bannerRefId);

        // Check if we're calling ShowBanner because we're returning from an overlay screen which hid the banner
        if (bannerOverlayDepth > 0 && !forceShow) {
            if (debugLogging)
                Debug.Log("AdMob Debug - bannerOverlayDepth was " + bannerOverlayDepth);

            // Decrease the overlay depth by 1
            bannerOverlayDepth--;

            // If the overlay depth is still above 0 then there must still be some overlays open
            if (bannerOverlayDepth > 0)
                return;

            if (debugLogging)
                Debug.Log("AdMob Debug - bannerOverlayDepth is now " + bannerOverlayDepth + " setting wanted visible state to " + bannerPrevState);

            // There isn't any more overlaying menus open, return to the previous banner ad state
            bannerWantedVisible[activeBannerRefId] = bannerPrevState;

            if (debugLogging)
                Debug.Log("AdMob Debug - Banner wanted set to prev state: " + bannerPrevState);
        } else {
            bannerWantedVisible[activeBannerRefId] = true;
            bannerOverlayDepth = 0;
        }

        if (!bannerWantedVisible[activeBannerRefId])
            return;

        // Check if we can perform the action for the current method
        if (!bannerIsVisible[activeBannerRefId]) {
            if (bannerIsReady[activeBannerRefId]) {
                // Show the banner
                if (isAdMobInitialized)
                    InternalShowBanner(activeBannerRefId);
            } else {
                if (!bannerIsLoading[activeBannerRefId]) {
                    LoadBannerAd(true, true, activeBannerRefId);
                } else {
                    // Banner is already loading, be patient
                    if (debugLogging)
                        Debug.Log("AdMob Debug - Banner already loading, now we wait..");
                }
            }
        }
    }

    private void InternalShowBanner(int bannerRefId) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null) {
            if (bannerWantedVisible[bannerRefId]) {
                // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                MobileAdsEventExecutor.ExecuteInUpdate(() => { adMobBanner[bannerRefId].Show(); });

                if (!bannerIsVisible[bannerRefId])
                    BannerAdVisible(bannerRefId);
            }
        }
    }

    /// <summary>
    /// Hides all banner adverts, will also cancel a banner advert from showing if one is loaded.
    /// </summary>
    /// <param name="isOverlay">Set to <c>true</c> if you want to hide the banner while opening an overlaying screen (such as the backscreen) and want to revert the banner ad status later.</param>
    public void HideBannerAd(bool isOverlay = false) {
        if (debugLogging)
            Debug.Log("AdMob Debug - HideBannerAd(" + isOverlay + ")");

        // If this is an overlaying screen (e.g backscreen) then we'll want to return to the previous banner state when we close it
        if (isOverlay) {
            bannerOverlayDepth++;

            if (bannerOverlayDepth == 1)
                bannerPrevState = bannerWantedVisible[activeBannerRefId];
        }

        if (debugLogging)
            Debug.Log("AdMob Debug - bannerOverlayDepth is now " + bannerOverlayDepth);

        // Mark wanted visible as false so if the banner ad hasn't loaded yet it'll make sure it isn't shown when loaded
        for (int i = 0; i < GetPlatformAdData().bannerFloorData.Length; i++) {
            if (bannerWantedVisible[i] || bannerIsVisible[i]) {
                bannerWantedVisible[i] = false;
                bannerIsVisible[i] = false;

                if (adMobBanner[i] != null) {
                    int bannerId = i;
                    // Hide the banner advert from view (This does not unload it from memory)
                    if (isAdMobInitialized)
                        InternalHideBanner(bannerId);
                }
            }
        }
    }

    private void InternalHideBanner(int bannerRefId) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
            adMobBanner[bannerRefId].Hide();

            if (bannerIsVisible[bannerRefId])
                BannerAdHidden(bannerRefId);
        }
    }

    public void HideBannerAd(int bannerRefId, bool isOverlay) {
        if (debugLogging)
            Debug.Log("AdMob Debug - HideBannerAd(" + bannerRefId + ", " + isOverlay + ")");

        // If this is an overlaying screen (e.g backscreen) then we'll want to return to the previous banner state when we close it
        if (isOverlay && bannerRefId == activeBannerRefId) {
            bannerOverlayDepth++;

            if (bannerOverlayDepth == 1)
                bannerPrevState = bannerWantedVisible[bannerRefId];
        }

        if (debugLogging)
            Debug.Log("AdMob Debug - bannerOverlayDepth is now " + bannerOverlayDepth);

        // Mark wanted visible as false so if the banner ad hasn't loaded yet it'll make sure it isn't shown when loaded
        bannerWantedVisible[bannerRefId] = false;
        bannerIsVisible[bannerRefId] = false;

        if (adMobBanner[bannerRefId] != null) {
            // Hide the banner advert from view (This does not unload it from memory)
            if (isAdMobInitialized)
                InternalHideBanner(bannerRefId);
        }
    }

    public void DestroyBannerAd(int bannerRefId, bool forceDestroy = false) {
        SetActiveBannerRefId(bannerRefId);
        DestroyBannerAd(forceDestroy);
    }

    /// <summary>
    /// Remove the banner from memory. (Required if you want to load a new banner ad type, however it's automatic when calling to load a new banner)
    /// </summary>
    public void DestroyBannerAd(bool forceDestroy = false) {
        if (debugLogging)
            Debug.Log("AdMob Debug - DestroyBannerAd(" + forceDestroy + ")");

        if (adMobBanner[activeBannerRefId] == null || forceDestroy) {
            bannerWantedVisible[activeBannerRefId] = false;
            bannerIsLoading[activeBannerRefId] = false;
            bannerIsReady[activeBannerRefId] = false;
            bannerIsVisible[activeBannerRefId] = false;
        }

        // Changed to only destroy if forceDestroy variable is true - No need to destroy the banner fully, it causes a bug where if another ad is loaded this frame it'll get destroyed instantly..
        if (adMobBanner[activeBannerRefId] != null) {
            if (forceDestroy) {
                if (isAdMobInitialized)
                    InternalDestroyBanner(activeBannerRefId);
            } else {
                HideBannerAd(false);
            }
        }
    }

    private void InternalDestroyBanner(int bannerRefId) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
            #if !UNITY_EDITOR
                MobileAdsEventExecutor.ExecuteInUpdate(() => adMobBanner[bannerRefId].Destroy());
            #else
                // The editor doesn't handle destroying ads correctly and it causes ads to get duplicated..
                MobileAdsEventExecutor.ExecuteInUpdate(() => adMobBanner[bannerRefId].Hide());
            #endif
        }
    }

    /// Get a cached points scale factor or generate a dummy banner to get the point scale factor for iOS admob
    public float GetPixelToPointFactor() {
        #if !UNITY_EDITOR
            if (!pointScaleFactorSet)
            {
                BannerView banner = new BannerView(GetPlatformAdData().bannerFloorData[0].floorId[0], AdSize.Banner, AdPosition.TopLeft);

                cachedPointScaleFactor = AdSize.Banner.Width / banner.GetWidthInPixels();
                pointScaleFactorSet = true;
            }
        #else
            cachedPointScaleFactor = 1f;
        #endif

        return cachedPointScaleFactor;
    }

    public void OpenAdInspector() {
        if (!isAdMobInitialized)
            return;

        if (!enableTestMode) {
            PlayerPrefs.SetInt("admob_test_mode", 1); // This will force the app into test mode from launch

            PickleCore.DisplayToastMessage("Test mode enabled, restart app to test ads and use ad inspector!", 10);
        } else {
            // Enable test mode and keep it enabled just incase we want to continue debugging with test logging
            enableTestMode = true;
            debugLogging = true;

            // Generate an ad request to use with the mediation test suite (this sets up test device ids and other request parameters)
            MobileAdsEventExecutor.ExecuteInUpdate(() => {
                MobileAds.OpenAdInspector(error => {
                    if (error != null) {
                        string errorMsg = error.GetMessage();

                        Debug.LogError("Failed to open ad inspector! (" + errorMsg + ")");

                        PickleCore.DisplayToastMessage("Failed to open ad inspector (" + errorMsg + ")");
                    } else {
                        Debug.Log("Ad inspector opened successfully!");
                    }
                });
            });
        }
    }

    private IEnumerator RetryBannerAdLoad(int bannerRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * (bannerFloorGroupAttemptedRetries[bannerRefId] + 1);

        if (curWaitTime > 0f) {
            if (debugLogging)
                Debug.Log("AdMob Debug - Waiting " + curWaitTime + " seconds to retry banner ad loading..");

            yield return new WaitForSecondsRealtime(curWaitTime);
        }

        // Move to a lower floor value
        bannerWantedFloorId++;

        // Check if we're out of floors, if we are we'll move back to the first floor and apply a delay between loading so we're not spamming admob with requests
        if (bannerWantedFloorId >= totalBannerFloors[bannerRefId]) {
            // We've cycled through a full floor group, increase the counter to apply more of a delay between admob requests
            bannerFloorGroupAttemptedRetries[bannerRefId]++;

            // Reset the wanted floor back to id 0
            bannerWantedFloorId = 0;
        }

        if (bannerInMemorySizeSet[bannerRefId] && bannerInMemoryPositionSet[bannerRefId]) {
            if (bannerInMemoryUseXYPosition[bannerRefId]) {
                MobileAdsEventExecutor.ExecuteInUpdate(() => LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPositionXY[bannerRefId].x, bannerInMemoryPositionXY[bannerRefId].y, bannerWantedVisible[bannerRefId]));
            } else {
                MobileAdsEventExecutor.ExecuteInUpdate(() => LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPosition[bannerRefId], bannerWantedVisible[bannerRefId]));
            }
        }
    }

    private IEnumerator RetryRewardedInterstitialAdLoad(int rewardedIntRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * (rewardedIntFloorGroupAttemptedRetries[rewardedIntRefId] + 1);

        if (curWaitTime > 0f) {
            if (debugLogging)
                Debug.Log("AdMob Debug - Waiting " + curWaitTime + " seconds to retry rewarded interstitial ad loading..");

            yield return new WaitForSecondsRealtime(curWaitTime);
        }

        // Move to a lower value floor
        rewardedIntWantedFloorId++;

        // Check if we're out of floors, if we are we'll move back to the first floor and apply a delay between loading so we're not spamming admob with requests
        if (rewardedIntWantedFloorId >= totalRewardedIntFloors[rewardedIntRefId]) {
            // We've cycled through a full floor group, increase the counter to apply more of a delay between admob requests
            rewardedIntFloorGroupAttemptedRetries[rewardedIntRefId]++;

            // Reset the wanted floor back to id 0
            rewardedIntWantedFloorId = 0;
        }

        MobileAdsEventExecutor.ExecuteInUpdate(() => LoadRewardedInterstitialAd(rewardedIntWantedVisible[rewardedIntRefId], rewardedIntRefId));
    }

    private IEnumerator RetryRewardAdLoad(int rewardRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * (rewardFloorGroupAttemptedRetries[rewardRefId] + 1);

        if (curWaitTime > 0f) {
            if (debugLogging)
                Debug.Log("AdMob Debug - Waiting " + curWaitTime + " seconds to retry reward ad loading..");

            yield return new WaitForSecondsRealtime(curWaitTime);
        }

        // Move to a lower value floor
        rewardWantedFloorId++;

        // Check if we're out of floors, if we are we'll move back to the first floor and apply a delay between loading so we're not spamming admob with requests
        if (rewardWantedFloorId >= totalRewardFloors[rewardRefId]) {
            // We've cycled through a full floor group, increase the counter to apply more of a delay between admob requests
            rewardFloorGroupAttemptedRetries[rewardRefId]++;

            // Reset the wanted floor back to id 0
            rewardWantedFloorId = 0;
        }

        MobileAdsEventExecutor.ExecuteInUpdate(() => LoadRewardAd(rewardWantedVisible[rewardRefId]));
    }

    private IEnumerator RetryInterstitialAdLoad(int interstitialRefId) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * (intFloorGroupAttemptedRetries[interstitialRefId] + 1);

        if (curWaitTime > 0f) {
            if (debugLogging)
                Debug.Log("AdMob Debug - Waiting " + curWaitTime + " seconds to retry interstitial ad loading..");

            yield return new WaitForSecondsRealtime(curWaitTime);
        }

        // Move to a lower value floor
        intWantedFloorId++;

        // Check if we're out of floors, if we are we'll move back to the first floor and apply a delay between loading so we're not spamming admob with requests
        if (intWantedFloorId >= totalIntFloors[interstitialRefId]) {
            // We've cycled through a full floor group, increase the counter to apply more of a delay between admob requests
            intFloorGroupAttemptedRetries[interstitialRefId]++;

            // Reset the wanted floor back to id 0
            intWantedFloorId = 0;
        }

        MobileAdsEventExecutor.ExecuteInUpdate(() => LoadInterstitialAd(intWantedVisible[interstitialRefId], true, interstitialRefId));
    }

    private string GetErrorCodeString(int errorCode) {
        // https://developers.google.com/admob/android/reference/com/google/android/gms/ads/AdRequest#summary
        switch (errorCode) {
            case 8: return "ERROR_CODE_APP_ID_MISSING";
            case 0: return "ERROR_CODE_INTERNAL_ERROR";
            case 11: return "ERROR_CODE_INVALID_AD_STRING";
            case 1: return "ERROR_CODE_INVALID_REQUEST";
            case 9: return "ERROR_CODE_MEDIATION_NO_FILL";
            case 2: return "ERROR_CODE_NETWORK_ERROR";
            case 3: return "ERROR_CODE_NO_FILL";
            case 10: return "ERROR_CODE_REQUEST_ID_MISMATCH";
        }
        
        // Unknown errors just return the raw code
        return "ERROR_CODE_" + errorCode;
    }

    // Rewarded Interstitial Ad Callbacks
    private void RewardedInterstitialAdVisible(int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad visible");

#if IVMETRICS_EVENTS_ENABLED
        IVMetricsManager.LogEvent("admob_rewardint_shown");
#endif
        
        rewardedIntIsVisible[rewardedIntRefId] = true;
        OnRewardedInterstitialAdShown?.Invoke();
    }

    // Reward Ad Callbacks
    private void RewardedInterstitialAdClosed(int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad closed");

        rewardedIntIsReady[rewardedIntRefId] = false;
        rewardedIntIsVisible[rewardedIntRefId] = false;
        rewardedIntIsLoading[rewardedIntRefId] = false;

        OnRewardedInterstitialAdClosed?.Invoke();

        // Wait until we're sure we know if the player watched the advert in full, as loading a new rewarded interstitial ad will cancel any incoming callbacks from the previous ad
        StartCoroutine(DelayedRewardedInterstitialAdReload(rewardedIntRefId));
    }

    private IEnumerator DelayedRewardedInterstitialAdReload(int rewardedIntRefId = 0) {
        // Wait 5 seconds
        yield return new WaitForSecondsRealtime(5f);

        LoadRewardedInterstitialAd(false, rewardedIntRefId, true);
    }

    // AdMob callback - !! not guaranteed to be called on main thread !!
    private void RewardedInterstitialAdFailedToShow(object sender, AdError adError, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging) {
            Debug.LogError("AdMob Debug - Failed to show rewarded interstitial ad! (Ref: " + rewardedIntRefId + ", Floor: " + rewardedIntWantedFloorId + ") " + adError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + adError.ToString());
        }

        rewardedIntIsReady[rewardedIntRefId] = false;
        rewardedIntIsLoading[rewardedIntRefId] = false;

        OnRewardedInterstitialAdFailedToShow?.Invoke(adError);

        FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.ADMOB_ERROR, "reward_show_failed_" + adError.GetCode());

        AttemptRewardedInterstitialAdRetry(rewardedIntRefId);
    }

    private int lastRewardIntLoadFailCode = -1;
    
    // AdMob callback - !! not guaranteed to be called on main thread !!
    private void RewardedInterstitialAdFailedToLoad(LoadAdError loadAdError, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging) {
            Debug.LogError("AdMob Debug - Failed to load reward ad! " + loadAdError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + loadAdError.ToString());
            Debug.LogError("AdMob Debug - Mediation Response: " + loadAdError.GetResponseInfo().ToString());
        }

        if (loadAdError != null && lastRewardIntLoadFailCode != loadAdError.GetCode()) {
            lastRewardIntLoadFailCode = loadAdError.GetCode();
            
#if IVMETRICS_EVENTS_ENABLED
            IVMetrics.IVMetricsManager.LogEvent("admob_rewardint_load_failed", GetErrorCodeString(lastRewardIntLoadFailCode));
#endif
            FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.ADMOB_ERROR, "rewardint_load_failed_" + lastRewardIntLoadFailCode);
        }

        rewardIsReady[rewardedIntRefId] = false;
        rewardIsLoading[rewardedIntRefId] = false;

        OnRewardedInterstitialAdFailedToLoad?.Invoke(loadAdError);
        AttemptRewardedInterstitialAdRetry(rewardedIntRefId);
    }

    // Application.internetReachability must be called from the main thread
    private void AttemptRewardedInterstitialAdRetry(int rewardedIntRefId) {
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && rewardedIntFloorGroupAttemptedRetries[rewardedIntRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryRewardedInterstitialAdLoad(rewardedIntRefId));
    }

    private void RewardedInterstitialAdRewarded(Reward reward, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad rewarded");

        OnRewardedInterstitialAdRewarded?.Invoke(reward);
    }

    private void RewardedInterstitialAdLoaded(int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad loaded");

        if (!hasLoadedAnyRewardedInt) {
            FirebaseAnalyticsManager.LogCustomEvent(PickleEventCategory.PickleScripts.ADMOB_TTL.ToString(), "first_rwint_load", Time.realtimeSinceStartup);
            hasLoadedAnyRewardedInt = true;
        }
        
#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_rewardint_loaded");
#endif

        rewardedIntIsReady[rewardedIntRefId] = true;
        rewardedIntIsLoading[rewardedIntRefId] = false;
        rewardedIntFloorGroupAttemptedRetries[rewardedIntRefId] = 0;
        rewardedIntWantedFloorId = 0;

        OnRewardedInterstitialAdReady?.Invoke(adMobRewardedInterstitialAd[rewardedIntRefId]);

        if (rewardedIntWantedVisible[rewardedIntRefId])
            ShowRewardedInterstitialAd(rewardedIntRefId);
    }

    private void RewardAdClosed(int rewardRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad closed");

        // This is called when the reward ad is closed by skipping OR when closed by watching to completion
        // Look at OnRewardAdRewarded if you want the completion status
        rewardIsReady[rewardRefId] = false;
        rewardIsVisible[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;

        OnRewardAdClosed?.Invoke();

        // Wait until we're sure we know if the player watched the advert in full, as loading a new reward ad will cancel any incoming callbacks from the previous ad
        StartCoroutine(DelayedRewardAdReload(rewardRefId));
    }

    private IEnumerator DelayedRewardAdReload(int rewardRefId) {
        // Wait 5 seconds
        yield return new WaitForSecondsRealtime(5f);

        LoadRewardAd(false, rewardRefId, true);
    }

    private void RewardAdLoaded(int rewardRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad loaded");

        if (!hasLoadedAnyReward) {
            FirebaseAnalyticsManager.LogCustomEvent(PickleEventCategory.PickleScripts.ADMOB_TTL.ToString(), "first_rw_load", Time.realtimeSinceStartup);
            hasLoadedAnyReward = true;
        }

        lastRewardLoadFailCode = -1;
        
#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_reward_loaded");
#endif

        rewardIsReady[rewardRefId] = true;
        rewardIsLoading[rewardRefId] = false;
        rewardFloorGroupAttemptedRetries[rewardRefId] = 0;
        rewardWantedFloorId = 0;

        OnRewardAdReady?.Invoke(adMobRewardedAd[rewardRefId]);

        if (rewardWantedVisible[rewardRefId])
            ShowRewardAd(rewardRefId);
    }
    
    private void RewardAdVisible(int rewardRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad visible");

#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_reward_shown");
#endif
        
        rewardIsVisible[rewardRefId] = true;

        OnRewardAdShown?.Invoke();
    }

    private void RewardAdRewarded(Reward reward, int rewardRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad rewarded");

        OnRewardAdRewarded?.Invoke(reward);
    }

    private void RewardAdFailedToShow(int rewardRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.LogError("AdMob Debug - Failed to show reward ad! Ref ID: " + rewardRefId);

        rewardIsReady[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;

        OnRewardAdFailedToShow?.Invoke();
        AttemptRewardAdRetry(rewardRefId);
    }

    private int lastRewardLoadFailCode = -1;
    
    private void RewardAdFailedToLoad(LoadAdError loadAdError, int rewardRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging && loadAdError != null) {
            Debug.LogError("AdMob Debug - Failed to load reward ad! " + loadAdError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + loadAdError.ToString());

            ResponseInfo responseInfo = loadAdError.GetResponseInfo();

            if (responseInfo != null)
                Debug.LogError("AdMob Debug - Mediation Response: " + responseInfo.ToString());
        }

        if (loadAdError != null && lastRewardLoadFailCode != loadAdError.GetCode()) {
            lastRewardLoadFailCode = loadAdError.GetCode();
            
#if IVMETRICS_EVENTS_ENABLED
            IVMetrics.IVMetricsManager.LogEvent("admob_reward_load_failed", GetErrorCodeString(lastRewardLoadFailCode));
#endif

            FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.ADMOB_ERROR, "reward_load_failed_" + lastRewardLoadFailCode);
        }
        
        rewardIsReady[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;

        OnRewardAdFailedToLoad?.Invoke(loadAdError);
        AttemptRewardAdRetry(rewardRefId);
    }

    private void AttemptRewardAdRetry(int rewardRefId) {
        // Note: Application.internetReachability must be called from the main thread or it can cause app crashes
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && rewardFloorGroupAttemptedRetries[rewardRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryRewardAdLoad(rewardRefId));
    }

    private void BannerAdHidden(int bannerRefId) {
        if (!isAdMobInitialized)
            return;

        bannerIsVisible[bannerRefId] = false;
        OnBannerAdClosed?.Invoke();

        // If the banner ad was marked as wanted visible between hide call and now re-show it
        if (bannerWantedVisible[bannerRefId])
            ShowBannerAd(bannerRefId);
    }

    private void BannerAdLoaded(int bannerRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Banner ad loaded");

        if (!hasLoadedAnyBanner) {
            FirebaseAnalyticsManager.LogCustomEvent(PickleEventCategory.PickleScripts.ADMOB_TTL.ToString(), "first_ban_load", Time.realtimeSinceStartup);
            hasLoadedAnyBanner = true;
        }

        lastBannerLoadFailCode = -1;
        
#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_banner_loaded");
#endif

        bannerIsReady[bannerRefId] = true;
        bannerIsLoading[bannerRefId] = false;
        bannerFloorGroupAttemptedRetries[bannerRefId] = 0;
        bannerWantedFloorId = 0;

        OnBannerAdReady?.Invoke(adMobBanner[bannerRefId]);

        if (bannerWantedVisible[bannerRefId]) {
            ShowBannerAd(bannerRefId);
        } else {
            HideBannerAd(bannerRefId, false);
        }
    }

    private void BannerAdVisible(int bannerRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Banner ad visible " + bannerRefId);

#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_banner_shown");
#endif
        
        bannerIsVisible[bannerRefId] = true;

        OnBannerAdShown?.Invoke();

        if (!bannerWantedVisible[bannerRefId])
            HideBannerAd(bannerRefId, false);
    }

    private int lastBannerLoadFailCode = -1;
    
    private void BannerAdFailedToLoad(LoadAdError loadAdError, int bannerRefId) {
        // Skip the banner failed callback in the editor as we don't want to be spammed with messages if we've deleted the banner ad prefabs
        if (!isAdMobInitialized || Application.isEditor)
            return;

        if (debugLogging) {
            Debug.LogError("AdMob Debug - Failed to load banner ad! " + loadAdError.GetMessage());

            try {
                // Trying to print responseInfo to text when the responseInfo.client is null throws a NullReferenceException
                // the client parameter is private so I'm just wrapping it in a try catch to suppress the error
                ResponseInfo responseInfo = loadAdError.GetResponseInfo();

                if (responseInfo != null)
                    Debug.LogError("AdMob Debug - Mediation Response: " + responseInfo);
            } catch (NullReferenceException e) { }
        }
        
        if (loadAdError != null && lastBannerLoadFailCode != loadAdError.GetCode()) {
            lastBannerLoadFailCode = loadAdError.GetCode();
            
#if IVMETRICS_EVENTS_ENABLED
            IVMetrics.IVMetricsManager.LogEvent("admob_banner_load_failed", GetErrorCodeString(lastBannerLoadFailCode));
#endif

            FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.ADMOB_ERROR, "banner_load_failed_" + lastBannerLoadFailCode);
        }

        bannerIsReady[bannerRefId] = false;
        bannerIsLoading[bannerRefId] = false;

        OnBannerAdFailedToLoad?.Invoke(loadAdError);
        AttemptBannerAdRetry(bannerRefId);
    }

    // Application.internetReachability must be called from the main thread
    private void AttemptBannerAdRetry(int bannerRefId) {
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && bannerFloorGroupAttemptedRetries[bannerRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryBannerAdLoad(bannerRefId));
    }

    // Interstitial Ad Callbacks
    private void InterstitialAdClosed(int interstitialRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Interstitial ad closed");

        intIsReady[interstitialRefId] = false;
        intIsVisible[interstitialRefId] = false;

        OnInterstitialAdClosed?.Invoke();

        // Automatically reload the next interstitial
        LoadInterstitialAd(false, true, interstitialRefId);
    }

    private void InterstitialAdLoaded(int interstitialRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Interstitial ad loaded");

        if (!hasLoadedAnyInt) {
            FirebaseAnalyticsManager.LogCustomEvent(PickleEventCategory.PickleScripts.ADMOB_TTL.ToString(), "first_int_load", Time.realtimeSinceStartup);
            hasLoadedAnyInt = true;
        }

        lastIntLoadFailCode = -1;
        
#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_interstitial_loaded");
#endif

        intIsReady[interstitialRefId] = true;
        intIsLoading[interstitialRefId] = false;
        intFloorGroupAttemptedRetries[interstitialRefId] = 0;
        intWantedFloorId = 0;

        OnInterstitialAdReady?.Invoke(adMobInterstitial[interstitialRefId]);

        if (intWantedVisible[interstitialRefId])
            ShowInterstitialAd(true, interstitialRefId);
    }

    private void InterstitialAdVisible(int interstitialRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - Interstitial ad visible");

#if IVMETRICS_EVENTS_ENABLED
        IVMetrics.IVMetricsManager.LogEvent("admob_int_shown");
#endif
        
        intIsVisible[interstitialRefId] = true;

        OnInterstitialAdShown?.Invoke();
    }

    private int lastIntLoadFailCode = -1;
    
    private void InterstitialAdFailedToLoad(LoadAdError loadAdError, int interstitialRefId) {
        if (!isAdMobInitialized)
            return;

        if (debugLogging && loadAdError != null) {
            Debug.LogError("AdMob Debug - Failed to load interstitial ad! " + loadAdError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + loadAdError);
        }

        if (loadAdError != null && lastIntLoadFailCode != loadAdError.GetCode()) {
            lastIntLoadFailCode = loadAdError.GetCode();
            
#if IVMETRICS_EVENTS_ENABLED
            IVMetrics.IVMetricsManager.LogEvent("admob_int_load_failed", GetErrorCodeString(lastIntLoadFailCode));
#endif

            FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.ADMOB_ERROR, "interstitial_load_failed_" + lastIntLoadFailCode);
        }
        
        intIsReady[interstitialRefId] = false;
        intIsLoading[interstitialRefId] = false;

        OnInterstitialAdFailedToLoad?.Invoke(loadAdError);
        AttemptInterstitialAdRetry(interstitialRefId);
    }

    // Application.internetReachability must be called from the main thread
    private void AttemptInterstitialAdRetry(int interstitialRefId) {
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && intFloorGroupAttemptedRetries[interstitialRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryInterstitialAdLoad(interstitialRefId));
    }

    private void AdPaidEvent(string adUnitId, ResponseInfo responseInfo, AdValue adValue) {
        #if UNITY_IOS
            double adjustedValueMicros = (double)adValue.Value / 1000000;
        #else
            long adjustedValueMicros = adValue.Value;
        #endif
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Paid event " + adUnitId + " (Value: " + adValue.Value + ") (adjustedValueMicros: " + adjustedValueMicros + ") (Precision: " + adValue.Precision.ToString() + ")");
        
        #if tenjin_admob_enabled
            string json = JsonUtility.ToJson(new AdMobImpressionDataToJSON {
                ad_unit_id = adUnitId,
                currency_code = adValue.CurrencyCode,
                response_id = responseInfo.GetResponseId(),
                value_micros = adjustedValueMicros,
                mediation_adapter_class_name = responseInfo.GetLoadedAdapterResponseInfo().AdapterClassName,
                precision_type = adValue.Precision.ToString()
            });

            if(TenjinManager.instance != null)
                TenjinManager.instance.LogAdMobImpressionFromJSON(json);
        #endif
    }
}