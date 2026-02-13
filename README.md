# Pickle Plugin

A collection of scripts alongside our Game Pickle plugin for use in Unity mobile games.

---

# Installation

## Manual Install from zip

1. From the [releases page](https://github.com/Seanm07/pickle-plugin/releases) under Assets download PicklePlugin.zip
2. Drag the Plugins folder into the root of your project Asset folder
3. If the project already has our pickle scripts we recommend just replacing each script so you don't break inspector references, otherwise just add the scripts folder anywhere in your project as appropriate

## Add scripts to scene

1. Either create an empty GameObject in your initial build scene or use an existing GameObject which should never be destroyed (DontDestroyOnLoad)
2. Attach IABManager.cs, FirebaseManager.cs, AdMob_Manager.cs, IAS_Manager.cs, CrossPlatformManager.cs, PickleCore.cs, UnityMainThreadDispatcher.cs to the GameObject
3. (Optional) Attach ClickLockManager.cs to the GameObject
4. (Optional) Attach TenjinManager.cs to the GameObject
5. Configure all inspector values as required

---

# Pickle Core functions

PickleCore.cs is the wrapper for C# interacting with our Java plugin on Android or iOS C++ plugin.

## Vibrations

### `PickleCore.DoHapticFeedback(float strength, bool overrideSystemSettings)`

Triggers a light haptic vibration.

> [!IMPORTANT]
> Requires Android manifest permission `<uses-permission-sdk-23 android:name="android.permission.VIBRATE" />` if you want to ensure a haptic is ALWAYS delivered regardless of system settings on Android API 33+

| Parameter | Type | Description |
|------------|------|-------------|
| `strength` | `float` | Vibration intensity between **0.0f** and **1.0f** this value is clamped and converted to one of 4 predefined system haptic strength values: <br><=0.25 CLOCK_TICK<br><=0.5 KEYBOARD_TAP<br><=0.75 VIRTUAL_KEY<br><=1.0 LONG_PRESS <br><br>**(Note: For precise control use [PickleCore.DoVibration(...)](#picklecoredovibrationlong-milliseconds-float-strength) instead)** |
| `overrideSystemSettings` | `bool` | When `true` vibration will trigger even if system-level button press haptics are disabled.<br><br>**(Note: API 33+ removed system haptic settings overriding, so this will instead replicate a haptic vibration via the regular Vibration API if the app has Vibration permission and the device is using API 33+)** |

### `PickleCore.DoVibration(long milliseconds, float strength)`

Triggers a vibration with more control over strength and duration.

> [!IMPORTANT]
> Requires Android manifest permission `<uses-permission android:name="android.permission.VIBRATE" />` or vibrations will not do anything.

| Parameter | Type | Description |
|------------|------|-------------|
| `milliseconds` | `long` | How many miliseconds the device will vibrate for, must be positive. |
| `strength` | `float` | Vibration intensity between **0.0f** and **1.0f** this value is clamped. <br><br>On iOS vibration strength is split into 5 predefined strengths: <br>>=0.8 heavy<br>>=0.6 medium<br>>= 0.4 rigid<br>>=0.2 soft<br>>=0 light |

### `PickleCore.DoControllerVibration(bool useLowFrequencyMotor long milliseconds, float strength)`

Triggers a vibration on all connected controllers. 

> [!NOTE]
> Only available on devices running Android API 31+ as controller support on Android was very limited before this.

> [!IMPORTANT]
> Requires Android manifest permission `<uses-permission android:name="android.permission.VIBRATE" />` or vibrations will not do anything.

> [!CAUTION]
> We currently do not support this functionality on iOS

> [!CAUTION]
> We do not support separate controller targetting, if this is a wanted feature please request it via an issue report

| Parameter | Type | Description |
|------------|------|-------------|
| `useLowFrequencyMotor` | `long` | Most controllers have 2 vibration motors, the low frequency motor is used for deeper more intense vibrations and the high frequency motor is used for subtle taps and haptic effects. |
| `milliseconds` | `long` | How many miliseconds the controllers will vibrate for, must be positive. |
| `strength` | `float` | Vibration intensity between **0.0f** and **1.0f** this value is clamped. |

### `PickleCore.StopVibration()`

Stops any active device vibrations.

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.StopControllerVibration()`

Stops any active vibration on all controllers, this stops both the low and high frequency motors.

> [!CAUTION]
> We currently do not support this functionality on iOS

## App Info

### `PickleCore.GetAppInstallTimestamp()`

Returns the local time as a unix timestamp long at the time of app install.

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.GetPackageList(string searchString)`

Returns a comma separated list of Android packages installed on the device containing the search string.

> [!CAUTION]
> Depreciated! No longer supported on Android 11+ due to new security policies, will just return blank

> [!CAUTION]
> We currently do not support this functionality on iOS

| Parameter | Type | Description |
|------------|------|-------------|
| `searchString` | `string` | String to be searched for in packages names such as `com.pickle.` will only return packages which contain this term |

### `PickleCore.DoesAppContainBadPermissions()`

Returns a bool on whether the app was detected to contain some dangerous permissions which we see modded APKs usually add to package adware/malware with their downloads.

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.AppRunId()`

A secondary method for checking if modded APKs packaged any bad permissions into the app whilst not directly looking like a direct permission checking function which can be blanket changed to return false.

- 58382 (14595) = DoesAppContainBadPermissions returned true so it wasn't patched out
- 48276 (12069) = DoesAppContainBadPermissions returned false and we encountered an error
- 30274 (7568) = DoesAppContainBadPermissions returned false and rechecking returned false again, seems good
- 28494 (7123) = DoesAppContainBadPermissions returned false but rechecking we found it actually to be true!!

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.GetDeveloperDeviceId()`

Returns the iOS IDFV value (unique device id across apps by the same developer)

> [!CAUTION]
> We currently do not support this functionality on Android

## System Info

### `PickleCore.GetMillisecondsSinceBoot()`

Returns how many milliseconds have passed since last device bootup as a long, useful for checking if user has changed device date/time where network is unavailable.

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.GetAndroidAPILevel()`

Returns Android API level as an int

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.DeviceHasNotch()`

Return true if the device was detected to have a notch cutout on the screen. This check is more advanced than built in Unity checks as we also support a range of manufacturer specific identifiers before the notch cutout got an android universal flag.

> [!CAUTION]
> We currently do not support this functionality on iOS

### `PickleCore.GetScreenSafeArea(bool avoidNavigationBar)`

Returns the screen safe area as a Rect. This check is more advanced than built in Unity checks as we also support a range of manufacturer specific safe areas before the notch cutout got an android universal flag, including some manufacturers which hard coded the safe area to specific device ids as marked in their documentation.

> [!NOTE]
> This will just return Unity's standard Screen.safeArea on iOS which is safe to use across all iOS devices

| Parameter | Type | Description |
|------------|------|-------------|
| `avoidNavigationBar` | `string` | When true the safe area will be pushed up above the permanent transparent software navigation bar if the device has one (this does not include the temporary navigation buttons which you can make appear by swiping) |

### `PickleCore.GetScreenWidth()`

Unity Android used to have a bug where querying Screen.width on Awake would return an incorrect value, this function was just a workaround for that.

> [!NOTE]
> This will just return Unity's standard Screen.width on iOS which is safe to use across all iOS devices

### `PickleCore.GetScreenHeight()`

Unity Android used to have a bug where querying Screen.height on Awake would return an incorrect value, this function was just a workaround for that.

> [!NOTE]
> This will just return Unity's standard Screen.height on iOS which is safe to use across all iOS devices

### `PickleCore.OpenSettingsApp()`

Opens the app settings page on the device for the current app. Useful if the player rejected some permissions but now decided they want to enable the functionality and the app is no longer allowed to prompt permission again, the only remaining option is for them to allow it via the app settings.

### `PickleCore.GetDensity()`

Get the screen dpi via Java's `Context.getResources().getDisplayMetrics().densityDpi;` which was more reliable in older versions of Unity and old devices.

> [!NOTE]
> This will just return Unity's standard Screen.dpi on iOS which is safe to use across all iOS devices

### `PickleCore.GetXDPI()`

Some android devices have very slightly different X and Y DPIs, it's usually not important but this function just exposes them in case they're needed. (On most devices XDPI, YDPI and DPI will be identical)

> [!NOTE]
> This will just return Unity's standard Screen.dpi on iOS which is safe to use across all iOS devices

### `PickleCore.GetYDPI()`

Some android devices have very slightly different X and Y DPIs, it's usually not important but this function just exposes them in case they're needed. (On most devices XDPI, YDPI and DPI will be identical)

> [!NOTE]
> This will just return Unity's standard Screen.dpi on iOS which is safe to use across all iOS devices

### `PickleCore.GetUsedMemory()`

> [!WARNING]
> Android memory allocation may not work like you expect! Total memory is the total amount the system has currently allocated to the JVM not the total memory available in the whole device and may be resized by the system based on what it thinks the app needs and depending on other running apps.

Returns the total used memory from the total memory current allocated to the JVM as a long.

> [!CAUTION]
> We currently do not support this functionality on iOS, it will return -1 and log an error

### `PickleCore.GetTotalMemory()`

> [!WARNING]
> Android memory allocation may not work like you expect! Total memory is the total amount the system has currently allocated to the JVM not the total memory available in the whole device and may be resized by the system based on what it thinks the app needs and depending on other running apps.

Returns the total used memory current allocated to the JVM as a long.

> [!CAUTION]
> We currently do not support this functionality on iOS, it will return -1 and log an error

### `PickleCore.GetMaxMemory()`

> [!WARNING]
> Android memory allocation may not work like you expect! Total memory is the total amount the system has currently allocated to the JVM not the total memory available in the whole device and may be resized by the system based on what it thinks the app needs and depending on other running apps.

Returns the maximum memory the system can currently allocate to the JVM as a long.

> [!CAUTION]
> We currently do not support this functionality on iOS, it will return -1 and log an error

### `PickleCore.GetFreeMemory()`

> [!WARNING]
> Android memory allocation may not work like you expect! Total memory is the total amount the system has currently allocated to the JVM not the total memory available in the whole device and may be resized by the system based on what it thinks the app needs and depending on other running apps.

Returns the total free memory from the total memory current allocated to the JVM as a long.

> [!CAUTION]
> We currently do not support this functionality on iOS, it will return -1 and log an error

### `PickleCore.IsAndroidTV()`

Returns true if the user device is an Android TV or Android Auto platform.

> [!CAUTION]
> We currently do not support this functionality on iOS, it will always return false

## Toasts

### `PickleCore.DisplayToastMessage(string message, bool longShowTime)`

Displays a small system-style popover message bottom middle of the screen.

| Parameter | Type | Description |
|------------|------|-------------|
| `message` | `string` | Message in the popover message, keep this short as different android versions trim at differen lengths set by the system |
| `longShowTime` | `bool` | Android toasts support showing for either 3.5 seconds or 2.0 seconds, when true it uses the longer show time (this same functionality is replicated for iOS) |

> [!NOTE]
> Toasts are usually not supported on iOS, however the plugin will replicate the behaviour of toasts on both platforms

### `PickleCore.CancelToastMessage()`

Force hide any active toast messages.

## Notifications

### `PickleCore.CreateNotificationGroup(string groupId, string groupName)`

Creates a notification group which is the container for different notification channels.

> [!NOTE]
> For example "promo" which would have channels of different offer types or a "gameplay" with channels such as direct messages, timed rewards etc

### `PickleCore.CreateNotificationChannel(string channelId, string groupId, string channelName, string channelDescription, bool showOnStatusBar, bool playSound, bool showHeadsUp)`

Creates a notification channel inside of a notification group which notifications can be scheduled inside.

> [!CAUTION]
> The notification group must be setup before creating a channel within it

> [!NOTE]
> For example inside the "promo" group you could have channels such as "offers", "daily rewards", "seasonal discounts"

### `PickleCore.SendNotification(int notificationId, string channelId, string messageTitle, string messageBody, int sendAfterSeconds, string smallIconName, string largeIconName, bool dismissAfterTapped)`

Schedules a notification to be sent.

> [!CAUTION]
> The notification channel must be setup before sending a notification to it

### `PickleCore.CancelNotification(int notificationId)`

Cancels the previously scheduled notification matching the notification id.

### `PickleCore.GetLaunchIntentExtras()`

Returns a string containing information about the current app launch allowing you to know if the app was launched via a notification and if any special parameters were attached to the notification such as where in the app to jump to or any rewards for clicking the notification.


### Example using the notification system

An example notification script from Helicopter Rescue - https://pastebin.com/PH3X9K7S

**This is how the new android notification system works:**
1. Create a notification group (you can re-call this as much as you want if it already exists then np, but note that clearing data removes notification groups) 
2. Create a notification channel referencing the groupid string you made for your group (you can re-call as much as you want etc etc)
3. SendNotification(...) with how many seconds etc, this will schedule the notification via the android scheduler to call onReceieve inside the plugin with the data we set to be sent when the alarm triggers, in this onReceieve function is where the visible notification is displayed to the user)

It's a good idea to make a group for each TYPE of notifications, e.g Promotions, Content Unlocked, Daily Reward notifications (the player can manually block notifications from each group)


## Firebase Analytics - PickleEventCategory Documentation

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
