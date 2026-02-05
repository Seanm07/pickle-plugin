using System;
using System.ComponentModel;
using UnityEngine;
using Firebase.Analytics;
using Firebase.Crashlytics;
#if IVMETRICS_EVENTS_ENABLED
    using IVMetrics;
#endif

#if UNITY_EDITOR
    using System.Text.RegularExpressions;
#endif

// Using our own class to store these default events so devices which don't have Google Play Services don't thrown an exception when looking them up
public class FirebaseDefaultEvents {
    public const string ADD_PAYMENT_INFO = "add_payment_info";
    public const string ADD_SHIPPING_INFO = "add_shipping_info";
    public const string ADD_TO_CART = "add_to_cart";
    public const string ADD_TO_WISHLIST = "add_to_wishlist";
    public const string AD_IMPRESSION = "ad_impression";
    public const string APP_OPEN = "app_open";
    public const string BEGIN_CHECKOUT = "begin_checkout";
    public const string CAMPAIGN_DETAILS = "campaign_details";
    public const string EARN_VIRTUAL_CURRENCY = "earn_virtual_currency";
    public const string GENERATE_LEAD = "generate_lead";
    public const string JOIN_GROUP = "join_group";
    public const string LEVEL_END = "level_end";
    public const string LEVEL_START = "level_start";
    public const string LEVEL_UP = "level_up";
    public const string LOGIN = "login";
    public const string POST_SCORE = "post_score";
    public const string PURCHASE = "purchase";
    public const string REFUND = "refund";
    public const string REMOVE_FROM_CART = "remove_from_cart";
    public const string SCREEN_VIEW = "screen_view";
    public const string SEARCH = "search";
    public const string SELECT_CONTENT = "select_content";
    public const string SELECT_PROMOTION = "select_promotion";
    public const string SHARE = "share";
    public const string SIGN_UP = "sign_up";
    public const string SPEND_VIRTUAL_CURRENCY = "spend_virtual_currency";
    public const string TUTORIAL_BEGIN = "tutorial_begin";
    public const string TUTORIAL_COMPLETE = "tutorial_complete";
    public const string UNLOCK_ACHIEVEMENT = "unlock_achievement";
    public const string VIEW_CART = "view_cart";
    public const string VIEW_ITEM = "view_item";
    public const string VIEW_ITEM_LIST = "view_item_list";
    public const string VIEW_PROMOTION = "view_promotion";
    public const string VIEW_SEARCH_RESULTS = "view_search_results";
}

// This class is setup like this so we force developers to use categories rather than allowing any string
public class PickleEventCategory {
    private string _category;

    private PickleEventCategory(string category) {
        _category = category;
    }

    public string ToString() {
        return _category;
    }
    
    internal static class AnalyticsKeys {
        public const string APP_STORE = "app_store";
        public const string ADMOB_TTL = "admob_ttl";
        public const string ADMOB_ERROR = "admob_error";
        public const string IAS_CLICK = "ias_click";
        public const string IAP_ERROR = "iap_error";
        public const string IAP_PURCHASE_COMPLETE = "iap_purchase_complete";
        public const string IAP_PURCHASE_FAILED = "iap_purchase_failed";
        public const string IAP_RESTORE_COMPLETE = "iap_restore_complete";
        public const string IAP_RESTORE_FAILED = "iap_restore_failed";
        public const string LOW_MEMORY_WARNING = "low_memory_warning";
        public const string PERSONALISATION_AUTH_FLOW = "personalisation_auth_flow";
        
        public const string MESSAGE_PROMPTS = "message_prompts";
        public const string ITEM_PURCHASED = "item_purchased";
        public const string ITEM_EQUIPPED = "item_equipped";
        public const string ITEM_COLLECTED = "item_collected";
        public const string MODE_SELECTED = "mode_selected";
        public const string MISSION_PROMPTED = "mission_prompted";
        public const string MISSION_PROMPT_ACCEPTED = "mission_prompt_accepted";
        public const string MISSION_PROMPT_DECLINED = "mission_prompt_declined";
        public const string MISSION_START = "mission_start";
        public const string MISSION_COMPLETE = "mission_complete";
        public const string MISSION_FAILED = "mission_failed";
        public const string MISSION_FAILED_REASON = "mission_failed_reason";
        public const string BUTTON_IMPRESSION = "button_impression";
        public const string BUTTON_CLICK = "button_click";
        public const string LEVEL_UP = "level_up";
        public const string REFERRAL_SHARE = "referral_share";
    }

