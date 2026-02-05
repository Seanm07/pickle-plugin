// Game Pickle Personalisation script for handling AdMob CMP prompts and iOS ATT requests
using System;
using UnityEngine;
using System.Collections;
using GoogleMobileAds.Ump.Api;

public class PersonalisationManager : MonoBehaviour {
	public static PersonalisationManager instance;

	// Whether the inapp screen for personalisation shows before the prompt messages
	public bool showInAppPersonalisationAndroid = true;
	public bool showInAppPersonalisationiOS = true;

	// AdMob recommends having a button to adjust privacy settings when ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required
	// this may become future policy enforced so probably better to implement it now rather than needing to update to add it later
	[Header("Gameobject to manage the privacy settings in options menu")]
	public GameObject privacyOptions;

	/// Callback once user has finished all required auth prompts (CSM and ATT) also called if there are none required to show right now (already accepted etc)
	public static Action OnAuthRequestsComplete;

	// Callbacks when we want an inapp personalisation screen to show/hide
	public static Action OnRequestPersonalisationScreenShow;
	public static Action OnRequestPersonalisationScreenHide;

	// We make a consent request asap and preload the consent form so it's ready for first use
	private ConsentForm preloadedConsentForm;
	private bool isConsentPreloaded;
	
	public bool isActiveAuthFlowPending { get; private set; }
	public bool didAuthRequestTimeout { get; private set; }
	public bool isMonitoringTimeout { get; private set; }
	
	void Awake() {
		instance = instance ?? this;

		// Disable privacy options by default, it'll get enabled once consent information is updated if required
		if (privacyOptions != null)
			privacyOptions.SetActive(false);
	}

	void Start() {
		if (!PickleCore.IsAndroidTV()) {
			// Send a consent request asap to start pre-loading the consent form
			RequestConsent(PreloadFormOnConsentUpdated, true, false);
		}
	}
	
	#if UNITY_IOS || UNITY_STANDALONE_OSX
		public enum TrackingAuthStatus {
			NOT_DETERMINED = 0, // ATT prompt not yet shown
			RESTRICTED = 1, // iOS 14+/macOS 11+ ATT restricted via system settings
			DENIED = 2, // iOS 14+/macOS 11+ user denied ATT tracking
			AUTHORIZED = 3 // Below iOS 14/macOS 11 always true, iOS 14+/macOS 11+ user accepted ATT tracking
		};
	
		[System.Runtime.InteropServices.DllImport("__Internal")]
		private static extern int GetTrackingAuthStatus();

		[System.Runtime.InteropServices.DllImport("__Internal")]
		private static extern int RequestTrackingAuth(AuthCalbackDelegate callback);

		[System.Runtime.InteropServices.DllImport("__Internal")]
		private static extern string GetSettingsURL();
	
		[AOT.MonoPInvokeCallback(typeof(AuthCalbackDelegate))]
		private static void ConvertPInvokeCallbackToMonoAction(UInt32 result){
			if(result > (int)TrackingAuthStatus.AUTHORIZED){
				Debug.LogError("Unexpected iOS tracking auth status: " + result);

				result = (int)TrackingAuthStatus.NOT_DETERMINED;
			}

			UnityMainThreadDispatcher.instance.Enqueue(() => instance.AuthRequestsComplete());
		}

		private delegate void AuthCalbackDelegate(UInt32 result);
	
		public void RequestATT() {
			if (Application.platform == RuntimePlatform.IPhonePlayer || Application.platform == RuntimePlatform.OSXPlayer){					  
				RequestTrackingAuth(ConvertPInvokeCallbackToMonoAction);
				return;
			}

			// Unknown platform, just continue with a NOT_DETERMINED auth status
			AuthRequestsComplete();
		}
	#endif

