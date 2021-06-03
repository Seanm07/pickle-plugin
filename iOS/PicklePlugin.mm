#include "PicklePlugin.h"

// Shared 
#import <AdSupport/AdSupport.h>

#ifdef __IPHONE_14_0
	// iOS 14 app tracking transparency
	#import <AppTrackingTransparency/ATTrackingManager.h>
	#import <StoreKit/SKAdNetwork.h>
#endif

#define TRACKING_NOT_DETERMINED 0
#define TRACKING_RESTRICTED 1
#define TRACKING_DENIED 2
#define TRACKING_AUTHED 3

// "+" methods are pretty much static methods (not really but basically)
// "-" methods are the opposite

// This can probably be removed, just not sure if the implementation is required even if it's blank
@implementation PicklePlugin {
}

extern UIViewController* UnityGetGLViewController();

+(PicklePlugin*) shareMessage:(char*)message shareSubject:(char*)subject shareDoShareIcon:(bool*)doShareIcon{
    return [[PicklePlugin alloc] initShareMessage:message initShareSubject:subject initDoShareIcon:doShareIcon];
}

-(PicklePlugin*) initShareMessage:(char*)message initShareSubject:(char*)subject initDoShareIcon:(bool*)doShareIcon{
    // Set self to the reference of the original caller
    self = [super init];
    
    // Create a new array named items
    NSMutableArray *items = [NSMutableArray new];
    
    if(strlen(message) > 0){
        // Add the message to the item array
        [items addObject:[NSString stringWithUTF8String:message]];
    }
    
	if(doShareIcon){
		UIImage *appIcon = [UIImage imageNamed: [[[[NSBundle mainBundle] infoDictionary] objectForKey:@"CFBundleIconFiles"] objectAtIndex:0]];
		
		if(appIcon != nil)
			[items addObject:appIcon];
	}
	
    /*if(strlen(imagePath) > 0){
        // Convert the imagePath into a UTF8 safe path
        NSString *filePath = [NSString stringWithUTF8String:imagePath];
        
        // Try get the contents of the file and store it as a UIImage type
        UIImage *image = [UIImage imageWithContentsOfFile:filePath];
        
        if(image != nil){
            // Attach the image
            [items addObject:image];
        } else {
            // The image path couldn't be resolved to a UIImage, just add the raw file path to the items instead
            [items addObject:[NSURL fileURLWithPath:filePath]];
        }
    }*/
    
    // Initialize a new activity https://developer.apple.com/documentation/uikit/uiactivityviewcontroller/1622019-initwithactivityitems
    UIActivityViewController *activity = [[UIActivityViewController alloc] initWithActivityItems:items applicationActivities:nil];
    
    if(strlen(subject) > 0){
        // Set the value of a key name "subject" to the subject parameter we sent on the activity we created
        [activity setValue:[NSString stringWithUTF8String:subject] forKey:@"subject"];
    }
    
	UIViewController *rootViewController = [UIApplication sharedApplication].keyWindow.rootViewController;
	
	// While the rootViewController has a parent set it to be the parent until it eventually no longer has a parent
	// https://pinkstone.co.uk/how-to-avoid-whose-view-is-not-in-the-window-hierarchy-error-when-presenting-a-uiviewcontroller/
	while(rootViewController.presentedViewController)
		rootViewController = rootViewController.presentedViewController;    
    
	
    // Present the view controller using the popover style
    activity.modalPresentationStyle = UIModalPresentationPopover;
    [rootViewController presentViewController:activity animated:YES completion:nil];
    
    // Get the popover presentation controller and configure it
    UIPopoverPresentationController *presentationController = [activity popoverPresentationController];
    presentationController.permittedArrowDirections = 0;
    presentationController.sourceView = rootViewController.view;
    presentationController.sourceRect = CGRectMake(CGRectGetMidX(rootViewController.view.bounds), CGRectGetMidY(rootViewController.view.bounds), 0, 0);;
	
    
    return self;
}

# pragma mark - C API

void ShowShare(char* message, char* subject, bool* doShareIcon){
    [PicklePlugin shareMessage:message shareSubject:subject shareDoShareIcon:doShareIcon];
}

int GetTrackingAuthStatus(){
	// Are the iOS 14 APIs available?
    if (@available(iOS 14, *)) {
        // Don't include any code we don't need though.
		#ifdef __IPHONE_14_0
			return (int)ATTrackingManager.trackingAuthorizationStatus;
		#endif
    } else {
        bool trackingEnabled = ASIdentifierManager.sharedManager.isAdvertisingTrackingEnabled;
        return trackingEnabled ? TRACKING_AUTHED : TRACKING_DENIED;
    }
}

void RequestTrackingAuth(AppTrackingTransparencyCallback callback){
	if (@available(iOS 14, *)) {
		#ifdef __IPHONE_14_0
			[ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
				// 0 - Not determined
				// 1 - Restricted
				// 2 - Denied
				// 3 - Authorized
				callback((unsigned int) status);
			}];
		#endif
	} else {
		bool trackingEnabled = ASIdentifierManager.sharedManager.isAdvertisingTrackingEnabled;
		NSLog(@"RequestTrackingAuth called but iOS 14 API not available, returning %d.\n", trackingEnabled);
		callback(trackingEnabled ? TRACKING_AUTHED : TRACKING_DENIED);
	}
}

char* GetSettingsURL(){
	// Grab the string to open the settings app
	NSURL * url = [NSURL URLWithString: UIApplicationOpenSettingsURLString];

	// convert to a c string & donesies
	return ConvertNSStringToCString(url.absoluteString);
}

// Convert an NSString to a regular null terminated array of chars
char* ConvertNSStringToCString( const NSString* nsString ){
	if (nsString == NULL) return NULL;

	const char* nsStringUtf8 = nsString.UTF8String;
	char* cString = (char*)malloc( strlen(nsStringUtf8) + 1);
	// strcpy includes the \0 so no need to memset
	strcpy( cString, nsStringUtf8 );

	return cString;
}

@end