#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Northwatch.Presentation.Diagnostics
{
    // Batch Players need an explicit camera render, after controls and instanced draws.
    [DefaultExecutionOrder(3000)]
    public sealed class MovementCheckRenderer : MonoBehaviour
    {
        private Camera view;
        private RenderTexture target;
        private UniversalRenderPipeline.SingleCameraRequest request;
        public int RenderedFrames { get; private set; }

        public void Initialize(Camera camera)
        {
            if (camera == null) throw new System.InvalidOperationException("Movement validation requires a camera.");
            view = camera;
            target = new RenderTexture(1920, 1080, 24);
            if (!target.Create()) throw new System.InvalidOperationException("Could not create movement validation target.");
            request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        }

        private void LateUpdate()
        {
            RenderPipeline.SubmitRenderRequest(view, request);
            RenderedFrames++;
        }

        private void OnDestroy()
        {
            if (target == null) return;
            target.Release(); Destroy(target);
        }
    }
}
#endif
