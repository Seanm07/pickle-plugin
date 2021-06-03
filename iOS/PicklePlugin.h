#import "UnityAppController.h"

@interface PicklePlugin : UIViewController

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
	
#ifdef __cplusplus
}
#endif

@end