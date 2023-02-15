using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Firebase.Analytics;
using GoogleMobileAds.Api;
using GoogleMobileAds.Api.Mediation.Chartboost;
using GoogleMobileAds.Api.Mediation.AdColony;
using GoogleMobileAds.Api.Mediation.UnityAds;
using GoogleMobileAds.Api.Mediation.InMobi;
#if UNITY_ANDROID
using GoogleMobileAds.Android;
#endif


public class AdMob_Manager : MonoBehaviour {
    [Serializable]
    public class PlatformData {
        public PlatformAdData AndroidAdData;
        public PlatformAdData IosAdData;

        public PlatformAdData GetActive() {
            switch (Application.platform) {
                case RuntimePlatform.Android: return AndroidAdData;

                case RuntimePlatform.IPhonePlayer: return IosAdData;

                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.OSXEditor:
#if UNITY_ANDROID
                    return AndroidAdData;
#elif UNITY_IOS
                        return IosAdData;
#else
                        break;
#endif
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

        [Header("NO LONGER USED! Copy these IDs into the floor data above!")]
        public string[] interstitialId = new string[0];
        public string[] bannerId = new string[0];
        public string[] rewardId = new string[0];
        public string[] rewardedInterstitialId = new string[0];
        
        [Header("Additional Ad Settings")]
        // TagForChildDirectedTreatment will stop admob tracking this user and will not deliver interest based ads
        // This will reduce revenue for the game so don't set this unless specifically asked to!
        public bool tagForChildDirectedTreatment = false;

        public List<string> testDeviceIds = new List<string>(); // List of device ids which will be in test mode when test mode is enabled
    }

    public static AdMob_Manager instance;

    [Header("AdMob Script Settings")] public bool enableAdMob = true; // Is admob enabled? Will any banners or interstitials be triggered?
    public bool debugLogging = false; // Should debug messages be logged?
    public bool enableTestMode = false; // Test mode will display test ads which we are allowed to click

    public PlatformData platformAdData = new PlatformData();

    public bool isAdMobInitialized { get; set; }


    public enum BannerSizeType { NONE, SMARTBANNER, BANNER, LEADERBOARD, MEDIUMRECTANGLE }

    [Header("Preloaded Banner Settings")] 
    public BannerSizeType mainPreloadBannerType = BannerSizeType.SMARTBANNER;
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
    public float timeBetweenAdLoadRetry = 10f;

    // How many attempts will be made to reload ads which fail to load (a manual ad load request will always happen regardless of retries made)
    // But note that the retry count only resets when an ad request returns a valid response
    public int maxAutomaticAdLoadRetries = 5;

    // Uses full width and 50px or 90px height (depending on size) which excludes the normal 32px height option (used for mediation which doesn't support 32px heights)
    public bool useCustomSmartBannerSize = false;

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

    public bool activeBannerIsReady { get; private set; }
    public bool activeBannerIsLoading { get; private set; }
    public bool activeBannerIsVisible { get; private set; }
    public bool activeBannerWantedVisible { get; private set; }
    
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

    // If the user has granted us to show personalised ads
    private bool isGrantedPersonalisation;

    // If the user has selected that they're 18+ (enables showing of all ad types up to MA)
    private bool isMatureUser;

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
    public static event Action OnInterstitialAdReady;
    public static event Action OnInterstitialAdShown;
    public static event Action OnInterstitialAdClosed;
    public static event Action<AdFailedToLoadEventArgs> OnInterstitialAdFailedToLoad;
    
    private EventHandler<EventArgs>[] interstitialOnAdLoadedCallback;
    private EventHandler<EventArgs>[] interstitialOnAdOpeningCallback;
    private EventHandler<EventArgs>[] interstitialOnAdClosedCallback;
    private EventHandler<AdFailedToLoadEventArgs>[] interstitialOnAdFailedToLoadCallback;

    // Universal banner ad callbacks
    public static event Action OnBannerAdReady;
    public static event Action OnBannerAdShown;
    public static event Action OnBannerAdClosed;
    public static event Action<AdFailedToLoadEventArgs> OnBannerAdFailedToLoad;
    
    // EventHandlers which allow us to send custom banner callbacks which include the bannerRefId
    private EventHandler<EventArgs>[] bannerOnAdLoadedCallback;
    private EventHandler<EventArgs>[] bannerOnAdOpeningCallback;
    private EventHandler<EventArgs>[] bannerOnAdClosedCallback;
    private EventHandler<AdFailedToLoadEventArgs>[] bannerOnAdFailedToLoadCallback;

    // Universal reward ad callbacks
    public static event Action OnRewardAdReady;
    public static event Action OnRewardAdShown;
    public static event Action<Reward> OnRewardAdRewarded;
    public static event Action OnRewardAdClosed;
    public static event Action<AdErrorEventArgs> OnRewardAdFailedToShow;
    public static event Action<AdFailedToLoadEventArgs> OnRewardAdFailedToLoad;

    private EventHandler<EventArgs>[] rewardOnAdLoadedCallback;
    private EventHandler<EventArgs>[] rewardOnAdOpeningCallback;
    private EventHandler<EventArgs>[] rewardOnAdClosedCallback;
    private EventHandler<Reward>[] rewardOnUserEarnedRewardCallback;
    private EventHandler<AdFailedToLoadEventArgs>[] rewardOnAdFailedToLoadCallback;
    private EventHandler<AdErrorEventArgs>[] rewardOnAdFailedToShowCallback;

    // Universal rewarded interstitial ad callbacks
    public static event Action OnRewardedInterstitialAdReady;
    public static event Action OnRewardedInterstitialAdShown;
    public static event Action<Reward> OnRewardedInterstitialAdRewarded;
    public static event Action OnRewardedInterstitialAdClosed;
    public static event Action<AdErrorEventArgs> OnRewardedInterstitialAdFailedToShow;
    public static event Action<AdFailedToLoadEventArgs> OnRewardedInterstitialAdFailedToLoad;

    private EventHandler<EventArgs>[] rewardedIntOnPresentFullscreenContent;
    private EventHandler<EventArgs>[] rewardedIntOnDismissFullscreenContent;
    private EventHandler<AdErrorEventArgs>[] rewardedIntOnAdFailedToPresentFullscreenContent;

    // Private actual admob plugin initialisation complete (we don't announce initialization to be done until waiting a few extra frames for the mediation to be ready too)
    private static event Action<InitializationStatus> OnInitializationComplete;

    // Public callback to say admob is ready, this is called after initialisation AND mediation is ready
    // (if we don't wait for mediation to be ready before making ad calls we risk some mediators causing app crashes!)
    public static event Action OnAdMobReady;

    public float scrDPI { get; private set; }

    private PlatformAdData GetPlatformAdData() {
        cachedPlatformDataRef = cachedPlatformDataRef ?? platformAdData.GetActive();

        return cachedPlatformDataRef;
    }

    public void DebugSetRewardReadyEditor(bool wantReady, int rewardRefId = 0) {
#if UNITY_EDITOR
        rewardIsReady[rewardRefId] = wantReady;
#endif
    }

    private void Awake() {
        instance = instance ?? this;

		if (PlayerPrefs.GetInt("admob_test_mode", 0) == 1) {
            enableTestMode = true;
            debugLogging = true;
        }
		
        // We support loading from multiple ad ids to load different types of ads without needing to destroy them to change type and letting us display ads back to back if needed
        // Here we initialise banner related arrays for each bannerRefId type
        PlatformAdData platformAdData = GetPlatformAdData();

        totalIntSlots = platformAdData.interstitialFloorData.Length;
        totalBannerSlots = platformAdData.bannerFloorData.Length;
        totalRewardSlots = platformAdData.rewardFloorData.Length;
        totalRewardedIntSlots = platformAdData.rewardedInterstitialFloorData.Length;

        totalIntFloors = new int[totalIntSlots];
        totalBannerFloors = new int[totalBannerSlots];
        totalRewardFloors = new int[totalRewardSlots];
        totalRewardedIntFloors = new int[totalRewardedIntSlots];
        
        for (int i = 0; i < totalIntSlots; i++) totalIntFloors[i] = platformAdData.interstitialFloorData[i].floorId.Length;
        for (int i = 0; i < totalBannerSlots; i++) totalBannerFloors[i] = platformAdData.bannerFloorData[i].floorId.Length;
        for (int i = 0; i < totalRewardSlots; i++) totalRewardFloors[i] = platformAdData.rewardFloorData[i].floorId.Length;
        for (int i = 0; i < totalRewardedIntSlots; i++) totalRewardedIntFloors[i] = platformAdData.rewardedInterstitialFloorData[i].floorId.Length;
        
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

        interstitialOnAdClosedCallback = new EventHandler<EventArgs>[totalIntSlots];
        interstitialOnAdLoadedCallback = new EventHandler<EventArgs>[totalIntSlots];
        interstitialOnAdOpeningCallback = new EventHandler<EventArgs>[totalIntSlots];
        interstitialOnAdFailedToLoadCallback = new EventHandler<AdFailedToLoadEventArgs>[totalIntSlots];
        
        bannerOnAdClosedCallback = new EventHandler<EventArgs>[totalBannerSlots];
        bannerOnAdLoadedCallback = new EventHandler<EventArgs>[totalBannerSlots];
        bannerOnAdOpeningCallback = new EventHandler<EventArgs>[totalBannerSlots];
        bannerOnAdFailedToLoadCallback = new EventHandler<AdFailedToLoadEventArgs>[totalBannerSlots];
        
        rewardOnAdClosedCallback = new EventHandler<EventArgs>[totalRewardSlots];
        rewardOnAdLoadedCallback = new EventHandler<EventArgs>[totalRewardSlots];
        rewardOnAdOpeningCallback = new EventHandler<EventArgs>[totalRewardSlots];
        rewardOnUserEarnedRewardCallback = new EventHandler<Reward>[totalRewardSlots];
        rewardOnAdFailedToLoadCallback = new EventHandler<AdFailedToLoadEventArgs>[totalRewardSlots];
        rewardOnAdFailedToShowCallback = new EventHandler<AdErrorEventArgs>[totalRewardSlots];
        
        rewardedIntOnPresentFullscreenContent = new EventHandler<EventArgs>[totalRewardedIntSlots];
        rewardedIntOnDismissFullscreenContent = new EventHandler<EventArgs>[totalRewardedIntSlots];
        rewardedIntOnAdFailedToPresentFullscreenContent = new EventHandler<AdErrorEventArgs>[totalRewardedIntSlots];

        // Get the screen dots per inch
        scrDPI = JarLoader.GetDensity();

        // If the screen DPI can't be calculated then fallback to default and hope the ad won't overlap
        // Really this should never happen as we're checking the raw device dpi via Java and if for some reason that fails we then fallback to Unity's Screen.dpi
        // Unity 5 and earlier had issues where Screen.dpi would fail on a bunch of devices which is why switched over to grabbing the system dpi manually via Java
        if (scrDPI <= 0) {
            Debug.LogError("DPI checks failed! Falling back to default!");
            scrDPI = 160f;
        }

        // Load the personalisation choice, ad removal status and maturity status
        isGrantedPersonalisation = PlayerPrefs.GetInt(PREF_PERSONALISATION, 0) == 1;
        hasPurchasedAdRemoval = PlayerPrefs.GetInt(PREF_AD_REMOVAL, 0) == 1;
        isMatureUser = PlayerPrefs.GetInt(PREF_MATURE, 0) == 1;
    }

    public void Start() {
        if (enableAdMob) {
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

            // Manually initialise admob with an ad manager
            // Initializing AdMob can cause ANRs so make sure the app is doing as little as possible when initializing
            UnityMainThreadDispatcher.instance.Enqueue(() => MobileAds.Initialize(OnInitializationComplete));
#endif
        } else {
            Debug.Log("AdMob is not enabled, no adverts will be triggered!");
        }
    }

    // AdMob banners on android use dp scaling
    public float PxToDp(float pixels) {
        return 160f * pixels / scrDPI;
    }

    // AdMob banners on iOS use pt scaling
    public float PxToPt(float pixels) {
        return pixels * GetPixelToPointFactor();
    }

#if UNITY_EDITOR
    private IEnumerator EditorScheduleFakeInitializationFinished() {
        yield return new WaitForSecondsRealtime(1f);
        
        OnInitializationFinished(default);
    }
#endif
    
    private void OnInitializationFinished(InitializationStatus status) {
        if (enableAdMob) {
            UnityMainThreadDispatcher.instance.Enqueue(() => FirebaseAnalyticsManager.LogEvent("admob_ttl", "init_partial", Time.realtimeSinceStartup));
            
            StartCoroutine(DelayedInitialisationCompletion(status));
        }
    }

    // Extra waiting for mediation plugins to become ready to fix crashes caused by trying to load ads before mediation finished initializing
    private IEnumerator DelayedInitialisationCompletion(InitializationStatus status) {
        // BUGFIX: Wait before marking admob as initialized and preloading any ads!
        // There was a bug with IronSource mediation where ironsource SOMETIMES doesn't finish initialising
        // This caused the app to get stuck in an ANR deadlock when trying to call IronSource.SetConsent(..) as it saw it wasn't initialised and tried to initialise it again
        // This wait is not needed if not using ironsource mediation
        //for (int i = 0; i < 15; i++)
        //    yield return null;

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

        UnityMainThreadDispatcher.instance.Enqueue(() => FirebaseAnalyticsManager.LogEvent("admob_ttl", "init_full", Time.realtimeSinceStartup));
        
        // Force pauses the app while interstitials are open on iOS, this also mutes game audio
        MobileAds.SetiOSAppPauseOnBackground(true);

        if (OnAdMobReady != null)
            OnAdMobReady.Invoke();

        if (totalRewardSlots > 0) {
            // There's no harm in attempting to load interstitials again even if one is already pending
            // Preload a reward ad
            PreloadRewardAd();
        }

        if (totalRewardedIntSlots > 0) {
            PreloadRewardedInterstitialAd();
        }

        if (!hasPurchasedAdRemoval) {
            // If we've already sent a manual request to load ads before initialisation finished, don't bother calling the preload banner function
            if (!activeBannerIsLoading) {
                // Preload a banner ad
                PreloadBannerAd();
            }

            // There's no harm in attempting to load interstitials again even if one is already pending
            // Preload a interstitial ad
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

    private void SetupRewardAdCallbacks(int rewardRefId = 0) {
        PlatformAdData platformAdData = GetPlatformAdData();

        adMobRewardedAd[rewardRefId] = new RewardedAd(platformAdData.rewardFloorData[rewardRefId].floorId[rewardWantedFloorId]);

        rewardOnAdClosedCallback[rewardRefId] = (sender, args) => RewardAdClosed(sender, args, rewardRefId);
        rewardOnAdLoadedCallback[rewardRefId] = (sender, args) => RewardAdLoaded(sender, args, rewardRefId);
        rewardOnAdOpeningCallback[rewardRefId] = (sender, args) => RewardAdVisible(sender, args, rewardRefId);
        rewardOnUserEarnedRewardCallback[rewardRefId] = (sender, reward) => RewardAdRewarded(sender, reward, rewardRefId);
        rewardOnAdFailedToLoadCallback[rewardRefId] = (sender, args) => RewardAdFailedToLoad(sender, args, rewardRefId);
        rewardOnAdFailedToShowCallback[rewardRefId] = (sender, args) => RewardAdFailedToShow(sender, args, rewardRefId);

        adMobRewardedAd[rewardRefId].OnAdClosed += rewardOnAdClosedCallback[rewardRefId];
        adMobRewardedAd[rewardRefId].OnAdLoaded += rewardOnAdLoadedCallback[rewardRefId];
        adMobRewardedAd[rewardRefId].OnAdOpening += rewardOnAdOpeningCallback[rewardRefId];
        adMobRewardedAd[rewardRefId].OnUserEarnedReward += rewardOnUserEarnedRewardCallback[rewardRefId];
        adMobRewardedAd[rewardRefId].OnAdFailedToLoad += rewardOnAdFailedToLoadCallback[rewardRefId];
        adMobRewardedAd[rewardRefId].OnAdFailedToShow += rewardOnAdFailedToShowCallback[rewardRefId];
    }

    private void RewardedInterstitialAdCallback(RewardedInterstitialAd ad, AdFailedToLoadEventArgs errorArgs, int rewardedIntRefId = 0) {
        // errorArgs will be null if the ad did not fail to load
        if (errorArgs == null) {
            adMobRewardedInterstitialAd[rewardedIntRefId] = ad;

            RewardedInterstitialAdLoaded(ad, rewardedIntRefId);
            
            rewardedIntOnPresentFullscreenContent[rewardedIntRefId] = (sender, args) =>  RewardedInterstitialAdVisible(sender, args, rewardedIntRefId);
            rewardedIntOnDismissFullscreenContent[rewardedIntRefId] = (sender, args) => RewardedInterstitialAdClosed(sender, args, rewardedIntRefId);
            rewardedIntOnAdFailedToPresentFullscreenContent[rewardedIntRefId] = (sender, args) => RewardedInterstitialAdFailedToShow(sender, args, rewardedIntRefId);

            adMobRewardedInterstitialAd[rewardedIntRefId].OnAdDidPresentFullScreenContent += rewardedIntOnPresentFullscreenContent[rewardedIntRefId];
            adMobRewardedInterstitialAd[rewardedIntRefId].OnAdDidDismissFullScreenContent += rewardedIntOnDismissFullscreenContent[rewardedIntRefId];
            adMobRewardedInterstitialAd[rewardedIntRefId].OnAdFailedToPresentFullScreenContent += rewardedIntOnAdFailedToPresentFullscreenContent[rewardedIntRefId];
        } else {
            RewardedInterstitialAdFailedToLoad(ad, errorArgs, rewardedIntRefId);
        }
    }
    
    private bool IsStandardBannerSize(AdSize adSize) {
        return (adSize == AdSize.Banner || adSize == AdSize.Leaderboard || adSize == AdSize.MediumRectangle || adSize == AdSize.SmartBanner || adSize == AdSize.IABBanner);
    }
    
    // When a new adsize is wanted a new ad request must be made from scratch, re-generating the adMobBanner reference
    // Note: If you just want to hide/move the admob banner use the respective hide or reposition functions instead
    private void SetupBannerAdCallbacks(AdSize adSize, AdPosition adPosition, int bannerRefId = 0) {
        PlatformAdData platformAdData = GetPlatformAdData();

        AdSize actualAdSize = adSize;

        // Don't use adaptive banner sizes in the editor as the admob editor previews doesn't know how to display them, so let it use smart banners in the editor
#if !UNITY_EDITOR
            // On devices if useCustomSmartBannerSize is set then smart banners are replaced with adaptive banners
            if (useCustomSmartBannerSize && adSize == AdSize.SmartBanner) {
    #if UNITY_IOS
			    float scrHeight = PxToPt(Screen.height);
    #else
                float scrHeight = PxToDp(Screen.height);
    #endif

                AdSize adaptiveSize = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
                actualAdSize = adaptiveSize;
            }
#endif

#if UNITY_EDITOR
        // The Unity editor banner preview throws a KeyNotFoundException if we try showing a custom sized banner
        // Show a standard sized banner to avoid this
        // Hopefully google fixes this some day...
        if (!IsStandardBannerSize(actualAdSize)) {
            switch (actualAdSize.AdType) {
                case AdSize.Type.Standard:
                    actualAdSize = AdSize.MediumRectangle;
                    break;
                case AdSize.Type.SmartBanner:
                    actualAdSize = AdSize.SmartBanner;
                    break;
                case AdSize.Type.AnchoredAdaptive:
                    actualAdSize = AdSize.MediumRectangle;
                    break;
            }

            Debug.Log("Falling back to showing a " + actualAdSize.Width + " x " + actualAdSize.Height + " editor admob ad as custom editor preview sizes are not yet supported!");
        }
#endif
        
        bannerOnAdClosedCallback[bannerRefId] = (sender, args) => BannerAdClosed(sender, args, bannerRefId);
        bannerOnAdLoadedCallback[bannerRefId] = (sender, args) => BannerAdLoaded(sender, args, bannerRefId);
        bannerOnAdOpeningCallback[bannerRefId] = (sender, args) => BannerAdVisible(sender, args, bannerRefId);
        bannerOnAdFailedToLoadCallback[bannerRefId] = (sender, args) => BannerAdFailedToLoad(sender, args, bannerRefId);
        
        // Unregister the previous banner (if one exists)
        if (adMobBanner[bannerRefId] != null) {
            adMobBanner[bannerRefId].OnAdClosed -= bannerOnAdClosedCallback[bannerRefId];
            adMobBanner[bannerRefId].OnAdLoaded -= bannerOnAdLoadedCallback[bannerRefId];
            adMobBanner[bannerRefId].OnAdOpening -= bannerOnAdOpeningCallback[bannerRefId];
            adMobBanner[bannerRefId].OnAdFailedToLoad -= bannerOnAdFailedToLoadCallback[bannerRefId];

            adMobBanner[bannerRefId].Destroy();
        }

        // Load a banner ad marking it as hidden, this script will handle showing the banner
        adMobBanner[bannerRefId] = new BannerView(platformAdData.bannerFloorData[bannerRefId].floorId[bannerWantedFloorId], actualAdSize, adPosition);

        // Fixes a bug where banners would flash on the screen for a frame when being loaded
        adMobBanner[bannerRefId].Hide();

        // Register the banner ad events
        adMobBanner[bannerRefId].OnAdClosed += bannerOnAdClosedCallback[bannerRefId];
        adMobBanner[bannerRefId].OnAdLoaded += bannerOnAdLoadedCallback[bannerRefId];
        adMobBanner[bannerRefId].OnAdOpening += bannerOnAdOpeningCallback[bannerRefId];
        adMobBanner[bannerRefId].OnAdFailedToLoad += bannerOnAdFailedToLoadCallback[bannerRefId];

        bannerIsLoading[bannerRefId] = true;
        bannerIsVisible[bannerRefId] = false;
        bannerIsReady[bannerRefId] = false;

        activeBannerIsLoading = true;
        activeBannerIsVisible = false;
        activeBannerIsReady = false;

        bannerInMemorySize[bannerRefId] = adSize;
        bannerInMemorySizeSet[bannerRefId] = true;

        bannerInMemoryPosition[bannerRefId] = adPosition;
        bannerInMemoryPositionSet[bannerRefId] = true;

        bannerInMemoryUseXYPosition[bannerRefId] = false;
    }

    // When a new adsize is wanted a new ad request must be made from scratch, re-generating the adMobBanner reference
    // Note: If you just want to hide/move the admob banner use the respective hide or reposition functions instead
    private void SetupBannerAdCallbacks(AdSize adSize, int xPos, int yPos, int bannerRefId = 0) {
        if (bannerRefId >= adMobBanner.Length) {
            Debug.LogError(bannerRefId + " is an invalid banner ref id, increase the size of the bannerId array to increase how many banners can be loaded at once.");
            return;
        }
        
        PlatformAdData platformAdData = GetPlatformAdData();

        // Unregister the previous banner (if one exists)
        if (adMobBanner[bannerRefId] != null) {
            adMobBanner[bannerRefId].OnAdClosed -= bannerOnAdClosedCallback[bannerRefId];
            adMobBanner[bannerRefId].OnAdLoaded -= bannerOnAdLoadedCallback[bannerRefId];
            adMobBanner[bannerRefId].OnAdOpening -= bannerOnAdOpeningCallback[bannerRefId];
            adMobBanner[bannerRefId].OnAdFailedToLoad -= bannerOnAdFailedToLoadCallback[bannerRefId];

            adMobBanner[bannerRefId].Destroy();
        }

        AdSize actualAdSize = adSize;

// Don't use adaptive banner sizes in the editor as the admob editor previews doesn't know how to display them, so let it use smart banners in the editor
#if !UNITY_EDITOR
        // On devices if useCustomSmartBannerSize is set then smart banners are replaced with adaptive banners
        if (useCustomSmartBannerSize && adSize == AdSize.SmartBanner) {
#if UNITY_IOS
			float scrHeight = PxToPt(Screen.height);
#else
            float scrHeight = PxToDp(Screen.height);
#endif

            //int wantedAdHeight = scrHeight > 720f ? 90 : 50;
            //actualAdSize = new AdSize(AdSize.FullWidth, wantedAdHeight);

            AdSize adaptiveSize = AdSize.GetCurrentOrientationAnchoredAdaptiveBannerAdSizeWithWidth(AdSize.FullWidth);
            actualAdSize = adaptiveSize;
        }
#endif
        
#if UNITY_EDITOR
        // The Unity editor banner preview throws a KeyNotFoundException if we try showing a custom sized banner
        // Show a standard sized banner to avoid this
        // Hopefully google fixes this some day...
        if (!IsStandardBannerSize(actualAdSize)) {
            switch (actualAdSize.AdType) {
                case AdSize.Type.Standard: actualAdSize = AdSize.MediumRectangle; break;
                case AdSize.Type.SmartBanner: actualAdSize = AdSize.SmartBanner; break;
                case AdSize.Type.AnchoredAdaptive:actualAdSize = AdSize.MediumRectangle; break;
            }

            Debug.Log("Falling back to showing a " + actualAdSize.Width + " x " + actualAdSize.Height + " editor admob ad as custom editor preview sizes are not yet supported!");
        }
#endif
        
        // Load a banner ad marking it as hidden, this script will handle showing the banner
        // Note: Don't worry about 'Invalid ad size requested' messages, they are just from UnityAds banner mediation
        // As UnityAds banners only support aspect ratios of 0.5 x 0.7 (or 0.7 x 0.5) (aka BANNER or LEADERBOARD ad sizes)
        adMobBanner[bannerRefId] = new BannerView(platformAdData.bannerFloorData[bannerRefId].floorId[bannerWantedFloorId], actualAdSize, xPos, yPos);

        // Fixes a bug where banners would flash on the screen for a frame when being loaded
        adMobBanner[bannerRefId].Hide();

        bannerIsLoading[bannerRefId] = true;
        bannerIsVisible[bannerRefId] = false;
        bannerIsReady[bannerRefId] = false;

        activeBannerIsLoading = true;
        activeBannerIsVisible = false;
        activeBannerIsReady = false;

        bannerOnAdClosedCallback[bannerRefId] = (sender, args) => BannerAdClosed(sender, args, bannerRefId);
        bannerOnAdLoadedCallback[bannerRefId] = (sender, args) => BannerAdLoaded(sender, args, bannerRefId);
        bannerOnAdOpeningCallback[bannerRefId] = (sender, args) => BannerAdVisible(sender, args, bannerRefId);
        bannerOnAdFailedToLoadCallback[bannerRefId] = (sender, args) => BannerAdFailedToLoad(sender, args, bannerRefId);

        // Register the banner ad events
        adMobBanner[bannerRefId].OnAdClosed += bannerOnAdClosedCallback[bannerRefId];
        adMobBanner[bannerRefId].OnAdLoaded += bannerOnAdLoadedCallback[bannerRefId];
        adMobBanner[bannerRefId].OnAdOpening += bannerOnAdOpeningCallback[bannerRefId];
        adMobBanner[bannerRefId].OnAdFailedToLoad += bannerOnAdFailedToLoadCallback[bannerRefId];

        bannerInMemorySize[bannerRefId] = adSize;
        bannerInMemorySizeSet[bannerRefId] = true;

        bannerInMemoryPosition[bannerRefId] = AdPosition.TopLeft; // Manual x,y positioning uses the top left as the base position
        bannerInMemoryPositionSet[bannerRefId] = true;

        bannerInMemoryPositionXY[bannerRefId] = new IntVector2(xPos, yPos);
        bannerInMemoryUseXYPosition[bannerRefId] = true;
    }

    private void SetupInterstitialAdCallbacks(int interstitialRefId = 0) {
        if (interstitialRefId >= adMobInterstitial.Length) {
            Debug.LogError(interstitialRefId + " is an invalid interstitial ref id, increase the size of the interstitialId array to increase how many interstitials can be loaded at once.");
            return;
        }
        
        PlatformAdData platformAdData = GetPlatformAdData();
        
        // Unregister the previous interstitial (if one existed)
        if (adMobInterstitial[interstitialRefId] != null) {
            adMobInterstitial[interstitialRefId].OnAdClosed -= interstitialOnAdClosedCallback[interstitialRefId];
            adMobInterstitial[interstitialRefId].OnAdLoaded -= interstitialOnAdLoadedCallback[interstitialRefId];
            adMobInterstitial[interstitialRefId].OnAdOpening -= interstitialOnAdOpeningCallback[interstitialRefId];
            adMobInterstitial[interstitialRefId].OnAdFailedToLoad -= interstitialOnAdFailedToLoadCallback[interstitialRefId];

            adMobInterstitial[interstitialRefId].Destroy();
        }

        adMobInterstitial[interstitialRefId] = new InterstitialAd(platformAdData.interstitialFloorData[interstitialRefId].floorId[intWantedFloorId]);

        interstitialOnAdClosedCallback[interstitialRefId] = (sender, args) => InterstitialAdClosed(sender, args, interstitialRefId);
        interstitialOnAdLoadedCallback[interstitialRefId] = (sender, args) => InterstitialAdLoaded(sender, args, interstitialRefId);
        interstitialOnAdOpeningCallback[interstitialRefId] = (sender, args) => InterstitialAdVisible(sender, args, interstitialRefId);
        interstitialOnAdFailedToLoadCallback[interstitialRefId] = (sender, args) => InterstitialAdFailedToLoad(sender, args, interstitialRefId);
        
        adMobInterstitial[interstitialRefId].OnAdClosed += interstitialOnAdClosedCallback[interstitialRefId];
        adMobInterstitial[interstitialRefId].OnAdLoaded += interstitialOnAdLoadedCallback[interstitialRefId];
        adMobInterstitial[interstitialRefId].OnAdOpening += interstitialOnAdOpeningCallback[interstitialRefId];
        adMobInterstitial[interstitialRefId].OnAdFailedToLoad += interstitialOnAdFailedToLoadCallback[interstitialRefId];
    }

    private AdRequest GenerateAdRequest() {
        AdRequest.Builder adBuilder = new AdRequest.Builder();
        RequestConfiguration.Builder adRequestConfiguration = new RequestConfiguration.Builder();

        PlatformAdData platformAdData = GetPlatformAdData();
        
        // Ad personalisation opt in
        adBuilder.AddExtra("npa", isGrantedPersonalisation ? "0" : "1");

        if (enableTestMode) {
            List<string> testDeviceIds = new List<string>();

            // All simulators are marked as test devices
            testDeviceIds.Add(AdRequest.TestDeviceSimulator);

            // Add all devices preset as test devices from the editor inspector
            foreach (string deviceId in platformAdData.testDeviceIds)
                testDeviceIds.Add(deviceId);

            adRequestConfiguration.SetTestDeviceIds(testDeviceIds);
        }

        Chartboost.AddDataUseConsent(isGrantedPersonalisation ? CBGDPRDataUseConsent.Behavioral : CBGDPRDataUseConsent.NonBehavioral);
        
        AdColonyAppOptions.SetUserId(FirebaseManager.instance.persistantUserId);
        AdColonyAppOptions.SetTestMode(enableTestMode);

        AdColonyAppOptions.SetGDPRRequired(true);
        AdColonyAppOptions.SetGDPRConsentString(isGrantedPersonalisation ? "1" : "0");

        UnityAds.SetConsentMetaData("gdpr.consent", isGrantedPersonalisation);

        Dictionary<string, string> inMobiConsentDict = new Dictionary<string, string>();
        inMobiConsentDict.Add("gdpr_consent_available", isGrantedPersonalisation ? "true" : "false");
        inMobiConsentDict.Add("gdpr", "1");

        InMobi.UpdateGDPRConsent(inMobiConsentDict);

        adRequestConfiguration.SetTagForChildDirectedTreatment(platformAdData.tagForChildDirectedTreatment ? TagForChildDirectedTreatment.True : TagForChildDirectedTreatment.False);

        MobileAds.SetRequestConfiguration(adRequestConfiguration.build());

        return adBuilder.Build();
    }

    public void SetPersonalisationGranted(bool isGranted) {
        PlayerPrefs.SetInt(PREF_PERSONALISATION, isGranted ? 1 : 0);
        isGrantedPersonalisation = isGranted;
    }

    public bool IsPersonalisationGranted() {
        return isGrantedPersonalisation;
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

    public void SetMatureUser(bool isMature) {
        PlayerPrefs.SetInt(PREF_MATURE, isMature ? 1 : 0);
        isMatureUser = isMature;
    }

    public bool IsMatureUser() {
        return isMatureUser;
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
            UnityMainThreadDispatcher.instance.Enqueue(() => RewardedInterstitialAd.LoadAd(GetPlatformAdData().rewardedInterstitialFloorData[rewardIntRefId].floorId[rewardedIntWantedFloorId], GenerateAdRequest(), (sender, args) => { RewardedInterstitialAdCallback(sender, args, rewardIntRefId);}));
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
        
        if(debugLogging)
            Debug.Log("AdMob Debug - ShowRewardedInterstitialAd()");

        rewardedIntWantedVisible[rewardIntRefId] = true;

        if (!rewardedIntIsVisible[rewardIntRefId]) {
            if (rewardedIntIsReady[rewardIntRefId]) {
                if (adMobRewardedInterstitialAd[rewardIntRefId] != null) {
                    // We're ready to show the rewarded interstitial ad
                    // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
                    UnityMainThreadDispatcher.instance.Enqueue(() => adMobRewardedInterstitialAd[rewardIntRefId].Show(reward => RewardedInterstitialAdRewarded(reward, rewardIntRefId)));
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
        
        if(debugLogging)
            Debug.Log("AdMob Debug - DestroyRewardedInterstitialAd()");

        if (adMobRewardedInterstitialAd[rewardIntRefId] != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
            UnityMainThreadDispatcher.instance.Enqueue(() => adMobRewardedInterstitialAd[rewardIntRefId].Destroy());
        }
    }

    public void DestroyRewardedInterstitialAd() {
        for(int i=0;i < rewardedIntWantedVisible.Length;i++)
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

            // If the reward ad instance isn't set then the callbacks will be registered
            SetupRewardAdCallbacks(rewardRefId);

            rewardIsLoading[rewardRefId] = true;
            rewardWantedVisible[rewardRefId] = displayImmediately;

            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
            UnityMainThreadDispatcher.instance.Enqueue(() => adMobRewardedAd[rewardRefId].LoadAd(GenerateAdRequest()));
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
                    UnityMainThreadDispatcher.instance.Enqueue(() => adMobRewardedAd[rewardRefId].Show());
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
        rewardIsLoading [rewardRefId]= false;
        rewardIsReady[rewardRefId] = false;

        if (debugLogging)
            Debug.Log("AdMob Debug - DestroyRewardAd()");

        adMobRewardedAd[rewardRefId].Destroy();
    }

    public void DestroyRewardAd() {
        for(int i=0;i < adMobRewardedAd.Length;i++)
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
            SetupInterstitialAdCallbacks();

            intIsLoading[interstitialRefId] = true;
            intWantedVisible[interstitialRefId] = displayImmediately;

            if (adMobInterstitial != null) {
                // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                UnityMainThreadDispatcher.instance.Enqueue(() => adMobInterstitial[interstitialRefId].LoadAd(GenerateAdRequest()));
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
                            UnityMainThreadDispatcher.instance.Enqueue(() => adMobInterstitial[interstitialRefId].Show());
                        }
                    }
                } else {
                    if (adMobInterstitial[interstitialRefId] != null) {
                        // Show the interstitial
                        // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                        UnityMainThreadDispatcher.instance.Enqueue(() => adMobInterstitial[interstitialRefId].Show());
                    }
                }
            } else {
                LoadInterstitialAd(true, useWaitScreen, interstitialRefId, true);
            }
        }
    }

    public bool isIntLoadingScreenActive { get; private set; }

    private IEnumerator ShowInterstitialAdAfterDelay(int interstitialRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            yield break;

        // Temp hide banner ad if one is visible
        HideBannerAd(true);

        yield return null;

        if(interstitialWaitScreen != null)
            interstitialWaitScreen.SetActive(true);
            
        isIntLoadingScreenActive = true;

        yield return new WaitForSecondsRealtime(interstitialWaitTime);

        OnInterstitialAdClosed += HideInterstitialLoadingScreen;

        // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
        UnityMainThreadDispatcher.instance.Enqueue(() => adMobInterstitial[interstitialRefId].Show());

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
        if(interstitialWaitScreen != null)
            interstitialWaitScreen.SetActive(false);

        // Show banner ad again if one was visible previously
        ShowBannerAd();

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

        if (adMobInterstitial != null) {
            // Ensure the admob request is sent on the main thread, otherwise it may cause unexpected behaviour on iOS
            UnityMainThreadDispatcher.instance.Enqueue(() => adMobInterstitial[interstitialRefId].Destroy());
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

        if (bannerRefId != activeBannerRefId)
            SetActiveBannerRefId(bannerRefId);

        bannerWantedVisible[bannerRefId] = displayImmediately;
        activeBannerWantedVisible = displayImmediately;

        if (displayImmediately)
            bannerOverlayDepth = 0;

        // Check if we can perform the action for the current method
        if (!bannerIsLoading[bannerRefId] && !bannerIsReady[bannerRefId] && !bannerIsVisible[bannerRefId]) {
            if (isAdMobInitialized) {
                bannerIsLoading[bannerRefId] = true;
                activeBannerIsLoading = true;

                SetupBannerAdCallbacks(adSize, adPosition, bannerRefId);

                // Ensure the admob request is sent on the main thread, otherwise it causes issues on iOS duplicating ads
                UnityMainThreadDispatcher.instance.Enqueue(() => InternalLoadBanner(bannerRefId, GenerateAdRequest()));
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
                        if(debugLogging)
                            Debug.Log("AdMob Debug - Repositioning AdMob Banner to " + adPosition);
                        
#if !UNITY_EDITOR
                        adMobBanner[bannerRefId].SetPosition(adPosition);
                        
                        if (displayImmediately && !bannerIsVisible[bannerRefId])
                             ShowBannerAd(bannerRefId, true);
#else
                        // AdMob banner editor previews currently do not support SetPosition so we need to destroy and remake the ad in the editor
                        bannerReloadCoroutine = StartCoroutine(ReloadBannerAd(adSize, adPosition, displayImmediately, bannerRefId));
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

                bannerReloadCoroutine = StartCoroutine(ReloadBannerAd(adSize, adPosition, displayImmediately, bannerRefId));
            }
        }
    }

    private Coroutine bannerReloadCoroutine;

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

        if (bannerRefId != activeBannerRefId)
            SetActiveBannerRefId(bannerRefId);

        bannerWantedVisible[bannerRefId] = displayImmediately;
        activeBannerWantedVisible = displayImmediately;

        if (displayImmediately)
            bannerOverlayDepth = 0;

        // Check if we can perform the action for the current method
        if (!bannerIsLoading[bannerRefId] && !bannerIsReady[bannerRefId] && !bannerIsVisible[bannerRefId]) {
            if (isAdMobInitialized) {
                bannerIsLoading[bannerRefId] = true;
                activeBannerIsLoading = true;

                SetupBannerAdCallbacks(adSize, xPos, yPos, bannerRefId);

                // Ensure the admob request is sent on the main thread, otherwise it causes issues on iOS duplicating ads
                UnityMainThreadDispatcher.instance.Enqueue(() => InternalLoadBanner(bannerRefId, GenerateAdRequest()));
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
                    if(debugLogging)
                        Debug.Log("AdMob Debug - Repositioning AdMob Banner to " + xPos + ", " + yPos);
                    
#if !UNITY_EDITOR
                    // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                    UnityMainThreadDispatcher.instance.Enqueue(() => InternalSetBannerPosition(bannerRefId, xPos, yPos));
                    
                    if (displayImmediately && !bannerIsVisible[bannerRefId])
                        ShowBannerAd(bannerRefId, true);
#else
                    // AdMob banner editor previews currently do not support SetPosition so we need to destroy and remake the ad in the editor
                    bannerReloadCoroutine = StartCoroutine(ReloadBannerAd(adSize, xPos, yPos, displayImmediately, bannerRefId));
#endif
                }
            } else {
                bannerReloadCoroutine = StartCoroutine(ReloadBannerAd(adSize, xPos, yPos, displayImmediately, bannerRefId));
            }
        }
    }

    private void InternalLoadBanner(int bannerRefId, AdRequest request) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null)
            adMobBanner[bannerRefId].LoadAd(request);
    }

