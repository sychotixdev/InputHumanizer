using System;

namespace InputHumanizer.Input
{
    internal class Delay
    {
        public static int GetDelay(int minimumDelay, int maximumDelay, int mean, int standardDeviation) => (int)MathF.Max(minimumDelay, MathF.Min(maximumDelay, NextGaussian(mean, standardDeviation)));

        private static float NextGaussian(int mean, int standardDeviation)
        {
            float u1 = 1.0f - (float)Random.Shared.NextDouble();
            float u2 = 1.0f - (float)Random.Shared.NextDouble();

            float randomStandardNormal = MathF.Sqrt(-2.0f * MathF.Log(u1)) * MathF.Sin(2.0f * MathF.PI * u2);

            return mean + standardDeviation * randomStandardNormal;
        }
    }
}
