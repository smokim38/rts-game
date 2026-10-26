using Northwatch.Presentation.Cameras;
using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Northwatch.Presentation
{
    [DefaultExecutionOrder(150)]
    public sealed class SelectionController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private DebugOverlay overlay;
        [SerializeField] private RectTransform box;
        [SerializeField] private float doubleTapInterval;
        [SerializeField] private float dragThreshold;
        [SerializeField] private MatchDriver driver;
        [SerializeField] private Camera view;
        [SerializeField] private CameraController cameraController;
        private InputActionAsset runtimeActions;
        private InputAction point, select, shift, control;
        private readonly InputAction[] groups = new InputAction[9];
        private Vector2 start, current;
        private bool held, dragging, centreAfterFrame;
        private int lastGroup;
        private double lastPress = double.NegativeInfinity;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Northwatch.SelectionInput");

        public void Configure(InputActionAsset input, DebugOverlay debugOverlay, RectTransform rectangle,
            float interval, float threshold)
        {
            actions = input; overlay = debugOverlay; box = rectangle;
            doubleTapInterval = interval; dragThreshold = threshold;
        }

        public void Bind(MatchDriver match, Camera camera, CameraController controller)
        { driver = match; view = camera; cameraController = controller; }

        private void OnEnable()
        {
            runtimeActions = Instantiate(actions);
            InputActionMap map = runtimeActions.FindActionMap("Selection", true);
            point = map.FindAction("Point", true); select = map.FindAction("Select", true);
            shift = map.FindAction("Shift", true); control = map.FindAction("Control", true);
            for (int i = 0; i < groups.Length; i++) groups[i] = map.FindAction("Group" + (i + 1), true);
            map.Enable();
            box.gameObject.SetActive(false);
        }

        private void Update() => Advance(Application.isFocused, Time.unscaledTimeAsDouble);

        public void Advance(bool focused, double now)
        {
            using (Marker.Auto())
            {
                current = point.ReadValue<Vector2>();
                if (!focused || overlay.SpawnMode)
                {
                    CancelDrag(); lastGroup = 0; centreAfterFrame = false;
                    return;
                }
                for (int i = 0; i < groups.Length; i++)
                {
                    if (!groups[i].WasPressedThisFrame()) continue;
                    bool save = control.IsPressed();
                    driver.Simulation.QueueSelection(SelectionRequest.Group(i + 1, save));
                    centreAfterFrame = !save && lastGroup == i + 1 && now - lastPress <= doubleTapInterval;
                    lastGroup = save || centreAfterFrame ? 0 : i + 1;
                    lastPress = now;
                    CancelDrag();
                }
                if (select.WasPressedThisFrame() && !overlay.ContainsScreenPoint(current) && view.pixelRect.Contains(current))
                {
                    start = current; held = true; dragging = false; lastGroup = 0;
                }
                if (!held) return;
                dragging |= (current - start).sqrMagnitude >= dragThreshold * dragThreshold;
                if (dragging) ShowBox();
                if (!select.WasReleasedThisFrame()) return;
                if (dragging)
                {
                    Vector2 a = view.ScreenToViewportPoint(start), b = view.ScreenToViewportPoint(current);
                    driver.Simulation.QueueSelection(SelectionRequest.Box(view.projectionMatrix * view.worldToCameraMatrix,
                        Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), shift.IsPressed()));
                }
                else driver.Simulation.QueueSelection(SelectionRequest.Click(view.ScreenPointToRay(current), shift.IsPressed()));
                CancelDrag();
            }
        }

        private void ShowBox()
        {
            var parent = (RectTransform)box.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, start, view, out Vector2 a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, current, view, out Vector2 b);
            box.anchoredPosition = Vector2.Min(a, b) - parent.rect.min;
            box.sizeDelta = Vector2.Max(a, b) - Vector2.Min(a, b);
            box.gameObject.SetActive(true);
        }

        private void CancelDrag() { held = dragging = false; box.gameObject.SetActive(false); }

        private void LateUpdate() => CompleteFrame();

        public void CompleteFrame()
        {
            if (!centreAfterFrame) return;
            centreAfterFrame = false;
            if (driver.Simulation.SelectedCount > 0) cameraController.CentreOn(driver.Simulation.SelectedCentre);
        }

        private void OnDisable()
        {
            CancelDrag(); centreAfterFrame = false; lastGroup = 0;
            if (runtimeActions == null) return;
            runtimeActions.Disable();
            if (Application.isPlaying) Destroy(runtimeActions);
            else DestroyImmediate(runtimeActions);
            runtimeActions = null;
        }
    }
}
