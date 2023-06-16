using UnityEngine;

public class IAS_Handler : MonoBehaviour {

	public IASAdSize adSize = IASAdSize.Square;
	public int adOffset = 0; // 0 for main ads / 1, 2, 3 for backscreen ads

	private int activeId; // Reference to the unique id for this ad on the IAS server (used for analytics)
	private int activeAdTextureId = -1; // Reference id for the advertTextures list
	private string activeUrl; // URL which will be opened when clicked

	private bool isTextureAssigned = false;
	private bool isActiveTextureAnimated = false;

	private int activeFrameIndex;
	private float timeSinceLastFrame;
	private float timeUntilNextFrame;
	
	private UITexture selfTexture;

	void Awake() {
		selfTexture = GetComponent<UITexture>();

		if (selfTexture) {
			storedAspectRatio = selfTexture.keepAspectRatio;

			storedRightAnchorTransform = selfTexture.rightAnchor.target;
			storedRightAnchorAbsolute = selfTexture.rightAnchor.absolute;
			storedRightAnchorRelative = selfTexture.rightAnchor.relative;

			storedLeftAnchorAbsolute = selfTexture.leftAnchor.absolute;
			storedLeftAnchorRelative = selfTexture.leftAnchor.relative;
		} else {
			Debug.LogError("UITexture component missing on IAS handler object!", gameObject);
		}
	}

	void OnEnable() {
		IAS_Manager.OnIASImageDownloaded += OnIASReady;
		IAS_Manager.OnForceChangeWanted += OnIASForced;
		IAS_Manager.OnIASForceReset += OnIASForceReset;
		IAS_Manager.OnAnimatedTexturesReady += OnIASAnimationReady;

		SetupAdvert();
	}

	void OnDisable() {
		IAS_Manager.OnIASImageDownloaded -= OnIASReady;
		IAS_Manager.OnForceChangeWanted -= OnIASForced;
		IAS_Manager.OnIASForceReset -= OnIASForceReset;
		IAS_Manager.OnAnimatedTexturesReady -= OnIASAnimationReady;

		isTextureAssigned = false; // Allows the texture on this IAS ad to be replaced
	}
	
	void Update() {
		if (isActiveTextureAnimated) {
			timeSinceLastFrame += Time.unscaledDeltaTime;
            
			if (timeSinceLastFrame >= timeUntilNextFrame) {
				IAS_Manager.IASTextureData adTextureData = IAS_Manager.instance.advertTextures[activeAdTextureId];
				IAS_Manager.IASTextureData.IASAnimatedFrameData animationFrame = adTextureData.animatedTextureFrames[activeFrameIndex];

				timeUntilNextFrame = animationFrame.timeUntilFrameChange;
				timeSinceLastFrame = 0f;
				activeFrameIndex = activeFrameIndex + 1 >= adTextureData.animationFrames ? 0 : activeFrameIndex + 1;

				selfTexture.mainTexture = animationFrame.texture;
			}
		}
        
#if UNITY_EDITOR
		if (Input.GetKeyDown(KeyCode.R)) {
			isTextureAssigned = false;

			IAS_Manager.RefreshBanners(adSize);
		}
#endif
	}

	private void OnIASAnimationReady(int adTextureId) {
		if (activeAdTextureId == adTextureId) {
			isActiveTextureAnimated = true;
			activeFrameIndex = 0;
			timeSinceLastFrame = 0f;

			if(IAS_Manager.instance.advertTextures.Count >= activeAdTextureId && IAS_Manager.instance.advertTextures[activeAdTextureId].animatedTextureFrames.Count > 0){
				IAS_Manager.IASTextureData.IASAnimatedFrameData animatedFrameData = IAS_Manager.instance.advertTextures[activeAdTextureId].animatedTextureFrames[0];
            
				selfTexture.mainTexture = animatedFrameData.texture;
			}
		}
	}
	
