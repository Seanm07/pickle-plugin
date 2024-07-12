# Pickle Plugin - Readme


An example notification script from Helicopter Rescue - https://pastebin.com/PH3X9K7S


## This is how the new android notification system works:
1. Create a notification group (you can re-call this as much as you want if it already exists then np, but note that clearing data removes notification groups) 
2. Create a notification channel referencing the groupid string you made for your group (you can re-call as much as you want etc etc)
3. SendNotification(...) with how many seconds etc, this will schedule the notification via the android scheduler to call onReceieve inside the plugin with the data we set to be sent when the alarm triggers, in this onReceieve function is where the visible notification is displayed to the user)

It's a good idea to make a group for each TYPE of notifications, e.g Promotions, Content Unlocked, Daily Reward notifications (the player can manually block notifications from each group)

## Classes
### com.pickle.picklecore.AppInfo

```c#
GetSelfPackageName(Context)
```
returns string of package name


```c#
GetInstallTimestamp(Context)
```
returns long of time since app was installed OR updated


```c#
GetInitialInstallTimestamp(Context)
```
returns long of time since app was initially installed (not affected by updates or clearing app data but uninstalling and reinstalling does reset this)


```c#
DoesAppContainBadPermissions(Context)
```
returns bool true if app contains permissions to REQUEST_DELETE_PACKAGES, REQUEST_INSTALL_PACKAGES, DELETE_PACKAGES or INSTALL_PACKAGES all dangerous permissions added by chinese spammy modded APK sites


```c#
GetRunId(Context)
```
returns an int to sneakily check for bad permissions without being super obvious to the person modifying the APK
// 58382 (14595) = DoesAppContainBadPermissions returned true so it wasn't patched out
// 48276 (12069) = DoesAppContainBadPermissions returned false and we encountered an error
// 30274 (7568) = DoesAppContainBadPermissions returned false and rechecking returned false again, seems good
// 28494 (7123) = DoesAppContainBadPermissions returned false but rechecking we found it actually to be true!!

### com.pickle.picklecore.LocalNotifications

```c#
CreateNotificationGroup(Context, (String)ID, (String)Name)
```

```c#
CreateNotificationChannel(Context, (String)ID, (String)GroupID, (String)Description, (bool)ShowOnStatusBar, (bool)PlaySound)
```

```c#
CreateNotificationChannel(...)
```

```c#
CreateNotificationChannel(...)
```

```c#
CreateNotificationChannel(...)
```

```c#
CreateNotificationChannel(...)
```

```c#
CreateNotificationChannel(...)
```

```c#
CreateNotificationChannel(...)
```

```c#
CreateNotificationChannel(...)
```

```c#
GetNeededImportanceLevel(ChannelData)
```
returns int


```c#
GetNeededPriorityLevel(...)
```
returns int


```c#
DeleteNotificationChannelGroup(...)
```

```c#
DeleteNotificationChannel(...)
```

```c#
GetChannelDataById(...)
```
returns ChannelData


```c#
SendNotification(...)
```

```c#
SendNotification(...)
```

```c#
SendNotification(...)
```

```c#
CancelNotification(...)
```

```c#
GetLaunchIntentExtras(...)
```

### com.pickle.picklecore.SystemInfo
```c#
GetAvailableMemory()
```
returns long


```c#
GetUsedMemory()
```
returns long


```c#
GetTotalMemory()
```
returns long


```c#
GetMaxMemory()
```
returns long


```c#
GetFreeMemory()
```
returns long


```c#
GetMillisecondsSinceBoot()
```
returns long


```c#
GetDensity(Context)
```
returns int


```c#
GetXDPI(Context)
```
returns float


```c#
GetYDPI(Context)
```
returns float


### com.pickle.picklecore.Toasts
```c#
Show(Context, (String)Message, (int)Duration)
```
See Toast.makeText here: https://developer.android.com/guide/topics/ui/notifiers/toasts.html


```c#
Hide()
```

### com.pickle.picklecore.Vibration
```c#
DoHapticFeedback(Activity, Context)
```

```c#
DoHapticFeedback(Activity, Context, (int)Type)
```
See https://developer.android.com/reference/android/view/HapticFeedbackConstants for type int values



#### Target SDK 26 or later to use these (script checks this first)
#### You also need to add android.Manifest.permission.VIBRATE and the user needs to grant it or these will seemingly do nothing:
```c#
DoVibrate(Context, (long)miliseconds)
```

```c#
DoVibrate(Context, (long)miliseconds, (int)strength)
```
strength is between 1 and 255, see https://developer.android.com/reference/android/os/VibrationEffect#createOneShot(long,%20int)


```c#
StopVibrate()
```

# Firebase Analytics - PickleEventCategory Documentation

The `PickleEventCategory` class contains a set of predefined event categories used for logging various events within an application.
We had to switch to predefined event categories as the firebase analytics backend was getting too messy and most the time developers were logging incorrectly which was causing game analytics to be useless.

## Internal Pickle Script Categories

### PickleScripts

#### APP_STORE
- **Description**: Track the source store which the app was installed from.
- **Code Reference**: `PickleEventCategory.PickleScripts.APP_STORE`
- **Raw Category Name**: `app_store`

#### ADMOB_TTL
- **Description**: Tracking of how long different admob components take to initialize.
- **Code Reference**: `PickleEventCategory.PickleScripts.ADMOB_TTL`
- **Raw Category Name**: `admob_ttl`

#### IAS_CLICK
- **Description**: Ad impressions on our internal ad system.
- **Code Reference**: `PickleEventCategory.PickleScripts.IAS_CLICK`
- **Raw Category Name**: `ias_click`

