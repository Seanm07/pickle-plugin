using UnityEngine;
using System.Collections;

public class JarLoader : MonoBehaviour {

	private static bool LoggingEnabled = false; // Toggle debug outputs

	private static bool ScriptReady = false; // True once there is a gameobject in the scene with JarLoader.cs which has awaken
	private static bool ScriptInitialised = false;

	#if UNITY_ANDROID && !UNITY_EDITOR
	private static AndroidJavaObject Activity;
	private static AndroidJavaObject ActivityContext;

	private static AndroidJavaClass AppInfoClass;
	private static AndroidJavaClass LocalNotificationsClass;
	private static AndroidJavaClass SystemInfoClass;
	private static AndroidJavaClass ToastsClass;
	private static AndroidJavaClass VibrationClass;
	#endif

	void Awake()
	{
		// Destroy if this already exists
		if(ScriptReady){
			Destroy(this);
			return;
		}

		ScriptReady = true;
	}

	private static void DebugLog(string Message)
	{
		if(LoggingEnabled)
			Debug.Log(Message);
	}

	private static void GetInstance()
	{
		DebugLog("Running GetInstance..");

		if(!ScriptInitialised){

			if(!ScriptReady){
				// JarLoader.cs wasn't attached to a persistant gameobject but a function was called!

				// Create a new gameobject for the JarLoader script
				GameObject JarLoaderObj = new GameObject("JarLoaderObj");

				// Attach the JarLoader script to the new gameobject and use this script for JarLoader.Instance references
				JarLoaderObj.AddComponent<JarLoader>();

				ScriptReady = true;

				DebugLog("JarLoader.cs was created");
			}

			#if UNITY_ANDROID && !UNITY_EDITOR
			if(ActivityContext == null || AppInfoClass == null || LocalNotificationsClass == null || SystemInfoClass == null || ToastsClass == null || VibrationClass == null){
				if(AndroidJNI.AttachCurrentThread() >= 0){
					AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
					Activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
					ActivityContext = Activity.Call<AndroidJavaObject>("getApplicationContext");
					
					AppInfoClass = new AndroidJavaClass("com.pickle.picklecore.AppInfo");
					LocalNotificationsClass = new AndroidJavaClass("com.pickle.picklecore.LocalNotifications");
					SystemInfoClass = new AndroidJavaClass("com.pickle.picklecore.SystemInfo");
					ToastsClass = new AndroidJavaClass("com.pickle.picklecore.Toasts");
					VibrationClass = new AndroidJavaClass("com.pickle.picklecore.Vibration");
				} else {
					DebugLog("Failed to attach current thread to Java (Dalvik) VM");
				}
			}

			ScriptInitialised = true;

			DebugLog("JarLoader.cs was initialised");

			#elif UNITY_EDITOR
				DebugLog("JarLoader.cs will not attach current thread in the editor!");
			#elif !UNITY_ANDROID
				DebugLog("JarLoader.cs will not attach current thread on non-android devices!");
			#endif
		}
	}

	/// <summary>
	/// Make the device vibrate a bit to give the user a response to their touch or to notify them
	/// </summary>
	/// <param name="Type">Haptic vibration type (1 = quick & weak, 2 = quick & mid, 3 = longer & strong)</param>
	public static void DoHapticFeedback(int Type = 1)
	{
		// Make sure we have an instance and the ActivityContext + Javaclass are ready
		GetInstance();

		DebugLog("Doing haptic feedback..");

		#if UNITY_ANDROID && !UNITY_EDITOR
			if(VibrationClass != null && ActivityContext != null && Activity != null){
				VibrationClass.CallStatic("DoHapticFeedback", Activity, ActivityContext, Type);
			} else {
				DebugLog("Failed to send haptic feedback!");
				
				//FirebaseAnalyticsManager.LogError("Haptic feedback error");
			}
		#elif UNITY_EDITOR 
			DebugLog ("JarLoader.cs will not send haptic feedback in the editor!");
		#elif !UNITY_ANDROID
			DebugLog("JarLoader.cs will not send haptic feedback on non-android devices!");
		#endif
	}

	public static void DoVibration(float Miliseconds, float Strength = 1f)
	{
		// Make sure we have an instance and the ActiviyContext + JavaClass are ready
		GetInstance();

		DebugLog("Doing vibration..");

		#if UNITY_ANDROID && !UNITY_EDITOR
			int IntStrength = Mathf.Clamp(Mathf.RoundToInt(Strength * 255f), 1, 255);
			long LongMiliseconds = (long)Miliseconds;

			if(VibrationClass != null && ActivityContext != null){
				VibrationClass.CallStatic("DoVibrate", ActivityContext, LongMiliseconds, IntStrength);
			} else {
				DebugLog("Failed to send vibration!");
				//FirebaseAnalyticsManager.LogError("Vibration error");
			}
		#elif UNITY_EDITOR
			DebugLog("JarLoader.cs will not send vibration in the editor!");
		#elif !UNITY_ANDROID
			DebugLog("JarLoader.cs will not send vibration on non-android devices!");
		#endif
	}

