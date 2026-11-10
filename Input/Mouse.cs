using Kalon;
using Kalon.Native.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using TradeMonitor.Utilities;


#if POE1
using Core = ExileCore;
using Shared = ExileCore.Shared;

#else
using Core = ExileCore2;
using Shared = ExileCore2.Shared;

#endif

namespace InputHumanizer.Input
{
    internal class Mouse
    {
        private static float NormalizeDistance(float distance, float maxDistance)
        {
            return Math.Min(distance / maxDistance, 1.0f);
        }

        private static float Lerp(float startValue, float endValue, float interpolationFactor)
        {
            return (1 - interpolationFactor) * startValue + interpolationFactor * endValue;
        }

        public static async Shared.SyncTask<bool> MoveMouse(InputHumanizer plugin, Vector2 targetPosition, int maxInterpolationDistance = 700, int minInterpolationDelay = 0, int maxInterpolationDelay = 300, CancellationToken cancellationToken = default)
        {
            var backgroundController = plugin.GetBackgroundInputController();
            var windowRect = plugin.GameController.Window.GetWindowRectangleTimeCache;
            Vector2 windowOffset = windowRect.TopLeft.ToStandardVector2();

            Vector2 startPosClient;
            Vector2 finalTargetClient;

            if (backgroundController != null)
            {
                // Get forced cursor position (SCREEN coordinates)
                var currentForcedPos = await backgroundController.GetForcedCursorPositionAsync();

                if (currentForcedPos.HasValue)
                {
                    startPosClient = currentForcedPos.Value - windowOffset;
                    plugin.DebugLog($"MoveMouse [BG]: Using forced cursor at CLIENT: ({startPosClient.X}, {startPosClient.Y})");
                }
                else
                {
                    // No forced cursor yet - convert real cursor from SCREEN to CLIENT
                    Vector2 realCursorScreen = Core.Input.ForceMousePosition.ToStandardVector2();
                    startPosClient = realCursorScreen - windowOffset;
                    plugin.DebugLog($"MoveMouse [BG]: No forced cursor, using real cursor. SCREEN: {realCursorScreen} -> CLIENT: {startPosClient}");
                }

                // Convert target from SCREEN to CLIENT
                finalTargetClient = targetPosition - windowOffset;

                plugin.DebugLog($"MoveMouse [BG]: WindowOffset: {windowOffset}, TargetScreen: {targetPosition} -> TargetClient: {finalTargetClient}, StartClient: {startPosClient}");
            }
            else
            {
                // Foreground mode - everything in SCREEN coordinates
                startPosClient = Core.Input.ForceMousePosition.ToStandardVector2();
                finalTargetClient = targetPosition;
                plugin.DebugLog($"MoveMouse [FG]: TargetScreen: {finalTargetClient}, StartScreen: {startPosClient}");
            }

            // Path Logic
            float distance = Vector2.Distance(startPosClient, finalTargetClient);
            float normalizedDistance = NormalizeDistance(distance, maxInterpolationDistance);
            float interpolatedValue = Lerp(minInterpolationDelay, maxInterpolationDelay, normalizedDistance);
            TimeSpan mouseSpeed = TimeSpan.FromMilliseconds(interpolatedValue);

            // Generate Path (now both start and target are in the same coordinate space)
            var movements = CursorMover.GenerateMovements(
                new Point((int)startPosClient.X, (int)startPosClient.Y),
                new Point((int)finalTargetClient.X, (int)finalTargetClient.Y),
                (int)mouseSpeed.TotalMilliseconds);

            List<CursorPointWithDelay> processedPoints = new List<CursorPointWithDelay>();
            double accumulatedDelay = 0;
            var movementsList = movements.ToList();

            foreach (var movement in movementsList)
            {
                if (movement.Points == null || !movement.Points.Any()) continue;

                var pointsList = movement.Points.ToList();
                double rawDelayPerPoint = movement.Delay.TotalMilliseconds / pointsList.Count;

                for (int i = 0; i < pointsList.Count; i++)
                {
                    accumulatedDelay += rawDelayPerPoint;
                    bool isLastPointOfEntirePath = (movement == movementsList.Last() && i == pointsList.Count - 1);

                    if (accumulatedDelay >= 1.0 || isLastPointOfEntirePath)
                    {
                        processedPoints.Add(new CursorPointWithDelay(
                            pointsList[i].X,
                            pointsList[i].Y,
                            (uint)Math.Floor(accumulatedDelay)
                        ));
                        accumulatedDelay -= Math.Floor(accumulatedDelay);
                    }
                }
            }

            if (backgroundController != null)
            {
                if (processedPoints.Any())
                {
                    var first = processedPoints.First();
                    var last = processedPoints.Last();
                    plugin.DebugLog($"MoveMouse [Pipe Send]: Points: {processedPoints.Count}, FirstPt: ({first.X},{first.Y}), LastPt: ({last.X},{last.Y})");
                }

                await backgroundController.SetCursorPathAsync(processedPoints.ToArray(), 5000);
            }
            else
            {
                await PlayForeground(plugin, DelaysAfterToBefore(processedPoints), cancellationToken);
            }

            return true;
        }

