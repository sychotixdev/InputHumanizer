using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System;
using System.Collections.Generic;
using System.Threading;using System.Windows.Forms;
using Vanara.PInvoke;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer
{
    public class InputHumanizer : BaseSettingsPlugin<InputHumanizerSettings>
    {
        private object Lock = new object();
        private string pluginWithlock = "None";
        private Dictionary<Keys, DateTime> ButtonDelays = new Dictionary<Keys, DateTime>();

        public override bool Initialise()
        {
            Name = "InputHumanizer";
            return true;
        }

        public override void AreaChange(AreaInstance area)
        {

        }

        public override void Render()
        {
            base.Render();
        }

        public bool ObtainLock(string requestingPlugin, TimeSpan waitTime)
        {
            bool gotLock = Monitor.TryEnter(Lock, waitTime);
            if (!gotLock)
            {
                LogError("InputHumanizer - Plugin " + requestingPlugin + " requested input lock but " + pluginWithlock + " is still holding it. Try your action again later.");
            }
            else
            {
                // We got the lock, lets save off our name for debugging purposes.
                pluginWithlock = requestingPlugin;
            }

            return gotLock;
        }

        public bool KeyDown(string requestingPlugin, Keys key)
        {
            if (!ConfirmRequestorHasLock(requestingPlugin))
                return false;

            Input.KeyDown(key);

            ButtonDelays[key] = DateTime.Now.AddMilliseconds(GenerateDelay());

            return true;
        }

        public bool KeyUp(string requestingPlugin, Keys key, bool releaseImmediately=false)
        {
            if (!ConfirmRequestorHasLock(requestingPlugin))
                return false;

            // If we were told to release immediately, skip this logic
            if (!releaseImmediately)
            {
                // Pull the generated delay for this key
                ButtonDelays.TryGetValue(key, out DateTime releaseTime);

                // Save off the 'now' date once since we use it multiple times
                DateTime now = DateTime.Now;

                // If we are not allowed to release this key yet, we need to sleep until we can
                if (now < releaseTime)
                {
                    TimeSpan remainingDelay = now.Subtract(releaseTime);
                    Thread.Sleep(remainingDelay);
                }
            }

            // Delays should now be handled just fine
            Input.KeyUp(key);

            ButtonDelays.Remove(key);

            return true;
        }

        public bool Click(string requestingPlugin, int x=0, int y=0)
        {
            if (!ConfirmRequestorHasLock(requestingPlugin))
                return false;

            // Only if a position is specified do we move the mouse
            if (x != 0 && y != 0)
            {
                if (!MoveMouse(requestingPlugin, x, y))
                    return false;
            }

            // We will also have a delay on the click, not just the move.
            Thread.Sleep(GenerateDelay());

            // Delays should now be handled just fine
            User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN | User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP, 0, 0, 0, (IntPtr)0);

            // Do we want to sleep TWICE here?
            Thread.Sleep(GenerateDelay());

            return true;
        }

        public bool MoveMouse(string requestingPlugin, int x, int y)
        {
            return MoveMouse(requestingPlugin, x, y, Settings.MaximumInterpolationDistance, Settings.MinimumInterpolationDelay, Settings.MaximumInterpolationDelay);
        }

        public bool MoveMouse(string requestingPlugin, int x, int y, int maxInterpolationDistance, int minInterpolationDelay, int maxInterpolationDelay)
        {
            if (!ConfirmRequestorHasLock(requestingPlugin))
                return false;

            Mouse.MoveMouse(new System.Numerics.Vector2(x, y), maxInterpolationDistance, minInterpolationDelay, maxInterpolationDelay);

            return true;
        }

        public void ReleaseLock(string requestingPlugin)
        {
            ButtonDelays.Clear();
            Monitor.Exit(Lock);
        }

        private bool ConfirmRequestorHasLock(string requestingPlugin)
        {
            if (!Monitor.IsEntered(Lock))
            {
                LogError("InputHumanizer - Plugin " + requestingPlugin + " requested input but did not have a lock. This is a plugin error.");
                return false;
            }

            return true;
        }

        public int GenerateDelay()
        {
            return Delay.GetDelay(Settings.MinimumDelay, Settings.MaximumDelay, Settings.DelayMean, Settings.DelayStandardDeviation);
        }

        public class InputHumanizerSettings : ISettings
        {
            public InputHumanizerSettings()
            {
                
            }

            [Menu("Minimum Interpolation Delay", "Minimum Delay in Milliseconds")]
            public RangeNode<int> MinimumInterpolationDelay { get; set; } = new(0, 0, 1000);

            [Menu("Maximum Interpolation Delay", "Maximum Delay in Milliseconds")]
            public RangeNode<int> MaximumInterpolationDelay { get; set; } = new(1000, 0, 1000);

            [Menu("Maximum Interpolation Distance")]
            public RangeNode<int> MaximumInterpolationDistance { get; set; } = new(2560, 0, 2560);

            [Menu("Delay Mean", "Mean of the Gaussian Distribution in Milliseconds")]
            public RangeNode<int> DelayMean { get; set; } = new(100, 0, 1000);

            [Menu("Delay Standard Deviation", "Standard Deviation of the Gaussian Distribution in Milliseconds")]
            public RangeNode<int> DelayStandardDeviation { get; set; } = new(50, 0, 1000);

            [Menu("Minimum Delay", "Minimum Delay in Milliseconds")]
            public RangeNode<int> MinimumDelay { get; set; } = new(50, 0, 1000);

            [Menu("Maximum Delay", "Maximum Delay in Milliseconds")]
            public RangeNode<int> MaximumDelay { get; set; } = new(150, 0, 1000);

            public ToggleNode Enable { get; set; } = new ToggleNode(true);
        }
    }
}