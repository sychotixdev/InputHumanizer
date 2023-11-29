using ExileCore.Shared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Vanara.PInvoke;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer.Input
{
    internal class InputController : IInputController
    {
        internal InputController(InputHumanizerSettings settings, InputLockManager manager)
        {
            this.Settings = settings;
            this.Manager = manager;
        }

        ~InputController()
        {
            Manager.ReleaseController();
        }
        
        private InputHumanizerSettings Settings { get; }
        private InputLockManager Manager { get; }
        private Dictionary<Keys, DateTime> ButtonDelays = new Dictionary<Keys, DateTime>();

        public bool KeyDown(Keys key)
        {
            ExileCore.Input.KeyDown(key);

            ButtonDelays[key] = DateTime.Now.AddMilliseconds(GenerateDelay());

            return true;
        }

        public async SyncTask<bool> KeyUp(Keys key, bool releaseImmediately = false)
        {// If we were told to release immediately, skip this logic
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
                    await Task.Delay(remainingDelay);
                }
            }

            // Delays should now be handled just fine
            ExileCore.Input.KeyUp(key);

            ButtonDelays.Remove(key);

            return true;
        }

        public async SyncTask<bool> Click(string requestingPlugin, int x = 0, int y = 0)
        {
            // Only if a position is specified do we move the mouse
            if (x != 0 && y != 0)
            {
                if (!await MoveMouse(x, y))
                    return false;
            }

            // We will also have a delay on the click, not just the move.
            await Task.Delay(GenerateDelay());

            // Delays should now be handled just fine
            User32.mouse_event(User32.MOUSEEVENTF.MOUSEEVENTF_LEFTDOWN | User32.MOUSEEVENTF.MOUSEEVENTF_LEFTUP, 0, 0, 0, (IntPtr)0);

            // Do we want to sleep TWICE here?
            await Task.Delay(GenerateDelay());

            return true;
        }

        public async SyncTask<bool> MoveMouse(int x, int y)
        {
            return await MoveMouse(x, y, Settings.MaximumInterpolationDistance, Settings.MinimumInterpolationDelay, Settings.MaximumInterpolationDelay);
        }

        public async SyncTask<bool> MoveMouse(int x, int y, int maxInterpolationDistance, int minInterpolationDelay, int maxInterpolationDelay)
        {
            return await Mouse.MoveMouse(new System.Numerics.Vector2(x, y), maxInterpolationDistance, minInterpolationDelay, maxInterpolationDelay);
        }

        public int GenerateDelay()
        {
            return Delay.GetDelay(Settings.MinimumDelay, Settings.MaximumDelay, Settings.DelayMean, Settings.DelayStandardDeviation);
        }

    }
}
