package com.pickle.picklecore;

import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.util.Log;
import android.view.HapticFeedbackConstants;
import android.view.View;

import androidx.core.content.ContextCompat;

public class Vibration {

    public static Vibrator vibrator;
    public static boolean isVibratorDisabled;
    public static boolean isVibratorInitialised;

    // Java doesn't support parameter defaults so this override is required
    public static void DoHapticFeedback(Context ctx) {
        DoHapticFeedback(ctx, 1);
    }

    public static void DoHapticFeedback(Context ctx, int type) {
        View rootView = null;

        try {
            rootView = ((Activity) ctx).getWindow().getDecorView().findViewById(android.R.id.content);
        } catch (Exception e) {
            Log.e("PicklePKG", "Vibration.DoHapticFeedback(..) failed to get rootView - " + e);
            return;
        }

        if (rootView == null) {
            Log.e("PicklePKG", "Vibration.DoHapticFeedback(..) rootView was null!");
            return;
        }

        // If haptic feedback isn't already enabled, enable it now
        if (!rootView.isHapticFeedbackEnabled())
            rootView.setHapticFeedbackEnabled(true);

        rootView.performHapticFeedback(type);
    }

    public static void DoVibrate(Context ctx, long miliseconds) {
        if (Build.VERSION.SDK_INT >= 26)
            DoVibrate(ctx, miliseconds, VibrationEffect.DEFAULT_AMPLITUDE);
    }

    // Strength is a range between 1 and 255 (use -1 or VibrationEffect.DEFAULT_AMPLITUDE to use device default)
    // Supress warnings about the permission for vibration not being in the manifest because we have the section wrapped in a check for the vibrate permission anyway
    @SuppressWarnings({"MissingPermission"})
    public static void DoVibrate(Context ctx, long miliseconds, int strength) {
        if (Build.VERSION.SDK_INT >= 26) {
            if (!isVibratorInitialised) {
                if (ContextCompat.checkSelfPermission(ctx, android.Manifest.permission.VIBRATE) == PackageManager.PERMISSION_GRANTED) {
                    vibrator = (Vibrator) ctx.getSystemService(Context.VIBRATOR_SERVICE);
                    isVibratorDisabled = vibrator == null || !vibrator.hasVibrator();

                    if (isVibratorDisabled)
                        Log.i("PicklePKG", "Vibration.DoVibrate(..) Vibration not supported on this device");
                } else {
                    Log.e("PicklePKG", "Vibration.DoVibrate(..) the app does not have VIBRATE permission!");
                    isVibratorDisabled = true;
                }

                isVibratorInitialised = true;
            }

            if (!isVibratorDisabled) {
                VibrationEffect effect = VibrationEffect.createOneShot(miliseconds, strength);
                vibrator.vibrate(effect);
            }
        } else if (!isVibratorInitialised) {
            Log.e("PicklePKG", "Vibration.DoVibrate(..) the app must target atleast SDK 26 to support VibrationEffect!");
            isVibratorInitialised = true;
            isVibratorDisabled = true;
        }
    }

    // Supress warnings about the permission for vibration not being in the manifest because we have the section wrapped in a check for the vibrate permission anyway
    @SuppressWarnings({"MissingPermission"})
    public static void StopVibrate() {
        if (Build.VERSION.SDK_INT >= 26) {
            if (isVibratorInitialised && !isVibratorDisabled)
                vibrator.cancel();
        }
    }
}
