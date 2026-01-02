using ExileCore2;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using InputHumanizer.Input;
using System;
using System.Threading;
using System.Threading.Tasks;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer
{
    public class InputHumanizer : BaseSettingsPlugin<InputHumanizerSettings>
    {
        private BackgroundInput BackgroundInput = null;

        public InputHumanizer()
        {
            BackgroundInput = new BackgroundInput(this);
        }

        public override bool Initialise()
        {
            GameController.PluginBridge.SaveMethod("InputHumanizer.TryGetInputController", (string requestingPlugin) =>
            {
                IInputController controller;
                if (TryGetInputController(requestingPlugin, out controller))
                {
                    return controller;
                }
                return null;
            });

            GameController.PluginBridge.SaveMethod("InputHumanizer.GetInputController", (string requestingPlugin, TimeSpan waitTime) =>
            {
                return GetInputController(requestingPlugin, waitTime);
            });

            if (!BackgroundInput.IsConnected && Settings.UseBackgroundInput.Value == true)
            {
                // If we are not connected and using background input, try to connect now
                BackgroundInput.ConnectAsync().Wait();
                LogMessage(("InputHumanizer - Background Input Controller connected: " + BackgroundInput.IsConnected));
            }

            Settings.UseBackgroundInput.OnValueChanged += (sender, e) =>
            {
                // If it is being enabled and we aren't connected... try to connect
                if (e == true)
                {
                    // Try to connect
                    if (!BackgroundInput.IsConnected)
                    {
                        BackgroundInput.ConnectAsync().Wait();
                    }
                }
            };

            return true;
        }

        public override void Tick()
        {
            base.Tick();

            var backgroundInputController = GetBackgroundInputController();
            if (backgroundInputController != null)
            {
                //_ = backgroundInputController.SendPingAsync();
            }
        }

        public BackgroundInput GetBackgroundInputController()
        {
            if (Settings.UseBackgroundInput.Value == true)
            {
                return BackgroundInput;
            }
            return null;
        }

        public async SyncTask<IInputController> GetInputController(string requestingPlugin, TimeSpan waitTime)
        {
            IInputController controller = await InputLockManager.Instance.GetInputControllerLock(requestingPlugin, this, Settings, waitTime);
            if (controller == null)
            {
                LogError($"InputHumanizer - Plugin {requestingPlugin} requested input controller but {InputLockManager.Instance.PluginWithSemaphore} is still holding it. Try your action again later.");
            }
            else
            {
                DebugLog("Plugin: " + requestingPlugin + " successfully got input controller lock.");
            }

            return controller;
        }

        public bool TryGetInputController(string requestingPlugin, out IInputController controller)
        {
            controller = InputLockManager.Instance.TryGetInputController(requestingPlugin, this, Settings);
            bool wasSuccess = controller != null;
            if (wasSuccess)
            {
                DebugLog("Plugin: " + requestingPlugin + " successfully got input controller lock.");
            }
            else
            {
                DebugLog("Plugin: " + requestingPlugin + " failed to get input lock.");
            }

            return wasSuccess;
        }

        public void DebugLog(String message)
        {
            if (Settings.Debug)
            {
                LogMsg(message);
            }
        }

        public class InputHumanizerSettings : ISettings
        {
            [Menu("Use Background Input", "Enables Background Input")]
            public ToggleNode UseBackgroundInput { get; set; } = new ToggleNode(false);

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


            [Menu("Use Wind Mouse", "Enables Wind Mouse Algorithm")]

            public ToggleNode UseWindMouse { get; set; } = new ToggleNode(false);

            [Menu("Wind Strength", "Wind Mouse magnitude of the wind force fluctuations")]

            public RangeNode<float> WindStrength { get; set; } = new RangeNode<float>(2.0f, 0.0f, 10.0f);
            [Menu("Gravity Strength", "Wind Mouse magnitude of the gravitational fornce")]

            public RangeNode<float> GravityStrength { get; set; } = new RangeNode<float>(9.0f, 0.0f, 15.0f);

            [Menu("Step size", "Wind Mouse maximum step size (velocity clip threshold)")]

            public RangeNode<float> StepSize { get; set; } = new RangeNode<float>(10.0f, 0.0f, 30.0f);

            [Menu("Target Area", "Wind Mouse distance where wind behavior changes from random to damped")]

            public RangeNode<float> TargetArea { get; set; } = new RangeNode<float>(12.0f, 0.0f, 30.0f);

            [Menu("Wind Mouse Minimum Delay", "Minimum Delay in Milliseconds")]
            public RangeNode<int> WindMouseMinimumDelay { get; set; } = new(5, 0, 100);

            [Menu("Wind Mouse Maximum Delay", "Maximum Delay in Milliseconds")]
            public RangeNode<int> WindMouseMaximumDelay { get; set; } = new(20, 0, 1000);

            public ToggleNode Enable { get; set; } = new ToggleNode(true);
            public ToggleNode Debug { get; set; } = new ToggleNode(false);
        }
    }
}