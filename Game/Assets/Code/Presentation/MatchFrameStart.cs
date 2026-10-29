using UnityEngine;

namespace Northwatch.Presentation
{
    // Complete before input, overlays or other scripts access simulation state.
    [DefaultExecutionOrder(-10000)]
    public sealed class MatchFrameStart : MonoBehaviour
    {
        private MatchDriver driver;
        internal void Configure(MatchDriver match) => driver = match;
        private void Update()
        {
            if (driver != null && driver.isActiveAndEnabled) driver.BeginFrame();
        }
    }
}
