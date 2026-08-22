using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CommandRouter))]
public class RadioInputController : MonoBehaviour
{
    private CommandRouter _router;

    private void Awake()
    {
        _router = GetComponent<CommandRouter>();
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            _router.SetChannel(_router.CurrentChannel == RadioChannel.ChiefEngineer
                ? RadioChannel.Lookout
                : RadioChannel.ChiefEngineer);
        }
        else if (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame)
        {
            if (_router.CurrentChannel != RadioChannel.ChiefEngineer) _router.SetChannel(RadioChannel.ChiefEngineer);
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame)
        {
            if (_router.CurrentChannel != RadioChannel.Lookout) _router.SetChannel(RadioChannel.Lookout);
        }
    }
}
