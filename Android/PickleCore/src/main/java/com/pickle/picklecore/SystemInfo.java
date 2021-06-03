package com.pickle.picklecore;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.SystemClock;
import android.provider.Settings;
import android.util.DisplayMetrics;
import android.util.Log;

public class SystemInfo {

    public static long GetAvailableMemory() {
        Runtime activeRuntime = Runtime.getRuntime();

        if (activeRuntime != null) {
            long maxMemory = activeRuntime.maxMemory();
            long usedMemory = activeRuntime.totalMemory() - activeRuntime.freeMemory();

            // Note: freeMemory() is NOT the full available memory, just the free memory from the amount of memory CURRENTLY allocated
            // We need to do it this way to get available memory from the MAX amount of memory we can use
            return maxMemory - usedMemory;
        } else {
            Log.e("PicklePKG", "SystemInfo.GetAvailableMemory() activeRuntime was null!");
            return -1L;
        }
    }

    public static long GetUsedMemory() {
        Runtime activeRuntime = Runtime.getRuntime();

        if (activeRuntime != null) {
            return activeRuntime.totalMemory() - activeRuntime.freeMemory();
        } else {
            Log.e("PicklePKG", "SystemInfo.GetUsedMemory() activeRuntime was null!");
            return -1L;
        }
    }

    public static long GetTotalMemory() {
        Runtime activeRuntime = Runtime.getRuntime();

        if (activeRuntime != null) {
            return activeRuntime.totalMemory();
        } else {
            Log.e("PicklePKG", "SystemInfo.GetTotalMemory() activeRuntime was null!");
            return -1L;
        }
    }

    public static long GetMaxMemory() {
        Runtime activeRuntime = Runtime.getRuntime();

        if (activeRuntime != null) {
            return activeRuntime.maxMemory();
        } else {
            Log.e("PicklePKG", "SystemInfo.GetMaxMemory() activeRuntime was null!");
            return -1L;
        }
    }

    public static long GetFreeMemory() {
        Runtime activeRuntime = Runtime.getRuntime();

        if (activeRuntime != null) {
            return activeRuntime.freeMemory();
        } else {
            Log.e("PicklePKG", "SystemInfo.GetFreeMemory() activeRuntime was null!");
            return -1L;
        }
    }

    public static long GetMillisecondsSinceBoot() {
        return SystemClock.elapsedRealtime();
    }

    private static DisplayMetrics GetDisplayMetrics(Context ctx) {
        return ctx.getResources().getDisplayMetrics();
    }

    public static int GetDensity(Context ctx) {
        if(ctx == null) return -1;

        DisplayMetrics displayMetrics = GetDisplayMetrics(ctx);

        if (displayMetrics != null) {
            return displayMetrics.densityDpi;
        } else {
            Log.e("PicklePKG", "SystemInfo.GetDensity(..) displayMetrics was null!");
            return -1;
        }
    }

    public static float GetXDPI(Context ctx) {
        if(ctx == null) return -1;

        DisplayMetrics displayMetrics = GetDisplayMetrics(ctx);

        if (displayMetrics != null) {
            return displayMetrics.xdpi;
        } else {
            Log.e("PicklePKG", "SystemInfo.GetXDPI(..) displayMetrics was null!");
            return -1;
        }
    }

    public static float GetYDPI(Context ctx) {
        if(ctx == null) return -1;

        DisplayMetrics displayMetrics = GetDisplayMetrics(ctx);

        if (displayMetrics != null) {
            return displayMetrics.ydpi;
        } else {
            Log.e("PicklePKG", "SystemInfo.GetYDPI(..) displayMetrics was null!");
            return -1;
        }
    }

    public static void OpenSettingsApp(Activity activity, Context ctx) {
        if(activity == null) return;

        String packageName = AppInfo.GetSelfPackageName(ctx);

        // Create an intent to launch the detailed settings page about an application
        Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);

        // Set the URI to the package from the package name
        Uri uri = Uri.fromParts("package", packageName, null);

        // Add the URI to our intent so we know which app to launch detailed settings for
        intent.setData(uri);
        intent.addCategory("android.intent.category.DEFAULT");

        // Start an activity with the intent we built
        activity.startActivity(intent);
    }
}
