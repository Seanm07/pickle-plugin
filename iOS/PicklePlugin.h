#import "UnityAppController.h"

@interface PicklePopupDelegate : UIViewController <UIPopoverPresentationControllerDelegate>
@end

@interface PicklePlugin : UIViewController <UIPopoverPresentationControllerDelegate>

    @property (nonatomic, strong) PicklePopupDelegate *popoverDelegate;
    @property (nonatomic, strong) PicklePlugin *sharedInstance;

    #ifdef __cplusplus
    // If c++ use "C linkage" to avoid name mangling issues
    extern "C" {
    #endif
        
        typedef void (*AppTrackingTransparencyCallback)(unsigned int result);
        
        // Display a share prompt in a centered popover window
        void ShowShare(char* message, char* subject, bool* doShareIcon);
        
        // Returns an integer for the tracking auth status, 0 = UNKNOWN, 1 = RESTRICTED, 2 = DENIED, 3 = AUTHORISED
        int GetTrackingAuthStatus();
        
        // Send a request to show the app tracking transparency prompt
        void RequestTrackingAuth(AppTrackingTransparencyCallback callback);
        
        // Returns raw IDFA (will be all 0s if ATT wasn't accepted) (unique id across all apps)
        char* GetIOSAdvertisingIdentifier();
        
        // Returns the URL to open the settings app
        char* GetSettingsURL();
        
        // Returns the identifierForVendor (IDFV) UUID as a string (unique id across apps by same developer)
        char* GetIdentifierForVendor();
        
        // Haptic feedback
        void TriggerHapticFeedback(char* style, double duration);
        
        // Show a notification message "toast" for duration seconds
        void ShowToast(char* message, double duration);
        
        // Force hides any active toasts
        void HideToast();
        
    #ifdef __cplusplus
    }
    #endif

@end