    [Description("Categories used by internal pickle scripts, you should not be logging to these categories manually!")]
    internal static class PickleScripts {
        [Description("Track the source store which the app was installed from")]
        public static readonly PickleEventCategory APP_STORE = new("app_store");

        [Description("Tracking of how long different admob components take to initialize")]
        public static readonly PickleEventCategory ADMOB_TTL = new("admob_ttl");
        
        [Description("Admob related errors")]
        public static readonly PickleEventCategory ADMOB_ERROR = new("admob_error");

        [Description("Ad clicks on our internal ad system")]
        public static readonly PickleEventCategory IAS_CLICK = new("ias_click");

        [Description("Errors with the in-app purchasing system")]
        public static readonly PickleEventCategory IAP_ERROR = new("iap_error");
        
        [Description("In-app purchases which were successfuly and triggered the success callback")]
        public static readonly PickleEventCategory IAP_PURCHASE_COMPLETE = new("iap_purchase_complete");

        [Description("In-app purchases which failed")]
        public static readonly PickleEventCategory IAP_PURCHASE_FAILED = new("iap_purchase_failed");

        [Description("In-app purchase restore actions which were successful")]
        public static readonly PickleEventCategory IAP_RESTORE_COMPLETE = new("iap_restore_complete");

        [Description("In-app purchase restore actions which failed")]
        public static readonly PickleEventCategory IAP_RESTORE_FAILED = new("iap_restore_failed");

        [Description("Track when the device reaches critically low memory")]
        public static readonly PickleEventCategory LOW_MEMORY_WARNING = new("low_memory_warning");

        [Description("Events logged relating to the personalisation and CMP flow")]
        public static readonly PickleEventCategory PERSONALISATION_AUTH_FLOW = new("personalisation_auth_flow");
    }

    [Description("Popup messages or dialogs which inform or ask the player a question")]
    public static readonly PickleEventCategory MESSAGE_PROMPTS = new("message_prompts");

    [Description("Item purchased with ingame currency or other method")]
    public static readonly PickleEventCategory ITEM_PURCHASED = new("item_purchased");

    [Description("Item equipped e.g weapons, vehicles, characters")]
    public static readonly PickleEventCategory ITEM_EQUIPPED = new("item_equipped");

    [Description("Item collected such as hidden packages or collectables")]
    public static readonly PickleEventCategory ITEM_COLLECTED = new("item_collected");

    [Description("Mode or map selected by the player")]
    public static readonly PickleEventCategory MODE_SELECTED = new("mode_selected");

    [Description("Mission prompted to player")]
    public static readonly PickleEventCategory MISSION_PROMPTED = new("mission_prompted");

    [Description("Mission prompt accepted by player")]
    public static readonly PickleEventCategory MISSION_PROMPT_ACCEPTED = new("mission_prompt_accepted");

    [Description("Mission prompt declined by player")]
    public static readonly PickleEventCategory MISSION_PROMPT_DECLINED = new("mission_prompt_declined");

    [Description("Mission entered/started")]
    public static readonly PickleEventCategory MISSION_START = new("mission_start");

    [Description("Mission completed successfully")]
    public static readonly PickleEventCategory MISSION_COMPLETE = new("mission_complete");

    [Description("Mission failed")]
    public static readonly PickleEventCategory MISSION_FAILED = new("mission_failed");

    [Description("Reason for mission failure")]
    public static readonly PickleEventCategory MISSION_FAILED_REASON = new("mission_failed_reason");

    [Description("Tracking of impressions on buttons")]
    public static readonly PickleEventCategory BUTTON_IMPRESSION = new("button_impression");

    [Description("Tracking of clicks on buttons")]
    public static readonly PickleEventCategory BUTTON_CLICK = new("button_click");

    [Description("Player levelled up, track the current level when levelling up in games with xp")]
    public static readonly PickleEventCategory LEVEL_UP = new("level_up");

    [Description("Referral and app sharing events")]
    public static readonly PickleEventCategory REFERRAL_SHARE = new("referral_share");
}

public class FirebaseAnalyticsManager : MonoBehaviour {
    void Awake() {
        // Assign the callback reference
        FirebaseManager.OnFirebaseInitialised += FirebaseInitialised;
        
        Application.lowMemory += OnLowMemory;
    }