	public static void StopVibration()
	{
		// Make sure we have any instance and the ActivityContext + JavaClass are ready
		GetInstance();

		Debug.Log("Stopping vibration..");

		#if UNITY_ANDROID && !UNITY_EDITOR
			if(VibrationClass != null && ActivityContext != null){
				VibrationClass.CallStatic("StopVibrate");
			} else {
				DebugLog("Failed to send stop vibration!");
				//FirebaseAnalyticsManager.LogError("Stop vibration error");
			}
		#elif UNITY_EDITOR
			DebugLog("JarLoader.cs will not send stop vibration in the editor!");
		#elif !UNITY_ANDROID
			DebugLog("JarLoader.cs will not send stoop vibration on non-android devices!");
		#endif
	}

	public static long GetAppInstallTimestamp()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting app install timestamp..");

		#if UNITY_ANDROID && !UNITY_EDITOR
		if(AppInfoClass != null && ActivityContext != null){
			return AppInfoClass.CallStatic<long>("GetInstallTimestamp", ActivityContext);
		} else {
			DebugLog("Failed to get app install timestamp!");
			//FirebaseAnalyticsManager.LogError("Install timestamp failure");
		}
		#elif UNITY_EDITOR
			DebugLog("JarLoader.cs will not get the install timestamp for this game in the editor!");
		#elif !UNITY_ANDROID
			DebugLog ("JarLoader.cs will not get the install timestamp for this game on non-android devices!");
		#endif

		// It failed so just return -1
		return -1L;
	}

	public static long GetMillisecondsSinceBoot()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting milliseconds since boot..");

		#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null && ActivityContext != null){
				return SystemInfoClass.CallStatic<long>("GetMillisecondsSinceBoot");
			} else {
				DebugLog("Failed to get milliseconds since boot!");
				//FirebaseAnalyticsManager.LogError("Miliseconds since boot failure");
			}
		#elif UNITY_EDITOR
			DebugLog("JarLoader.cs will not get the milliseconds since boot in the editor!");
		#elif !UNITY_ANDROID
			DebugLog("JarLoader.cs will not get the milliseconds since boot on non-android devices!");
		#endif

		// On iOS we would call:
		// NSProcessInfo.processInfo().systemUptime;
		
		// It failed to just return -1
		return -1L;
	}

	public static bool DeviceHasNotch()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Checking if device has a notch..");

		bool deviceHasNotch = false;
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(SystemInfoClass != null && Activity != null && ActivityContext != null){
			deviceHasNotch = SystemInfoClass.CallStatic<bool>("HasNotchCutout", Activity, ActivityContext);
		} else {
			DebugLog("Failed to get device notch status!");
			//FirebaseAnalyticsManager.LogError("Java notch status error");
		}
#endif

		// If deviceHasNotch is false, double check by comparing the unity safe area value with the screen width/height
		if (!deviceHasNotch) {
			Rect safeArea = Screen.safeArea;
			deviceHasNotch = (safeArea.xMin > 0 || safeArea.yMin > 0 || safeArea.xMax < Screen.width || safeArea.yMax < Screen.height);
		}

		return deviceHasNotch;
	}
	
	public static Rect GetScreenSafeArea(bool alsoAvoidNavigationArea = false)
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting screen safe area..");

		Rect safeArea = Rect.zero;

#if UNITY_ANDROID && !UNITY_EDITOR
		if(SystemInfoClass != null && Activity != null && ActivityContext != null){
			int[] safeZoneArray = SystemInfoClass.CallStatic<int[]>("GetSafeZone", Activity, ActivityContext, alsoAvoidNavigationArea);

			if (safeZoneArray != null && safeZoneArray.Length == 4) {
				safeArea.xMin = safeZoneArray[0];
				safeArea.yMin = safeZoneArray[1];
				safeArea.xMax = safeZoneArray[2];
				safeArea.yMax = safeZoneArray[3];
			}
		} else {
			DebugLog("Failed to get device safe area!");
			//FirebaseAnalyticsManager.LogError("Java safe area error");
		}
