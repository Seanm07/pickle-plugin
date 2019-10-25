package com.pickle.picklecore;

import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.util.Log;

import java.io.File;
import java.util.List;

public class AppInfo {

    public static String GetSelfPackageName(Context ctx) {
        return ctx.getPackageName();
    }

    // Time since the app was installed OR updated
    public static long GetInstallTimestamp(Context ctx) {
        // Get app package name
        String packageName = GetSelfPackageName(ctx);

        if (packageName.isEmpty()) {
            Log.e("PicklePKG", "ApplicationInfo.GetInstallTimestamp(..) GetSelfPackageName() was empty!");
            return 0L;
        }

        // Get app package manager reference
        PackageManager ctxPackageManager = ctx.getPackageManager();

        if (ctxPackageManager == null) {
            Log.e("PicklePKG", "ApplicationInfo.GetInstallTimestamp(..) failed to get getPackageManager()");
            return 0L;
        }

        // Get app info
        ApplicationInfo appInfo = null;

        try {
            appInfo = ctxPackageManager.getApplicationInfo(packageName, 0);
        } catch (PackageManager.NameNotFoundException e) {
            Log.e("PicklePKG", "ApplicationInfo.GetInstallTimestamp(..) package name " + packageName + " not installed? - " + e);
            return 0L;
        }

        if (appInfo == null) {
            Log.e("PicklePKG", "ApplicationInfo.GetInstallTimestamp(..) appInfo was null!");
            return 0L;
        }

        String appFile = appInfo.sourceDir;

        if (appFile.isEmpty()) {
            Log.e("PicklePKG", "ApplicationInfo.GetInstallTimestamp(..) appFile is empty!");
            return 0L;
        }

        long installTimestamp = 0L;

        try {
            installTimestamp = new File(appFile).lastModified();
        } catch (Exception e) {
            Log.e("PicklePKG", "ApplicationInfo.GetInstallTimestamp(..) failed to get appFile last modified time! - " + e);
            return 0L;
        }

        return installTimestamp;
    }

    // Time since app was initially installed (Updates do not affect this, but reinstalling the app does)
    public static long GetInitialInstallTimestamp(Context ctx) {
        // Get app package name
        String packageName = GetSelfPackageName(ctx);

        if (packageName.isEmpty()) {
            Log.e("PicklePKG", "ApplicationInfo.GetInitialInstallTimestamp(..) GetSelfPackageName() was empty!");
            return 0L;
        }

        // Get app package manager reference
        PackageManager ctxPackageManager = ctx.getPackageManager();

        if (ctxPackageManager == null) {
            Log.e("PicklePKG", "ApplicationInfo.GetInitialInstallTimestamp(..) failed to get getPackageManager()");
            return 0L;
        }

        // Get package info
        PackageInfo packageInfo = null;

        try {
            packageInfo = ctxPackageManager.getPackageInfo(packageName, 0);
        } catch (PackageManager.NameNotFoundException e) {
            Log.e("PicklePKG", "ApplicationInfo.GetInitialInstallTimestamp(..) package name " + packageName + " not installed? - " + e);
            return 0L;
        }

        if (packageInfo == null) {
            Log.e("PicklePKG", "ApplicationInfo.GetInitialInstallTimestamp(..) packageInfo was null!");
            return 0L;
        }

        long installTimestamp = 0L;

        try {
            installTimestamp = packageInfo.firstInstallTime;
        } catch (Exception e) {
            Log.e("PicklePKG", "ApplicationInfo.GetInitialInstallTimestamp(..) failed to get appFile last modified time! - " + e);
            return 0L;
        }

        return installTimestamp;
    }

    public static String GetPackageList(Context ctx, final String searchString) {
        // Get app package manager reference
        PackageManager ctxPackageManager = ctx.getPackageManager();

        if (ctxPackageManager == null) {
            Log.e("PicklePKG", "ApplicationInfo.GetPackageList(..) failed to get getPackageManager()");
            return "";
        }

        // Get a list of installed applications on the device
        List<ApplicationInfo> packageList = ctxPackageManager.getInstalledApplications(0);

        if (packageList.isEmpty()) {
            Log.e("PicklePKG", "ApplicationInfo.GetPackageList(..) packageList was empty!");
            return "";
        }

        // We have a list built of all packages but we need to convert them into a comma separated string
        StringBuilder finalList = new StringBuilder();

        // Iterate through the packages we have collected, appending package names to the StringBuilder
        for (ApplicationInfo listItem : packageList) {
            // Skip applications with null or empty className values
            if (listItem.packageName == null) continue;

            // Check if the package name matched the searchString filter we defined (if any)
            boolean isFilterMatch = (searchString.isEmpty() || (!searchString.isEmpty() && listItem.packageName.toLowerCase().contains(searchString)));

            if (isFilterMatch) {
                finalList.append(listItem.packageName);
                finalList.append(",");
            }
        }

        // Make sure the finalList isn't empty
        if (finalList.length() <= 0) {
            Log.e("PicklePKG", "ApplicationInfo.GetPackageList(..) finalList was empty!");
            return "";
        }

        // Return the final comma separated package list
        return finalList.toString();
    }

}
