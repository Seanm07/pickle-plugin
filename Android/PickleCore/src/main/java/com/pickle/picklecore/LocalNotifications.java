package com.pickle.picklecore;

import android.Manifest;
import android.app.Activity;
import android.app.AlarmManager;
import android.app.NotificationChannel;
import android.app.NotificationChannelGroup;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.Resources;
import android.graphics.BitmapFactory;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;

import androidx.core.app.NotificationCompat;
import androidx.core.app.NotificationManagerCompat;

import java.util.HashMap;
import java.util.Map;

// Default system notification groups
import static android.app.Notification.CATEGORY_CALL;
import static android.app.Notification.CATEGORY_EMAIL;
import static android.app.Notification.CATEGORY_ERROR;
import static android.app.Notification.CATEGORY_EVENT;
import static android.app.Notification.CATEGORY_ALARM;
import static android.app.Notification.CATEGORY_MESSAGE;
import static android.app.Notification.CATEGORY_NAVIGATION;
import static android.app.Notification.CATEGORY_PROGRESS;
import static android.app.Notification.CATEGORY_PROMO;
import static android.app.Notification.CATEGORY_RECOMMENDATION;
import static android.app.Notification.CATEGORY_REMINDER;
import static android.app.Notification.CATEGORY_SERVICE;
import static android.app.Notification.CATEGORY_SOCIAL;
import static android.app.Notification.CATEGORY_STATUS;
import static android.app.Notification.CATEGORY_SYSTEM;
import static android.app.Notification.CATEGORY_TRANSPORT;

// Notification priority & importance levels, allows additional functionality
import static android.app.NotificationManager.IMPORTANCE_DEFAULT;
import static android.app.NotificationManager.IMPORTANCE_HIGH;
import static android.app.NotificationManager.IMPORTANCE_LOW;
import static android.app.NotificationManager.IMPORTANCE_MIN;

import static androidx.core.app.NotificationCompat.PRIORITY_DEFAULT;
import static androidx.core.app.NotificationCompat.PRIORITY_HIGH;
import static androidx.core.app.NotificationCompat.PRIORITY_LOW;
import static androidx.core.app.NotificationCompat.PRIORITY_MIN;
import static androidx.core.app.NotificationCompat.VISIBILITY_PUBLIC;

public class LocalNotifications extends BroadcastReceiver {

    public static class ChannelGroupData {
        public String name;
        public Map<String, ChannelData> channelData = new HashMap<String, ChannelData>();

        public ChannelGroupData(String inName) {
            name = inName;
        }
    }

    public static class ChannelData {
        public String groupId, name, description;
        public boolean showOnStatusBar, playSound, showHeadsUp;

        public ChannelData(String inGroupId, String inName, String inDescription, boolean inShowOnStatusBar, boolean inPlaySound, boolean inShowHeadsUp) {
            groupId = inGroupId;
            name = inName;
            description = inDescription;
            showOnStatusBar = inShowOnStatusBar;
            playSound = inPlaySound;
            showHeadsUp = inShowHeadsUp;
        }

        public void UpdateChannelData(String inName, String inDescription, boolean inShowOnStatusBar, boolean inPlaySound, boolean inShowHeadsUp) {
            name = inName;
            description = inDescription;
            showOnStatusBar = inShowOnStatusBar;
            playSound = inPlaySound;
            showHeadsUp = inShowHeadsUp;
        }
    }

    // Setup the hash map with the system default notification group already pre-added
    public static Map<String, ChannelGroupData> channelGroups = new HashMap<String, ChannelGroupData>() {{
        put(CATEGORY_EVENT, new ChannelGroupData(CATEGORY_EVENT));
        put(CATEGORY_ALARM, new ChannelGroupData(CATEGORY_ALARM));
        put(CATEGORY_CALL, new ChannelGroupData(CATEGORY_CALL));
        put(CATEGORY_EMAIL, new ChannelGroupData(CATEGORY_EMAIL));
        put(CATEGORY_ERROR, new ChannelGroupData(CATEGORY_ERROR));
        put(CATEGORY_MESSAGE, new ChannelGroupData(CATEGORY_MESSAGE));
        put(CATEGORY_NAVIGATION, new ChannelGroupData(CATEGORY_NAVIGATION));
        put(CATEGORY_PROGRESS, new ChannelGroupData(CATEGORY_PROGRESS));
        put(CATEGORY_PROMO, new ChannelGroupData(CATEGORY_PROMO));
        put(CATEGORY_RECOMMENDATION, new ChannelGroupData(CATEGORY_RECOMMENDATION));
        put(CATEGORY_REMINDER, new ChannelGroupData(CATEGORY_REMINDER));
        put(CATEGORY_SERVICE, new ChannelGroupData(CATEGORY_SERVICE));
        put(CATEGORY_SOCIAL, new ChannelGroupData(CATEGORY_SOCIAL));
        put(CATEGORY_STATUS, new ChannelGroupData(CATEGORY_STATUS));
        put(CATEGORY_SYSTEM, new ChannelGroupData(CATEGORY_SYSTEM));
        put(CATEGORY_TRANSPORT, new ChannelGroupData(CATEGORY_TRANSPORT));
    }};

