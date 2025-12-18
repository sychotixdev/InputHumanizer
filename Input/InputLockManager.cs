using ExileCore2.Shared;
using ExileCore2.Shared.Interfaces;
using System;
using System.Threading;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer.Input
{
    public class InputLockManager
    {
        public static InputLockManager Instance { get; } = new InputLockManager();

        public string PluginWithSemaphore { get; private set; } = "None";

        private SemaphoreSlim Semaphore { get; set; } = new(1);

        private InputLockManager() { }

        public async SyncTask<IInputController> GetInputControllerLock(string requestingPlugin, InputHumanizer plugin, InputHumanizerSettings settings, CancellationToken cancellationToken = default)
        {
            await Semaphore.WaitAsync(cancellationToken);
            PluginWithSemaphore = requestingPlugin;
            return new InputController(plugin, settings, this);
        }

        public async SyncTask<IInputController> GetInputControllerLock(string requestingPlugin, InputHumanizer plugin, InputHumanizerSettings settings, TimeSpan waitPeriod)
        {
            if (await Semaphore.WaitAsync(waitPeriod))
            {
                PluginWithSemaphore = requestingPlugin;
                return new InputController(plugin, settings, this);
            }

            return null;
        }

        public IInputController TryGetInputController(string requestingPlugin, InputHumanizer plugin, InputHumanizerSettings settings)
        {
            if (Semaphore.Wait(TimeSpan.Zero))
            {
                PluginWithSemaphore = requestingPlugin;
                return new InputController(plugin, settings, this);
            }

            return null;
        }

        public void ReleaseController()
        {
            PluginWithSemaphore = "None";
            Semaphore.Release();
        }
    }
}
