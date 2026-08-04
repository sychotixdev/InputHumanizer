using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static InputHumanizer.InputHumanizer;

#if POE1
using Core = ExileCore;
using Shared = ExileCore.Shared;

#else
using Core = ExileCore2;
using Shared = ExileCore2.Shared;

#endif

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
            // No pipe I/O from the finalizer thread - just make sure the lock can't
            // leak if a consumer never disposed us. The hook's own inactivity timeout
            // handles handing manual control back in that pathological case.
            Manager.ReleaseController();
        }

        public void Dispose()
        {
            // NOTE: the old `_ = ReleaseControl();` here was a no-op - SyncTasks are
            // cooperatively pumped, so a discarded SyncTask never executes its body and
            // the ClientIdle pipe command was never sent. Go through the plain-Task
            // pipe API synchronously instead (same pattern as BackgroundInput.Dispose),
            // and do it BEFORE releasing the semaphore so the next acquirer can't race
            // an in-flight release.
            ReleaseControlSync();
            GC.SuppressFinalize(this);
            Manager.ReleaseController();
        }

        // True once a ClientIdle has been successfully delivered (or there was no
        // background pipe to deliver it to), so ReleaseControl() followed by Dispose()
        // doesn't send it twice.
        private bool _released;

        private void ReleaseControlSync()
        {
            if (_released)
                return;

            try
            {
                var background = Plugin.GetBackgroundInputController();
                if (background != null)
                {
                    Plugin.DebugLog("Releasing control (sync, from Dispose).");
                    background.ReleaseControlAsync(1000).GetAwaiter().GetResult();
                }

                _released = true;
            }
            catch
            {
                // Best-effort: a dead pipe still gets cleaned up by the hook's
                // inactivity timeout.
            }
        }

        private InputHumanizer Plugin { get; }
        private InputHumanizerSettings Settings { get; }
        private InputLockManager Manager { get; }
        private Dictionary<Keys, DateTime> ButtonDelays = new Dictionary<Keys, DateTime>();

        int minDelayOverride = -1;
        int maxDelayOverride = -1;
        int meanDelayOverride = -1;
        int standardDeviationDelayOverride = -1;

        public async Shared.SyncTask<bool> KeyDown(Keys key, CancellationToken cancellationToken = default)
        {
            await Task.Delay(GenerateDelay(), cancellationToken);

            Plugin.DebugLog("KeyDown: " + key);

            if (Plugin.GetBackgroundInputController() != null)
            {
                await Plugin.GetBackgroundInputController().KeyDownAsync((int)key);
            }
            else
            {
                Core.Input.KeyDown(key);
            }


            ButtonDelays[key] = DateTime.Now.AddMilliseconds(GenerateDelay());
            return true;
        }

        public async Shared.SyncTask<bool> KeyUp(Keys key, bool releaseImmediately = false, CancellationToken cancellationToken = default)
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

            if (Plugin.GetBackgroundInputController() != null)
            {
                await Plugin.GetBackgroundInputController().KeyUpAsync((int)key);
            }
            else
            {
                Core.Input.KeyUp(key);
            }

            ButtonDelays.Remove(key);

            return true;
        }

        public async Shared.SyncTask<bool> Click(CancellationToken cancellationToken = default)
        {
            return await Click(MouseButtons.Left, null, cancellationToken);
        }

        public async Shared.SyncTask<bool> Click(MouseButtons button, CancellationToken cancellationToken = default)
        {
            return await Click(button, null, cancellationToken);
        }

        public async Shared.SyncTask<bool> Click(MouseButtons button, Vector2? coordinate, CancellationToken cancellationToken = default)
        {
            return await ClickWithModifiers(button, coordinate, MouseModifiers.None, cancellationToken);
        }

        public async Shared.SyncTask<bool> ClickWithModifiers(MouseButtons button, Vector2? coordinate, MouseModifiers modifiers, CancellationToken cancellationToken = default)
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
            if (Settings.UseBackgroundInput && Plugin.GetBackgroundInputController() != null)
            {
                await Plugin.GetBackgroundInputController().ClickAsync(button == MouseButtons.Right, coordinate, modifiers);
            }
            else
            {
                // 1. Press Modifiers Down
                List<Keys> pressedModifiers = new List<Keys>();
                if (modifiers.HasFlag(MouseModifiers.Ctrl)) pressedModifiers.Add(Keys.ControlKey);
                if (modifiers.HasFlag(MouseModifiers.Shift)) pressedModifiers.Add(Keys.ShiftKey);
                if (modifiers.HasFlag(MouseModifiers.Alt)) pressedModifiers.Add(Keys.Menu); // Menu is the Alt key in WinForms Keys


                foreach (var key in pressedModifiers)
                {
                    await KeyDown(key, cancellationToken);
                }

                if (pressedModifiers.Count > 0)
                {
                    // Delay after pressing modifiers
                    await Task.Delay(GenerateDelay(), cancellationToken);
                }

                Core.Input.Click(button);

                if (pressedModifiers.Count > 0)
                {
                    // Delay before releasing modifiers
                    await Task.Delay(GenerateDelay(), cancellationToken);
                }

                // 3. Release Modifiers Up (in reverse order for natural feel)
                pressedModifiers.Reverse();
                foreach (var key in pressedModifiers)
                {
                    await KeyUp(key, false, cancellationToken);
                }
            }


            Plugin.DebugLog("Click Delay 2");
            // Do we want to sleep TWICE here?
            await Task.Delay(GenerateDelay(), cancellationToken);

            return true;
        }

        public async Shared.SyncTask<bool> VerticalScroll(bool forward, int numClicks, CancellationToken cancellationToken = default)
        {
            return await VerticalScroll(forward, numClicks, null, cancellationToken);
        }

        public async Shared.SyncTask<bool> VerticalScroll(bool forward, int numClicks, Vector2? coordinate, CancellationToken cancellationToken = default)
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
            if (Plugin.GetBackgroundInputController() != null)
            {
                var scrollAmount = (forward ? numClicks : -numClicks) * 120;
                await Plugin.GetBackgroundInputController().MouseWheelAsync(scrollAmount);
            }
            else
            {
                Core.Input.VerticalScroll(forward, numClicks);
            }

            Plugin.DebugLog("Vertical Scroll Delay 2");
            // Do we want to sleep TWICE here?
            await Task.Delay(GenerateDelay(), cancellationToken);

            return true;
        }

        public async Shared.SyncTask<bool> MoveMouse(Vector2 coordinate, CancellationToken cancellationToken = default)
        {
            if (Settings.UseWindMouse)
            {
                return await MoveMouseWindMouseImpl(coordinate, Settings.GravityStrength, Settings.WindStrength, Settings.WindMouseMinimumDelay, Settings.WindMouseMaximumDelay, Settings.StepSize, Settings.TargetArea, cancellationToken);
            }
            else
            {
                return await MoveMouse(coordinate, Settings.MaximumInterpolationDistance, Settings.MinimumInterpolationDelay, Settings.MaximumInterpolationDelay, cancellationToken);
            }
        }

        public async Shared.SyncTask<bool> MoveMouse(Vector2 coordinate, int maxInterpolationDistance, int minInterpolationDelay, int maxInterpolationDelay, CancellationToken cancellationToken = default)
        {
            Plugin.DebugLog("Mouse Move start");
            return await Mouse.MoveMouse(Plugin, coordinate, maxInterpolationDistance, minInterpolationDelay, maxInterpolationDelay, cancellationToken);
        }

        public async Shared.SyncTask<bool> MoveMouseWindMouseImpl(Vector2 coordinate, double gravityStrength, double windStrength, int minInterpolationDelay, int maxInterpolationDelay, double stepSize, double targetArea, CancellationToken cancellationToken = default)
        {
            Plugin.DebugLog("Mouse Move start");
            var startPoint = Core.Input.ForceMousePosition;

            return await Mouse.WindMouseImpl(Plugin, startPoint.X, startPoint.Y, coordinate.X, coordinate.Y, gravityStrength, windStrength, minInterpolationDelay, maxInterpolationDelay, stepSize, targetArea, cancellationToken);
        }

        public int GenerateDelay()
        {
            var minDelay = minDelayOverride != -1 ? minDelayOverride : Settings.MinimumDelay;
            var maxDelay = maxDelayOverride != -1 ? maxDelayOverride : Settings.MaximumDelay;
            var mean = meanDelayOverride != -1 ? meanDelayOverride : Settings.DelayMean;
            var standardDeviation = standardDeviationDelayOverride != -1 ? standardDeviationDelayOverride : Settings.DelayStandardDeviation;

            return Delay.GetDelay(minDelay, maxDelay, mean, standardDeviation);
        }

        public async Shared.SyncTask<bool> ClearMousePosition(CancellationToken cancellationToken = default)
        {
            if (Plugin.GetBackgroundInputController() != null)
            {
                Plugin.DebugLog("Clearing mouse position.");
                return await Plugin.GetBackgroundInputController().ClearCursorAsync();
            }

            return true;
        }


        public async Shared.SyncTask<Vector2?> GetCursorPos(CancellationToken cancellationToken = default)
        {
            Plugin.DebugLog("Getting cursor position");

            if (Plugin.GetBackgroundInputController() != null)
            {
                return await Plugin.GetBackgroundInputController().GetForcedCursorPositionAsync();
            }
            else
            {
                var pos = Core.Input.ForceMousePosition;
                return new Vector2(pos.X, pos.Y);
            }

        }

        public async Shared.SyncTask<bool> ReleaseControl(CancellationToken cancellationToken = default)
        {
            if (Plugin.GetBackgroundInputController() != null)
            {
                Plugin.DebugLog("Releasing control.");
                var ok = await Plugin.GetBackgroundInputController().ReleaseControlAsync();
                if (ok)
                    _released = true; // Dispose() can skip its duplicate ClientIdle.
                return ok;
            }

            _released = true;
            return true;
        }

        public void SetDelayOverrides(int minDelay = -1, int maxDelay = -1, int mean = -1, int standardDeviation = -1)
        {
            minDelayOverride = minDelay;
            maxDelayOverride = maxDelay;
            meanDelayOverride = mean;
            standardDeviationDelayOverride = standardDeviation;
        }
    }
}
