using System.Runtime.InteropServices;

namespace InputHumanizer.Input
{
    /// <summary>
    /// One cursor position and the wait before it, in milliseconds. Sent as-is over the
    /// background input pipe, so the layout must not change.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CursorPointWithDelay
    {
        public int X;
        public int Y;
        public uint DelayMs;

        public CursorPointWithDelay(int x, int y, uint delayMs)
        {
            X = x;
            Y = y;
            DelayMs = delayMs;
        }
    }
}