#endif

		// If safeZone is empty fallback to the unity safe zone
		if (safeArea == Rect.zero) {
			safeArea = Screen.safeArea;
		}

		return safeArea;
	}
	
	public static int GetScreenWidth()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting screen width..");

		int scrWidth = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
		if(SystemInfoClass != null && Activity != null && ActivityContext != null){
			scrWidth = SystemInfoClass.CallStatic<int>("GetWidth", Activity, ActivityContext);
		} else {
			DebugLog("Failed to get screen width!");
			//FirebaseAnalyticsManager.LogError("Java safe area error");
		}
#endif

		// If width is 0 fallback to the unity screen width
		if (scrWidth <= 0) {
			scrWidth = Screen.width;
		}

		return scrWidth;
	}
	
	public static int GetScreenHeight()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting screen height..");

		int scrHeight = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
		if(SystemInfoClass != null && Activity != null && ActivityContext != null){
			scrHeight = SystemInfoClass.CallStatic<int>("GetHeight", Activity, ActivityContext);
		} else {
			DebugLog("Failed to get screen height!");
			//FirebaseAnalyticsManager.LogError("Java safe area error");
		}
#endif

		// If width is 0 fallback to the unity screen width
		if (scrHeight <= 0) {
			scrHeight = Screen.height;
		}

		return scrHeight;
	}
	
	public static void OpenSettingApp()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Opening settings app..");
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(SystemInfoClass != null && Activity != null && ActivityContext != null){
			SystemInfoClass.CallStatic("OpenSettingsApp", Activity, ActivityContext);
		} else {
			DebugLog("Failed to open setting app!");
			//FirebaseAnalyticsManager.LogError("Java settings app error");
		}
#else
		DebugLog("JarLoader.cs will not open setting app on non-android devices!");
#endif
	}
	
	public static int GetDensity()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting display density..");

		#if UNITY_ANDROID && !UNITY_EDITOR
		if(SystemInfoClass != null && ActivityContext != null){
		return SystemInfoClass.CallStatic<int>("GetDensity", ActivityContext);
		} else {
		DebugLog("Failed to get display density!");
			//FirebaseAnalyticsManager.LogError("Java DPI error");
		}
		#elif UNITY_EDITOR 
		DebugLog ("JarLoader.cs will not get display desity from jar in the editor!");
#elif !UNITY_ANDROID
		DebugLog("JarLoader.cs will not get display desity on non-android devices!");
		return Mathf.RoundToInt(Screen.dpi);
#endif

        // Nothing has been returned yet so just return Screen.dpi instead (Note that this will return 0 if it fails)
        return Mathf.RoundToInt(Screen.dpi);
    }

	public static float GetXDPI()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting XDPI..");

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null && ActivityContext != null){
				return SystemInfoClass.CallStatic<float>("GetXDPI", ActivityContext);
			} else {
				DebugLog("Failed to get XDPI!");
				//FirebaseAnalyticsManager.LogError("Java XDPI error");
			}
#elif UNITY_EDITOR
			DebugLog ("JarLoader.cs will not get XDPI from jar in the editor!");
#elif !UNITY_ANDROID
			DebugLog("JarLoader.cs will not get XDPI on non-android devices!");
#endif

		// Nothing has been returned yet so just return Screen.dpi instead (Note that this will return 0 if it fails)
		return Mathf.RoundToInt(Screen.dpi);
	}

	public static float GetYDPI()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting YDPI..");

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null && ActivityContext != null){
				return SystemInfoClass.CallStatic<float>("GetYDPI", ActivityContext);
			} else {
				DebugLog("Failed to get XDPI!");
				//FirebaseAnalyticsManager.LogError("Java YDPI error");
			}
#elif UNITY_EDITOR
			DebugLog ("JarLoader.cs will not get YDPI from jar in the editor!");
#elif !UNITY_ANDROID
			DebugLog("JarLoader.cs will not get YDPI on non-android devices!");
#endif

		// Nothing has been returned yet so just return Screen.dpi instead (Note that this will return 0 if it fails)
		return Mathf.RoundToInt(Screen.dpi);
	}

	public static string GetSelfPackageName()
	{
		// Make sure we have an instance and the AcitivtyContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting self package name..");

		string PackageName = "Unknown";

#if UNITY_ANDROID && !UNITY_EDITOR
		if(AppInfoClass != null && ActivityContext != null){
			// Get the package name being used by this app on the current device
			PackageName = AppInfoClass.CallStatic<string>("GetSelfPackageName", ActivityContext);
		} else {
			DebugLog("The Java class or AcitivtyContext wasn't ready when getting package list!");
		}
#endif

		return PackageName;
	}

	public static string GetPackageList(string searchString = default(string))
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

		DebugLog("Getting package list..");