    private void InternalSetBannerPosition(int bannerRefId, int xPos, int yPos) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null)
            adMobBanner[bannerRefId].SetPosition(xPos, yPos);
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

            if (bannerRefId == activeBannerRefId)
                activeBannerWantedVisible = displayImmediately;
        } else {
            if (bannerInMemoryUseXYPosition[bannerRefId]) {
                LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPositionXY[bannerRefId].x, bannerInMemoryPositionXY[bannerRefId].y, displayImmediately, bannerRefId);
            } else {
                LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPosition[bannerRefId], displayImmediately, bannerRefId);
            }
        }
    }

    private int activeBannerRefId;

    public void SetActiveBannerRefId(int bannerRefId) {
        // Force hide all banners and reset their overlay depths
        HideBannerAd(false);

        activeBannerRefId = bannerRefId;

        activeBannerIsReady = bannerIsReady[activeBannerRefId];
        activeBannerIsLoading = bannerIsLoading[activeBannerRefId];
        activeBannerIsVisible = bannerIsVisible[activeBannerRefId];
        activeBannerWantedVisible = bannerWantedVisible[activeBannerRefId];
    }

    public void ShowBannerAd(int bannerRefId, bool forceShow = false) {
        SetActiveBannerRefId(bannerRefId);
        ShowBannerAd(forceShow);
    }

    /// <summary>
    /// Shows a banner advert if one is loaded in memory.
    /// </summary>
    public void ShowBannerAd(bool forceShow = false) {
        if (!enableAdMob || hasPurchasedAdRemoval)
            return;

        if (debugLogging)
            Debug.Log("AdMob Debug - ShowBannerAd(" + forceShow + ")");

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
            activeBannerWantedVisible = bannerPrevState;

            if (debugLogging)
                Debug.Log("AdMob Debug - Banner wanted set to prev state: " + bannerPrevState);
        } else {
            bannerWantedVisible[activeBannerRefId] = true;
            activeBannerWantedVisible = true;
            bannerOverlayDepth = 0;
        }

        if (!bannerWantedVisible[activeBannerRefId])
            return;

        // Check if we can perform the action for the current method
        if (!bannerIsVisible[activeBannerRefId]) {
            if (bannerIsReady[activeBannerRefId]) {
                // Show the banner
                // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                if (isAdMobInitialized)
                    UnityMainThreadDispatcher.instance.Enqueue(() => InternalShowBanner(activeBannerRefId));
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
            adMobBanner[bannerRefId].Show();
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
                    // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                    if (isAdMobInitialized)
                        UnityMainThreadDispatcher.instance.Enqueue(() => InternalHideBanner(bannerId));
                }
            }
        }

        activeBannerWantedVisible = false;
        activeBannerIsVisible = false;
    }

    private void InternalHideBanner(int bannerRefId) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null)
            adMobBanner[bannerRefId].Hide();
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

        if (bannerRefId == activeBannerRefId) {
            activeBannerWantedVisible = false;
            activeBannerIsVisible = false;
        }

        if (adMobBanner[bannerRefId] != null) {
            // Hide the banner advert from view (This does not unload it from memory)
            // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
            if (isAdMobInitialized)
                UnityMainThreadDispatcher.instance.Enqueue(() => InternalHideBanner(bannerRefId));
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

            activeBannerWantedVisible = false;
            activeBannerIsLoading = false;
            activeBannerIsReady = false;
            activeBannerIsVisible = false;

            //bannerInMemorySizeSet[activeBannerRefId] = false;
            //bannerInMemorySize[activeBannerRefId] = default(AdSize);

            //bannerInMemoryPositionSet[activeBannerRefId] = false;
            //bannerInMemoryPosition[activeBannerRefId] = default(AdPosition);
        }

        // Changed to only destroy if forceDestroy variable is true - No need to destroy the banner fully, it causes a bug where if another ad is loaded this frame it'll get destroyed instantly..
        if (adMobBanner[activeBannerRefId] != null) {
            if (forceDestroy) {
                adMobBanner[activeBannerRefId].OnAdClosed -= bannerOnAdClosedCallback[activeBannerRefId];
                adMobBanner[activeBannerRefId].OnAdLoaded -= bannerOnAdLoadedCallback[activeBannerRefId];
                adMobBanner[activeBannerRefId].OnAdOpening -= bannerOnAdOpeningCallback[activeBannerRefId];
                adMobBanner[activeBannerRefId].OnAdFailedToLoad -= bannerOnAdFailedToLoadCallback[activeBannerRefId];

                // Ensure the admob request is sent on the main thread, otherwise it may caused unexpected behaviour on iOS
                if (isAdMobInitialized)
                    UnityMainThreadDispatcher.instance.Enqueue(() => InternalDestroyBanner(activeBannerRefId));
            } else {
                HideBannerAd(false);
            }
        }
    }

    private void InternalDestroyBanner(int bannerRefId) {
        if (adMobBanner.Length > bannerRefId && adMobBanner[bannerRefId] != null)
            adMobBanner[bannerRefId].Destroy();
    }

    /// <summary>
    /// Get a cached points scale factor or generate a dummy banner to get the point scale factor for iOS admob
    /// </summary>
    public float GetPixelToPointFactor() {
#if !UNITY_EDITOR
            if (!pointScaleFactorSet)
            {
                BannerView banner = new BannerView(GetPlatformAdData().bannerId[0], AdSize.Banner, AdPosition.TopLeft);

                cachedPointScaleFactor = AdSize.Banner.Width / banner.GetWidthInPixels();
                pointScaleFactorSet = true;
            }
#else
        cachedPointScaleFactor = 1f;
#endif

        return cachedPointScaleFactor;
    }

    [Obsolete("Call OpenAdInspector() instead. The mediation test suite is depreciated and no longer used.")]
    public void ShowMediationTestSuite() {
        OpenAdInspector();
    }
    
    public void OpenAdInspector() {
        if (!isAdMobInitialized)
            return;
        
        if (PlayerPrefs.GetInt("admob_test_mode", 0) == 0) {
            PlayerPrefs.SetInt("admob_test_mode", 1);
            
            MessagePopupManager.Instance.ShowMessage("AdMob Test Mode Enabled", "You must now restart the app to get test ads and use the ad inspector!", Application.Quit, "Close app");
        } else {
            // Enable test mode and keep it enabled just incase we want to continue debugging with test logging
            enableTestMode = true;
            debugLogging = true;

            UnityMainThreadDispatcher.instance.Enqueue(() => DoOpenAdInspectorMainThread());
        }
    }

    private void DoOpenAdInspectorMainThread() {
        // Generate an ad request to use with the mediation test suite (this sets up test device ids and other request parameters)
        MobileAds.OpenAdInspector(error => {
            if (error != null) {
                string errorMsg = error.GetMessage();
                
                Debug.LogError("Failed to open ad inspector! (" + errorMsg + ")");
                
                MessagePopupManager.Instance.ShowMessage("Failed to open Ad Inspector!", errorMsg);
            } else {
                Debug.Log("Ad inspector opened successfully!");
            }
        });
    }

    private IEnumerator RetryBannerAdLoad(int bannerRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * bannerFloorGroupAttemptedRetries[bannerRefId];

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
                UnityMainThreadDispatcher.instance.Enqueue(() => LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPositionXY[bannerRefId].x, bannerInMemoryPositionXY[bannerRefId].y, bannerWantedVisible[bannerRefId]));
            } else {
                UnityMainThreadDispatcher.instance.Enqueue(() => LoadBannerAd(bannerInMemorySize[bannerRefId], bannerInMemoryPosition[bannerRefId], bannerWantedVisible[bannerRefId]));
            }
        }
    }

    private IEnumerator RetryRewardedInterstitialAdLoad(int rewardedIntRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * rewardedIntFloorGroupAttemptedRetries[rewardedIntRefId];

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

        UnityMainThreadDispatcher.instance.Enqueue(() => LoadRewardedInterstitialAd(rewardedIntWantedVisible[rewardedIntRefId], rewardedIntRefId));
    }
    
    private IEnumerator RetryRewardAdLoad(int rewardRefId = 0) {
        if (!enableAdMob || !isAdMobInitialized)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * rewardFloorGroupAttemptedRetries[rewardRefId];

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

        UnityMainThreadDispatcher.instance.Enqueue(() => LoadRewardAd(rewardWantedVisible[rewardRefId]));
    }

    private IEnumerator RetryInterstitialAdLoad(int interstitialRefId) {
        if (!enableAdMob || !isAdMobInitialized || hasPurchasedAdRemoval)
            yield break;

        float curWaitTime = timeBetweenAdLoadRetry * intFloorGroupAttemptedRetries[interstitialRefId];

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
        
        UnityMainThreadDispatcher.instance.Enqueue(() => LoadInterstitialAd(intWantedVisible[interstitialRefId], true, interstitialRefId));
    }

    // Rewarded Interstitial Ad Callbacks
    private void RewardedInterstitialAdVisible(object sender, EventArgs args, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad visible");

        rewardedIntIsVisible[rewardedIntRefId] = true;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardedInterstitialAdShown?.Invoke());
    }

    // Reward Ad Callbacks
    private void RewardedInterstitialAdClosed(object sender, EventArgs args, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad closed");

        rewardedIntIsReady[rewardedIntRefId] = false;
        rewardedIntIsVisible[rewardedIntRefId] = false;
        rewardedIntIsLoading[rewardedIntRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardedInterstitialAdClosed?.Invoke());

        // Wait until we're sure we know if the player watched the advert in full, as loading a new rewarded interstitial ad will cancel any incoming callbacks from the previous ad
        StartCoroutine(DelayedRewardedInterstitialAdReload(rewardedIntRefId));
    }

    private IEnumerator DelayedRewardedInterstitialAdReload(int rewardedIntRefId = 0) {
        // Wait 5 seconds
        yield return new WaitForSecondsRealtime(5f);

        LoadRewardedInterstitialAd(false, rewardedIntRefId, true);
    }

    private void RewardedInterstitialAdFailedToShow(object sender, AdErrorEventArgs loadFailArgs, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;
        
        AdError adError = loadFailArgs.AdError;

        if (debugLogging) {
            Debug.LogError("AdMob Debug - Failed to show rewarded interstitial ad! (Ref: " + rewardedIntRefId + ", Floor: " + rewardedIntWantedFloorId + ") " + adError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + adError.ToString());
        }

        rewardedIntIsReady[rewardedIntRefId] = false;
        rewardedIntIsLoading[rewardedIntRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardedInterstitialAdFailedToShow?.Invoke(loadFailArgs));

        FirebaseAnalyticsManager.LogEvent("reward_ad_show_failed", "error", adError.GetCode());

        UnityMainThreadDispatcher.instance.Enqueue(() => AttemptRewardedInterstitialAdRetry(rewardedIntRefId));
    }

    private void RewardedInterstitialAdFailedToLoad(object sender, AdFailedToLoadEventArgs loadFailArgs, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;
        
        LoadAdError loadAdError = loadFailArgs.LoadAdError;

        if (debugLogging) {
            Debug.LogError("AdMob Debug - Failed to load reward ad! " + loadAdError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + loadAdError.ToString());
            Debug.LogError("AdMob Debug - Mediation Response: " + loadAdError.GetResponseInfo().ToString());
        }

        rewardIsReady[rewardedIntRefId] = false;
        rewardIsLoading[rewardedIntRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardedInterstitialAdFailedToLoad?.Invoke(loadFailArgs));

        FirebaseAnalyticsManager.LogEvent("reward_ad_load_failed", "error", loadAdError.GetCode());

        UnityMainThreadDispatcher.instance.Enqueue(() => AttemptRewardedInterstitialAdRetry(rewardedIntRefId));
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

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardedInterstitialAdRewarded?.Invoke(reward));
    }
    
    private void RewardedInterstitialAdLoaded(RewardedInterstitialAd ad, int rewardedIntRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Rewarded Interstitial ad loaded");

        if (!hasLoadedAnyRewardedInt) {
            UnityMainThreadDispatcher.instance.Enqueue(() => FirebaseAnalyticsManager.LogEvent("admob_ttl", "first_rwint_load", Time.realtimeSinceStartup));
            hasLoadedAnyRewardedInt = true;
        }
        
        rewardedIntIsReady[rewardedIntRefId] = true;
        rewardedIntIsLoading[rewardedIntRefId] = false;
        rewardedIntFloorGroupAttemptedRetries[rewardedIntRefId] = 0;
        rewardedIntWantedFloorId = 0;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardedInterstitialAdReady?.Invoke());

        if (rewardedIntWantedVisible[rewardedIntRefId])
            ShowRewardedInterstitialAd(rewardedIntRefId);
    }

    // Reward Ad Callbacks
    private void RewardAdClosed(object sender, EventArgs args, int rewardRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad closed");

        // This is called when the reward ad is closed by skipping OR when closed by watching to completion
        // Look at OnRewardAdRewarded if you want the completion status
        rewardIsReady[rewardRefId] = false;
        rewardIsVisible[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardAdClosed?.Invoke());

        // Wait until we're sure we know if the player watched the advert in full, as loading a new reward ad will cancel any incoming callbacks from the previous ad
        StartCoroutine(DelayedRewardAdReload(rewardRefId));
    }

    private IEnumerator DelayedRewardAdReload(int rewardRefId) {
        // Wait 5 seconds
        yield return new WaitForSecondsRealtime(5f);

        LoadRewardAd(false, rewardRefId, true);
    }

    private void RewardAdLoaded(object sender, EventArgs args, int rewardRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad loaded");

        if (!hasLoadedAnyReward) {
            UnityMainThreadDispatcher.instance.Enqueue(() => FirebaseAnalyticsManager.LogEvent("admob_ttl", "first_rw_load", Time.realtimeSinceStartup));
            hasLoadedAnyReward = true;
        }
        
        rewardIsReady[rewardRefId] = true;
        rewardIsLoading[rewardRefId] = false;
        rewardFloorGroupAttemptedRetries[rewardRefId] = 0;
        rewardWantedFloorId = 0;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardAdReady?.Invoke());

        if (rewardWantedVisible[rewardRefId])
            ShowRewardAd(rewardRefId);
    }

    private void RewardAdVisible(object sender, EventArgs args, int rewardRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad visible");

        rewardIsVisible[rewardRefId] = true;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardAdShown?.Invoke());
    }

    private void RewardAdRewarded(object sender, Reward reward, int rewardRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Reward ad rewarded");

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardAdRewarded?.Invoke(reward));
    }

    private void RewardAdFailedToShow(object sender, AdErrorEventArgs loadFailArgs, int rewardRefId) {
        if (!isAdMobInitialized)
            return;
        
        AdError adError = loadFailArgs.AdError;

        if (debugLogging) {
            Debug.LogError("AdMob Debug - Failed to show reward ad! " + adError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + adError.ToString());
        }

        rewardIsReady[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardAdFailedToShow?.Invoke(loadFailArgs));

        FirebaseAnalyticsManager.LogEvent("reward_ad_show_failed", "error", adError.GetCode());

        UnityMainThreadDispatcher.instance.Enqueue(() => AttemptRewardAdRetry(rewardRefId));
    }

    private void RewardAdFailedToLoad(object sender, AdFailedToLoadEventArgs loadFailArgs, int rewardRefId) {
        if (!isAdMobInitialized)
            return;
        
        LoadAdError loadAdError = loadFailArgs.LoadAdError;

        if (debugLogging && loadAdError != null) {
            Debug.LogError("AdMob Debug - Failed to load reward ad! " + loadAdError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + loadAdError.ToString());

            ResponseInfo responseInfo = loadAdError.GetResponseInfo();
            
            if(responseInfo != null)
                Debug.LogError("AdMob Debug - Mediation Response: " + responseInfo.ToString());
        }

        rewardIsReady[rewardRefId] = false;
        rewardIsLoading[rewardRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnRewardAdFailedToLoad?.Invoke(loadFailArgs));

        FirebaseAnalyticsManager.LogEvent("reward_ad_load_failed", "error", loadAdError.GetCode());

        UnityMainThreadDispatcher.instance.Enqueue(() => AttemptRewardAdRetry(rewardRefId));
    }

    // Application.internetReachability must be called from the main thread
    private void AttemptRewardAdRetry(int rewardRefId) {
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && rewardFloorGroupAttemptedRetries[rewardRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryRewardAdLoad(rewardRefId));
    }

    // Banner Ad Callbacks
    private void BannerAdClosed(object sender, EventArgs args, int bannerRefId) {
        if (!isAdMobInitialized)
            return;
        
        bannerIsVisible[bannerRefId] = false;

        if (bannerRefId == activeBannerRefId) {
            activeBannerIsVisible = false;
        }

        UnityMainThreadDispatcher.instance.Enqueue(() => OnBannerAdClosed?.Invoke());

        if (bannerWantedVisible[bannerRefId])
            ShowBannerAd(bannerRefId);
    }

    private void BannerAdLoaded(object sender, EventArgs args, int bannerRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Banner ad loaded");

        if (!hasLoadedAnyBanner) {
            UnityMainThreadDispatcher.instance.Enqueue(() => FirebaseAnalyticsManager.LogEvent("admob_ttl", "first_ban_load", Time.realtimeSinceStartup));
            hasLoadedAnyBanner = true;
        }
        
        bannerIsReady[bannerRefId] = true;
        bannerIsLoading[bannerRefId] = false;
        bannerFloorGroupAttemptedRetries[bannerRefId] = 0;
        bannerWantedFloorId = 0;

        if (bannerRefId == activeBannerRefId) {
            activeBannerIsReady = true;
            activeBannerIsLoading = false;
        }
        
        UnityMainThreadDispatcher.instance.Enqueue(() => OnBannerAdReady?.Invoke());

        if (bannerWantedVisible[bannerRefId]) {
            ShowBannerAd(bannerRefId);
        } else {
            HideBannerAd(bannerRefId, false);
        }
    }

    private void BannerAdVisible(object sender, EventArgs args, int bannerRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Banner ad visible");

        bannerIsVisible[bannerRefId] = true;

        if (bannerRefId == activeBannerRefId) {
            activeBannerIsVisible = true;
        }

        UnityMainThreadDispatcher.instance.Enqueue(() => OnBannerAdShown?.Invoke());

        if (!bannerWantedVisible[bannerRefId])
            HideBannerAd(bannerRefId, false);
    }

    private void BannerAdFailedToLoad(object sender, AdFailedToLoadEventArgs loadFailArgs, int bannerRefId) {
        // Skip the banner failed callback in the editor as we don't want to be spammed with messages if we've deleted the banner ad prefabs
        if (!isAdMobInitialized || Application.isEditor)
            return;
        
        LoadAdError loadAdError = loadFailArgs.LoadAdError;

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

        bannerIsReady[bannerRefId] = false;
        bannerIsLoading[bannerRefId] = false;

        if (bannerRefId == activeBannerRefId) {
            activeBannerIsReady = false;
            activeBannerIsLoading = false;
        }

        UnityMainThreadDispatcher.instance.Enqueue(() => OnBannerAdFailedToLoad?.Invoke(loadFailArgs));

        FirebaseAnalyticsManager.LogEvent("banner_ad_load_failed", "error", loadAdError.GetCode());

        UnityMainThreadDispatcher.instance.Enqueue(() => AttemptBannerAdRetry(bannerRefId));
    }

    // Application.internetReachability must be called from the main thread
    private void AttemptBannerAdRetry(int bannerRefId) {
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && bannerFloorGroupAttemptedRetries[bannerRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryBannerAdLoad(bannerRefId));
    }

    // Interstitial Ad Callbacks
    private void InterstitialAdClosed(object sender, EventArgs args, int interstitialRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Interstitial ad closed");

        intIsReady[interstitialRefId] = false;
        intIsVisible[interstitialRefId]  = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnInterstitialAdClosed?.Invoke());

        // Automatically reload the next interstitial
        LoadInterstitialAd(false, true, interstitialRefId);
    }

    private void InterstitialAdLoaded(object sender, EventArgs args, int interstitialRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Interstitial ad loaded");

        if (!hasLoadedAnyInt) {
            UnityMainThreadDispatcher.instance.Enqueue(() => FirebaseAnalyticsManager.LogEvent("admob_ttl", "first_int_load", Time.realtimeSinceStartup));
            hasLoadedAnyInt = true;
        }

        intIsReady[interstitialRefId] = true;
        intIsLoading[interstitialRefId] = false;
        intFloorGroupAttemptedRetries[interstitialRefId] = 0;
        intWantedFloorId = 0;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnInterstitialAdReady?.Invoke());

        if (intWantedVisible[interstitialRefId])
            ShowInterstitialAd(true, interstitialRefId);
    }

    private void InterstitialAdVisible(object sender, EventArgs args, int interstitialRefId) {
        if (!isAdMobInitialized)
            return;
        
        if (debugLogging)
            Debug.Log("AdMob Debug - Interstitial ad visible");

        intIsVisible[interstitialRefId] = true;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnInterstitialAdShown?.Invoke());
    }

    private void InterstitialAdFailedToLoad(object sender, AdFailedToLoadEventArgs loadFailArgs,  int interstitialRefId) {
        if (!isAdMobInitialized)
            return;
        
        LoadAdError loadAdError = loadFailArgs.LoadAdError;

        if (debugLogging && loadAdError != null) {
            Debug.LogError("AdMob Debug - Failed to load interstitial ad! " + loadAdError.GetMessage());
            Debug.LogError("AdMob Debug - Error: " + loadAdError);
        }

        intIsReady[interstitialRefId] = false;
        intIsLoading[interstitialRefId] = false;

        UnityMainThreadDispatcher.instance.Enqueue(() => OnInterstitialAdFailedToLoad?.Invoke(loadFailArgs));

        FirebaseAnalyticsManager.LogEvent("interstitial_ad_load_failed", "error", loadAdError.GetCode());

        UnityMainThreadDispatcher.instance.Enqueue(() => AttemptInterstitialAdRetry(interstitialRefId));
    }

    // Application.internetReachability must be called from the main thread
    private void AttemptInterstitialAdRetry(int interstitialRefId) {
        // If an internet connection is available and we haven't attempted to retry loading the ads too many times schedule for the ad to be reloaded
        if (Application.internetReachability != NetworkReachability.NotReachable && intFloorGroupAttemptedRetries[interstitialRefId] < maxAutomaticAdLoadRetries)
            StartCoroutine(RetryInterstitialAdLoad(interstitialRefId));
    }
}