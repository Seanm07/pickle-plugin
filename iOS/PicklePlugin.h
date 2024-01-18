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
        
        // Returns the URL to open the settings app
        char* GetSettingsURL();
        
        // Haptic feedback
        void TriggerHapticFeedback(char* style, double duration);
        
    #ifdef __cplusplus
    }
    #endif

@end
