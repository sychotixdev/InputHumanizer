using ExileCore.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static InputHumanizer.InputHumanizer;

namespace InputHumanizer.Input
{
    internal class InputLockManager
    {
        private static InputLockManager _instance = new InputLockManager();
        public static InputLockManager Instance { get { return _instance; }  private set { } }

        public String PluginWithSemaphore { get; private set; }


        private SemaphoreSlim Semaphore { get; set; }

        private InputLockManager() {
            Semaphore = new SemaphoreSlim(1);
            PluginWithSemaphore = "None";
        }

        public async SyncTask<IInputController> GetController(String requestingPlugin, InputHumanizerSettings settings, TimeSpan waitPeriod)
        {
            if (await Semaphore.WaitAsync(waitPeriod))
            {
                PluginWithSemaphore = requestingPlugin;
                return new InputController(settings, this);
            }

            // Unable to get controller in specified time
            return null;
        }

        public void ReleaseController()
        {
            PluginWithSemaphore = "None";
            Semaphore.Release();
        }
    }
}