        /// <summary>
        /// The default and WindMouse foreground loops moved to a point and then waited its delay;
        /// <see cref="PathPlayback"/> waits a point's delay before moving to it. Shift each delay
        /// onto the following point so those modes keep their original timing.
        /// </summary>
        private static CursorPointWithDelay[] DelaysAfterToBefore(IReadOnlyList<CursorPointWithDelay> points)
        {
            var shifted = new CursorPointWithDelay[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                uint before = i == 0 ? 0 : points[i - 1].DelayMs;
                shifted[i] = new CursorPointWithDelay(points[i].X, points[i].Y, before);
            }
            return shifted;
        }

        /// <summary>
        /// Foreground playback for every movement mode: runs on its own thread with a
        /// high-resolution timer (see <see cref="PathPlayback"/>), awaited as one task.
        /// </summary>
        private static async Task PlayForeground(InputHumanizer plugin, IReadOnlyList<CursorPointWithDelay> points, CancellationToken cancellationToken)
        {
            if (points.Count == 0) return;
            var report = await PathPlayback.PlayAsync(points, (x, y) => Core.Input.SetCursorPos(new Vector2(x, y)), cancellationToken);
            plugin.DebugLog($"Playback [FG]: {report.PointsSent} points ({report.PointsSkipped} skipped), " +
                            $"{report.DurationMs:F1} ms for {report.ScheduledMs:F0} ms scheduled, late mean {report.MeanLateMs:F2} / max {report.MaxLateMs:F2} ms");
        }