	// Returns true if ATT or AdMob need to show an auth prompt
	private bool NeedToShowAuthPrompt() {
		// Get the AdMob consent status
		ConsentStatus consentStatus = ConsentInformation.ConsentStatus;
		
		#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
			// Get the iOS App Tracking Transparency auth status
			TrackingAuthStatus attAuthStatus = (TrackingAuthStatus)GetTrackingAuthStatus();

			// If ATT or AdMob is pending an auth prompt return true
			return (attAuthStatus == TrackingAuthStatus.NOT_DETERMINED || consentStatus == ConsentStatus.Required);
		#else
			return consentStatus == ConsentStatus.Required;
		#endif
	}
	
	private void RequestConsent(Action<FormError> callback, bool forceRequest = false, bool monitorForManualTimeout = false) {
		if (!isConsentPreloaded || forceRequest) {
			// If we're not actively waiting on the consent request then we don't need to monitor it for a timeout
			if (monitorForManualTimeout) {
				isMonitoringTimeout = true;
				StartCoroutine(DoConsentTimeoutMonitor(callback));
			}

			bool useDebugGeography = Application.isEditor || AdMob_Manager.instance.enableTestMode;
			
			DebugGeography debugMode = useDebugGeography ? DebugGeography.EEA : DebugGeography.Disabled;
			ConsentDebugSettings consentDebugSettings = new ConsentDebugSettings {
				DebugGeography = debugMode
			};
			
			if (useDebugGeography) {
				// All simulators are marked as test devices
				consentDebugSettings.TestDeviceHashedIds.Add(GoogleMobileAds.Api.AdRequest.TestDeviceSimulator);
					
				// Add the current device as a tester
				consentDebugSettings.TestDeviceHashedIds.Add(AdMob_Manager.instance.GetTestDeviceId());
			}
			
			ConsentRequestParameters requestParameters = new ConsentRequestParameters {
				TagForUnderAgeOfConsent = AdMob_Manager.instance.IsTaggedForChildDirectedTreatment(), 
				ConsentDebugSettings = consentDebugSettings
			};
			
			ConsentInformation.Update(requestParameters, callback);
		} else {
			// Consent already pre-loaded, no need to request again as forceRequest was false
			callback?.Invoke((FormError) null);
		}
	}
	
	private IEnumerator DoConsentTimeoutMonitor(Action<FormError> callback) {
		// If the CMP prompt is not ready within 5 seconds trigger the callback with a timeout error
		// Fixes an issue where a network with a either VERY slow or no connection gets stuck for a long time/forever
		float timeWaited = 0f;

		while (isActiveAuthFlowPending && isMonitoringTimeout && timeWaited <= 5f) {
			timeWaited += Time.unscaledDeltaTime;
			yield return null;
		}

		// If the auth flow is still pending, then force call the callback as we've timed out
		if (isActiveAuthFlowPending && isMonitoringTimeout) {
			didAuthRequestTimeout = true;
			callback?.Invoke((FormError) null);
		}
	}

	private void UpdatePrivacyButton() {
		// Show the privacy options button if it's required for this user
		if (privacyOptions != null)
			privacyOptions.SetActive(ConsentInformation.CanRequestAds() && ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required);
	}

	private void PreloadFormOnConsentUpdated(FormError error) {
		if (error != null) {
			Debug.LogError("Preload consent form error: " + error.ErrorCode + " - " + error.Message);
			ConsentDialogEnd();
			return;
		}
		
		// Start pre-loading the consent form
		ConsentForm.Load((consentForm, formError) => {
			UnityMainThreadDispatcher.instance.Enqueue(() => {
				if (formError != null) {
					isConsentPreloaded = false;
					Debug.LogError("Consent form error: " + formError.ErrorCode + " - " + formError.Message);
				} else {
					isConsentPreloaded = true;
					preloadedConsentForm = consentForm;
				}
			});
		});
	}
	
