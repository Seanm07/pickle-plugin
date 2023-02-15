using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

public class ClickLockManager : MonoBehaviour
{
    public class ActiveClickLockStorage {
        public string centerTextString;
        public bool showSkipAfterTime;
        public bool silentSkip;
        
        public float visibleTime;

        public ActiveClickLockStorage(string text, bool allowShowSkipAfterTime, bool doSilentSkip) {
            centerTextString = text;
            showSkipAfterTime = allowShowSkipAfterTime;
            silentSkip = doSilentSkip;
        }
    }
    
    public static ClickLockManager Instance;

    private List<ActiveClickLockStorage> activeClickLocks = new List<ActiveClickLockStorage>();
    public int activeClickLockCount { get; set; }
    
    public GameObject container;
    public UILabel centerText;
    public GameObject skipOverlay;
    public UISprite background;
    
    private bool isVisible = false;

    private bool appInFocus = true;
    private bool appIsPaused = false;

    private bool wasAdMobBannerVisible;
    private int adMobBannerHideDepth = 0;
    
    private void Awake()
    {
        Instance = Instance ?? this;
    }

	public void ShowIABClickLock() {
        if (CrossPlatformManager.GetActiveStore() == AppStore.UDP) {
            ShowClickLock("Hold on!", true);
        } else {
            ShowClickLock("Hold on!", false);
        }
    }

    public void ShowClickLock(string message, bool showSkipAfterTime = true, bool closeAllPrevious = false, bool instantlyShowSkip = false, bool silentSkip = false, bool opaqueBackground = false) {
        if (closeAllPrevious) {
            if(wasAdMobBannerVisible && adMobBannerHideDepth <= 0)
                for(int i=0;i < adMobBannerHideDepth;i++)
                    AdMob_Manager.instance.ShowBannerAd();
            
            adMobBannerHideDepth = 0;
            
            activeClickLocks.Clear();
            activeClickLockCount = 0;
        }

        activeClickLocks.Add(new ActiveClickLockStorage(message, showSkipAfterTime, silentSkip));
        activeClickLockCount++;
        
        centerText.text = message;
        background.alpha = opaqueBackground ? 1f : 0.8f;

        skipOverlay.SetActive(instantlyShowSkip);
        container.SetActive(true);
        
        isVisible = true;

        if (AdMob_Manager.instance != null && AdMob_Manager.instance.activeBannerWantedVisible) {
            wasAdMobBannerVisible = true;
            adMobBannerHideDepth++;
            
            AdMob_Manager.instance.HideBannerAd(true);
        } else {
			wasAdMobBannerVisible = false;
		}
    }
    
    // Removes the topmost click lock screen
    public void HideClickLock() {
        if (activeClickLockCount > 0) {
            activeClickLocks.RemoveAt(activeClickLockCount - 1);
            activeClickLockCount--;
        }
        
        // Check again now that we've removed the topmost screen
        if (activeClickLockCount <= 0) {
            NGUITools.SetActive(skipOverlay, false);
            NGUITools.SetActive(container, false);

            isVisible = false;
        } else {
            // Hide the skip overlay if the previous screen isn't ready to allow skipping yet
            ActiveClickLockStorage newActiveClickLock = activeClickLocks[activeClickLockCount - 1];
            
            NGUITools.SetActive(skipOverlay, newActiveClickLock.showSkipAfterTime && newActiveClickLock.visibleTime >= 20f);
        }
        
        adMobBannerHideDepth = adMobBannerHideDepth - 1 <= 0 ? 0 : adMobBannerHideDepth - 1;
            
        if(adMobBannerHideDepth <= 0 && wasAdMobBannerVisible)
            AdMob_Manager.instance.ShowBannerAd();
    }

    public void SkipClickLock() {
        if (activeClickLockCount > 0) {
            ActiveClickLockStorage activeClickLock = activeClickLocks[activeClickLockCount - 1];
            
            if(!activeClickLock.silentSkip)
                MessagePopupManager.Instance.ShowMessage("Something went wrong!", "We were unable to complete your request!\n\n[FFFF00][sup]Try restarting the app or try again later.[/sup][-]");
            
            HideClickLock();
        }
    }

    // Resets all active click lock timers
    public void ResetTimer() {
        foreach (ActiveClickLockStorage activeClickLock in activeClickLocks)
            activeClickLock.visibleTime = 0f;

        // Force hide the skip button if it's visible
        NGUITools.SetActive(skipOverlay, false);
    }

    public void Update() {
        if (isVisible && appInFocus && !appIsPaused) {
            foreach (ActiveClickLockStorage activeClickLock in activeClickLocks) {
                if (activeClickLock.showSkipAfterTime) {
                    activeClickLock.visibleTime += Time.unscaledDeltaTime;

                    if (activeClickLock.visibleTime >= 20f)
                        NGUITools.SetActive(skipOverlay, true);
                }
            }
        }
    }

    private void OnApplicationFocus(bool inFocus) {
        // Pause the skip button counter while the app is in the background
        // This is to fix the skip button appearing while the IAB purchase dialog is open
        // but still allows the skip button to appear if something went wrong causing the purchase dialog to fail to open
        appInFocus = inFocus;

        // Reset the timer for any application leaving/entering events as it shows something is atleast happening
        ResetTimer();
    }

    private void OnApplicationPause(bool isPaused) {
        appIsPaused = isPaused;

        // Reset the timer for any application pausing/unpausing events as it shows something is atleast happening
        ResetTimer();
    }
}
