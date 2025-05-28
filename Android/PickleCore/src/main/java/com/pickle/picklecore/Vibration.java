package com.pickle.picklecore;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.VibrationAttributes;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;
import android.util.Log;
import android.view.HapticFeedbackConstants;
import android.view.InputDevice;
import android.view.View;
import android.view.Window;

import androidx.core.content.ContextCompat;

public class Vibration {

    public static Vibrator vibrator;
    public static boolean isVibratorDisabled;
    public static boolean isVibratorInitialised;

    // Left motor: Low frequency (intense crashes/explosions)
    // Right motor: High frequency (subtle taps and haptic effects)
    @SuppressLint("MissingPermission")
    public static void DoControllerVibrate(boolean lowFrequency, long milliseconds, int strength){
        // API 31+ required for controller vibration as the vibration manager is required
        if(Build.VERSION.SDK_INT < Build.VERSION_CODES.S) return;

        if(strength < 0 || strength > 255) return;

        for(int deviceId : InputDevice.getDeviceIds()) {
            InputDevice inputDevice = InputDevice.getDevice(deviceId);

            if (inputDevice == null) {
                Log.e("PicklePKG", "Vibration.DoControllerVibrate(..) no input device found with deviceId: " + deviceId);
                return;
            }

            VibratorManager vibratorManager = inputDevice.getVibratorManager();

            if (vibratorManager == null) {
                Log.e("PicklePKG", "Vibration.DoControllerVibrate(..) null vibratorManager on deviceId: " + deviceId);
                return;
            }

            int[] vibratorIds = vibratorManager.getVibratorIds();
            int vibratorCount = vibratorIds.length; // 0 on devices which don't support vibration
            int targetVibratorIndex = lowFrequency ? 0 : 1;

            // Game controllers typically have 2 vibration motors one for low frequency and one for high frequency
            if (vibratorCount > 0) {
                // If the controller doesn't have a highFrequency motor just use the lowFrequency motor at min strength
                if(vibratorCount <= targetVibratorIndex){
                    targetVibratorIndex = 0;
                    strength = 1;
                }

                Vibrator vibrator = vibratorManager.getVibrator(vibratorIds[targetVibratorIndex]);

                if (vibrator == null) {
                    Log.e("PicklePKG", "Vibration.DoControllerVibrate(..) null vibrator index " + targetVibratorIndex + " on deviceId: " + deviceId);
                    return;
                }

                if (strength <= 0 || milliseconds <= 0L) {
                    vibrator.cancel();
                } else {
                    vibrator.vibrate(VibrationEffect.createOneShot(milliseconds, strength));
                }
            }
        }
    }

    // Java doesn't support parameter defaults so this override is required
    public static void DoHapticFeedback(Activity activity, Context ctx) {
        DoHapticFeedback(activity, ctx, HapticFeedbackConstants.CONFIRM, false);
    }