    private void OnLowMemory() {
        // Log an event so we can see low memory warnings in user event flow
        LogEvent(PickleEventCategory.PickleScripts.LOW_MEMORY_WARNING.ToString());
    }

    private static bool IsFirebaseInitialised(Action action) {
        bool isInitialised = FirebaseManager.IsInitialised();

        if (!isInitialised)
            FirebaseManager.AddToInitialiseQueue(action);

        return isInitialised;
    }

    // Called when firebase has successfully been initialised and analytics is ready for use
    private void FirebaseInitialised() {
        FirebaseManager.OnFirebaseInitialised -= FirebaseInitialised;

        SetUserId(FirebaseManager.instance.persistantUserId);

        LogEvent(FirebaseDefaultEvents.APP_OPEN);
    }

    [Description("Change the users analytics id to track their lifetime events, actions which link a user to past actions such as logging into an account should restore their original userid")]
    public static void ChangeUserId(string userId) {
        FirebaseManager.instance.persistantUserId = userId;
        PlayerPrefs.SetString("firebase_user_id", userId);

        FirebaseManager.instance.analyticsManager.SetUserId(userId);
    }

    private void SetUserId(string userId) {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetUserId(userId)))
            return;

        FirebaseAnalytics.SetUserId(userId);
        Crashlytics.SetUserId(userId);
    }

    // Note: This is enabled by default
    public static void SetAnalyticsEnabled(bool wantEnabled) {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetAnalyticsEnabled(wantEnabled)))
            return;

        FirebaseAnalytics.SetAnalyticsCollectionEnabled(wantEnabled);
    }

    // Note: This is enabled by default
    public static void SetCrashlyticsEnabled(bool wantEnabled) {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetCrashlyticsEnabled(wantEnabled)))
            return;

        Crashlytics.IsCrashlyticsCollectionEnabled = wantEnabled;
    }

    private static string EditorWarnNumberUsage(string eventValue) {
        #if UNITY_EDITOR
            // Events with dimensions are limited, developers should not be logging floats or a wide range of strings as a string eventValue
            Regex numberUsageRegex = new Regex(@"\d+\.\d+|\d{4,}"); // Check if the string contains a decimal or int 4 digits or longer

            if (numberUsageRegex.IsMatch(eventValue)) {
                Debug.LogError("String event values must not contain floats or large integer ranges! Use the float or int parameter instead of string!");
                Debug.Break(); // If you are hitting this you need to fix your LogEvent call ask Sean for help on discord - do not edit this script
            }
        #endif

        return eventValue;
    }
    
    public static void LogEvent(PickleEventCategory eventCategory) {
        LogEvent(eventCategory.ToString());
    }
    
    public static void LogEvent(PickleEventCategory eventCategory, string eventValue) {
        LogEvent(eventCategory.ToString(), "type", EditorWarnNumberUsage(eventValue));
    }
    
    public static void LogEvent(PickleEventCategory eventCategory, float eventValue) {
        LogEvent(eventCategory.ToString(), "value", eventValue);
    }
    
    public static void LogEvent(PickleEventCategory eventCategory, int eventValue) {
        LogEvent(eventCategory.ToString(), "value", eventValue);
    }

    /// <summary>Only use custom categories in rare cases where PickleEventCategory does not fit your purpose!</summary>
    [Description("Only use custom event categories in rare cases where a PickleEventCategory does not fit your purpose!")]
    public static void LogCustomEvent(string eventCategory) {
        LogEvent(eventCategory);
    }
    
    /// <summary>Only use custom categories in rare cases where PickleEventCategory does not fit your purpose!</summary>
    [Description("Only use custom event categories in rare cases where a PickleEventCategory does not fit your purpose!")]
    public static void LogCustomEvent(string eventCategory, string eventValue) {
        LogEvent(eventCategory, "type", EditorWarnNumberUsage(eventValue));
    }
    
    /// <summary>Only use custom categories in rare cases where PickleEventCategory does not fit your purpose!</summary>
    [Description("Only use custom event categories in rare cases where a PickleEventCategory does not fit your purpose!")]
    public static void LogCustomEvent(string eventCategory, float eventValue) {
        LogEvent(eventCategory, "value", eventValue);
    }
    
    /// <summary>Only use custom categories in rare cases where PickleEventCategory does not fit your purpose!</summary>
    [Description("Only use custom event categories in rare cases where a PickleEventCategory does not fit your purpose!")]
    public static void LogCustomEvent(string eventCategory, int eventValue) {
        LogEvent(eventCategory, "value", eventValue);
    }
    
    /// <summary>Custom event dimensions must be manually setup on the firebase backend for any data to be logged!</summary>
    [Description("Custom event dimensions must be manually setup on the firebase backend for any data to be logged!")]
    public static void LogCustomEvent(string eventCategory, string eventDimension, string eventValue) {
        LogEvent(eventCategory, eventDimension, EditorWarnNumberUsage(eventValue));
    }
    
    /// <summary>Custom event metrics must be manually setup on the firebase backend for any data to be logged!</summary>
    [Description("Custom event dimensions must be manually setup on the firebase backend for any data to be logged!")]
    public static void LogCustomEvent(string eventCategory, string eventMetric, float eventValue) {
        LogEvent(eventCategory, eventMetric, eventValue);
    }
    
    /// <summary>Custom event metrics must be manually setup on the firebase backend for any data to be logged!</summary>
    [Description("Custom event dimensions must be manually setup on the firebase backend for any data to be logged!")]
    public static void LogCustomEvent(string eventCategory, string eventMetric, int eventValue) {
        LogEvent(eventCategory, eventMetric, eventValue);
    }

    public static void LogScreen(string screenName) {
        if (!IsFirebaseInitialised(() => LogScreen(screenName)))
            return;
        
        LogEvent(FirebaseDefaultEvents.SCREEN_VIEW, FirebaseAnalytics.ParameterScreenName, screenName);
    }

    public static void LogError(string errorMessage) {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogError(errorMessage)))
            return;

        Crashlytics.Log(errorMessage);
    }

    public static void LogException(Exception exceptionMessage) {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogException(exceptionMessage)))
            return;

        Crashlytics.LogException(exceptionMessage);
    }
    
    public static void SetUserProperty(string propertyName, string propertyValue) {
        if (String.IsNullOrEmpty(propertyName) || String.IsNullOrEmpty(propertyValue))
            return;

        // Queue this function call until firebase is initialized
        if (!IsFirebaseInitialised(() => SetUserProperty(propertyName, propertyValue)))
            return;

        FixFormatting(ref propertyName, FormatType.EventName);
        FixFormatting(ref propertyValue, FormatType.EventParameterName);

        if (!IsReserved(propertyName)) {
            FirebaseAnalytics.SetUserProperty(propertyName, propertyValue);
            Crashlytics.SetCustomKey(propertyName, propertyValue);
        }
    }
    
    public static void SetUserProperty(string propertyName, double propertyValue) {
        SetUserProperty(propertyName, propertyValue.ToString());
    }
    
    public static void SetUserProperty(string propertyName, long propertyValue) {
        SetUserProperty(propertyName, propertyValue.ToString());
    }

    private static void LogEvent(string eventCategory, string eventDimension, string eventValue) {
        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventDimension) || String.IsNullOrEmpty(eventValue))
            return;

        // Queue this function call until firebase is initialized
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventDimension, eventValue)))
            return;

        FixFormatting(ref eventCategory, FormatType.EventName);
        FixFormatting(ref eventDimension, FormatType.EventParameterName);
        FixFormatting(ref eventValue, FormatType.EventParameterValue);