    public static void CreateNotificationGroup(Context ctx, String id, String name) {
        if(ctx == null) return;

        if (channelGroups.containsKey(id)) {
            channelGroups.get(id).name = name;
        } else {
            channelGroups.put(id, new ChannelGroupData(name));
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            // Notification channels & groups are only supported in API 26+
            NotificationChannelGroup channelGroup = new NotificationChannelGroup(id, name);

            NotificationManager notificationManager = ctx.getSystemService(NotificationManager.class);
            notificationManager.createNotificationChannelGroup(channelGroup);
        }
    }

    public static void CreateNotificationChannel(Context ctx, String id, String groupId, String name, String description, boolean showOnStatusBar, boolean playSound) {
        CreateNotificationChannel(ctx, id, groupId, name, description, showOnStatusBar, playSound, false);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String groupId, String name, String description, boolean showOnStatusBar) {
        CreateNotificationChannel(ctx, id, groupId, name, description, showOnStatusBar, false, false);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String groupId, String name, String description) {
        CreateNotificationChannel(ctx, id, groupId, name, description, false, false, false);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String name, String description, boolean showOnStatusBar, boolean playSound, boolean showHeadsUp) {
        CreateNotificationChannel(ctx, id, CATEGORY_EVENT, name, description, showOnStatusBar, playSound, showHeadsUp);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String name, String description, boolean showOnStatusBar, boolean playSound) {
        CreateNotificationChannel(ctx, id, CATEGORY_EVENT, name, description, showOnStatusBar, playSound, false);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String name, String description, boolean showOnStatusBar) {
        CreateNotificationChannel(ctx, id, CATEGORY_EVENT, name, description, showOnStatusBar, false, false);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String name, String description) {
        CreateNotificationChannel(ctx, id, CATEGORY_EVENT, name, description, false, false, false);
    }

