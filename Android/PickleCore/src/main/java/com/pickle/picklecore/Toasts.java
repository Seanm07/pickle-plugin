package com.pickle.picklecore;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.TextView;
import android.widget.Toast;

public class Toasts {

    private static Toast activeToast = null;
    private static TextView activeTextView = null;

    public static void ShowToast(final Context ctx, final String msg, final boolean longDuration) {
        if(ctx == null) return;

        // Get an instance of the main looper thread allowing us to use it for popping toast on the main thread
        Handler mainLooper = new Handler(Looper.getMainLooper());

        if(mainLooper == null) {
            Log.e("PicklePKG", "Toasts.ShowToast() mainLooper was null");
            return;
        }

        // Post a message to the mainLooper requesting it to pop new toast
        mainLooper.post(() -> {
            if(activeToast != null){
                // Already an active toast, update it rather than creating new
                activeToast.setText(msg);
                activeToast.setDuration(longDuration ? Toast.LENGTH_LONG : Toast.LENGTH_SHORT);
            } else {
                // Setup the requested toast message
                activeToast = Toast.makeText(ctx, msg, longDuration ? Toast.LENGTH_LONG : Toast.LENGTH_SHORT);
            }

            activeToast.show();
        });
    }

    public static void HideToast() {
        // If there are no active toasts, return early
        if (activeToast == null) return;

        // Get an instance of the main looper thread, allowing us to interact with the current active toast
        Handler mainLooper = new Handler(Looper.getMainLooper());

        if(mainLooper == null) {
            Log.e("PicklePKG", "Toasts.HideToast() mainLooper was null");
            return;
        }

        // Post a message to the mainLoop requesting the active toast to be cancelled
        mainLooper.post(() -> {
            // Make sure activeToast wasn't disposed between initial call and main thread dispatch
            if(activeToast != null) {
                // Force cancel the current active toast message
                activeToast.cancel();
                activeToast = null;
            }
        });
    }

    public static void ShowTextOverlay(final Activity activity, final String msg, final int fontSize, final int backgroundAlpha) {
        if(activity == null) return;

        // Get an instance of the main looper thread allowing us to use it for popping textview on the main thread
        Handler mainLooper = new Handler(Looper.getMainLooper());

        if(mainLooper == null) {
            Log.e("PicklePKG", "Toasts.ShowTextOverlay() mainLooper was null");
            return;
        }

        // Post a message to the mainLooper requesting it to pop new textview
        mainLooper.post(() -> {
            if(activeTextView == null){
                activeTextView = new TextView(activity);
                activeTextView.setTextSize(fontSize);
                activeTextView.setBackgroundColor(Color.argb(backgroundAlpha, 0, 0, 0));
                activeTextView.setTextColor(Color.WHITE);
                activeTextView.setPadding(25, 15, 25, 15);
                activeTextView.setGravity(Gravity.CENTER);

                WindowManager.LayoutParams params = new WindowManager.LayoutParams();
                params.width = WindowManager.LayoutParams.WRAP_CONTENT;
                params.height = WindowManager.LayoutParams.WRAP_CONTENT;
                params.format = PixelFormat.TRANSLUCENT;
                params.flags = WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL;
                params.type = WindowManager.LayoutParams.TYPE_APPLICATION_PANEL;
                params.token = activity.getWindow().getDecorView().getWindowToken();

                params.gravity = Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL;
                params.y = 250;

                WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
                wm.addView(activeTextView, params);
            }

            activeTextView.setText(msg);
        });
    }

    public static void HideTextOverlay(final Activity activity) {
        // If there are no active text views, return early
        if (activeTextView == null || activity == null) return;

        // Get an instance of the main looper thread, allowing us to interact with the current active toast
        Handler mainLooper = new Handler(Looper.getMainLooper());

        if(mainLooper == null) {
            Log.e("PicklePKG", "Toasts.HideTextOverlay() mainLooper was null");
            return;
        }

        // Post a message to the mainLoop requesting the active textview to be cancelled
        mainLooper.post(() -> {
            // Make sure activeTextView wasn't disposed between initial call and main thread dispatch
            if(activeTextView != null) {
                try {
                    WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
                    wm.removeViewImmediate(activeTextView);
                    activeTextView = null;
                } catch (Exception e) {
                    Log.e("PicklePKG", "Toasts.HideTextOverlay() failed to remove view - " + e);
                }
            }
        });
    }

}
