using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Northwatch.Presentation.Cameras
{
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private float panSpeed;
        [SerializeField] private float edgeDistance;
        [SerializeField] private float nearestZoom;
        [SerializeField] private float farthestZoom;
        [SerializeField] private float zoomSensitivity;
        private InputActionAsset runtimeActions;
        private InputAction pan, point, zoom;
        private Transform rig;
        private Quaternion angle;
        private Vector3 centre, offset;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Northwatch.Camera");

        public void Configure(Camera camera, InputActionAsset input, float speed, float edgePixels,
            float nearest, float farthest, float sensitivity)
        {
            view = camera; actions = input; panSpeed = speed; edgeDistance = edgePixels;
            nearestZoom = nearest; farthestZoom = farthest; zoomSensitivity = sensitivity;
        }

        private void OnEnable()
        {
            rig = view.transform;
            angle = rig.rotation;
            Vector3 forward = rig.forward;
            Vector3 ground = rig.position - forward * (rig.position.y / forward.y);
            offset = rig.position - ground;
            centre = CameraMotion.ClampCentre(ground, UnitSimulation.MapHalfWidth);
            rig.position = centre + offset;
            view.orthographicSize = Mathf.Clamp(view.orthographicSize, nearestZoom, farthestZoom);
            runtimeActions = Instantiate(actions);
            InputActionMap map = runtimeActions.FindActionMap("GameplayCamera", true);
            pan = map.FindAction("Pan", true);
            point = map.FindAction("Point", true);
            zoom = map.FindAction("Zoom", true);
            map.Enable();
        }

        private void Update() => Advance(Time.unscaledDeltaTime, Application.isFocused);

        public void CentreOn(Vector3 position)
        {
            centre = CameraMotion.ClampCentre(position, UnitSimulation.MapHalfWidth);
            rig.position = centre + offset;
        }

        public void Advance(float seconds, bool focused)
        {
            using (Marker.Auto())
            {
                if (!focused) return;
                Vector2 input = pan.ReadValue<Vector2>();
                if (point.controls.Count != 0)
                    input += CameraMotion.EdgePan(point.ReadValue<Vector2>(), view.pixelRect, edgeDistance);
                view.orthographicSize = CameraMotion.Zoom(view.orthographicSize, zoom.ReadValue<float>(),
                    zoomSensitivity, nearestZoom, farthestZoom);
                centre = CameraMotion.ClampCentre(centre + CameraMotion.PanDelta(input, angle,
                    view.orthographicSize, panSpeed, seconds), UnitSimulation.MapHalfWidth);
                rig.position = centre + offset;
            }
        }

        private void OnDisable()
        {
            if (runtimeActions == null) return;
            runtimeActions.Disable();
            if (Application.isPlaying) Destroy(runtimeActions);
            else DestroyImmediate(runtimeActions);
            runtimeActions = null;
        }
    }
}
