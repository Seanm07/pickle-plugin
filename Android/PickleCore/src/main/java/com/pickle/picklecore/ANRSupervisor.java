package com.pickle.picklecore;

import android.os.Handler;
import android.os.Looper;
import java.util.concurrent.*;
import java.util.logging.*;

// A class supervising the UI thread for ANR errors. Use
// {@link #start()} and {@link #stop()} to control
// when the UI thread is supervised
public class ANRSupervisor {

    static ANRSupervisor instance;

    public static Logger logger = Logger.getLogger("ANR");
    public static void Log(Object log) { logger.log(Level.INFO, "com.pickle.picklecore [ANR] " + log); }

    // The {@link ExecutorService} checking the UI thread
    private ExecutorService mExecutor;

    // The {@link ANRSupervisorRunnable} running on a separate thread
    public final ANRSupervisorRunnable mSupervisorRunnable;

    public ANRSupervisor(Looper looper, int timeoutCheckDuration, int checkInterval)
    {
        mExecutor = Executors.newSingleThreadExecutor();
        mSupervisorRunnable = new ANRSupervisorRunnable(looper, timeoutCheckDuration, checkInterval);
    }

    public static void create()
    {
        if (instance == null)
        {
            instance = new ANRSupervisor(Looper.getMainLooper(), 2, 5);
        }
    }

    public static synchronized void start()
    {
        synchronized (instance.mSupervisorRunnable)
        {
            if (instance.mSupervisorRunnable.isStopped())
            {
                instance.mExecutor.execute(instance.mSupervisorRunnable);
            }
            else
            {
                instance.mSupervisorRunnable.resume();
            }
        }
    }

    // Stops the supervision. The stop is delayed, so if start() is called right after stop(),
    // both methods will have no effect. There will be at least one more ANR check before the supervision is stopped.
    public static synchronized void stop()
    {
        instance.mSupervisorRunnable.stop();
    }
}

// A {@link Runnable} testing the UI thread every 5 seconds until {@link #stop()} is called
class ANRSupervisorRunnable implements Runnable
{
    // The {@link Handler} to access the UI threads message queue
    private Handler mHandler;

    // The stop flag
    private boolean mStopped;

    // Flag indicating the stop was performed
    private boolean mStopCompleted = true;

    private int mTimeoutCheck;
    private int mCheckInterval;
    private int mFalsePositiveCheckDelay = 1;
    private int mMaxReportSendWaitDuration = 5;

    public ANRSupervisorRunnable(Looper looper, int timeoutCheckDuration, int checkInterval)
    {
        mHandler = new Handler(looper);
        mTimeoutCheck = timeoutCheckDuration;
        mCheckInterval = checkInterval;
    }

    @Override public void run()
    {
        this.mStopCompleted = false;

        // Loop until stop() was called or thread is interrupted
        while (!Thread.interrupted())
        {
            try
            {
                Thread.sleep(mCheckInterval * 1000);

                // Create new callback
                ANRSupervisorCallback callback = new ANRSupervisorCallback();

                // Perform test, Handler should run the callback within X seconds
                synchronized (callback)
                {
                    this.mHandler.post(callback);
                    callback.wait(mTimeoutCheck * 1000);

                    // Check if called
                    if (!callback.isCalled())
                    {
                        ANRSupervisor.Log("Thread " + this.mHandler.getLooper() + " DID NOT respond within " + mTimeoutCheck + " seconds");
                        for (int timePassed = 0; timePassed < mMaxReportSendWaitDuration * 1000; timePassed += 100)
                        {
                            if (timePassed >= mFalsePositiveCheckDelay * 1000)
                            {
                                break;
                            }
                            Thread.sleep(100);
                        }

                        if (!callback.isCalled())
                        {
                            // If the supervised thread still did not respond, quit the app.
                            android.os.Process.killProcess(android.os.Process.myPid());

                            System.exit(0); // SNAFU
                        }
                    }
                }

                // Check if stopped
                this.checkStopped();
            }
            catch (InterruptedException e)
            {
                ANRSupervisor.Log("Interruption caught.");
                break;
            }
        }

        // Set stop completed flag
        this.mStopCompleted = true;
    }

    private synchronized void checkStopped() throws InterruptedException
    {
        if (this.mStopped)
        {
            // Wait 1 second
            Thread.sleep(1000);

            // Break if still stopped
            if (this.mStopped)
            {
                throw new InterruptedException();
            }
        }
    }

    synchronized void stop()
    {
        this.mStopped = true;
    }

    synchronized void resume()
    {
        this.mStopped = false;
    }

    synchronized boolean isStopped() { return this.mStopCompleted; }
}

// A {@link Runnable} which calls {@link #notifyAll()} when run.
class ANRSupervisorCallback implements Runnable
{
    private boolean mCalled;

    public ANRSupervisorCallback() { super(); }

    @Override public synchronized void run()
    {
        this.mCalled = true;
        this.notifyAll();
    }

    synchronized boolean isCalled() { return this.mCalled; }
}