#if UNITY_ANDROID && !UNITY_EDITOR
		string PackageList = string.Empty;

		if(AppInfoClass != null && ActivityContext != null){
			// Get the list of installed packages on the device
			PackageList = AppInfoClass.CallStatic<string>("GetPackageList", ActivityContext, searchString);
		} else {
			DebugLog("The Java class or ActivityContext wasn't ready when getting package list!");
		}

		if (!string.IsNullOrEmpty (PackageList)) {
			DebugLog("Output: " + PackageList);
		} else {
			DebugLog("Output was null or empty!");
		}
		return PackageList;
#elif UNITY_EDITOR
		DebugLog("JarLoader.cs will not GetPackageList in the editor!");
#elif !UNITY_ANDROID
		DebugLog("JarLoader.cs will not GetPackageList on non-android devices!");
#endif

		return string.Empty;
	}

	public static void DisplayToastMessage(string inString)
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
		if(ToastsClass != null){
		// Get the list of installed packages on the device
		ToastsClass.CallStatic("Show", ActivityContext, inString , 5);

		DebugLog("Toast has been popped!");
		} else {
		DebugLog("The Java class wasn't ready when displaying a toast!");
		}
#else
		DebugLog("JarLoader.cs - Toast: " + inString);
#endif
	}

	// MaxMemory - UsedMemory
	public static long GetAvailableMemory()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null){
				return SystemInfoClass.CallStatic<long>("GetAvailableMemory");
			} else {
				DebugLog("The Java class or AcitivtyContext wasn't ready when getting available memory!");
			}
#endif

		// In the editor just get the total available system memory and convert it to bytes (from kb)
		return ((long)SystemInfo.systemMemorySize * 1000L);
	}

	// TotalMemory - FreeMemory
	public static long GetUsedMemory()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null){
				return SystemInfoClass.CallStatic<long>("GetUsedMemory");
			} else {
				DebugLog("The Java class or AcitivtyContext wasn't ready when getting used memory!");
			}
#endif

		// In the editor get the used memory via System.GC
		return (System.GC.GetTotalMemory(false));
	}

	// Returns the total memory available to the Java VM (this value can change over time as the system assigns more memory into the Java VM)
	public static long GetTotalMemory()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null){
				return SystemInfoClass.CallStatic<long>("GetTotalMemory");
			} else {
				DebugLog("The Java class or AcitivtyContext wasn't ready when getting total memory!");
			}
#endif

		// In the editor just get the total available system memory and convert it to bytes (from mb)
		return ((long)SystemInfo.systemMemorySize * 1024L * 1024L);
	}

	// Returns the maximum amount of memory that the Java VM will attempt to use
	public static long GetMaxMemory()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null){
				return SystemInfoClass.CallStatic<long>("GetMaxMemory");
			} else {
				DebugLog("The Java class or AcitivtyContext wasn't ready when getting max memory!");
			}
#endif

		// In the editor just get the total available system memory and convert it to bytes (from kb)
		return ((long)SystemInfo.systemMemorySize * 1024L * 1024L);
	}

	// Amount of free memory currently allocated to the Java VM (more may be assigned by the system as needed)
	public static long GetFreeMemory()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
			if(SystemInfoClass != null){
				return SystemInfoClass.CallStatic<long>("GetFreeMemory");
			} else {
				DebugLog("The Java class or AcitivtyContext wasn't ready when getting free memory!");
			}
#endif

		// In the editor..
		return (System.GC.GetTotalMemory(false) - ((long)SystemInfo.systemMemorySize * 1000L));
	}

	public static void CancelToastMessage()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
		if(ToastsClass != null){
		// Get the list of installed packages on the device
		ToastsClass.CallStatic("Hide");

		DebugLog("Toast has been cancelled!");
		} else {
		DebugLog("The Java class wasn't ready when cancelling a toast!");
		}
#else
		DebugLog("JarLoader.cs - Cancelling Toast");