	private void ShowFormOnConsentUpdated(FormError error) {
		if (error != null) {
			Debug.LogError("Show consent form error: " + error.ErrorCode + " - " + error.Message);
			ConsentDialogEnd();
			return;
		}

		// Show the admob consent dialog
		ConsentForm.LoadAndShowConsentFormIfRequired((formError) => {
			UnityMainThreadDispatcher.instance.Enqueue(() => {
				if (formError != null)
					Debug.LogError("Consent form error: " + formError.ErrorCode + " - " + formError.Message);

				// Call the dialog end function regardless of form show state
				ConsentDialogEnd();
			});
		});
	}

	private void ConsentDialogEnd() {
		UpdatePrivacyButton();
		
		#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
			// Get the iOS App Tracking Transparency auth status
			TrackingAuthStatus attAuthStatus = (TrackingAuthStatus)GetTrackingAuthStatus();

			if(attAuthStatus == TrackingAuthStatus.NOT_DETERMINED){
				FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "ios_att_required");

				// The iOS App Tracking Transparency prompt is still pending auth, show the dialog now
				RequestATT();
			} else {
				// No more prompts to show, trigger the end callback
				AuthRequestsComplete();
			}
		#else
			// No more prompts to show, trigger the end callback
			AuthRequestsComplete();
		#endif
	}

	private void AuthRequestsComplete() {
		// Initialise admob now regardless of the consent state, better to have unpersonalised ads than no ads
		if (!AdMob_Manager.instance.hasAdMobInitializeBeenCalled && !PickleCore.IsAndroidTV())
			AdMob_Manager.instance.InitializeAdMob();
		
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.HideClickLock();

		EndPersonalisationFlow();
	}
	
	private void StartAuthRequests(string clickLockString = "Loading") {
		StartCoroutine(DoStartAuthRequests(clickLockString));
	}
	
	private IEnumerator DoStartAuthRequests(string clickLockString = "Loading") {
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ShowClickLock(clickLockString, true, false, false, false, true);

		// Need to wait a frame otherwise the hold on screen won't appear before the tracking auth prompt freezes the app to show the message
		yield return null;

		// If the pre-loaded consent form is not done loading or is null, request another update and force a LoadAndShowConsentFormIfRequired
		if (!isConsentPreloaded || preloadedConsentForm == null) {
			RequestConsent(formError => UnityMainThreadDispatcher.instance.Enqueue(() => ShowFormOnConsentUpdated(formError)), true, false);
		} else {
			// Consent form already pre-loaded
			preloadedConsentForm.Show((formError) => {
				if (formError != null)
					Debug.LogError("Consent form error: " + formError.ErrorCode + " - " + formError.Message);

				// Call the dialog end function regardless of form show state
				ConsentDialogEnd();
			});
		}
	}

	private void EndPersonalisationFlow() {
		isActiveAuthFlowPending = false;

		if (OnAuthRequestsComplete != null) {
			UnityMainThreadDispatcher.instance.Enqueue(() => {
				OnAuthRequestsComplete?.Invoke();
				OnAuthRequestsComplete = null;
			});
		}
	}
	
	// Public methods //
	
	// Start the main auth request flow, example call: PersonalisationManager.instance.DoAuthFlow(ContinueToGameWhenAuthFlowDoneFunction);
	public void DoAuthFlow(Action onCompleteCallback, bool forceSkipInAppPersonalisation = false, string clickLockString = "Loading") {
		if (isActiveAuthFlowPending) {
			Debug.LogWarning("Dismissing duplicate auth flow request - existing request already pending!");
			return;
		}
		
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ShowClickLock(clickLockString, true, false, false, false, true);
		
		isActiveAuthFlowPending = true;
		didAuthRequestTimeout = false;
		isMonitoringTimeout = false;
		OnAuthRequestsComplete += onCompleteCallback;
		
		FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "request_start");
		
		// Silently request to update the consent status so we know whether the auth prompt needs to be shown
		RequestConsent(error => {
			// Trigger the callback on the main thread or things get crashy :(
			UnityMainThreadDispatcher.instance.Enqueue(() => {
				isMonitoringTimeout = false; // Force end the timeout check if it's still running
				
				if (error != null || didAuthRequestTimeout) {
					if(error != null){
						Debug.LogError("Silent consent error: " + error.ErrorCode + " - " + error.Message);
						FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "auth_error_" + error.ErrorCode);
					}

					if (didAuthRequestTimeout) {
						Debug.LogError("Auth request timeout! Force skipping prompt.");
						FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "auth_timeout");
					}

					ConsentDialogEnd();
					return;
				}

				UpdatePrivacyButton();

				// Returns true if either admob consent or iOS ATT has an auth prompt to show
				bool needToShowAuthPrompt = NeedToShowAuthPrompt();

				if (needToShowAuthPrompt) {
					RuntimePlatform activePlatform = Application.platform;
					
					#if UNITY_EDITOR
						// Application.platform returns e.g RuntimePlatform.WindowsEditor in the editor however we just want to know android or iOS so check the editor build setting instead
						activePlatform = UnityEditor.EditorUserBuildSettings.activeBuildTarget == UnityEditor.BuildTarget.iOS ? RuntimePlatform.IPhonePlayer : RuntimePlatform.Android;
					#endif
					
					if (!forceSkipInAppPersonalisation && ((activePlatform == RuntimePlatform.Android && showInAppPersonalisationAndroid) || (activePlatform == RuntimePlatform.IPhonePlayer && showInAppPersonalisationiOS))) {
						if (ClickLockManager.Instance != null)
							ClickLockManager.Instance.HideClickLock();

						FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "ingame_screen_before_auth");
						
						// Show the inapp personalisation screen, auth prompts will show after the user taps continue
						if(OnRequestPersonalisationScreenShow != null){
							OnRequestPersonalisationScreenShow.Invoke();
						} else {
							// Nothing is listening for the OnRequestPersonalisationScreenShow callback so instantly start the auth request
							StartAuthRequests(clickLockString);
						}
					} else {
						FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "show_auth_prompts");
						
						// This game isn't set to show the inapp personalisation screen or forceSkipInAppPersonalisation was true, instantly start showing auth prompts
						StartAuthRequests(clickLockString);
					}
				} else {
					FirebaseAnalyticsManager.LogEvent(PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW, "auth_not_required");
					
					Debug.Log("Auth not required, skipping auth request prompts");

					// We don't need to show any prompts, just trigger the end callback immediately
					AuthRequestsComplete();
				}
			});
		}, false, true);
	}
	
	// Debug function for clearing consent state, should not be used by normal players - call ShowPrivacyOptionsForm instead to have normal players change their consent state
	public void ResetConsent() {
		ConsentInformation.Reset();
	}

	// Call this when the continue button on your inapp personalisation screen is clicked
	public void OnContinueButtonClicked() {
		OnRequestPersonalisationScreenHide?.Invoke();

		StartAuthRequests();
	}

	// This should be called by your privacy buttons, it allows players to change their consent state after the initial CMP prompt
	public void ShowPrivacyOptionsForm(string clickLockString = "Loading") {
		if(ClickLockManager.Instance != null)
			ClickLockManager.Instance.ShowClickLock(clickLockString, true, false, false, false, true);
		
		#if UNITY_EDITOR
			// BUGFIX: Editor does not currently support the privacy options form so just call the normal form show function
			ConsentForm.Load((consentForm, error) => {
				consentForm.Show(formError => {
					UnityMainThreadDispatcher.instance.Enqueue(() => {
						if (error != null)
							Debug.LogError("Privacy form error: " + error.ErrorCode + " - " + error.Message);

						if (ClickLockManager.Instance != null)
							ClickLockManager.Instance.HideClickLock();
					});
				});
			});
		#else
			ConsentForm.ShowPrivacyOptionsForm((error) => {
				UnityMainThreadDispatcher.instance.Enqueue(() => {
					if (error != null)
						Debug.LogError("Privacy form error: " + error.ErrorCode + " - " + error.Message);

					if(ClickLockManager.Instance != null)
						ClickLockManager.Instance.HideClickLock();
				});
			});
		#endif
	}
}