#if IVMETRICS_EVENTS_ENABLED
        switch (eventCategory) {
            case FirebaseDefaultEvents.SCREEN_VIEW:
                IVMetricsManager.LogEvent(eventCategory, IVParameterName.ScreenName, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.APP_STORE: // Log which app store this user originally installed from
                IVMetricsManager.LogEvent("app_store_" + eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.IAS_CLICK: // Clicked an IAS advert
                IVMetricsManager.LogEvent(eventCategory);
                break;
            
            case PickleEventCategory.AnalyticsKeys.IAP_ERROR: // IAP system failed to initialise
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.IAP_PURCHASE_COMPLETE: // Made a successful IAP purchase
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.IAP_PURCHASE_FAILED: // IAP purchase failed
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.ITEM_PURCHASED: // Made an ingame purchase with game currency
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.ITEM_EQUIPPED: // Selected/equipped an ingame item
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.ITEM_COLLECTED: // Collected a collectable item
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MODE_SELECTED: // Selected a game mode to play
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MISSION_PROMPTED: // Mission prompt appears
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MISSION_PROMPT_ACCEPTED: // Mission prompt accepted
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MISSION_PROMPT_DECLINED: // Mission prompt declined
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MISSION_START: // Entered a mission
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MISSION_COMPLETE: // Completed a mission
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MISSION_FAILED: // Failed a mission
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.LEVEL_UP: // Levelled up
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.REFERRAL_SHARE: // Referral actions
                IVMetricsManager.LogEvent(eventCategory, eventValue);
                break;
            
            case PickleEventCategory.AnalyticsKeys.MESSAGE_PROMPTS: // Message prompts such as rating prompts
                if(eventValue.Contains("rating_first_rating") || eventValue.Contains("rating_second_rating"))
                    IVMetricsManager.LogEvent(eventValue);
                break;
        }
#endif
        
        if (!IsReserved(eventCategory))
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventDimension, eventValue));
    }

    private static void LogEvent(string eventCategory, string eventDimension, double eventValue) {
        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventDimension))
            return;

        // Queue this function call until firebase is initialized
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventDimension, eventValue)))
            return;

        FixFormatting(ref eventCategory, FormatType.EventName);
        FixFormatting(ref eventDimension, FormatType.EventParameterName);

        if (!IsReserved(eventCategory))
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventDimension, eventValue));
    }

    private static void LogEvent(string eventCategory, string eventDimension, long eventValue) {
        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventDimension))
            return;

        // Queue this function call until firebase is initialized
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventDimension, eventValue)))
            return;

        FixFormatting(ref eventCategory, FormatType.EventName);
        FixFormatting(ref eventDimension, FormatType.EventParameterName);

        if (!IsReserved(eventCategory))
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventDimension, eventValue));
    }

    private static void LogEvent(string eventCategory) {
        if (String.IsNullOrEmpty(eventCategory))
            return;

        // Queue this function call until firebase is initialized
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory)))
            return;

        FixFormatting(ref eventCategory, FormatType.EventName);

        if (!IsReserved(eventCategory))
            FirebaseAnalytics.LogEvent(eventCategory);
    }

    private static bool IsReserved(string inPropertyName) {
        // Only check reserved strings in debug mode or the editor as string comparison isn't cheap
        // and if the developer is using a reserved keyword in a final build then it's too late to remove it anyway
        if (Application.isEditor || FirebaseManager.instance.debugMode) {
            // Reversed properties are event names which are not allowed to be used as they're reserved for internal use
            string[] reservedPropertyNames = {
                "app_clear_data",
                "app_uninstall",
                "app_update",
                "error",
                "first_open",
                "first_visit",
                "in_app_purchase",
                "notification_dismiss",
                "notification_foreground",
                "notification_open",
                "notification_receive",
                "os_update",
                "session_start",
                "user_engagement"
            };

            foreach (string propertyName in reservedPropertyNames) {
                if (inPropertyName == propertyName) {
                    Debug.LogError(propertyName + " is reserved for internal use and cannot be used as an event name!");

                    return true;
                }
            }
        }

        return false;
    }

    private enum FormatType {
        EventName, EventParameterName, EventParameterValue, UserPropertyName, UserPropertyValue
    }

    private static void FixFormatting(ref string input, FormatType type) {
        int charLimit = GetCharacterLimit(type);

        // Firebase only allows alphanumeric characters and underscores in names
        string output = input.Replace(' ', '_').ToLowerInvariant();
        output = output.Replace('(', '_');
        output = output.Replace(')', '_');

        // If the type is not a value replace the invalid character . with _
        if (type != FormatType.EventParameterValue && type != FormatType.UserPropertyValue)
            output = output.Replace('.', '_');

        if (output.Length > charLimit)
            output = output.Substring(0, charLimit);

        input = output;
    }

    private static int GetCharacterLimit(FormatType type) {
        switch (type) {
            case FormatType.EventName: return 40;
            case FormatType.EventParameterName: return 40;
            case FormatType.EventParameterValue: return 100;
            case FormatType.UserPropertyName: return 24;
            case FormatType.UserPropertyValue: return 36;
            default: return 24; // Default character limit
        }
    }
}