using ExileCore.Shared;
using Kalon;
using Kalon.Native.Structs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var currentPosition = ExileCore.Input.ForceMousePositionNum;

            float distance = Vector2.Distance(currentPosition, targetPosition);
            float normalizedDistance = NormalizeDistance(distance, maxInterpolationDistance);

            float interpolatedValue = Lerp(minInterpolationDelay, maxInterpolationDelay, normalizedDistance);

            TimeSpan mouseSpeed = TimeSpan.FromMilliseconds(interpolatedValue + Random.Shared.Next(25, 100));

            var movements = CursorMover.GenerateMovements(new Point((int)currentPosition.X, (int)currentPosition.Y), new Point((int)targetPosition.X, (int)targetPosition.Y), (int)mouseSpeed.TotalMilliseconds);


            var stopwatch = Stopwatch.StartNew();
            TimeSpan totalDelay = TimeSpan.Zero;

            foreach ( var movement in movements)
            {
                // First, we need to loop through and spam SetCursorPos to get us to each location
                foreach(var point in movement.Points)
                {
                    ExileCore.Input.SetCursorPos(new Vector2(point.X, point.Y));
                }

                totalDelay = totalDelay.Add(movement.Delay);

                if (stopwatch.Elapsed < totalDelay)
                {
                    plugin.DebugLog("InputHumanizer: we actually ended up sleeping in MouseMove");
                    await Task.Delay(totalDelay - stopwatch.Elapsed, cancellationToken);
                }
            }

            return true;
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
                ExileCore.Input.SetCursorPos(new Vector2(position.X, position.Y));

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
