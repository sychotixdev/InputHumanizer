using Kalon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace InputHumanizer
{
    internal class Mouse
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT cursorPosition);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public Int32 X;
            public Int32 Y;
        }

        public static POINT GetCursorPosition()
        {
            GetCursorPos(out var cursorPosition);

            return cursorPosition;
        }

        private static float NormalizeDistance(float distance, float maxDistance)
        {
            return Math.Min(distance / maxDistance, 1.0f);
        }

        private static float Lerp(float startValue, float endValue, float interpolationFactor)
        {
            return (1 - interpolationFactor) * startValue + interpolationFactor * endValue;
        }

        public static void MoveMouse(Vector2 targetPosition, int maxInterpolationDistance = 700, int minInterpolationDelay = 0, int maxInterpolationDelay = 300)
        {
            POINT currentPosition = GetCursorPosition();

            float distance = Vector2.Distance(new Vector2(currentPosition.X, currentPosition.Y), targetPosition);
            float normalizedDistance = NormalizeDistance(distance, maxInterpolationDistance);

            float interpolatedValue = Lerp(minInterpolationDelay, maxInterpolationDelay, normalizedDistance);

            TimeSpan mouseSpeed = TimeSpan.FromMilliseconds(interpolatedValue + Random.Shared.Next(25, 100));

            CursorMover.MoveCursor((int)targetPosition.X, (int)targetPosition.Y, mouseSpeed);
        }
    }
}
