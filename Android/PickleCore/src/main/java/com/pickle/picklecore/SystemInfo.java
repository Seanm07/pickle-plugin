package com.pickle.picklecore;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.Resources;
import android.graphics.Insets;
import android.graphics.Rect;
import android.net.Uri;
import android.os.Build;
import android.os.SystemClock;
import android.provider.Settings;
import android.text.TextUtils;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.Display;
import android.view.DisplayCutout;
import android.view.KeyCharacterMap;
import android.view.KeyEvent;
import android.view.RoundedCorner;
import android.view.Surface;
import android.view.WindowInsets;
import android.view.WindowMetrics;

import java.lang.reflect.Method;

public class SystemInfo {

    public static int GetAPILevel() {
        return Build.VERSION.SDK_INT;
    }

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

    // Android 9+ has a standardised way of getting display cutouts
    // before android 9 every manufacturer did their own implementation..
    // This function checks through all the notch detection methods to see if we hit any
    public static boolean HasNotchCutout(Activity activity, Context ctx) {
        if(ctx == null || activity == null) return false;

        // API 28+ has standardised notch support
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.P) {
            DisplayCutout displayCutout = activity.getWindow().getDecorView().getRootWindowInsets().getDisplayCutout();

            if(displayCutout != null)
                return true;
        }

        ClassLoader classLoader = ctx.getClassLoader();

        // Huawei display cutout https://developer.huawei.com/consumer/cn/devservice/doc/50114
        try {
            Class<?> hwNotchSizeUtil = classLoader.loadClass("com.huawei.android.util.HwNotchSizeUtil");
            if(hwNotchSizeUtil != null) {
                Method hasNotchInScreen = hwNotchSizeUtil.getMethod("hasNotchInScreen");
                if(hasNotchInScreen != null)
                    return (boolean) hasNotchInScreen.invoke(hwNotchSizeUtil);
            }
        } catch(Exception e){}

        // Oppo display cutout https://open.oppomobile.com/wiki/doc#id=10159
        if(ctx.getPackageManager().hasSystemFeature("com.oppo.feature.screen.heteromorphism"))
            return true;

        // Vivo display cutout https://dev.vivo.com.cn/documentCenter/doc/145
        try {
            @SuppressLint("PrivateApi") Class ftFeature = classLoader.loadClass("android.util.FtFeature");
            Method[] methods = ftFeature.getDeclaredMethods();
            for(Method method: methods){
                if(method != null && method.getName().equalsIgnoreCase("isFeatureSupport")){
                    return (boolean) method.invoke(ftFeature, 0x00000020); // 0x00000020 is the screen notch feature
                }
            }
        } catch(Exception e){}

        // Xiaomi display cutout https://dev.mi.com/console/doc/detail?pId=1293 https://dev.mi.com/console/doc/detail?pId=1341
        if(SystemProperties.getProperty("ro.miui.notch").equals("1"))
            return true;

        // Samsung basically had 1 device which wasn't on Android P with a notch/hole punch, the Galaxy A8s (SM-68870)
        // Documentation for how the hole punch is defined is here http://support-cn.samsung.com/Upload/DeveloperChina/DeveloperChinaFile/201812121519497015B9B23C0D7.pdf
        try {
            Resources res = ctx.getResources();
            int resourceId = res.getIdentifier("config_mainBuiltInDisplayCutout", "string", "android");
            String spec = resourceId > 0 ? res.getString(resourceId) : null;
            if (spec != null && !TextUtils.isEmpty(spec))
                return true;
        } catch (Exception e){}

