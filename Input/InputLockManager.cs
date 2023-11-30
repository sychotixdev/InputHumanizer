using ExileCore.Shared;
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

        public async SyncTask<IInputController> GetController(string requestingPlugin, InputHumanizerSettings settings, CancellationToken cancellationToken = default)
        {
            await Semaphore.WaitAsync(cancellationToken);
            PluginWithSemaphore = requestingPlugin;
            return new InputController(settings, this);
        }

        public async SyncTask<IInputController> GetController(string requestingPlugin, InputHumanizerSettings settings, TimeSpan waitPeriod)
        {
            if (await Semaphore.WaitAsync(waitPeriod))
            {
                PluginWithSemaphore = requestingPlugin;
                return new InputController(settings, this);
            }

            // Unable to get controller in specified time
            return null;
        }

        public bool TryGetController(string requestingPlugin, InputHumanizerSettings settings, out IInputController controller)
        {
            if (Semaphore.Wait(TimeSpan.Zero))
            {
                PluginWithSemaphore = requestingPlugin;
                controller = new InputController(settings, this);
                return true;
            }

            controller = null;
            return false;
        }

        internal void ReleaseController()
        {
            PluginWithSemaphore = "None";
            Semaphore.Release();
        }
    }
}