	private void OnIASReady(IASAdSize loadedAdSize) {
		if(adSize == loadedAdSize)
			SetupAdvert();
	}

	private void OnIASForced(IASAdSize loadedAdSize) {
		if (adSize == loadedAdSize) {
			isTextureAssigned = false;
			isActiveTextureAnimated = false;
			activeAdTextureId = -1;

			SetupAdvert();
		}
	}
	
	private void OnIASForceReset() {
		isTextureAssigned = false;
		isActiveTextureAnimated = false;
		activeAdTextureId = -1;
        
		selfTexture.mainTexture = null;
	}

	// Cop Duty only
	private UIWidget.AspectRatioSource storedAspectRatio;
	private Transform storedRightAnchorTransform;
	private float storedRightAnchorRelative;
	private float storedRightAnchorAbsolute;

	private float storedLeftAnchorRelative;
	private float storedLeftAnchorAbsolute;

	private bool adWasDisabled = false;
	
	private void SetupAdvert()
	{
		// If the app is actually the paid version, disable all ads including backscreen ads
		// If this is the free version and the player purchased the IAP to disable ads then we still give backscreen ads
		if (GameManager.Instance.isAdsDisabled && adOffset == 0) {
			selfTexture.mainTexture = null;
			activeUrl = "";
			
			// Resize the advert container so anything relying on the ad width is able to take over the ad space
			selfTexture.keepAspectRatio = UIWidget.AspectRatioSource.Free;
			selfTexture.leftAnchor.Set(0f, 0f);
			selfTexture.rightAnchor.Set(selfTexture.cachedTransform, 0f, 0f);
			selfTexture.ResetAndUpdateAnchors();
			
			adWasDisabled = true;
			return;
		} else if(adWasDisabled) {
			// Ads came back :O
			selfTexture.keepAspectRatio = storedAspectRatio;
			selfTexture.leftAnchor.Set(storedLeftAnchorRelative, storedLeftAnchorAbsolute);
			selfTexture.rightAnchor.Set(storedRightAnchorTransform, storedRightAnchorRelative, storedRightAnchorAbsolute);
			selfTexture.ResetAndUpdateAnchors();

			adWasDisabled = false;
		}
		
		if (!isTextureAssigned && IAS_Manager.IsAdReady(adSize, adOffset)) {
			// This will only get marked as true when the animation frames have finished loading
			isActiveTextureAnimated = false;

			Texture2D adTexture = IAS_Manager.GetAdTexture(adSize, adOffset);

			if (adTexture != null) {
				// Show the static IAS image, if it's animated the animated frames with asynchronously load
				selfTexture.mainTexture = adTexture;
				isTextureAssigned = true;

				IAS_Manager.AdData adData = IAS_Manager.instance.GetAdData(adSize, adOffset);
                
				activeId = IAS_Manager.GetAdId(adSize, adOffset, adData);
				activeAdTextureId = IAS_Manager.GetAdTextureId(adSize, adOffset, adData);
				activeUrl = IAS_Manager.GetAdURL(adSize, adOffset, adData);

				IAS_Manager.instance.OnImpression(activeId); // DO NOT REMOVE THIS LINE

				bool isAnimatedTexture = IAS_Manager.IsAnimatedAdTexture(adSize, adOffset, adData);

				if (isAnimatedTexture) {
					IAS_Manager.instance.LoadAnimatedTextures(activeAdTextureId, adSize, adOffset);
				}
			}
		}
	}

	void OnClick()
	{
		DoClick();
	}

	public void OnControllerSelect()
	{
		DoClick();
	}

	void DoClick() {
		if (adWasDisabled) return;
		
		if (selfTexture != null && !string.IsNullOrEmpty(activeUrl)) {
			IAS_Manager.instance.OnClick(activeId); // DO NOT REMOVE THIS LINE

			IAS_Manager.instance.OpenURL(activeUrl);
		}
	}

}
