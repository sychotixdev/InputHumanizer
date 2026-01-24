using InputHumanizer.Input;
using System;
using static InputHumanizer.InputHumanizer;


#if POE1
using Core = ExileCore;
using Components = ExileCore.PoEMemory.Components;
using Elements = ExileCore.PoEMemory.Elements;
using MemoryObjects = ExileCore.PoEMemory.MemoryObjects;
using Shared = ExileCore.Shared;
using SharedEnums = ExileCore.Shared.Enums;
using Attributes = ExileCore.Shared.Attributes;
using Interfaces = ExileCore.Shared.Interfaces;
using Nodes = ExileCore.Shared.Nodes;

#else
using Core = ExileCore2;
using Shared = ExileCore2.Shared;
using Attributes = ExileCore2.Shared.Attributes;
using Interfaces = ExileCore2.Shared.Interfaces;
using Nodes = ExileCore2.Shared.Nodes;
#endif

namespace InputHumanizer
{
    public class InputHumanizer : Core.BaseSettingsPlugin<InputHumanizerSettings>
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
#if POE1
        public override Core.Job Tick()
        {
            TickLogic();
            return base.Tick();
        }

#else
    public override void Tick()
    {
        base.Tick();
        TickLogic();
    }

#endif

        private void TickLogic()
        {
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

        public async Shared.SyncTask<IInputController> GetInputController(string requestingPlugin, TimeSpan waitTime)
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

        public class InputHumanizerSettings : Interfaces.ISettings
        {
            [Attributes.Menu("Use Background Input", "Enables Background Input")]
            public Nodes.ToggleNode UseBackgroundInput { get; set; } = new Nodes.ToggleNode(false);

            [Attributes.Menu("Minimum Interpolation Delay", "Minimum Delay in Milliseconds")]
            public Nodes.RangeNode<int> MinimumInterpolationDelay { get; set; } = new(0, 0, 1000);

            [Attributes.Menu("Maximum Interpolation Delay", "Maximum Delay in Milliseconds")]
            public Nodes.RangeNode<int> MaximumInterpolationDelay { get; set; } = new(1000, 0, 1000);

            [Attributes.Menu("Maximum Interpolation Distance")]
            public Nodes.RangeNode<int> MaximumInterpolationDistance { get; set; } = new(2560, 0, 2560);

            [Attributes.Menu("Delay Mean", "Mean of the Gaussian Distribution in Milliseconds")]
            public Nodes.RangeNode<int> DelayMean { get; set; } = new(100, 0, 1000);

            [Attributes.Menu("Delay Standard Deviation", "Standard Deviation of the Gaussian Distribution in Milliseconds")]
            public Nodes.RangeNode<int> DelayStandardDeviation { get; set; } = new(50, 0, 1000);

            [Attributes.Menu("Minimum Delay", "Minimum Delay in Milliseconds")]
            public Nodes.RangeNode<int> MinimumDelay { get; set; } = new(50, 0, 1000);

            [Attributes.Menu("Maximum Delay", "Maximum Delay in Milliseconds")]
            public Nodes.RangeNode<int> MaximumDelay { get; set; } = new(150, 0, 1000);


            [Attributes.Menu("Use Wind Mouse", "Enables Wind Mouse Algorithm")]

            public Nodes.ToggleNode UseWindMouse { get; set; } = new Nodes.ToggleNode(false);

            [Attributes.Menu("Wind Strength", "Wind Mouse magnitude of the wind force fluctuations")]

            public Nodes.RangeNode<float> WindStrength { get; set; } = new Nodes.RangeNode<float>(2.0f, 0.0f, 10.0f);
            [Attributes.Menu("Gravity Strength", "Wind Mouse magnitude of the gravitational fornce")]

            public Nodes.RangeNode<float> GravityStrength { get; set; } = new Nodes.RangeNode<float>(9.0f, 0.0f, 15.0f);

            [Attributes.Menu("Step size", "Wind Mouse maximum step size (velocity clip threshold)")]

            public Nodes.RangeNode<float> StepSize { get; set; } = new Nodes.RangeNode<float>(10.0f, 0.0f, 30.0f);

            [Attributes.Menu("Target Area", "Wind Mouse distance where wind behavior changes from random to damped")]

            public Nodes.RangeNode<float> TargetArea { get; set; } = new Nodes.RangeNode<float>(12.0f, 0.0f, 30.0f);

            [Attributes.Menu("Wind Mouse Minimum Delay", "Minimum Delay in Milliseconds")]
            public Nodes.RangeNode<int> WindMouseMinimumDelay { get; set; } = new(5, 0, 100);

            [Attributes.Menu("Wind Mouse Maximum Delay", "Maximum Delay in Milliseconds")]
            public Nodes.RangeNode<int> WindMouseMaximumDelay { get; set; } = new(20, 0, 1000);

            public Nodes.ToggleNode Enable { get; set; } = new Nodes.ToggleNode(true);
            public Nodes.ToggleNode Debug { get; set; } = new Nodes.ToggleNode(false);
        }
    }
}