        // If we got this far and didn't hit any notch detections then assume we don't have one
        return false;
    }

    public static int GetWidth(Activity activity, Context ctx){
        if(ctx == null || activity == null) return 0;

        // API 30+ wants to use the new getDisplay method and windowMetrics for screen size
        if(Build.VERSION.SDK_INT >= Build.VERSION_CODES.R){
            WindowMetrics windowMetrics = activity.getWindowManager().getCurrentWindowMetrics();

            Rect scrBounds = windowMetrics.getBounds();

            return scrBounds.width();
        } else {
            Display display = activity.getWindowManager().getDefaultDisplay();
            DisplayMetrics displayMetrics = new DisplayMetrics();

            display.getRealMetrics(displayMetrics);

            return displayMetrics.widthPixels;
        }
    }

    public static int GetHeight(Activity activity, Context ctx){
        if(ctx == null || activity == null) return 0;

        // API 30+ wants to use the new getDisplay method and windowMetrics for screen size
        if(Build.VERSION.SDK_INT >= Build.VERSION_CODES.R){
            WindowMetrics windowMetrics = activity.getWindowManager().getCurrentWindowMetrics();

            Rect scrBounds = windowMetrics.getBounds();

            return scrBounds.height();
        } else {
            Display display = activity.getWindowManager().getDefaultDisplay();
            DisplayMetrics displayMetrics = new DisplayMetrics();

            display.getRealMetrics(displayMetrics);

            return displayMetrics.heightPixels;
        }
    }

    public static int[] GetSafeZone(Activity activity, Context ctx, boolean navbarSafeZone){
        if(ctx == null || activity == null) return new int[0];

        // Get the screen width/height so we can return a safe zone similar to Unity's safe zone
        DisplayMetrics displayMetrics = new DisplayMetrics();
        Display display;
        int scrRotation, scrHeight, scrWidth;
        int notchSize = 0;
        int navSize = 0;

        // API 30+ wants to use the new getDisplay method and windowMetrics for screen size
        if(Build.VERSION.SDK_INT >= Build.VERSION_CODES.R){
            WindowMetrics windowMetrics = activity.getWindowManager().getCurrentWindowMetrics();
            display = activity.getDisplay();

            Rect scrBounds = windowMetrics.getBounds();

            scrRotation = display.getRotation();
            scrWidth = scrBounds.width();
            scrHeight = scrBounds.height();

            if(navbarSafeZone){
                // Deduct the size of the onscreen nav bar if one exists
                WindowInsets windowInsets = windowMetrics.getWindowInsets();
                Insets insets = windowInsets.getInsetsIgnoringVisibility(WindowInsets.Type.navigationBars());

                navSize = insets.bottom + insets.top;
            }
        } else {
            display = activity.getWindowManager().getDefaultDisplay();

            display.getRealMetrics(displayMetrics);

            scrRotation = display.getRotation();
            scrHeight = displayMetrics.heightPixels;
            scrWidth = displayMetrics.widthPixels;

            if (navbarSafeZone){
                // We need to get the height of the navigation buttons and add them to screen size
                boolean hasPhysicalHomeKey = KeyCharacterMap.deviceHasKey(KeyEvent.KEYCODE_HOME);

                // If we don't have a physical home button then we must have an on-screen nav bar
                if (!hasPhysicalHomeKey) {
                    Resources resources = ctx.getResources();
                    @SuppressLint("InternalInsetResource") int navBarResId = resources.getIdentifier("navigation_bar_height", "dimen", "android");

                    if (navBarResId > 0)
                        navSize = navbarSafeZone ? resources.getDimensionPixelSize(navBarResId) : 0;
                }
            }
        }

        // API 28+ has standardised notch support
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
            WindowInsets windowInsets = activity.getWindow().getDecorView().getRootWindowInsets();

            if(windowInsets != null) {
                int topLeftRoundedOffset = 0;
                int bottomRightRoundedOffset = 0;

                // API 31+ has API for rounded screen corners which a ton of modern phones now have
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    RoundedCorner topLeftRoundedCorner = windowInsets.getRoundedCorner(RoundedCorner.POSITION_TOP_LEFT);
                    RoundedCorner bottomRightRoundedCorner = windowInsets.getRoundedCorner(RoundedCorner.POSITION_BOTTOM_RIGHT);

                    if(topLeftRoundedCorner != null) topLeftRoundedOffset = topLeftRoundedCorner.getRadius() / 2;
                    if(bottomRightRoundedCorner != null) bottomRightRoundedOffset = bottomRightRoundedCorner.getRadius() / 2;
                }

                DisplayCutout cutout = windowInsets.getDisplayCutout();

                // cutout is null if the screen doesn't have a cutout notch
                if (cutout != null) {
                    // This method kinda returns a safezone for us already so instead of setting notchSize just return the rect here
                    int leftInset = cutout.getSafeInsetLeft();
                    int topInset = cutout.getSafeInsetTop();
                    int rightInset = cutout.getSafeInsetRight();
                    int bottomInset = cutout.getSafeInsetBottom();

                    // AdMob only adds left/right margins for rounded corners so we'll do the same thing
                    // If any insets are within rounded corner radius, increase the value to be on the radius edge
                    if (leftInset < topLeftRoundedOffset) leftInset = topLeftRoundedOffset;
                    if (rightInset < bottomRightRoundedOffset) rightInset = bottomRightRoundedOffset;

                    return new int[]{leftInset, topInset, scrWidth - (leftInset + rightInset), scrHeight - (topInset + (Math.max(navSize, bottomInset)))};
                } else if(topLeftRoundedOffset > 0 || bottomRightRoundedOffset > 0){
                    // Screen doesn't have a notch cutout but it does have rounded corners
                    return new int[]{topLeftRoundedOffset, 0, scrWidth - (topLeftRoundedOffset + bottomRightRoundedOffset), scrHeight - navSize};
                }
            }
        }

        ClassLoader classLoader = ctx.getClassLoader();

        // Huawei display cutout https://developer.huawei.com/consumer/cn/devservice/doc/50114
        try {
            Class<?> hwNotchSizeUtil = classLoader.loadClass("com.huawei.android.util.HwNotchSizeUtil");
            if(hwNotchSizeUtil != null) {
                Method getNotchSize = hwNotchSizeUtil.getMethod("getNotchSize");

                int[] size = (int[]) getNotchSize.invoke(hwNotchSizeUtil);

                if (size.length >= 2 && size[1] > 0)
                    notchSize = size[1];
            }
        } catch(Exception e){}

        // Oppo display cutout https://open.oppomobile.com/wiki/doc#id=10159
        String oppoNotchProperty = SystemProperties.getProperty("ro.oppo.screen.heteromorphism");
        String[] splitOppoNotchProperty = oppoNotchProperty.split("[,:]");
        int[] oppoNotchPropertyValues = new int[splitOppoNotchProperty.length];

        try {
            for(int i=0;i < splitOppoNotchProperty.length;++i)
                oppoNotchPropertyValues[i] = Integer.parseInt(splitOppoNotchProperty[i]);
        } catch(NumberFormatException e){
            oppoNotchPropertyValues = null;
        }

        if(oppoNotchPropertyValues != null && oppoNotchPropertyValues.length == 4){
            // The array is notch distance from left/top in order of: left, top, right, bottom
            if(oppoNotchPropertyValues[3] > 0)
                notchSize = oppoNotchPropertyValues[3];
        }

        // Vivo display cutout https://dev.vivo.com.cn/documentCenter/doc/103
        try {
            @SuppressLint("PrivateApi") Class ftFeature = classLoader.loadClass("android.util.FtFeature");
            Method[] methods = ftFeature.getDeclaredMethods();
            for(Method method: methods){
                if(method != null && method.getName().equalsIgnoreCase("isFeatureSupport")){
                    if ((boolean) method.invoke(ftFeature, 0x00000020)){
                        // Vivo doesn't directly have any way of getting any info about the notch size..
                        // from their documentation though it seems to always be 32dp
                        float dpi = GetDensity(ctx);
                        if(dpi <= 0) dpi = 160f; // Fallback to the android baseline dpi if we failed to get dpi

                        notchSize = (int) Math.ceil(32f * (dpi / 160f));
                    }
                }
            }
        } catch(Exception e){}

        // Xiaomi display cutout https://dev.mi.com/console/doc/detail?pId=1293 https://dev.mi.com/console/doc/detail?pId=1341
        if(SystemProperties.getProperty("ro.miui.notch").equals("1")){
            Resources res = ctx.getResources();
            int notchHeightResId = res.getIdentifier("notch_height", "dimen", "android");

            if(notchHeightResId > 0) {
                int xiaomiNotchPixelHeight = res.getDimensionPixelSize(notchHeightResId);

                if(xiaomiNotchPixelHeight > 0)
                    notchSize = xiaomiNotchPixelHeight;
            }
        }

        // Samsung basically had 1 device which wasn't on Android P with a notch/hole punch, the Galaxy A8s (SM-68870)
        // Documentation for how the hole punch is defined is here http://support-cn.samsung.com/Upload/DeveloperChina/DeveloperChinaFile/201812121519497015B9B23C0D7.pdf
        try {
            Resources res = ctx.getResources();
            int resourceId = res.getIdentifier("config_mainBuiltInDisplayCutout", "string", "android");
            String spec = resourceId > 0 ? res.getString(resourceId) : null;
            if (spec != null && !TextUtils.isEmpty(spec)){
                @SuppressLint("InternalInsetResource") int statusBarResourceId = res.getIdentifier("status_bar_height", "dimen", "android");

                if(statusBarResourceId > 0){
                    int samsungNotchPixelHeight = res.getDimensionPixelSize(statusBarResourceId);

                    if(samsungNotchPixelHeight > 0)
                        notchSize = samsungNotchPixelHeight;
                }
            }
        } catch (Exception e){}

        if(notchSize > 0) {
            // Note: Screen height and width will auto adjust with orientation
            // As far as I know all device notches below API 28 are only on the top of the phone
            // (Some tablets have a pinhole/notch on the landscape top edge but it seems these are all API 28+)
            // Note: The rotation angle is relative to how the UI is rotated not how the phone is rotated so it's anti-clockwise
            switch (scrRotation) {
                // Portrait (notch top / nav bottom)
                case Surface.ROTATION_0: return new int[] { 0, notchSize, scrWidth, scrHeight - (navSize + notchSize) };

                // Landscape left (notch left / nav right)
                case Surface.ROTATION_90: return new int[] { notchSize, 0, scrWidth - (navSize + notchSize), scrHeight };

                // Upside down portrait (notch bottom / nav bottom) (this one is weird, I would have expected the nav to be top)
                case Surface.ROTATION_180: return new int[] { 0, 0, scrWidth, scrHeight - (Math.max(notchSize, navSize)) };

                // Landscape right (notch right / nav left)
                case Surface.ROTATION_270: return new int[] { navSize, 0, scrWidth - (navSize + notchSize), scrHeight };
            }
        }

        // Return an empty int array if the notchSize was 0, so we can atleast fallback to the unity safezone
        return new int[0];
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

        try {
            // Start an activity with the intent we built
            activity.startActivity(intent);
        } catch (ActivityNotFoundException e){
            // Some android TV were throwing an ActivityNotFoundException when trying to open the settings app
            // seems like some android TVs have this activity stripped or changed in some way
            Log.e("PicklePKG", "SystemInfo.OpenSettingsApp(..) activity ACTION_APPLICATION_DETAILS_SETTINGS not available!");

            Toasts.Show(ctx, "Application settings unavailable on this device!", 3);
        }
    }

    public static boolean IsAndroidTV(Context ctx) {
        if(ctx == null) return false;

        return ctx.getPackageManager().hasSystemFeature(PackageManager.FEATURE_LEANBACK) ||
                ctx.getPackageManager().hasSystemFeature(PackageManager.FEATURE_AUTOMOTIVE);
    }
}