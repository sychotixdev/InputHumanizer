# InputHumanizer

An ExileAPI plugin designed to add a single location for all plugins to easily add more human-like inputs. This includes more human-like mouse movement and configurable delays which will apply to all plugins using the library.

**Installation:**

* Place this plugin in the ```/Plugins/Source/``` directory for ExileAPI.

* Place this library in the root directory. https://github.com/sychotixdev/InputHumanizerLib/releases


**Usage:**

* Add a reference to the InputHumanizerLib to gain access to the IInputController interface.
* Use one of the two PluginBridge methods to get an IInputController. This controller will give you exclusive access (within the framework of the plugin) to inputs until the object has been disposed. Ensure that you dispose of the object, or you may impact other plugins.


**An example use:**

```
var tryGetInputController = GameController.PluginBridge.GetMethod<Func<string, IInputController>>("InputHumanizer.TryGetInputController");
if (tryGetInputController == null)
{
    LogError("InputHumanizer method not registered.");
    return false;
}

if ((_inputController = tryGetInputController(this.Name)) != null)
{
    using (_inputController)
    {

    }
}
```
