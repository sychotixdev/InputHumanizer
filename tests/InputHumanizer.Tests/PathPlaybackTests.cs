using System.Diagnostics;
using InputHumanizer.Input;
using Xunit;

namespace InputHumanizer.Tests;

/// <summary>Seam 4: foreground playback timing, with the cursor call stood in by a recorder.</summary>
public class PathPlaybackTests
{
    private static CursorPointWithDelay[] Path(int count, uint delayMs) =>
        Enumerable.Range(1, count).Select(i => new CursorPointWithDelay(i, i, delayMs)).ToArray();

    [Fact]
    public async Task Plays_8ms_paths_on_schedule_off_the_calling_thread()
    {
        var points = Path(75, 8); // 600 ms, a typical movement
        var times = new List<double>();
        var threads = new HashSet<int>();
        var sw = Stopwatch.StartNew();

        var report = await PathPlayback.PlayAsync(points, (x, y) =>
        {
            times.Add(sw.Elapsed.TotalMilliseconds);
            threads.Add(Environment.CurrentManagedThreadId);
        });

        Assert.Equal(600, report.ScheduledMs);
        Assert.Equal(75, report.PointsSent + report.PointsSkipped);
        // Windows can still pre-empt the thread for ~20 ms now and then (a point or two may then be
        // skipped), so single-point bounds are loose; the mean is what proves the precision.
        // Typical values here: mean late well under 0.5 ms, max under 2 ms.
        Assert.True(report.PointsSent >= 72, $"{report.PointsSkipped} points skipped");
        Assert.InRange(report.DurationMs, 599, 640);
        Assert.True(report.MeanLateMs < 2, $"mean late {report.MeanLateMs:F2} ms");
        Assert.True(report.MaxLateMs < 40, $"max late {report.MaxLateMs:F2} ms");
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads);

        // consecutive moves really are ~8 ms apart (the old Task.Delay loop gave ~15.6 ms or frame steps)
        var gaps = times.Zip(times.Skip(1), (a, b) => b - a).OrderBy(g => g).ToArray();
        Assert.InRange(gaps[gaps.Length / 2], 7.5, 8.5); // median gap
    }

    [Fact]
    public void Late_points_are_skipped_but_the_last_one_is_always_sent()
    {
        var points = Path(20, 8);
        var sent = new List<int>();
        var report = PathPlayback.Play(points, (x, y) =>
        {
            sent.Add(x);
            if (sent.Count == 1) Thread.Sleep(60); // stall: several later points become overdue
        });

        Assert.True(report.PointsSkipped > 0);
        Assert.Equal(20, sent[^1]);
        Assert.Equal(20, report.PointsSent + report.PointsSkipped);
    }

    [Fact]
    public async Task Cancellation_stops_playback()
    {
        var points = Path(200, 10); // 2 s
        using var cts = new CancellationTokenSource(100);
        var sent = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PathPlayback.PlayAsync(points, (_, _) => Interlocked.Increment(ref sent), cts.Token));
        Assert.InRange(sent, 1, 30);
    }

    [Fact]
    public void Empty_path_does_nothing()
    {
        var report = PathPlayback.Play(Array.Empty<CursorPointWithDelay>(), (_, _) => throw new Exception("not called"));
        Assert.Equal(0, report.PointsSent);
    }
}
