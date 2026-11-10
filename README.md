# InputHumanizer

An ExileAPI plugin designed to add a single location for all plugins to easily add more human-like inputs. This includes more human-like mouse movement and configurable delays which will apply to all plugins using the library.

**Installation:**

* Place this plugin in the ```/Plugins/Source/``` directory for ExileAPI.

* Place this library in the root directory. https://github.com/sychotixdev/InputHumanizerLib/releases

**ClickBot movement (optional):**

* **Use ClickBot** moves the mouse along paths from a ClickBot3000 model. It needs the ClickBotPaths plugin, which is distributed separately and is not part of this repository. When that plugin isn't installed, or fails, InputHumanizer logs it once and uses Wind Mouse instead.
* **ClickBot Landing Box**: 0 lands exactly on the requested coordinate. Above 0, the model chooses a human-like landing point inside a square of that many pixels around it.

Foreground mouse paths (all movement modes) now play on their own thread with a high-resolution timer, so points with 5–10 ms steps keep their timing instead of being stretched to ExileAPI's frame time. A custom ExileAPI input manager therefore needs to accept cursor moves from a non-main thread.


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
