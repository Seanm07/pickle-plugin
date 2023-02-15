using System;
using Firebase;
using UnityEngine;
using Firebase.Analytics;
using Firebase.Crashlytics;

public class FirebaseAnalyticsManager : MonoBehaviour
{
    void Awake()
    {
        // Assign the callback reference
        FirebaseManager.OnFirebaseInitialised += FirebaseInitialised;
    }

    private static bool IsFirebaseInitialised(Action action) {
        bool isInitialised = FirebaseManager.IsInitialised();
        
        if (!isInitialised)
            FirebaseManager.instance.AddToInitialiseQueue(action);
        
        return isInitialised;
    }
    
    // Called when firebase has successfully been initialised and analytics is ready for use
    private void FirebaseInitialised()
    {
        FirebaseManager.OnFirebaseInitialised -= FirebaseInitialised;
        
        SetUserId(FirebaseManager.instance.persistantUserId);
        
        LogEvent(FirebaseAnalytics.EventAppOpen);
    }
    
    private void SetUserId(string userId)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetUserId(userId))) return;
        
        if (!IsReserved(userId))
        {
            FirebaseAnalytics.SetUserId(userId);
            Crashlytics.SetUserId(userId);
        }
    }

    // Note: This is enabled by default
    public static void SetAnalyticsEnabled(bool wantEnabled)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetAnalyticsEnabled(wantEnabled))) return;
        
        FirebaseAnalytics.SetAnalyticsCollectionEnabled(wantEnabled);
    }

    // Note: This is enabled by default
    public static void SetCrashlyticsEnabled(bool wantEnabled)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetCrashlyticsEnabled(wantEnabled))) return;
        
        Crashlytics.IsCrashlyticsCollectionEnabled = wantEnabled;
    }

    public static void LogEvent(string eventName)
    {
        eventName = FixFormatting(eventName, FormatType.EventName);

        if (String.IsNullOrEmpty(eventName)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventName))) return;
        
        if (!IsReserved(eventName))
        {
            FirebaseAnalytics.LogEvent(eventName);
        }
    }

    [Obsolete("Not supported by firebase! Requires an event category, event name within category and value within name!")]
    public static void LogEvent(string eventCategory, string eventValue)
    {
        Debug.LogError("Obsolete! Firebase event logging requires an event category, an event name within category and a value within the event name!");
        /*
        eventCategory = FixFormatting(eventCategory, FormatType.EventName);
        eventValue = FixFormatting(eventValue, FormatType.EventParameterName);

        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventValue)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventValue))) return;
        
        if (!IsReserved(eventCategory))
        {
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(FirebaseAnalytics.ParameterValue, eventValue));
        }*/
    }

    public static void LogEvent(string eventCategory, Parameter[] eventParameters) {
        eventCategory = FixFormatting(eventCategory, FormatType.EventName);
        
        if (String.IsNullOrEmpty(eventCategory)) return;
        
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventParameters))) return;
        
        if (!IsReserved(eventCategory))
        {
            FirebaseAnalytics.LogEvent(eventCategory, eventParameters);
        }
    }

    public static void LogEvent(string eventCategory, string eventLabel, string eventValue)
    {
        eventCategory = FixFormatting(eventCategory, FormatType.EventName);
        eventLabel = FixFormatting(eventLabel, FormatType.EventParameterName);
        eventValue = FixFormatting(eventValue, FormatType.EventParameterValue);

        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventLabel) || String.IsNullOrEmpty(eventValue)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventLabel, eventValue))) return;
        
        if (!IsReserved(eventCategory))
        {
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventLabel, eventValue));
        }
    }

    public static void LogEvent(string eventCategory, string eventLabel, int eventValue)
    {
        eventCategory = FixFormatting(eventCategory, FormatType.EventName);
        eventLabel = FixFormatting(eventLabel, FormatType.EventParameterName);

        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventLabel)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventLabel, eventValue))) return;
        
        if (!IsReserved(eventCategory))
        {
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventLabel, eventValue));
        }
    }

    public static void LogEvent(string eventCategory, string eventLabel, float eventValue)
    {
        eventCategory = FixFormatting(eventCategory, FormatType.EventName);
        eventLabel = FixFormatting(eventLabel, FormatType.EventParameterName);

        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventLabel)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventLabel, eventValue))) return;
        
        if (!IsReserved(eventCategory))
        {
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventLabel, eventValue));
        }
    }

    public static void LogEvent(string eventCategory, string eventLabel, long eventValue)
    {
        eventCategory = FixFormatting(eventCategory, FormatType.EventName);
        eventLabel = FixFormatting(eventLabel, FormatType.EventParameterName);

        if (String.IsNullOrEmpty(eventCategory) || String.IsNullOrEmpty(eventLabel)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogEvent(eventCategory, eventLabel, eventValue))) return;
        
        if (!IsReserved(eventCategory))
        {
            FirebaseAnalytics.LogEvent(eventCategory, new Parameter(eventLabel, eventValue));
        }
    }

    public static void LogScreen(string screenName) {
        screenName = FixFormatting(screenName, FormatType.EventName);
        
        if (String.IsNullOrEmpty(screenName)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogScreen(screenName))) return;
        
        FirebaseAnalytics.LogEvent(FirebaseAnalytics.EventScreenView, new Parameter(FirebaseAnalytics.ParameterScreenName, screenName));
    }

    public static void LogError(string errorMessage)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogError(errorMessage))) return;
        
        Crashlytics.Log(errorMessage);
    }

    public static void LogException(Exception exceptionMessage)
    {
        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => LogException(exceptionMessage))) return;
        
        Crashlytics.LogException(exceptionMessage);
    }

    public static void SetUserProperty(string propertyName, string propertyValue) {
        propertyName = FixFormatting(propertyName, FormatType.UserPropertyName);
        propertyValue = FixFormatting(propertyValue, FormatType.UserPropertyValue);

        if (String.IsNullOrEmpty(propertyName) || String.IsNullOrEmpty(propertyValue)) return;

        // If firebase isn't initialised yet then this function call is queued and re-called when it is
        if (!IsFirebaseInitialised(() => SetUserProperty(propertyName, propertyValue))) return;
        
        if (!IsReserved(propertyName))
        {
            FirebaseAnalytics.SetUserProperty(propertyName, propertyValue);
            Crashlytics.SetCustomKey(propertyName, propertyValue);
        }
    }

    private static bool IsReserved(string inPropertyName)
    {
        // Only check reversed strings in debug mode or the editor as string comparison isn't cheap
        // and if the developer is using a reserved keyword in a final build then it's too late to remove it anyway
        if (Application.isEditor || FirebaseManager.instance.debugMode)
        {
            // Reversed properties are event names which are not allowed to be used as they're reserved for internal use
            string[] reservedPropertyNames =
            {
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

            foreach (string propertyName in reservedPropertyNames)
            {
                if (inPropertyName == propertyName)
                {
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
    
    private static string FixFormatting(string input, FormatType type) {
        if (String.IsNullOrEmpty(input)) return String.Empty;

        int charLimit = 24;
        
        // https://support.google.com/firebase/answer/9237506?hl=en
        switch (type) {
            case FormatType.EventName: charLimit = 40; break;
            case FormatType.EventParameterName: charLimit = 40; break;
            case FormatType.EventParameterValue: charLimit = 100; break;
            case FormatType.UserPropertyName: charLimit = 24; break;
            case FormatType.UserPropertyValue: charLimit = 36; break;
        }
        
        string output = input.Replace(' ', '_').ToLowerInvariant();
        output = output.Replace('.', '_');
        output = output.Replace('(', '_');
        output = output.Replace(')', '_');

        if (output.Length > charLimit)
            output = output.Substring(0, charLimit);

        return output;
    }
    
    
}