using ExileCore.Shared;
using Kalon;
using Kalon.Native.Structs;
using System;
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

        public static async SyncTask<bool> MoveMouse(Vector2 targetPosition, int maxInterpolationDistance = 700, int minInterpolationDelay = 0, int maxInterpolationDelay = 300, CancellationToken cancellationToken = default)
        {
            var currentPosition = ExileCore.Input.ForceMousePositionNum;

            float distance = Vector2.Distance(currentPosition, targetPosition);
            float normalizedDistance = NormalizeDistance(distance, maxInterpolationDistance);

            float interpolatedValue = Lerp(minInterpolationDelay, maxInterpolationDelay, normalizedDistance);

            TimeSpan mouseSpeed = TimeSpan.FromMilliseconds(interpolatedValue + Random.Shared.Next(25, 100));

            var movements = CursorMover.GenerateMovements(new Point((int)currentPosition.X, (int)currentPosition.Y), new Point((int)targetPosition.X, (int)targetPosition.Y), (int)mouseSpeed.TotalMilliseconds);

            foreach( var movement in movements)
            {
                // First, we need to loop through and spam SetCursorPos to get us to each location
                foreach(var point in movement.Points)
                {
                    ExileCore.Input.SetCursorPos(new Vector2(point.X, point.Y));
                }

                await Task.Delay(movement.Delay, cancellationToken);
            }

            return true;
        }
    }
}
