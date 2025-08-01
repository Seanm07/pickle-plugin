package com.pickle.picklecore;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.widget.Toast;

public class Toasts {

    private static Toast activeToast = null;

    public static void Show(final Context ctx, final String msg, final int duration) {
        if(ctx == null) return;

        Handler mainLooper = null;

        try {
            // Get an instance of the main looper thread allowing us to use it for popping toast
            mainLooper = new Handler(Looper.getMainLooper());
        } catch (Exception e) {
            Log.e("PicklePKG", "Toasts.Show(..) failed to getMainLooper() - " + e);
            return;
        }

        // Post a message to the mainLooper requesting it to pop new toast
        mainLooper.post(
                new Runnable() {
                    @Override
                    public void run() {
                        try {
                            if(activeToast != null){
                                // Already an active toast, update it rather than creating new
                                activeToast.setText(msg);
                                activeToast.setDuration(duration);
                            } else {
                                // Setup the requested toast message
                                activeToast = Toast.makeText(ctx, msg, duration);
                            }
                        } catch (Exception e) {
                            Log.e("PicklePKG", "Toasts.Show(..) failed to make toast - " + e);
                            return;
                        }

                        try {
                            // Show the prepared toast message
                            activeToast.show();
                        } catch (Exception e) {
                            Log.e("PicklePKG", "Toasts.Show(..) failed to show prepared toast! - " + e);
                        }
                    }
                }
        );
    }

    public static void Hide() {
        // If there are no active toasts, return early
        if (activeToast == null) return;

        Handler mainLooper = null;

        try {
            // Get an instance of the main looper thread, allowing us to interact with the current active toast
            mainLooper = new Handler(Looper.getMainLooper());
        } catch (Exception e) {
            Log.e("PicklePKG", "Toasts.Hide() failed to getMainLooper() - " + e);
        }

        if(mainLooper != null) {
            // Post a message to the mainLoop requesting the active toast to be cancelled
            mainLooper.post(
                    new Runnable() {
                        @Override
                        public void run() {
                            try {
                                // Force cancel the current active toast message
                                activeToast.cancel();

                                activeToast = null;
                            } catch (Exception e) {
                                Log.e("PicklePKG", "Toasts.Hide(..) failed to cancel active toast! - " + e);
                            }
                        }
                    }
            );
        }
    }

}