#endif
	}
	
	public static void CreateNotificationGroup(string groupId, string groupName)
	{
		GetInstance();
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(LocalNotificationsClass != null){
			// Get the list of installed packages on the device
			LocalNotificationsClass.CallStatic("CreateNotificationGroup", ActivityContext, groupId, groupName);

			DebugLog("CreateNotificationGroup calls with id: " + groupId + " and name: " + groupName);
		} else {
			DebugLog("The Java class wasn't ready when calling CreateNotificationGroup(..)!");
		}
#else
		DebugLog("JarLoader.cs - CreateNotificationGroup");
#endif
	}
	
	public static void CreateNotificationChannel(string channelId, string groupId, string channelName, string channelDescription, bool showOnStatusBar = false, bool playSound = false, bool showHeadsUp = false)
	{
		GetInstance();
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(LocalNotificationsClass != null){
			// Get the list of installed packages on the device
			LocalNotificationsClass.CallStatic("CreateNotificationChannel", ActivityContext, channelId, groupId, channelName, channelDescription, showOnStatusBar, playSound, showHeadsUp);

			DebugLog("CreateNotificationChannel was ran");
		} else {
			DebugLog("The Java class wasn't ready when calling CreateNotificationChannel(..)!");
		}
#else
		DebugLog("JarLoader.cs - CreateNotificationChannel");
#endif
	}
	
	public static void SendNotification(int notificationId, string channelId, string msgTitle, string msgBody, int sendAfterSeconds, string smallIconName, string largeIconName = "", bool dismissAfterTapped = true)
	{
		GetInstance();
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(LocalNotificationsClass != null){
			// Get the list of installed packages on the device
			LocalNotificationsClass.CallStatic("SendNotification", ActivityContext, Activity, notificationId, channelId, msgTitle, msgBody, sendAfterSeconds, smallIconName, largeIconName, dismissAfterTapped);

			DebugLog("SendNotification was ran");
		} else {
			DebugLog("The Java class wasn't ready when calling SendNotification(..)!");
		}
#else
		DebugLog("JarLoader.cs - SendNotification");
#endif
	}
	
	public static void CancelNotification(int notificationId)
	{
		GetInstance();
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(LocalNotificationsClass != null){
			// Get the list of installed packages on the device
			LocalNotificationsClass.CallStatic("CancelNotification", ActivityContext, Activity, notificationId);

			DebugLog("CancelNotification was ran");
		} else {
			DebugLog("The Java class wasn't ready when calling CancelNotification(..)!");
		}
#else
		DebugLog("JarLoader.cs - SendNotification");
#endif
	}
	
	public static bool DoesAppContainBadPermissions()
	{
		GetInstance();
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(AppInfoClass != null && ActivityContext != null){
			DebugLog("DoesAppContainBadPermissions about to run");

			return AppInfoClass.CallStatic<bool>("DoesAppContainBadPermissions", ActivityContext);

			DebugLog("DoesAppContainBadPermissions was ran");
		} else {
			DebugLog("The Java class or AcitivtyContext wasn't ready when getting bad permissions!");
		}
#else
		DebugLog("JarLoader.cs - DoesAppContainBadPermissions");
#endif

		return false;
	}

	public static int nextRunIdOffset { get; set; }
	
	public static int AppRunId() {
		GetInstance();

		int runIdOffset = nextRunIdOffset;
		nextRunIdOffset = 0;
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(AppInfoClass != null && ActivityContext != null){
			return runIdOffset + AppInfoClass.CallStatic<int>("GetRunId", ActivityContext);
		} else {
			DebugLog("The Java class or AcitivtyContext wasn't ready when getting run id!");
		}
#else
		DebugLog("JarLoader.cs - AppRunId");
#endif

		return runIdOffset + 48276;
	}

	public static string GetLaunchIntentExtras() {
		GetInstance();

		string launchIntentExtras = string.Empty;
		
#if UNITY_ANDROID && !UNITY_EDITOR
		if(LocalNotificationsClass != null){
			// Get the list of installed packages on the device
			launchIntentExtras = LocalNotificationsClass.CallStatic<string>("GetLaunchIntentExtras", Activity);

			DebugLog("GetLaunchIntentExtras was ran");
		} else {
			DebugLog("The Java class wasn't ready when calling GetLaunchIntentExtras()!");
		}
#else
		launchIntentExtras = ""; // Set this to "notificationId:1" for editor testing (change 1 to the notification id to test)
		
		DebugLog("JarLoader.cs - GetLaunchIntentExtras");
#endif

		return launchIntentExtras;
	}

	// Returns an array of accounts on the device in hash form
	// Useful for comparing player identities for IAB and restoring purchases
	/*public static string[] GetAccountHashes()
	{
		// Make sure we have an instance and the ActivityContext + JavaClass is ready
		GetInstance();

#if UNITY_ANDROID && !UNITY_EDITOR
			if(JavaClass != null){
				// Get an array of accounts on the device
				JavaClass.CallStatic<string[]>("GetAccounts", ActivityContext);

				DebugLog("Accounts have been requested!");
			} else {
				DebugLog("The java class wasn't ready when requesting accounts!");
			}
#else
			DebugLog("JarLoader.cs Requested account hashes!");
#endif

		return new string[0];
	}*/

}