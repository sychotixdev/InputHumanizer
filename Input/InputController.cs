using ExileCore.Shared;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer.Input
{
    internal class InputController : IInputController
    {
        internal InputController(InputHumanizer plugin, InputHumanizerSettings settings, InputLockManager manager)
        {
            Plugin = plugin;
            Settings = settings;
            Manager = manager;
        }

        ~InputController()
        {
            Manager.ReleaseController();
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Manager.ReleaseController();
        }

        private InputHumanizer Plugin { get; }
        private InputHumanizerSettings Settings { get; }
        private InputLockManager Manager { get; }
        private Dictionary<Keys, DateTime> ButtonDelays = new Dictionary<Keys, DateTime>();

        public bool KeyDown(Keys key)
        {
            Plugin.DebugLog("KeyDown: " + key);
            ExileCore.Input.KeyDown(key);

            ButtonDelays[key] = DateTime.Now.AddMilliseconds(GenerateDelay());

            return true;
        }

        public async SyncTask<bool> KeyUp(Keys key, bool releaseImmediately = false, CancellationToken cancellationToken = default)
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
                    TimeSpan remainingDelay = releaseTime.Subtract(now);
                    Plugin.DebugLog("KeyUp remaining delay key:" + key + " delay: " + remainingDelay);
                    await Task.Delay(remainingDelay, cancellationToken);
                }
            }

            Plugin.DebugLog("KeyUp: " + key);
            // Delays should now be handled just fine
            ExileCore.Input.KeyUp(key);

            ButtonDelays.Remove(key);

            return true;
        }

        public async SyncTask<bool> Click(CancellationToken cancellationToken = default)
        {
            return await Click(MouseButtons.Left, null, cancellationToken);
        }

        public async SyncTask<bool> Click(MouseButtons button, CancellationToken cancellationToken = default)
        {
            return await Click(button, null, cancellationToken);
        }

        public async SyncTask<bool> Click(MouseButtons button, Vector2 coordinate, CancellationToken cancellationToken = default)
        {
            return await Click(button, (Vector2?)coordinate, cancellationToken);
        }

        private async SyncTask<bool> Click(MouseButtons button, Vector2? coordinate, CancellationToken cancellationToken = default)
        {
            // Only if a position is specified do we move the mouse
            if (coordinate != null)
            {
                if (!await MoveMouse(coordinate.Value, cancellationToken))
                    return false;
            }

            Plugin.DebugLog("Click Delay");
            // We will also have a delay on the click, not just the move.
            await Task.Delay(GenerateDelay(), cancellationToken);

            Plugin.DebugLog("Click " + button);
            // Delays should now be handled just fine
            ExileCore.Input.Click(button);

            Plugin.DebugLog("Click Delay 2");
            // Do we want to sleep TWICE here?
            await Task.Delay(GenerateDelay(), cancellationToken);

            return true;
        }

        public async SyncTask<bool> VerticalScroll(bool forward, int numClicks, CancellationToken cancellationToken = default)
        {
            return await VerticalScroll(forward, numClicks, null, cancellationToken);
        }

        public async SyncTask<bool> VerticalScroll(bool forward, int numClicks, Vector2? coordinate, CancellationToken cancellationToken = default)
        {
            // Only if a position is specified do we move the mouse
            if (coordinate != null)
            {
                if (!await MoveMouse(coordinate.Value, cancellationToken))
                    return false;
            }

            Plugin.DebugLog("Vertical Scroll Delay");
            // We will also have a delay on the click, not just the move.
            await Task.Delay(GenerateDelay(), cancellationToken);

            Plugin.DebugLog("Vertical Scroll");
            // Delays should now be handled just fine
            ExileCore.Input.VerticalScroll(forward, numClicks);

            Plugin.DebugLog("Vertical Scroll Delay 2");
            // Do we want to sleep TWICE here?
            await Task.Delay(GenerateDelay(), cancellationToken);

            return true;
        }

        public async SyncTask<bool> MoveMouse(Vector2 coordinate, CancellationToken cancellationToken = default)
        {
            return await MoveMouse(coordinate, Settings.MaximumInterpolationDistance, Settings.MinimumInterpolationDelay, Settings.MaximumInterpolationDelay, cancellationToken);
        }

        public async SyncTask<bool> MoveMouse(Vector2 coordinate, int maxInterpolationDistance, int minInterpolationDelay, int maxInterpolationDelay, CancellationToken cancellationToken = default)
        {
            Plugin.DebugLog("Mouse Move start");
            return await Mouse.MoveMouse(Plugin, coordinate, maxInterpolationDistance, minInterpolationDelay, maxInterpolationDelay, cancellationToken);
        }

        public int GenerateDelay()
        {
            return Delay.GetDelay(Settings.MinimumDelay, Settings.MaximumDelay, Settings.DelayMean, Settings.DelayStandardDeviation);
        }
    }
}
