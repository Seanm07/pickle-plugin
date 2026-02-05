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

		if (!selfTexture) 
			Debug.LogError("UITexture component missing on IAS handler object!", gameObject);
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
	
	private void SetupAdvert()
	{
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
		if (selfTexture != null && !string.IsNullOrEmpty(activeUrl)) {
			IAS_Manager.instance.OnClick(activeId); // DO NOT REMOVE THIS LINE

			IAS_Manager.instance.OpenURL(activeUrl);
		}
	}

}
