using System;
using UnityEngine;

public class PickleCore : MonoBehaviour {
    private static bool isPluginInterfaceReady;

    #if UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void TriggerHapticFeedback(string style, double duration);
        
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void ShowToast(string message, double duration);
        
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void HideToast();
        
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string GetSettingsURL();
    
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string GetIdentifierForVendor();
    #endif
    
    #if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject activity, context;
        private static AndroidJavaClass appInfo, localNotifications, systemInfo, toasts, vibration;
    
        private static bool AttachCurrentThread(out int e) => (e = AndroidJNI.AttachCurrentThread()) >= 0;
        private static void DetachCurrentThread() => AndroidJNI.DetachCurrentThread();

        private static bool isJNISetup = false;

        private static void CallStatic(AndroidJavaClass targetClass, string methodName, params object[] args) {
            SetupJavaNativeInterfaceIfNotSetup();

            targetClass?.CallStatic(methodName, args);
        }

        private static T CallStatic<T>(AndroidJavaClass targetClass, string methodName, params object[] args) {
            SetupJavaNativeInterfaceIfNotSetup();
            
            Type targetType = typeof(T);

            // I couldn't think of another way of calling the Java function without directly defining the return time in the call
            // if you can think of a way to call a Java function without directly defining the return type let me know - Sean 
            switch (Type.GetTypeCode(targetType)) {
                case TypeCode.Boolean: return (T) Convert.ChangeType(targetClass?.CallStatic<bool>(methodName, args), targetType);
                case TypeCode.Byte: return (T) Convert.ChangeType(targetClass?.CallStatic<byte>(methodName, args), targetType);
                case TypeCode.Char: return (T) Convert.ChangeType(targetClass?.CallStatic<char>(methodName, args), targetType);
                case TypeCode.Double: return (T) Convert.ChangeType(targetClass?.CallStatic<double>(methodName, args), targetType);
                case TypeCode.Int16: return (T) Convert.ChangeType(targetClass?.CallStatic<short>(methodName, args), targetType);
                case TypeCode.Int32: return (T) Convert.ChangeType(targetClass?.CallStatic<int>(methodName, args), targetType);
                case TypeCode.Int64: return (T) Convert.ChangeType(targetClass?.CallStatic<long>(methodName, args), targetType);
                case TypeCode.SByte: return (T) Convert.ChangeType(targetClass?.CallStatic<sbyte>(methodName, args), targetType);
                case TypeCode.Single: return (T) Convert.ChangeType(targetClass?.CallStatic<Single>(methodName, args), targetType);
                case TypeCode.String: return (T) Convert.ChangeType(targetClass?.CallStatic<string>(methodName, args), targetType);
                case TypeCode.UInt16: return (T) Convert.ChangeType(targetClass?.CallStatic<UInt16>(methodName, args), targetType);
                case TypeCode.UInt32: return (T) Convert.ChangeType(targetClass?.CallStatic<UInt32>(methodName, args), targetType);
                case TypeCode.UInt64: return (T) Convert.ChangeType(targetClass?.CallStatic<UInt64>(methodName, args), targetType);
                
                case TypeCode.Object:
                    // If this is an array check what type the array is
                    switch (Type.GetTypeCode(targetType.GetElementType())) {
                        case TypeCode.Boolean: return (T) Convert.ChangeType(targetClass?.CallStatic<bool[]>(methodName, args), targetType);
                        case TypeCode.Byte: return (T) Convert.ChangeType(targetClass?.CallStatic<byte[]>(methodName, args), targetType);
                        case TypeCode.Char: return (T) Convert.ChangeType(targetClass?.CallStatic<char[]>(methodName, args), targetType);
                        case TypeCode.Double: return (T) Convert.ChangeType(targetClass?.CallStatic<double[]>(methodName, args), targetType);
                        case TypeCode.Int16: return (T) Convert.ChangeType(targetClass?.CallStatic<short[]>(methodName, args), targetType);
                        case TypeCode.Int32: return (T) Convert.ChangeType(targetClass?.CallStatic<int[]>(methodName, args), targetType);
                        case TypeCode.Int64: return (T) Convert.ChangeType(targetClass?.CallStatic<long[]>(methodName, args), targetType);
                        case TypeCode.SByte: return (T) Convert.ChangeType(targetClass?.CallStatic<sbyte[]>(methodName, args), targetType);
                        case TypeCode.Single: return (T) Convert.ChangeType(targetClass?.CallStatic<Single[]>(methodName, args), targetType);
                        case TypeCode.String: return (T) Convert.ChangeType(targetClass?.CallStatic<string[]>(methodName, args), targetType);
                        case TypeCode.UInt16: return (T) Convert.ChangeType(targetClass?.CallStatic<UInt16[]>(methodName, args), targetType);
                        case TypeCode.UInt32: return (T) Convert.ChangeType(targetClass?.CallStatic<UInt32[]>(methodName, args), targetType);
                        case TypeCode.UInt64: return (T) Convert.ChangeType(targetClass?.CallStatic<UInt64[]>(methodName, args), targetType);
                    }
                    break;
            }
            
            return default;
        }

