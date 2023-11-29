using ExileCore;
using ExileCore.Shared;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using InputHumanizer.Input;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using Vanara.PInvoke;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer
{
    public class InputHumanizer : BaseSettingsPlugin<InputHumanizerSettings>
    {
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

        public async SyncTask<IInputController> GetInputController(string requestingPlugin, TimeSpan waitTime)
        {

            IInputController controller = await InputLockManager.Instance.GetController(requestingPlugin, Settings, waitTime);
            if (controller == null)
            {
                LogError("InputHumanizer - Plugin " + requestingPlugin + " requested input controller but " + InputLockManager.Instance.PluginWithSemaphore + " is still holding it. Try your action again later.");
                return null;
            }

            return controller;
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