    @SuppressWarnings({"MissingPermission"})
    public static void DoHapticFeedback(Activity activity, Context ctx, int strength, boolean ignoreDeviceHapticSetting) {
        if(ctx == null || activity == null || activity.isFinishing() || activity.isDestroyed()) return;

        View rootView = null;

        try {
            Window activityWindow = activity.getWindow();
            if (activityWindow != null) {
                View activityView = activityWindow.getDecorView();

                if(activityView != null) {
                    rootView = activityView.findViewById(android.R.id.content);
                } else {
                    Log.e("PicklePKG", "Vibration.DoHapticFeedback(..) null activityView!");
                    return;
                }
            } else {
                Log.e("PicklePKG", "Vibration.DoHapticFeedback(..) null activityWindow!");
                return;
            }
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

        int type = HapticFeedbackConstants.CLOCK_TICK;

        switch(strength){
            /*case 1: type = HapticFeedbackConstants.VIRTUAL_KEY; break;*/
            case 2: type = HapticFeedbackConstants.KEYBOARD_TAP; break;
            case 3: type = HapticFeedbackConstants.VIRTUAL_KEY; break;
            case 4: type = HapticFeedbackConstants.LONG_PRESS; break;
        }

        if(ignoreDeviceHapticSetting) {
            // Android API 33+ does not support the flag to ignore device haptic settings, if we already have the VIBRATE permission just use the vibrator for haptics
            if(Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                if (ContextCompat.checkSelfPermission(ctx, Manifest.permission.VIBRATE) == PackageManager.PERMISSION_GRANTED) {
                    long vibrationMilliseconds = 1L;

                    // Convert strength to millisecond durations
                    switch (strength) {
                        /*case 1: vibrationMilliseconds = 1L; break;*/
                        case 2: vibrationMilliseconds = 20L; break;
                        case 3: vibrationMilliseconds = 50L; break;
                        case 4: vibrationMilliseconds = 100L; break;
                    }

                    DoVibrate(ctx, vibrationMilliseconds, 1);
                } else {
                    rootView.performHapticFeedback(type);
                }
            } else {
                rootView.performHapticFeedback(type, HapticFeedbackConstants.FLAG_IGNORE_GLOBAL_SETTING);
            }
        } else {
            rootView.performHapticFeedback(type);
        }
    }

    public static void DoVibrate(Context ctx, long milliseconds) {
        DoVibrate(ctx, milliseconds, -1);
    }

    // Strength is a range between 1 and 255 (use -1 or VibrationEffect.DEFAULT_AMPLITUDE to use device default)
    // Suppress warnings about the permission for vibration not being in the manifest because we have the section wrapped in a check for the vibrate permission anyway
    @SuppressWarnings({"MissingPermission"})
    public static void DoVibrate(Context ctx, long milliseconds, int strength) {
        if(ctx == null) return;
        if(strength <= 0 || strength > 255) strength = -1;

        if (!isVibratorInitialised) {
            if (ContextCompat.checkSelfPermission(ctx, Manifest.permission.VIBRATE) == PackageManager.PERMISSION_GRANTED) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    // SDK 31+ VIBRATOR_SERVICE is depreciated and the VIBRATOR_MANAGER_SERVICE should be used instead
                    VibratorManager vibratorManager = (VibratorManager) ctx.getSystemService(Context.VIBRATOR_MANAGER_SERVICE);
                    vibrator = vibratorManager.getDefaultVibrator();
                } else {
                    vibrator = (Vibrator) ctx.getSystemService(Context.VIBRATOR_SERVICE);
                }

                isVibratorDisabled = vibrator == null || !vibrator.hasVibrator();

                if (isVibratorDisabled)
                    Log.i("PicklePKG", "Vibration.DoVibrate(..) Vibration not supported on this device");
            } else {
                Log.e("PicklePKG", "Vibration.DoVibrate(..) the app does not have VIBRATE permission!");
                isVibratorDisabled = true;
            }

            isVibratorInitialised = true;
        }

        // Make sure the vibrator isn't disabled
        if (!isVibratorDisabled) {
            // Crashlytics found some cases where vibrator became null later, if it's suddenly null we'll mark it to be reinitialised
            if(vibrator != null) {
                // Android 26+ deprecated directly vibrating with milliseconds and added the VibrationEffect parameter for building the vibration properties
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                    VibrationEffect effect = VibrationEffect.createOneShot(milliseconds, strength);

                    if (effect != null) {
                        if(Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU){
                            vibrator.vibrate(effect, VibrationAttributes.createForUsage(VibrationAttributes.USAGE_MEDIA));
                        } else {
                            vibrator.vibrate(effect);
                        }
                    }
                } else {
                    vibrator.vibrate(milliseconds);
                }
            } else {
                // Re-initialise the vibrator next vibration usage
                isVibratorInitialised = false;
            }
        }
    }

    // Suppress warnings about the permission for vibration not being in the manifest because we have the section wrapped in a check for the vibrate permission anyway
    @SuppressWarnings({"MissingPermission"})
    public static void StopVibrate() {
        if (isVibratorInitialised && !isVibratorDisabled)
            vibrator.cancel();
    }
}