    public static void CreateNotificationChannel(Context ctx, String id, String groupId, String name, String description, boolean showOnStatusBar, boolean playSound, boolean showHeadsUp) {
        if(ctx == null) return;

        if (channelGroups.containsKey(groupId)) {
            ChannelGroupData channelGroup = channelGroups.get(groupId);

            // Add information about the channel to a hashmap so we can treat all android versions as if they have channels
            if (channelGroup.channelData.containsKey(id)) {
                channelGroup.channelData.get(id).UpdateChannelData(name, description, showOnStatusBar, playSound, showHeadsUp);
            } else {
                channelGroup.channelData.put(id, new ChannelData(groupId, name, description, showOnStatusBar, playSound, showHeadsUp));
            }

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                // Notification channels & groups are only supported in API 26+
                NotificationChannel channel = new NotificationChannel(id, name, GetNeededImportanceLevel(GetChannelDataById(id)));
                channel.setDescription(description);
                channel.setGroup(groupId);

                NotificationManager notificationManager = ctx.getSystemService(NotificationManager.class);
                notificationManager.createNotificationChannel(channel);
            }
        } else {
            Log.e("PicklePKG", "Failed to create notification channel! Notification group with id " + groupId + " does not exist!");
        }
    }

    private static int GetNeededImportanceLevel(ChannelData channelData) {
        int importanceLevel = IMPORTANCE_MIN; // MIN importance gives no sound, heads-up or showing on status bar

        if (channelData.showHeadsUp) {
            importanceLevel = IMPORTANCE_HIGH; // HIGH importance needed to show a heads-up message
        } else if (channelData.playSound) {
            importanceLevel = IMPORTANCE_DEFAULT; // DEFAULT importance needed to make sound
        } else if (channelData.showOnStatusBar) {
            importanceLevel = IMPORTANCE_LOW; // LOW importance needed to show on status bar
        }

        return importanceLevel;
    }

    private static int GetNeededPriorityLevel(ChannelData channelData) {
        int priorityLevel = PRIORITY_MIN; // MIN priority gives no sound, heads-up or showing on status bar

        if (channelData.showHeadsUp) {
            priorityLevel = PRIORITY_HIGH; // HIGH priority needed to show a heads-up message
        } else if (channelData.playSound) {
            priorityLevel = PRIORITY_DEFAULT; // DEFAULT priority needed to make sound
        } else if (channelData.showOnStatusBar) {
            priorityLevel = PRIORITY_LOW; // LOW priority needed to show on status bar
        }

        return priorityLevel;
    }

    // Note: This also deletes all channels within the group
    public static void DeleteNotificationChannelGroup(Context ctx, String id) {
        if(ctx == null) return;

        if (channelGroups.containsKey(id))
            channelGroups.remove(id);

        // Allow deleting of channel groups even if they're not found in the channelGroups hash map as it won't cause any problems if not found
        // And maybe the app wants to delete groups created from previous sessions?
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            // Notification channels & groups are only supported in API 26+
            NotificationManager notificationManager = ctx.getSystemService(NotificationManager.class);
            notificationManager.deleteNotificationChannelGroup(id);
        }
    }

    public static void DeleteNotificationChannel(Context ctx, String id) {
        if(ctx == null) return;

        for (int groupInt = 0; groupInt < channelGroups.size(); groupInt++) {
            ChannelGroupData channelGroup = channelGroups.get(groupInt);

            if (channelGroup.channelData.containsKey(id))
                channelGroup.channelData.remove(id);
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            // Notification channels & groups are only supported in API 26+
            NotificationManager notificationManager = ctx.getSystemService(NotificationManager.class);
            notificationManager.deleteNotificationChannel(id);
        }
    }

    private static ChannelData GetChannelDataById(String id) {
        for (Map.Entry<String, ChannelGroupData> channelGroup : channelGroups.entrySet()) {
            ChannelGroupData groupData = channelGroup.getValue();

            if (groupData.channelData.containsKey(id))
                return groupData.channelData.get(id);
        }

        return null;
    }

    public static void SendNotification(Context ctx, Activity activity, int notificationId, String channelId, String msgTitle, String msgBody, int sendAfterSeconds, String smallIconName, String largeIconName) {
        SendNotification(ctx, activity, notificationId, channelId, msgTitle, msgBody, sendAfterSeconds, smallIconName, largeIconName, true);
    }

    public static void SendNotification(Context ctx, Activity activity, int notificationId, String channelId, String msgTitle, String msgBody, int sendAfterSeconds, String smallIconName) {
        SendNotification(ctx, activity, notificationId, channelId, msgTitle, msgBody, sendAfterSeconds, smallIconName, "", true);
    }

    public static void SendNotification(Context ctx, Activity activity, int notificationId, String channelId, String msgTitle, String msgBody, int sendAfterSeconds) {
        SendNotification(ctx, activity, notificationId, channelId, msgTitle, msgBody, sendAfterSeconds, "notification_icon", "", true);
    }

    public static void SendNotification(Context ctx, Activity activity, int notificationId, String channelId, String msgTitle, String msgBody, int sendAfterSeconds, String smallIconName, String largeIconName, boolean removeWhenTapped) {
        if(ctx == null || activity == null || activity.isFinishing() || (Build.VERSION.SDK_INT >= Build.VERSION_CODES.JELLY_BEAN_MR1 && activity.isDestroyed())) return;

        ChannelData channelData = GetChannelDataById(channelId);

        if (channelData == null) {
            Log.e("PicklePKG", "Failed to send notification! Invalid channel ID, make sure to call CreateNotificationChannel(..) before sending a notification!");
            return;
        }

        if (smallIconName.isEmpty()) {
            Log.e("PicklePKG", "Could not send notification! Small icon name not set, this would result in a crash when delivering the notification!");
            return;
        }

        if (ctx == null) {
            Log.e("PicklePKG", "Failed to show notification! ctx was null!");
            return;
        }

        if (activity == null) {
            Log.e("PicklePKG", "Failed to show notification! activity was null!");
            return;
        }

        // Create the intent which will be called when the alarm service triggers
        // This should contain everything needed for the notification
        Intent intent = new Intent(ctx, LocalNotifications.class);

        if (intent == null) {
            Log.e("PicklePKG", "Failed to show notification! intent was null!");
            return;
        }

        intent.putExtra("notificationId", notificationId);
        intent.putExtra("channelId", channelId);
        intent.putExtra("msgTitle", msgTitle);
        intent.putExtra("msgBody", msgBody);
        intent.putExtra("sendAfterSeconds", sendAfterSeconds);
        intent.putExtra("smallIconName", smallIconName);
        intent.putExtra("largeIconName", largeIconName);
        intent.putExtra("removeWhenTapped", removeWhenTapped);
        intent.putExtra("priority", GetNeededPriorityLevel(channelData));

        PendingIntent alarmIntent = PendingIntent.getBroadcast(activity, notificationId, intent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        if (alarmIntent == null) {
            Log.e("PicklePKG", "Failed to show notification! alarmIntent was null!");
            return;
        }

        AlarmManager alarmManager = (AlarmManager) activity.getSystemService(Context.ALARM_SERVICE);

        if (alarmManager == null) {
            Log.e("PicklePKG", "Failed to show notification! alarmManager was null!");
            return;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            // Get app package name
            String packageName = AppInfo.GetSelfPackageName(ctx);

            if (packageName.isEmpty()) {
                Log.e("PicklePKG", "LocalNotifications.SendNotification(..) GetSelfPackageName() was empty!");
                return;
            }

            // Get app package manager reference
            PackageManager ctxPackageManager = ctx.getPackageManager();

            if (ctxPackageManager == null) {
                Log.e("PicklePKG", "LocalNotifications.SendNotification(..) failed to get getPackageManager()");
                return;
            }

            // Android 12+ requires android.permission.SCHEDULE_EXACT_ALARM to use setExact and policy says it must only be used for user scheduled events
            if(ctxPackageManager.checkPermission(Manifest.permission.REQUEST_DELETE_PACKAGES, packageName) == PackageManager.PERMISSION_GRANTED){
                // The app has SCHEDULE_EXACT_ALARM permission, use setExact
                alarmManager.setExact(AlarmManager.RTC_WAKEUP, System.currentTimeMillis() + (sendAfterSeconds * 1000), alarmIntent);
            } else {
                // Fallback to inexact scheduling, the system can delay this until the device wakes up for other actions or can re-order notifications to save battery
                alarmManager.set(AlarmManager.RTC_WAKEUP, System.currentTimeMillis() + (sendAfterSeconds * 1000), alarmIntent);
            }
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            alarmManager.setExact(AlarmManager.RTC_WAKEUP, System.currentTimeMillis() + (sendAfterSeconds * 1000), alarmIntent);
        } else {
            alarmManager.set(AlarmManager.RTC_WAKEUP, System.currentTimeMillis() + (sendAfterSeconds * 1000), alarmIntent);
        }
    }

    public static void CancelNotification(Context ctx, Activity activity, int notificationId) {
        if(ctx == null || activity == null || activity.isFinishing() || (Build.VERSION.SDK_INT >= Build.VERSION_CODES.JELLY_BEAN_MR1 && activity.isDestroyed())){
            Log.e("PicklePKG", "Failed to cancel notification! Invalid context or activity!");
            return;
        }

        Intent intent = new Intent(ctx, LocalNotifications.class);

        if (intent == null) {
            Log.e("PicklePKG", "Failed to cancel notification! intent was null!");
            return;
        }

        PendingIntent alarmIntent = PendingIntent.getBroadcast(activity, notificationId, intent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        if (alarmIntent == null) {
            Log.e("PicklePKG", "Failed to show notification! alarmIntent was null!");
            return;
        }

        AlarmManager alarmManager = (AlarmManager) activity.getSystemService(Context.ALARM_SERVICE);

        if (alarmManager == null) {
            Log.e("PicklePKG", "Failed to show notification! alarmManager was null!");
            return;
        }

        alarmManager.cancel(alarmIntent);
    }

    public static String GetLaunchIntentExtras(Activity activity)
    {
        if(activity == null){
            Log.e("PicklePKG", "Failed to get launch intent extras! activity was null!");
            return "";
        }

        Intent intent = activity.getIntent();

        if(intent == null){
            Log.e("PicklePKG", "Failed to get launch intent extras! intent was null!");
            return "";
        }

        Bundle bundle = activity.getIntent().getExtras();

        String intentExtrasString = "";

        if(bundle != null){
            for(String key : bundle.keySet()){
                // Split each item with a pipe character as it's not a character which would be used
                if(!intentExtrasString.isEmpty())
                    intentExtrasString += "|";

                String value = bundle.get(key) != null ? bundle.get(key).toString() : "";

                // Split the key and value by a colon (C# splits by first colon, value is ok to contain colons)
                intentExtrasString += key + ":" + value;
            }
        }

        return intentExtrasString;
    }

    // Called by the alarm manager once it's time to send the notification
    // (Make sure an activity and receiver is setup in the android manifest!)
    @Override
    public void onReceive(Context ctx, Intent alarmIntent) {
        if (ctx == null) {
            Log.e("PicklePKG", "Failed to show notification! ctx was null!");
            return;
        }

        if (alarmIntent == null) {
            Log.e("PicklePKG", "Failed to show notification! alarmIntent was null!");
            return;
        }

        // Get the data stored in the alarm intent
        int notificationId = alarmIntent.getIntExtra("notificationId", 0);
        String channelId = alarmIntent.getStringExtra("channelId");
        String msgTitle = alarmIntent.getStringExtra("msgTitle");
        String msgBody = alarmIntent.getStringExtra("msgBody");
        int sendAfterSeconds = alarmIntent.getIntExtra("sendAfterSeconds", 0);
        String smallIconName = alarmIntent.getStringExtra("smallIconName");
        String largeIconName = alarmIntent.getStringExtra("largeIconName");
        boolean removeWhenTapped = alarmIntent.getBooleanExtra("removeWhenTapped", true);
        int priority = alarmIntent.getIntExtra("priority", PRIORITY_DEFAULT);


        NotificationManagerCompat notificationManager = NotificationManagerCompat.from(ctx);

        if (notificationManager == null) {
            Log.e("PicklePKG", "Failed to show notification! notificationManager was null!");
            return;
        }

        // Create the intent for the notification to launch the app when tapped
        // (getLaunchIntentForPackage looks for the intent containing the LAUNCHER category or LEANBACK_LAUNCHER on android TV)
        Intent intent = ctx.getPackageManager().getLaunchIntentForPackage(ctx.getPackageName());

        if (intent == null) {
            Log.e("PicklePKG", "Failed to show notification! intent was null!");
            return;
        }

        intent.setFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);

        // Attach some extra information to the intent so we can see which notification was tapped to launch the app
        intent.putExtra("notificationId", notificationId);
        intent.putExtra("channelId", channelId);
        intent.putExtra("msgTitle", msgTitle);
        intent.putExtra("msgBody", msgBody);
        intent.putExtra("smallIconName", smallIconName);
        intent.putExtra("largeIconName", largeIconName);
        intent.putExtra("sendAfterSeconds", sendAfterSeconds);

        PendingIntent pendingIntent = PendingIntent.getActivity(ctx, notificationId, intent, PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);

        NotificationCompat.Builder builder;

        Resources res = ctx.getResources();

        if (res != null) {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                builder = new NotificationCompat.Builder(ctx, channelId)
                        .setSmallIcon(res.getIdentifier(smallIconName, "drawable", ctx.getPackageName()))
                        .setContentTitle(msgTitle)
                        .setContentText(msgBody)
                        .setContentIntent(pendingIntent)
                        .setAutoCancel(removeWhenTapped)
                        .setVisibility(VISIBILITY_PUBLIC);

                if (!largeIconName.isEmpty())
                    builder.setLargeIcon(BitmapFactory.decodeResource(res, res.getIdentifier(largeIconName, "drawable", ctx.getPackageName())));
            } else {
                builder = new NotificationCompat.Builder(ctx, "default")
                        .setSmallIcon(ctx.getResources().getIdentifier(smallIconName, "drawable", ctx.getPackageName()))
                        .setContentTitle(msgTitle)
                        .setContentText(msgBody)
                        .setContentIntent(pendingIntent)
                        .setAutoCancel(removeWhenTapped)
                        .setPriority(priority);

                if (Build.VERSION.SDK_INT >= 21)
                    builder.setVisibility(VISIBILITY_PUBLIC);

                if (!largeIconName.isEmpty())
                    builder.setLargeIcon(BitmapFactory.decodeResource(res, res.getIdentifier(largeIconName, "drawable", ctx.getPackageName())));
            }
        } else {
            Log.e("PicklePKG", "Failed to show notification! res was null!");
            return;
        }

        notificationManager.notify(notificationId, builder.build());
    }

}