#### IAS_IMPRESSION
- **Description**: Ad clicks on our internal ad system.
- **Code Reference**: `PickleEventCategory.PickleScripts.IAS_IMPRESSION`
- **Raw Category Name**: `ias_impression`

#### IAP_PURCHASE_COMPLETE
- **Description**: In-app purchases which were successfully and triggered the success callback.
- **Code Reference**: `PickleEventCategory.PickleScripts.IAP_PURCHASE_COMPLETE`
- **Raw Category Name**: `iap_purchase_complete`

#### IAP_PURCHASE_ERROR
- **Description**: In-app purchases which failed.
- **Code Reference**: `PickleEventCategory.PickleScripts.IAP_PURCHASE_ERROR`
- **Raw Category Name**: `iap_purchase_error`

#### IAB_RESTORE_SUCCESS
- **Description**: In-app purchase restore actions which were successful.
- **Code Reference**: `PickleEventCategory.PickleScripts.IAB_RESTORE_SUCCESS`
- **Raw Category Name**: `iab_restore_success`

#### IAB_RESTORE_FAILED
- **Description**: In-app purchase restore actions which failed.
- **Code Reference**: `PickleEventCategory.PickleScripts.IAB_RESTORE_FAILED`
- **Raw Category Name**: `iab_restore_failed`

#### LOW_MEMORY_WARNING
- **Description**: Track when the device reaches critically low memory.
- **Code Reference**: `PickleEventCategory.PickleScripts.LOW_MEMORY_WARNING`
- **Raw Category Name**: `low_memory_warning`

#### PERSONALISATION_AUTH_FLOW
- **Description**: Events logged relating to the personalisation and CMP flow.
- **Code Reference**: `PickleEventCategory.PickleScripts.PERSONALISATION_AUTH_FLOW`
- **Raw Category Name**: `personalisation_auth_flow`

## General Event Categories

#### MESSAGE_PROMPTS
- **Description**: Popup messages or dialogs which inform or ask the player a question.
- **Code Reference**: `PickleEventCategory.MESSAGE_PROMPTS`
- **Raw Category Name**: `message_prompts`

#### ITEM_PURCHASED
- **Description**: Item purchased with in-game currency or other method.
- **Code Reference**: `PickleEventCategory.ITEM_PURCHASED`
- **Raw Category Name**: `item_purchased`

#### ITEM_EQUIPPED
- **Description**: Item equipped, e.g., weapons, vehicles, characters.
- **Code Reference**: `PickleEventCategory.ITEM_EQUIPPED`
- **Raw Category Name**: `item_equipped`

#### ITEM_COLLECTED
- **Description**: Item collected, such as hidden packages or collectibles.
- **Code Reference**: `PickleEventCategory.ITEM_COLLECTED`
- **Raw Category Name**: `item_collected`

#### MODE_SELECTED
- **Description**: Mode or map selected by the player.
- **Code Reference**: `PickleEventCategory.MODE_SELECTED`
- **Raw Category Name**: `mode_selected`

#### MISSION_PROMPTED
- **Description**: Mission prompted to player.
- **Code Reference**: `PickleEventCategory.MISSION_PROMPTED`
- **Raw Category Name**: `mission_prompted`

#### MISSION_PROMPT_ACCEPTED
- **Description**: Mission prompt accepted by player.
- **Code Reference**: `PickleEventCategory.MISSION_PROMPT_ACCEPTED`
- **Raw Category Name**: `mission_prompt_accepted`

#### MISSION_PROMPT_DECLINED
- **Description**: Mission prompt declined by player.
- **Code Reference**: `PickleEventCategory.MISSION_PROMPT_DECLINED`
- **Raw Category Name**: `mission_prompt_declined`

#### MISSION_START
- **Description**: Mission entered/started.
- **Code Reference**: `PickleEventCategory.MISSION_START`
- **Raw Category Name**: `mission_start`

#### MISSION_COMPLETE
- **Description**: Mission completed successfully.
- **Code Reference**: `PickleEventCategory.MISSION_COMPLETE`
- **Raw Category Name**: `mission_complete`

#### MISSION_FAILED
- **Description**: Mission failed.
- **Code Reference**: `PickleEventCategory.MISSION_FAILED`
- **Raw Category Name**: `mission_failed`

#### MISSION_FAILED_REASON
- **Description**: Reason for mission failure.
- **Code Reference**: `PickleEventCategory.MISSION_FAILED_REASON`
- **Raw Category Name**: `mission_failed_reason`

#### BUTTON_IMPRESSION
- **Description**: Tracking of impressions on buttons.
- **Code Reference**: `PickleEventCategory.BUTTON_IMPRESSION`
- **Raw Category Name**: `button_impression`

#### BUTTON_CLICK
- **Description**: Tracking of clicks on buttons.
- **Code Reference**: `PickleEventCategory.BUTTON_CLICK`
- **Raw Category Name**: `button_click`

#### LEVEL_UP
- **Description**: Player leveled up, track the current level when leveling up in games with XP.
- **Code Reference**: `PickleEventCategory.LEVEL_UP`
- **Raw Category Name**: `level_up`

#### REFERRAL_SHARE
- **Description**: Referral and app sharing events.
- **Code Reference**: `PickleEventCategory.REFERRAL_SHARE`
- **Raw Category Name**: `referral_share`

#### REFUEL
- **Description**: Events relating to the refuel flow.
- **Code Reference**: `PickleEventCategory.REFUEL`
- **Raw Category Name**: `refuel`
