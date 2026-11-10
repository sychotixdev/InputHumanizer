using System;
using System.Collections.Generic;
using System.Numerics;

namespace InputHumanizer.Input
{
    /// <summary>
    /// Gets mouse paths from the optional, separately distributed ClickBotPaths plugin through
    /// ExileAPI's PluginBridge. InputHumanizer has no reference to that plugin: the method is
    /// looked up by name on every call, so it works whenever the plugin is loaded and returns
    /// false (callers fall back to WindMouse) when it isn't.
    /// </summary>
    public sealed class ClickBotPathSource
    {
        /// <summary>PluginBridge name of the ClickBot path generator (contract version 1).</summary>
        public const string GeneratePathV1Name = "ClickBot.GeneratePath.v1";

        private readonly Func<string, object> _lookup;
        private readonly Action<string> _log;
        private bool _unavailableLogged;

        /// <param name="lookup">Returns the PluginBridge method registered under a name, or null.</param>
        /// <param name="log">Receives one message each time ClickBot becomes unavailable.</param>
        public ClickBotPathSource(Func<string, object> lookup, Action<string> log)
        {
            _lookup = lookup;
            _log = log;
        }

        /// <summary>Outcome of <see cref="Generate"/>: the points, or why there are none.</summary>
        public readonly struct PathResult
        {
            public PathResult(CursorPointWithDelay[] points, string failure)
            {
                Points = points ?? Array.Empty<CursorPointWithDelay>();
                Failure = failure;
            }

            public CursorPointWithDelay[] Points { get; }

            /// <summary>Null on success; otherwise the message to log.</summary>
            public string Failure { get; }

            public bool Ok => Failure == null;
        }

        /// <summary>
        /// Generate a path from <paramref name="start"/> to <paramref name="target"/>.
        /// <paramref name="boxSize"/> 0 lands exactly on the target; above 0, ClickBot picks the
        /// landing point inside a square of that size around it. The returned points exclude the
        /// start (the cursor is already there); each carries the wait before it.
        /// Safe to call from any thread: it runs the model but logs nothing. Pass the result to
        /// <see cref="Accept"/> on ExileAPI's thread.
        /// </summary>
        public PathResult Generate(Vector2 start, Vector2 target, float boxSize)
        {
            // (startX, startY, targetX, targetY, boxSize, durationMs) -> (x/y pairs, step in ms).
            // Framework types only, so the delegate type is identical in every plugin load context.
            var method = _lookup(GeneratePathV1Name) as Func<double, double, double, double, double, double, (float[] Points, double StepMs)>;
            if (method == null)
                return new PathResult(null, "ClickBot: the ClickBotPaths plugin is not loaded; using WindMouse.");

            (float[] Points, double StepMs) result;
            try
            {
                result = method(start.X, start.Y, target.X, target.Y, Math.Max(0f, boxSize), 0);
            }
            catch (Exception ex)
            {
                return new PathResult(null, $"ClickBot: path generation failed ({ex.Message}); using WindMouse.");
            }

            if (result.Points == null || result.Points.Length < 4 || result.StepMs <= 0)
                return new PathResult(null, "ClickBot: returned an empty path; using WindMouse.");

            return new PathResult(ToCursorPoints(result.Points, result.StepMs), null);
        }

        /// <summary>
        /// Report a <see cref="Generate"/> result: logs a failure once until the next success.
        /// Call on ExileAPI's thread. Returns whether the path can be used.
        /// </summary>
        public bool Accept(PathResult result)
        {
            if (result.Ok)
            {
                _unavailableLogged = false;
                return true;
            }

            if (!_unavailableLogged)
            {
                _unavailableLogged = true;
                _log(result.Failure);
            }
            return false;
        }

        /// <summary><see cref="Generate"/> and <see cref="Accept"/> in one call, on the calling thread.</summary>
        public bool TryGetPath(Vector2 start, Vector2 target, float boxSize, out CursorPointWithDelay[] points)
        {
            var result = Generate(start, target, boxSize);
            points = result.Points;
            return Accept(result);
        }

        /// <summary>
        /// Convert x/y pairs spaced <paramref name="stepMs"/> apart into cursor points, dropping the
        /// first pair (the start). Positions are rounded to whole pixels; whole-millisecond delays
        /// carry the fractional remainder forward so the total duration is kept.
        /// </summary>
        public static CursorPointWithDelay[] ToCursorPoints(float[] xy, double stepMs)
        {
            int count = xy.Length / 2;
            var points = new List<CursorPointWithDelay>(Math.Max(0, count - 1));
            double carry = 0;
            for (int k = 1; k < count; k++)
            {
                carry += stepMs;
                uint delay = (uint)Math.Floor(carry);
                carry -= delay;
                points.Add(new CursorPointWithDelay(
                    (int)Math.Round(xy[2 * k], MidpointRounding.AwayFromZero),
                    (int)Math.Round(xy[2 * k + 1], MidpointRounding.AwayFromZero),
                    delay));
            }
            return points.ToArray();
        }
    }
}
