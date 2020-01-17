# Pickle Plugin - Readme incomplete - Will finish in future


JarLoader.cs - https://pastebin.com/xZag2wjL

An example notification script from Helicopter Rescue - https://pastebin.com/PH3X9K7S


This is how the new android notification system works:
1. Create a notification group (you can re-call this as much as you want if it already exists then np, but note that clearing data removes notification groups) 
2. Create a notification channel referencing the groupid string you made for your group (you can re-call as much as you want etc etc)
3. SendNotification(...) with how many seconds etc, this will schedule the notification via the android scheduler to call onReceieve inside the plugin with the data we set to be sent when the alarm triggers, in this onReceieve function is where the visible notification is displayed to the user)

It's a good idea to make a group for each TYPE of notifications, e.g Promotions, Content Unlocked, Daily Reward notifications (the player can manually block notifications from each group)

Classes
com.pickle.picklecore.AppInfo

GetSelfPackageName(Context) > returns string of package name
GetInstallTimestamp(Context) > returns long of time since app was installed OR updated
GetInitialInstallTimestamp(Context) > returns long of time since app was initially installed (not affected by updates or clearing app data but uninstalling and reinstalling does reset this)
DoesAppContainBadPermissions(Context) > returns bool true if app contains permissions to REQUEST_DELETE_PACKAGES, REQUEST_INSTALL_PACKAGES, DELETE_PACKAGES or INSTALL_PACKAGES all dangerous permissions added by chinese spammy modded APK sites

GetRunId(Context) > returns an int to sneakily check for bad permissions without being super obvious to the person modifying the APK
// 58382 (14595) = DoesAppContainBadPermissions returned true so it wasn't patched out
// 48276 (12069) = DoesAppContainBadPermissions returned false and we encountered an error
// 30274 (7568) = DoesAppContainBadPermissions returned false and rechecking returned false again, seems good
// 28494 (7123) = DoesAppContainBadPermissions returned false but rechecking we found it actually to be true!!

com.pickle.picklecore.LocalNotifications

CreateNotificationGroup(Context, (String)ID, (String)Name)
CreateNotificationChannel(Context, (String)ID, (String)GroupID, (String)Description, (bool)ShowOnStatusBar, (bool)PlaySound
CreateNotificationChannel
CreateNotificationChannel
CreateNotificationChannel
CreateNotificationChannel
CreateNotificationChannel
CreateNotificationChannel
CreateNotificationChannel
GetNeededImportanceLevel(ChannelData)
GetNeededPriorityLevel
DeleteNotificationChannelGroup
DeleteNotificationChannel
GetChannelDataById
SendNotification
SendNotification
SendNotification
CancelNotification
GetLaunchIntentExtras

com.pickle.picklecore.SystemInfo
GetAvailableMemory() > returns long
GetUsedMemory() > returns long
GetTotalMemory() > returns long
GetMaxMemory() > returns long
GetFreeMemory() > returns long
GetMillisecondsSinceBoot() > returns long
GetDensity(Context) > returns int
GetXDPI(Context) > returns float
GetYDPI(Context) > returns float

com.pickle.picklecore.Toasts
Show(Context, (String)Message, (int)Duration) (See Toast.makeText here: https://developer.android.com/guide/topics/ui/notifiers/toasts.html )
Hide()

com.pickle.picklecore.Vibration
DoHapticFeedback(Activity, Context)
DoHapticFeedback(Activity, Context, (int)Type) (See https://developer.android.com/reference/android/view/HapticFeedbackConstants for type int values)

Target SDK 26 or later to use these (script checks this first)
You also need to add android.Manifest.permission.VIBRATE and the user needs to grant it or these will seemingly do nothing:
DoVibrate(Context, (long)miliseconds)
DoVibrate(Context, (long)miliseconds, (int)strength) (strength is between 1 and 255, see https://developer.android.com/reference/android/os/VibrationEffect#createOneShot(long,%20int) )
StopVibrate()
