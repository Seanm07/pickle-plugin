#include "PicklePlugin.h"

#import <AdSupport/AdSupport.h>

#if defined(__IPHONE_14_0) || defined(__MAC_11_0)
    // iOS 14 app tracking transparency
    #import <AppTrackingTransparency/ATTrackingManager.h>
    #import <StoreKit/SKAdNetwork.h>
#endif

#define TRACKING_NOT_DETERMINED 0
#define TRACKING_RESTRICTED 1
#define TRACKING_DENIED 2
#define TRACKING_AUTHED 3

@implementation PicklePopupDelegate

#if defined(__IPHONE_13_0)
    // New popover dismiss handler, introduced iOS 13
    - (void)presentationControllerDidDismiss:(UIPresentationController *)presentationController {
        UnitySendMessage("ShareManager", "OnSharePromptClosedIOS", "");
    }
#else
    // Old popover dismiss handler, introduced iOS 8 and depricated iOS 13
    - (void)popoverPresentationControllerDidDismissPopover:(UIPopoverPresentationController *)popoverPresentationController {
        UnitySendMessage("ShareManager", "OnSharePromptClosedIOS", "");
    }
#endif

    // Called as soon as the popover has been requested to be displayed
    /*- (void)prepareForPopoverPresentation:(UIPopoverPresentationController *)popoverPresentationController {
        
    }*/

    // Called when the popover is fully displayed and transition has completed
    - (void)popoverPresentationCompleted {
        UnitySendMessage("ShareManager", "OnSharePromptOpenedIOS", "");
    }

    - (void)popoverPresentationDismissDueToCompletion:(bool)completed{
        UnitySendMessage("ShareManager", "OnSharePromptClosedIOS", "");
    }

@end

@implementation PicklePlugin

    extern UIViewController* UnityGetGLViewController();

    // Used to keep the reference of the share popup window alive to trigger the callback events
    static PicklePlugin *sharedInstance = nil;

    +(PicklePlugin*) sharedInstance {
        static dispatch_once_t onceToken;
        dispatch_once(&onceToken, ^{
            sharedInstance = [[self alloc] init];
        });
        return sharedInstance;
    }

    +(PicklePlugin*) shareMessage:(char*)message shareSubject:(char*)subject shareDoShareIcon:(bool*)doShareIcon{
        return [[self sharedInstance] initShareMessage:message initShareSubject:subject initDoShareIcon:doShareIcon];
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
        
        // Create an instance of the PicklePopupDelegate
        self.popoverDelegate = [[PicklePopupDelegate alloc] init];
        
        // Present the view controller using the popover style
        activity.modalPresentationStyle = UIModalPresentationPopover;
        
        // Setup a callback when an event in the share dialog is completed (e.g copy to clipboard, send email)
        // Note: This will also trigger after selecting to an action which opens another popover window then closing that, e.g share via email then closing email form)
        activity.completionWithItemsHandler = ^(UIActivityType _Nullable activityType, BOOL completed, NSArray * _Nullable returnedItems, NSError * _Nullable activityError) {
            [self.popoverDelegate popoverPresentationDismissDueToCompletion:completed];
        };
        
        [rootViewController presentViewController:activity animated:YES completion:^{
            [self.popoverDelegate popoverPresentationCompleted];
        }];
        
        // Get the popover presentation controller and configure it
        UIPopoverPresentationController *presentationController = [activity popoverPresentationController];
        presentationController.permittedArrowDirections = 0;
        presentationController.sourceView = rootViewController.view;
        presentationController.sourceRect = CGRectMake(CGRectGetMidX(rootViewController.view.bounds), CGRectGetMidY(rootViewController.view.bounds), 0, 0);
        
        // Set the presentationController to delegate callbacks to the PicklePopupDelegate instance we created
        presentationController.delegate = self.popoverDelegate;
        
        return self;
    }

    - (void)triggerHapticFeedbackWithStyle:(UIImpactFeedbackStyle)style duration:(NSTimeInterval)duration {
        if (@available(iOS 10.0, *)) {
            UIImpactFeedbackGenerator *generator = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
            [generator prepare];
            [generator impactOccurred];
            
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(duration * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                [generator prepare];
            });
        } else {
            // We could use AudioServicesPlaySystemSound to vibrate below iOS 10 but our games don't support that anyway so don't bother
            NSLog(@"[PP] Haptic feedback is not supported on this device.");
        }
    }

    # pragma mark - C API

    void ShowShare(char* message, char* subject, bool* doShareIcon){
        [PicklePlugin shareMessage:message shareSubject:subject shareDoShareIcon:doShareIcon];
    }

    int GetTrackingAuthStatus(){
        // Make sure the supported APIs are available
        if (@available(iOS 14, macOS 11, *)) {
            return (int)ATTrackingManager.trackingAuthorizationStatus;
        } else {
            bool trackingEnabled = ASIdentifierManager.sharedManager.isAdvertisingTrackingEnabled;
            return trackingEnabled ? TRACKING_AUTHED : TRACKING_DENIED;
        }
    }

    void RequestTrackingAuth(AppTrackingTransparencyCallback callback){
        if (@available(iOS 14, macOS 11, *)) {
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
                // 0 - Not determined
                // 1 - Restricted
                // 2 - Denied
                // 3 - Authorized
                callback((unsigned int) status);
            }];
        } else {
            bool trackingEnabled = ASIdentifierManager.sharedManager.isAdvertisingTrackingEnabled;
            NSLog(@"[PP] RequestTrackingAuth called but the ATTrackingManager API is not available, returning %d.\n", trackingEnabled);
            callback(trackingEnabled ? TRACKING_AUTHED : TRACKING_DENIED);
        }
    }

    char* GetSettingsURL(){
        // Grab the string to open the settings app
        NSURL * url = [NSURL URLWithString: UIApplicationOpenSettingsURLString];

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

    void TriggerHapticFeedback(char* style, double duration) {
        NSString *styleString = [NSString stringWithUTF8String:style];
        UIImpactFeedbackStyle impactStyle = UIImpactFeedbackStyleLight; // Default style
        
        if ([styleString isEqualToString:@"medium"]) {
            impactStyle = UIImpactFeedbackStyleMedium;
        } else if ([styleString isEqualToString:@"heavy"]) {
            impactStyle = UIImpactFeedbackStyleHeavy;
        }
        
        double feedbackDuration = duration > 0 ? duration : 0.1; // Default duration if not provided
        
        [[PicklePlugin new] triggerHapticFeedbackWithStyle:impactStyle duration:feedbackDuration];
    }
@end
