using System.Numerics;
using InputHumanizer.Input;
using Xunit;

namespace InputHumanizer.Tests;

using GeneratePathV1 = Func<double, double, double, double, double, double, (float[] Points, double StepMs)>;

/// <summary>Seam 4: InputHumanizer's ClickBot client, with the PluginBridge lookup stood in.</summary>
public class ClickBotPathSourceTests
{
    private static ClickBotPathSource Source(object? method, List<string> logs) =>
        new(name => name == ClickBotPathSource.GeneratePathV1Name ? method! : null!, logs.Add);

    [Fact]
    public void Missing_plugin_returns_false_and_logs_once()
    {
        var logs = new List<string>();
        var source = Source(null, logs);

        Assert.False(source.TryGetPath(new Vector2(0, 0), new Vector2(100, 100), 0, out var points));
        Assert.False(source.TryGetPath(new Vector2(0, 0), new Vector2(100, 100), 0, out _));
        Assert.Empty(points);
        Assert.Single(logs);
        Assert.Contains("not loaded", logs[0]);
    }

    [Fact]
    public void Wrong_delegate_type_counts_as_missing()
    {
        var logs = new List<string>();
        Func<double, double, float[]> wrong = (_, _) => new float[4];
        Assert.False(Source(wrong, logs).TryGetPath(Vector2.Zero, Vector2.One, 0, out _));
        Assert.Single(logs);
    }

    [Fact]
    public void Throwing_generator_falls_back_and_logs_again_after_a_success()
    {
        var logs = new List<string>();
        bool fail = true;
        GeneratePathV1 method = (sx, sy, tx, ty, box, dur) =>
            fail ? throw new InvalidOperationException("no model") : (new float[] { 0, 0, 5, 5, 10, 10 }, 8.0);
        var source = Source(method, logs);

        Assert.False(source.TryGetPath(Vector2.Zero, new Vector2(10, 10), 0, out _));
        Assert.False(source.TryGetPath(Vector2.Zero, new Vector2(10, 10), 0, out _));
        Assert.Single(logs);
        Assert.Contains("no model", logs[0]);

        fail = false;
        Assert.True(source.TryGetPath(Vector2.Zero, new Vector2(10, 10), 0, out _));
        fail = true;
        Assert.False(source.TryGetPath(Vector2.Zero, new Vector2(10, 10), 0, out _));
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public void Passes_start_target_and_landing_box_and_lets_the_model_pick_the_duration()
    {
        double[]? args = null;
        GeneratePathV1 method = (sx, sy, tx, ty, box, dur) =>
        {
            args = new[] { sx, sy, tx, ty, box, dur };
            return (new float[] { 1, 2, 300, 400 }, 8.0);
        };

        Assert.True(Source(method, new()).TryGetPath(new Vector2(1, 2), new Vector2(300, 400), 24, out _));
        Assert.Equal(new double[] { 1, 2, 300, 400, 24, 0 }, args);
    }

    [Fact]
    public void Exact_landing_passes_box_zero()
    {
        double box = -1;
        GeneratePathV1 method = (sx, sy, tx, ty, b, dur) => { box = b; return (new float[] { 0, 0, 1, 1 }, 8.0); };
        Assert.True(Source(method, new()).TryGetPath(Vector2.Zero, Vector2.One, 0, out _));
        Assert.Equal(0, box);
    }

    [Fact]
    public void Converts_pairs_to_cursor_points_without_the_start()
    {
        GeneratePathV1 method = (sx, sy, tx, ty, b, dur) =>
            (new float[] { 10, 20, 12.4f, 20.5f, 15.6f, 21.5f, 20, 25 }, 8.0);

        Assert.True(Source(method, new()).TryGetPath(new Vector2(10, 20), new Vector2(20, 25), 0, out var points));
        Assert.Equal(new[] { (12, 21, 8u), (16, 22, 8u), (20, 25, 8u) },
            points.Select(p => (p.X, p.Y, p.DelayMs)).ToArray());
    }

    [Fact]
    public void Fractional_steps_keep_the_total_duration()
    {
        var xy = new float[2 * 11]; // start + 10 points
        var points = ClickBotPathSource.ToCursorPoints(xy, 7.5);
        Assert.Equal(10, points.Length);
        Assert.Equal(75u, (uint)points.Sum(p => (long)p.DelayMs));
        Assert.All(points, p => Assert.InRange(p.DelayMs, 7u, 8u));
    }

    [Fact]
    public void Too_short_path_counts_as_unavailable()
    {
        var logs = new List<string>();
        GeneratePathV1 method = (sx, sy, tx, ty, b, dur) => (new float[] { 0, 0 }, 8.0);
        Assert.False(Source(method, logs).TryGetPath(Vector2.Zero, Vector2.One, 0, out _));
        Assert.Single(logs);
    }
}