        private static void SetupJavaNativeInterfaceIfNotSetup(){
            if(isJNISetup) return;

            if (AttachCurrentThread(out int errorCode)) {
                // Setup the Java Native Interface so our script can talk to the Java plugin
                AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                context = activity.Call<AndroidJavaObject>("getApplicationContext");

                appInfo = new AndroidJavaClass("com.pickle.picklecore.AppInfo");
                localNotifications = new AndroidJavaClass("com.pickle.picklecore.LocalNotifications");
                systemInfo = new AndroidJavaClass("com.pickle.picklecore.SystemInfo");
                toasts = new AndroidJavaClass("com.pickle.picklecore.Toasts");
                vibration = new AndroidJavaClass("com.pickle.picklecore.Vibration");

                isJNISetup = true;
            } else {
                Debug.LogError("Failed to setup the PickleCore JNI! Could not attach current thread to Java VM (" + errorCode + ")");
            }
        }
    #endif
    
    void Awake() {
        if (isPluginInterfaceReady)
            Destroy(this);

        isPluginInterfaceReady = true;

        #if UNITY_ANDROID && !UNITY_EDITOR
            SetupJavaNativeInterfaceIfNotSetup();
        #endif
    }

    #region Vibration
        public static void DoHapticFeedback(float strength = 1f, bool overrideSystemSettings = false) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(vibration, "DoHapticFeedback", activity, context, Mathf.Clamp(Mathf.RoundToInt(strength * 4f), 1, 4), overrideSystemSettings);
            #elif UNITY_IPHONE && !UNITY_EDITOR
                TriggerHapticFeedback(strength >= 0.8f ? "heavy" : strength >= 0.6f ? "medium" : strength >= 0.4f ? "rigid" : strength >= 0.2f ? "soft" : "light", 0.1f);
            #endif
        }

        public static void DoVibration(long milliseconds, float strength = 1f) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(vibration, "DoVibrate", context, milliseconds, Mathf.Clamp(Mathf.RoundToInt(strength * 255f), 1, 255));
            #elif UNITY_IPHONE && !UNITY_EDITOR
                TriggerHapticFeedback(strength >= 0.8f ? "heavy" : strength >= 0.6f ? "medium" : strength >= 0.4f ? "rigid" : strength >= 0.2f ? "soft" : "light", milliseconds);
            #endif
        }
        
        // If the device has a connected controller with vibration support, this will vibrate the controller
        // Note: Only supported on API 31+ (android 12+) devices
        // Use low frequency motor for intense crashes/explosions and high frequency motor for subtle taps and haptic effects
        public static void DoControllerVibration(bool useLowFrequencyMotor, long milliseconds, float strength = 1f) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(vibration, "DoControllerVibrate", useLowFrequencyMotor, milliseconds, Mathf.Clamp(Mathf.RoundToInt(strength * 255f), 1, 255));
            #elif UNITY_IPHONE && !UNITY_EDITOR
                // Not yet supported by us
            #endif
        }

        public static void StopVibration() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(vibration, "StopVibrate");
            #else
                // not supported by us
            #endif
        }

        public static void StopControllerVibration() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(vibration, "DoControllerVibrate", false, 0L, 0);
                CallStatic(vibration, "DoControllerVibrate", true, 0L, 0);
            #else
                // not supported by us
            #endif
        }
    #endregion

    #region App Info
        public static long GetAppInstallTimestamp() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<long>(appInfo, "GetInstallTimestamp", context);
            #else
                return -1L;  // TODO: iOS API
            #endif
        }
        
        // NOTICE: Android 11+ this no longer returns all apps due to a new security policy
        public static string GetPackageList(string searchString = default) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<string>(appInfo, "GetPackageList", context, searchString);
            #else
                return string.Empty;
            #endif
        }
        
        public static bool DoesAppContainBadPermissions() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<bool>(appInfo, "DoesAppContainBadPermissions", context);
            #else
                return false;
            #endif
        }
    
        public static int nextRunIdOffset { get; set; }

        public static int AppRunId() {
            int runIdOffset = nextRunIdOffset;
            nextRunIdOffset = 0;
            
            #if UNITY_ANDROID && !UNITY_EDITOR
                return runIdOffset + CallStatic<int>(appInfo, "GetRunId", context);
            #else
                return runIdOffset + 30274;
            #endif
        }

        public static string GetDeveloperDeviceId() {
            #if UNITY_IPHONE && !UNITY_EDITOR
                return GetIdentifierForVendor();
            #else
                return ""; // iOS only value, blank on other platforms
            #endif
        }
    #endregion

    #region System Info
        public static long GetMillisecondsSinceBoot() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<long>(systemInfo, "GetMillisecondsSinceBoot");
            #else
                return -1L;  // TODO: iOS API
            #endif
        }
    
        public static int GetAndroidAPILevel() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<int>(systemInfo, "GetAPILevel");
            #else
                return -1;
            #endif
        }
    
        public static bool DeviceHasNotch() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<bool>(systemInfo, "HasNotchCutout", activity, context);
            #else
                return false; // TODO: Add iOS API
            #endif
        }

        public static Rect GetScreenSafeArea(bool avoidNavigationBar = false) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                int[] safeZoneArray = CallStatic<int[]>(systemInfo, "GetSafeZone", activity, context, avoidNavigationBar);

                if (safeZoneArray != null && safeZoneArray.Length == 4)
                    return new Rect(safeZoneArray[0], safeZoneArray[1], safeZoneArray[2], safeZoneArray[3]);
            #endif
            
            return Screen.safeArea;
        }

        public static int GetScreenWidth() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<int>(systemInfo, "GetWidth", activity, context);
            #else
                return Screen.width;
            #endif
        }

        public static int GetScreenHeight() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<int>(systemInfo, "GetHeight", activity, context);
            #else
                return Screen.height;
            #endif
        }

        public static void OpenSettingsApp() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(systemInfo, "OpenSettingsApp", activity, context);
            #elif UNITY_IPHONE && !UNITY_EDITOR
                Application.OpenURL(GetSettingsURL());
            #endif
            
            
        }

        public static int GetDensity() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<int>(systemInfo, "GetDensity", context);
            #else
                return Mathf.RoundToInt(Screen.dpi);
            #endif
        }

        public static float GetXDPI() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<float>(systemInfo, "GetXDPI", context);
            #else
                return Screen.dpi;
            #endif
        }
        
        public static float GetYDPI() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<float>(systemInfo, "GetYDPI", context);
            #else
                return Screen.dpi;
            #endif
        }
        
        public static long GetUsedMemory() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<long>(systemInfo, "GetUsedMemory");
            #else
                return -1L;
            #endif
        }
        
        public static long GetTotalMemory() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<long>(systemInfo, "GetTotalMemory");
            #else
                return -1L;
            #endif
        }
        
        public static long GetMaxMemory() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<long>(systemInfo, "GetMaxMemory");
            #else
                return -1L;
            #endif
        }
        
        public static long GetFreeMemory() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<long>(systemInfo, "GetFreeMemory");
            #else
                return -1L;
            #endif
        }
        
        public static bool IsAndroidTV() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<bool>(systemInfo, "IsAndroidTV", context);
            #else
                return false;
            #endif
        }
    #endregion

    #region Toasts
        [Obsolete("Android toasts do not support specific seconds, only long or short use DisplayToastMessage(string, bool) instead!")]
        public static void DisplayToastMessage(string toastMessage, int seconds) {
            DisplayToastMessage(toastMessage, seconds > 2);
        }
        
        // longShowTime true = 3.5 seconds / longShowTime false = 2 seconds (system defined we can't change these)
        public static void DisplayToastMessage(string toastMessage, bool longShowTime = false) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(toasts, "ShowToast", context, toastMessage, longShowTime);
            #elif UNITY_IPHONE && !UNITY_EDITOR
                ShowToast(toastMessage, longShowTime ? 3.5f : 2f);
            #else
                Debug.Log("Toast not supported on this platform: " + toastMessage);
            #endif
        }

        public static void CancelToastMessage() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(toasts, "HideToast");
            #elif UNITY_IPHONE && !UNITY_EDITOR
                HideToast();
            #endif
        }
        
        // Show white text overlay label on screen with a black box background, e.g if the game requires a controller you might want to show a connect controller prompt
        public static void ShowTextOverlay(string message, int fontSize, float backgroundOpacity) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(toasts, "ShowTextOverlay", activity, message, fontSize, Mathf.Clamp(Mathf.RoundToInt(backgroundOpacity * 255f), 0, 255));
            #else
                Debug.Log("Text overlay not supported on this platform: " + message);
            #endif
        }
        
        public static void HideTextOverlay() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(toasts, "HideTextOverlay", activity);
            #endif
        }
    #endregion

    #region Local Notifications
        public static void CreateNotificationGroup(string groupId, string groupName) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(localNotifications, "CreateNotificationGroup", context, groupId, groupName);
            #endif
        }

        public static void CreateNotificationChannel(string channelId, string groupId, string channelName, string channelDescription, bool showOnStatusBar = false, bool playSound = false, bool showHeadsUp = false) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(localNotifications, "CreateNotificationChannel", context, channelId, groupId, channelName, channelDescription, showOnStatusBar, playSound, showHeadsUp);
            #endif
        }

        public static void SendNotification(int notificationId, string channelId, string messageTitle, string messageBody, int sendAfterSeconds, string smallIconName, string largeIconName = "", bool dismissAfterTapped = true) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(localNotifications, "SendNotification", context, activity, notificationId, channelId, messageTitle, messageBody, sendAfterSeconds, smallIconName, largeIconName, dismissAfterTapped);
            #endif
        }

        public static void CancelNotification(int notificationId) {
            #if UNITY_ANDROID && !UNITY_EDITOR
                CallStatic(localNotifications, "CancelNotification", context, activity, notificationId);
            #endif
        }
        
        public static string GetLaunchIntentExtras() {
            #if UNITY_ANDROID && !UNITY_EDITOR
                return CallStatic<string>(localNotifications, "GetLaunchIntentExtras", activity);
            #else
                return string.Empty;
            #endif
        }
    #endregion
}
