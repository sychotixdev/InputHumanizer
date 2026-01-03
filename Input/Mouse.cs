using ExileCore2.Shared;
using Kalon;
using Kalon.Native.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

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

        public static async SyncTask<bool> MoveMouse(InputHumanizer plugin, Vector2 targetPosition, int maxInterpolationDistance = 700, int minInterpolationDelay = 0, int maxInterpolationDelay = 300, CancellationToken cancellationToken = default)
        {
            var backgroundController = plugin.GetBackgroundInputController();
            var windowRect = plugin.GameController.Window.GetWindowRectangleTimeCache;
            Vector2 windowOffset = windowRect.TopLeft;

            Vector2 startPosClient;
            Vector2 finalTargetClient;

            if (backgroundController != null)
            {
                // Get forced cursor position (already in CLIENT coordinates)
                var currentForcedPos = await backgroundController.GetForcedCursorPositionAsync();

                if (currentForcedPos.HasValue)
                {
                    // Already in CLIENT coordinates - use directly
                    startPosClient = currentForcedPos.Value;
                    plugin.DebugLog($"MoveMouse [BG]: Using forced cursor at CLIENT: ({startPosClient.X}, {startPosClient.Y})");
                }
                else
                {
                    // No forced cursor yet - convert real cursor from SCREEN to CLIENT
                    Vector2 realCursorScreen = ExileCore2.Input.ForceMousePosition;
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
                startPosClient = ExileCore2.Input.ForceMousePosition;
                finalTargetClient = targetPosition;
                plugin.DebugLog($"MoveMouse [FG]: TargetScreen: {finalTargetClient}, StartScreen: {startPosClient}");
            }

            // Path Logic
            float distance = Vector2.Distance(startPosClient, finalTargetClient);
            float normalizedDistance = NormalizeDistance(distance, maxInterpolationDistance);
            float interpolatedValue = Lerp(minInterpolationDelay, maxInterpolationDelay, normalizedDistance);
            TimeSpan mouseSpeed = TimeSpan.FromMilliseconds(interpolatedValue + Random.Shared.Next(25, 100));

            // Generate Path (now both start and target are in the same coordinate space)
            var movements = CursorMover.GenerateMovements(
                new Point((int)startPosClient.X, (int)startPosClient.Y),
                new Point((int)finalTargetClient.X, (int)finalTargetClient.Y),
                (int)mouseSpeed.TotalMilliseconds);

            if (backgroundController != null)
            {
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
                // Standard Foreground logic...
                var stopwatch = Stopwatch.StartNew();
                TimeSpan totalDelay = TimeSpan.Zero;
                foreach (var movement in movements)
                {
                    foreach (var point in movement.Points)
                    {
                        ExileCore2.Input.SetCursorPos(new Vector2(point.X, point.Y));
                    }
                    totalDelay = totalDelay.Add(movement.Delay);
                    if (stopwatch.Elapsed < totalDelay)
                        await Task.Delay(totalDelay - stopwatch.Elapsed, cancellationToken);
                }
            }

            return true;
        }

        private static Vector2 GetClampedWindowIntersection(Vector2 currentScreen, Vector2 targetClient, RectangleF windowRect)
        {
            if (windowRect.Contains(currentScreen.X, currentScreen.Y))
            {
                return currentScreen - windowRect.TopLeft;
            }

            Vector2 currentClient = currentScreen - windowRect.TopLeft;
            float xmin = 0, ymin = 0, xmax = windowRect.Width, ymax = windowRect.Height;
            float t = 1.0f;

            // Safety: Only calculate t if there is actual movement on that axis to avoid DivByZero
            if (Math.Abs(targetClient.X - currentClient.X) > 0.01f)
            {
                if (currentClient.X < xmin) t = Math.Min(t, (xmin - currentClient.X) / (targetClient.X - currentClient.X));
                if (currentClient.X > xmax) t = Math.Min(t, (xmax - currentClient.X) / (targetClient.X - currentClient.X));
            }

            if (Math.Abs(targetClient.Y - currentClient.Y) > 0.01f)
            {
                if (currentClient.Y < ymin) t = Math.Min(t, (ymin - currentClient.Y) / (targetClient.Y - currentClient.Y));
                if (currentClient.Y > ymax) t = Math.Min(t, (ymax - currentClient.Y) / (targetClient.Y - currentClient.Y));
            }

            return currentClient + (targetClient - currentClient) * t;
        }

        // Credits: https://ben.land/post/2021/04/25/windmouse-human-mouse-movement/#the-code
        private static Random random = new Random();
        private static readonly double sqrt3 = Math.Sqrt(3);
        private static readonly double sqrt5 = Math.Sqrt(5);


        public static async SyncTask<bool> WindMouseImpl(InputHumanizer plugin, double startX, double startY, double destX, double destY,
                                      double gravity, double wind, int minWait,
                                      int maxWait, double maxStep, double targetArea, CancellationToken cancellationToken = default)
        {
            double dist, veloX = 0, veloY = 0, windX = 0, windY = 0;

            List<Point> positions = new List<Point>();

            while ((dist = Hypot(startX - destX, startY - destY)) >= 1)
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

                veloX += windX + gravity * (destX - startX) / dist;
                veloY += windY + gravity * (destY - startY) / dist;

                double veloMag = Hypot(veloX, veloY);

                if (veloMag > maxStep)
                {
                    double randomDist = maxStep / 2 + random.NextDouble() * maxStep / 2;
                    veloX = (veloX / veloMag) * randomDist;
                    veloY = (veloY / veloMag) * randomDist;
                }

                startX += veloX;
                startY += veloY;

                int mx = (int)Math.Round(startX);
                int my = (int)Math.Round(startY);

                positions.Add(new Point(mx, my));
            }

            plugin.DebugLog("InputHumanizer: Total points for MouseMove = " + positions.Count);

            var stopwatch = Stopwatch.StartNew();
            TimeSpan totalDelay = TimeSpan.Zero;

            foreach (var position in positions)
            {
                ExileCore2.Input.SetCursorPos(new Vector2(position.X, position.Y));

                int delay = random.Next(minWait, maxWait);

                totalDelay = totalDelay.Add(TimeSpan.FromMilliseconds(delay));

                if (stopwatch.Elapsed < totalDelay)
                {
                    plugin.DebugLog("InputHumanizer: we actually ended up sleeping in MouseMove");
                    await Task.Delay(totalDelay - stopwatch.Elapsed, cancellationToken);
                }
            }

            return true;
        }

        private static double Hypot(double dx, double dy)
        {
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