        /// <summary>
        /// Move along a ClickBot3000 model path. Returns null when ClickBot is unavailable (the
        /// ClickBotPaths plugin isn't loaded or failed), so the caller can fall back.
        /// </summary>
        public static async Shared.SyncTask<bool?> ClickBotImpl(InputHumanizer plugin, ClickBotPathSource source, Vector2 targetPosition,
            float landingBox, CancellationToken cancellationToken = default)
        {
            var backgroundController = plugin.GetBackgroundInputController();
            Vector2 start, target;

            if (backgroundController != null)
            {
                // background input works in CLIENT coordinates
                var windowOffset = plugin.GameController.Window.GetWindowRectangleTimeCache.TopLeft.ToStandardVector2();
                var forcedScreen = await backgroundController.GetForcedCursorPositionAsync();
                start = (forcedScreen ?? Core.Input.ForceMousePosition.ToStandardVector2()) - windowOffset;
                target = targetPosition - windowOffset;
            }
            else
            {
                start = Core.Input.ForceMousePosition.ToStandardVector2();
                target = targetPosition;
            }

            // Generate and play in one task on a dedicated thread. ExileAPI's thread is free while
            // the model runs (and while it loads, on the first move) and while the cursor moves;
            // awaiting once means one frame of delay at the end instead of one between steps.
            var move = await Task.Factory.StartNew(
                () => GenerateAndPlay(source, start, target, landingBox, backgroundController, cancellationToken),
                cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // logging stays on ExileAPI's thread
            if (!source.Accept(move.Path))
                return null;

            plugin.DebugLog($"ClickBot: {move.Path.Points.Length} points from ({start.X},{start.Y}) to ({target.X},{target.Y}) " +
                            $"generated in {move.GenerateMs:F1} ms (BG={backgroundController != null})");
            if (move.Playback is { } report)
                plugin.DebugLog($"Playback [FG]: {report.PointsSent} points ({report.PointsSkipped} skipped), " +
                                $"{report.DurationMs:F1} ms for {report.ScheduledMs:F0} ms scheduled, late mean {report.MeanLateMs:F2} / max {report.MaxLateMs:F2} ms");

            return true;
        }

        private sealed record ClickBotMove(ClickBotPathSource.PathResult Path, double GenerateMs, PlaybackReport Playback);

        // runs on the worker thread: no ExileAPI calls except the cursor move itself
        private static ClickBotMove GenerateAndPlay(ClickBotPathSource source, Vector2 start, Vector2 target, float landingBox,
            BackgroundInput backgroundController, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var path = source.Generate(start, target, landingBox);
            double generateMs = sw.Elapsed.TotalMilliseconds;
            if (!path.Ok)
                return new ClickBotMove(path, generateMs, null);

            cancellationToken.ThrowIfCancellationRequested();
            if (backgroundController != null)
            {
                // the pipe client serialises commands with its own lock
                backgroundController.SetCursorPathAsync(path.Points, 5000).GetAwaiter().GetResult();
                return new ClickBotMove(path, generateMs, null);
            }

            var report = PathPlayback.Play(path.Points, (x, y) => Core.Input.SetCursorPos(new Vector2(x, y)), cancellationToken);
            return new ClickBotMove(path, generateMs, report);
        }

        // Credits: https://ben.land/post/2021/04/25/windmouse-human-mouse-movement/#the-code
        private static Random random = new Random();
        private static readonly double sqrt3 = Math.Sqrt(3);
        private static readonly double sqrt5 = Math.Sqrt(5);


        public static async Shared.SyncTask<bool> WindMouseImpl(InputHumanizer plugin, double startX, double startY, double destX, double destY,
                                      double gravity, double wind, int minWait,
                                      int maxWait, double maxStep, double targetArea, CancellationToken cancellationToken = default)
        {
            var backgroundController = plugin.GetBackgroundInputController();
            var windowRect = plugin.GameController.Window.GetWindowRectangleTimeCache;
            Vector2 windowOffset = windowRect.TopLeft.ToStandardVector2(); // SCREEN coords of client (0,0)

            // ---------------------------------------------------------------------
            // Resolve START / DEST coordinates
            // ---------------------------------------------------------------------

            bool isBackground = backgroundController != null;

            // These will be the coordinates used by the WindMouse algorithm
            double curX, curY;
            double targetX, targetY;

            if (isBackground)
            {
                // ---- START: SCREEN → CLIENT
                var forcedScreen = await backgroundController.GetForcedCursorPositionAsync();
                Vector2 startScreen = forcedScreen ?? Core.Input.ForceMousePosition.ToStandardVector2();

                curX = startScreen.X - windowOffset.X;
                curY = startScreen.Y - windowOffset.Y;

                // ---- DEST: SCREEN → CLIENT
                targetX = destX - windowOffset.X;
                targetY = destY - windowOffset.Y;
            }
            else
            {
                // Foreground mode: everything stays in SCREEN space
                curX = startX;
                curY = startY;
                targetX = destX;
                targetY = destY;
            }

            // ---------------------------------------------------------------------
            // WindMouse algorithm (coordinate-space agnostic)
            // ---------------------------------------------------------------------

            double dist, veloX = 0, veloY = 0, windX = 0, windY = 0;

            var points = new List<CursorPointWithDelay>();

            while ((dist = Hypot(curX - targetX, curY - targetY)) >= 1)
            {
                wind = Math.Min(wind, dist);

                if (dist >= targetArea)
                {
                    windX = windX / sqrt3 + (2 * random.NextDouble() - 1) * wind / sqrt5;
                    windY = windY / sqrt3 + (2 * random.NextDouble() - 1) * wind / sqrt5;
                }
                else
                {
                    windX /= sqrt3;
                    windY /= sqrt3;

                    if (maxStep < 3)
                        maxStep = random.NextDouble() * 3 + 3;
                    else
                        maxStep /= sqrt5;
                }

                veloX += windX + gravity * (targetX - curX) / dist;
                veloY += windY + gravity * (targetY - curY) / dist;

                double veloMag = Hypot(veloX, veloY);
                if (veloMag > maxStep)
                {
                    double clipped = maxStep / 2 + random.NextDouble() * maxStep / 2;
                    veloX = (veloX / veloMag) * clipped;
                    veloY = (veloY / veloMag) * clipped;
                }

                curX += veloX;
                curY += veloY;

                int x = (int)Math.Round(curX);
                int y = (int)Math.Round(curY);
                int delay = random.Next(minWait, maxWait);

                points.Add(new CursorPointWithDelay(x, y, (uint)delay));
            }

            plugin.DebugLog($"WindMouse: Generated {points.Count} points (BG={isBackground})");

            // ---------------------------------------------------------------------
            // Dispatch
            // ---------------------------------------------------------------------

            if (isBackground)
            {
                // CLIENT coordinates → pipe
                if (points.Count > 0)
                {
                    var first = points.First();
                    var last = points.Last();
                    plugin.DebugLog(
                        $"WindMouse [BG]: FirstClient=({first.X},{first.Y}) LastClient=({last.X},{last.Y})");
                }

                await backgroundController.SetCursorPathAsync(points.ToArray(), 5000);
            }
            else
            {
                // SCREEN coordinates; the old loop moved first and then waited, so keep that timing
                await PlayForeground(plugin, DelaysAfterToBefore(points), cancellationToken);
            }

            return true;
        }

        private static double Hypot(double dx, double dy)
        {
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
