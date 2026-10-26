using UnityEngine;
using UnityEngine.InputSystem;

namespace Northwatch.Presentation.Diagnostics
{
    // Only the explicit selection check installs deterministic polling for its virtual pointer.
    [DefaultExecutionOrder(-100)]
    public sealed class SelectionInputPump : MonoBehaviour
    {
        private InputSettings.UpdateMode previous;

        private void OnEnable()
        {
            previous = InputSystem.settings.updateMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
        }

        private void Update() => InputSystem.Update();

        private void OnDisable() => InputSystem.settings.updateMode = previous;
    }
}
