using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace InputHumanizer.Input
{
    /// <summary>Timing of one foreground playback.</summary>
    public sealed class PlaybackReport
    {
        public double DurationMs { get; init; }
        public double ScheduledMs { get; init; }
        public double MaxLateMs { get; init; }
        public double MeanLateMs { get; init; }
        public int PointsSent { get; init; }
        public int PointsSkipped { get; init; }
    }

    /// <summary>
    /// Plays a cursor path in the foreground at its real timing.
    ///
    /// ExileAPI resumes awaiting plugin code once per frame, so awaiting a delay between points
    /// can't be more precise than the frame time, and Windows' default timer is ~15.6 ms on top.
    /// Paths with 5–10 ms steps were thinned out and their speed profile flattened. Instead, the
    /// whole path plays on its own thread, waiting with a high-resolution waitable timer and a
    /// short spin; the caller awaits only the finished task.
    ///
    /// Each point is due at the sum of its own and all earlier delays. A point is skipped only if
    /// the next one is already due; the last point is always sent.
    /// </summary>
    public static class PathPlayback
    {
        /// <summary>Spin for the last part of each wait (ms) for sub-millisecond accuracy.</summary>
        public const double SpinMs = 1.0;

        /// <summary>Play on a dedicated thread; the task completes when the last point is sent.</summary>
        public static Task<PlaybackReport> PlayAsync(IReadOnlyList<CursorPointWithDelay> points, Action<int, int> setCursor,
            CancellationToken cancellationToken = default)
        {
            return Task.Factory.StartNew(() => Play(points, setCursor, cancellationToken), cancellationToken,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        /// <summary>Play on the calling thread (blocks for the path's duration).</summary>
        public static PlaybackReport Play(IReadOnlyList<CursorPointWithDelay> points, Action<int, int> setCursor,
            CancellationToken cancellationToken = default)
        {
            var due = new double[points.Count];
            double t = 0;
            for (int i = 0; i < points.Count; i++)
            {
                t += points[i].DelayMs;
                due[i] = t;
            }

            IntPtr timer = CreateTimer();
            var thread = Thread.CurrentThread;
            var oldPriority = thread.Priority;
            double maxLate = 0, sumLate = 0;
            int sent = 0, skipped = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                thread.Priority = ThreadPriority.Highest;
                for (int i = 0; i < points.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool isLast = i == points.Count - 1;
                    if (!isLast && sw.Elapsed.TotalMilliseconds >= due[i + 1])
                    {
                        skipped++;
                        continue;
                    }

                    WaitUntil(sw, due[i], timer, cancellationToken);
                    double late = sw.Elapsed.TotalMilliseconds - due[i];
                    maxLate = Math.Max(maxLate, late);
                    sumLate += late;
                    setCursor(points[i].X, points[i].Y);
                    sent++;
                }
            }
            finally
            {
                thread.Priority = oldPriority;
                if (timer != IntPtr.Zero) CloseHandle(timer);
            }

            return new PlaybackReport
            {
                DurationMs = sw.Elapsed.TotalMilliseconds,
                ScheduledMs = t,
                MaxLateMs = maxLate,
                MeanLateMs = sent > 0 ? sumLate / sent : 0,
                PointsSent = sent,
                PointsSkipped = skipped,
            };
        }

        private static void WaitUntil(Stopwatch sw, double dueMs, IntPtr timer, CancellationToken cancellationToken)
        {
            double remaining = dueMs - sw.Elapsed.TotalMilliseconds;
            // sleep in slices of at most 50 ms so cancellation is noticed during long pauses
            while (remaining > SpinMs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double sleepMs = Math.Min(remaining - SpinMs, 50);
                if (timer != IntPtr.Zero)
                {
                    long due100ns = -(long)(sleepMs * 10_000); // negative = relative
                    if (SetWaitableTimerEx(timer, ref due100ns, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0))
                        WaitForSingleObject(timer, INFINITE);
                    else
                        Thread.Sleep(1);
                }
                else
                {
                    Thread.Sleep(1);
                }
                remaining = dueMs - sw.Elapsed.TotalMilliseconds;
            }
            while (sw.Elapsed.TotalMilliseconds < dueMs) Thread.SpinWait(20);
        }

        private static IntPtr CreateTimer()
        {
            try
            {
                IntPtr timer = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
                if (timer == IntPtr.Zero) timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TIMER_ALL_ACCESS); // pre-1803 Windows
                return timer;
            }
            catch (DllNotFoundException)
            {
                return IntPtr.Zero; // not on Windows: Thread.Sleep + spin
            }
        }

        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002, TIMER_ALL_ACCESS = 0x1F0003, INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attrs, string name, uint flags, uint access);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimerEx(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg,
            IntPtr wakeContext, uint tolerableDelay);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
