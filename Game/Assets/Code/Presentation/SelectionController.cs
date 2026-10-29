using Northwatch.Presentation.Cameras;
using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Northwatch.Presentation
{
    [DefaultExecutionOrder(150)]
    public sealed class SelectionController : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private DebugOverlay overlay;
        [SerializeField] private RectTransform box;
        [SerializeField] private MatchDriver driver;
        [SerializeField] private Camera view;
        [SerializeField] private CameraController cameraController;
        private InputActionAsset runtimeActions;
        private InputSettings previousSettings, runtimeSettings;
        private HideFlags previousSettingsFlags;
        private InputAction point, shift, control, alt, escape, switchOption;
        private readonly InputAction[] buttons = new InputAction[3];
        private readonly PointerGesture[] gestures = new PointerGesture[3];
        private readonly InputAction[] groups = new InputAction[9];
        private Vector2 current, previous, lastClickPosition;
        private bool centreAfterFrame, panStarted;
        private int lastGroup;
        private double lastPress = double.NegativeInfinity, lastClick = double.NegativeInfinity;
        private MoveMarker moveMarker;
        private Text help;
        public bool Modern { get; private set; }
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Northwatch.SelectionInput");

        public void Configure(InputActionAsset input, DebugOverlay debugOverlay, RectTransform rectangle)
        {
            actions = input; overlay = debugOverlay; box = rectangle;
        }

        public void Bind(MatchDriver match, Camera camera, CameraController controller)
        { driver = match; view = camera; cameraController = controller; }

        private void OnEnable()
        {
            Modern = false;
            // Merging can discard the press origin or an out-and-back excursion within a frame.
            previousSettings = InputSystem.settings;
            runtimeSettings = Instantiate(previousSettings);
            // Input System otherwise destroys temporary settings when replacing them.
            previousSettingsFlags = previousSettings.hideFlags;
            previousSettings.hideFlags = HideFlags.None;
            runtimeSettings.hideFlags = HideFlags.None;
            runtimeSettings.disableRedundantEventsMerging = true;
            InputSystem.settings = runtimeSettings;
            runtimeActions = Instantiate(actions);
            InputActionMap map = runtimeActions.FindActionMap("Selection", true);
            point = map.FindAction("Point", true); buttons[0] = map.FindAction("Select", true);
            buttons[1] = map.AddAction("Right", InputActionType.Button, "<Mouse>/rightButton");
            buttons[2] = map.AddAction("Middle", InputActionType.Button, "<Mouse>/middleButton");
            escape = map.AddAction("Deselect", InputActionType.Button, "<Keyboard>/escape");
            switchOption = map.AddAction("TestControlsOption", InputActionType.Button, "<Keyboard>/f2");
            alt = map.AddAction("Alt", InputActionType.Button, "<Keyboard>/leftAlt");
            alt.AddBinding("<Keyboard>/rightAlt");
            shift = map.FindAction("Shift", true); control = map.FindAction("Control", true);
            for (int i = 0; i < groups.Length; i++) groups[i] = map.FindAction("Group" + (i + 1), true);
            for (int i = 0; i < buttons.Length; i++) buttons[i].started += BeginGesture;
            point.performed += TrackPointer;
            map.Enable();
            box.gameObject.SetActive(false);
            help = overlay.transform.Find("Panel/SelectionHelp").GetComponent<Text>();
            ShowOption();
            moveMarker = new GameObject("MoveMarker").AddComponent<MoveMarker>();
            moveMarker.transform.SetParent(driver.transform, false);
            moveMarker.Initialize(driver.SelectionMaterial);
        }

        private void ShowOption() => help.text = HelpText(Modern);

        public static string HelpText(bool modern) => modern
            ? "Modern (F2 test switch): left selects; right moves; middle drags.\nShift adds; double click selects type; Ctrl+1-9 saves; 1-9 recalls."
            : "Classic (F2 test switch): left selects / moves; right clears / drags.\nShift adds; double click selects type; Ctrl+1-9 saves; 1-9 recalls.";

        private void Update() => Advance(Application.isFocused, Time.unscaledTimeAsDouble);

        private void BeginGesture(InputAction.CallbackContext context)
        {
            Vector2 position = point.ReadValue<Vector2>();
            if (overlay.SpawnMode || overlay.ContainsScreenPoint(position) || !view.pixelRect.Contains(position)) return;
            for (int i = 0; i < buttons.Length; i++)
                if (context.action == buttons[i]) gestures[i].Begin(position);
        }

        private void TrackPointer(InputAction.CallbackContext context)
        {
            Vector2 position = context.ReadValue<Vector2>();
            for (int i = 0; i < gestures.Length; i++) gestures[i].Track(position, PointerGesture.Radius(Screen.height));
        }

        public void Advance(bool focused, double now)
        {
            using (Marker.Auto())
            {
                current = point.ReadValue<Vector2>();
                if (!focused)
                {
                    CancelDrag(); lastGroup = 0; lastClick = double.NegativeInfinity; centreAfterFrame = false;
                    return;
                }
                if (switchOption.WasPressedThisFrame())
                {
                    Modern = !Modern; ShowOption(); CancelDrag();
                    lastClick = double.NegativeInfinity;
                }
                if (escape.WasPressedThisFrame())
                {
                    driver.Simulation.QueueSelection(SelectionRequest.Clear());
                    CancelDrag(); lastClick = double.NegativeInfinity; centreAfterFrame = false;
                    return;
                }
                if (overlay.SpawnMode)
                {
                    CancelDrag(); lastGroup = 0; lastClick = double.NegativeInfinity; centreAfterFrame = false;
                    return;
                }
                for (int i = 0; i < groups.Length; i++)
                {
                    if (!groups[i].WasPressedThisFrame()) continue;
                    bool save = control.IsPressed();
                    driver.Simulation.QueueSelection(SelectionRequest.Group(i + 1, save));
                    centreAfterFrame = !save && lastGroup == i + 1 && now - lastPress <= PointerGesture.DoublePressInterval;
                    lastGroup = save || centreAfterFrame ? 0 : i + 1;
                    lastPress = now;
                    CancelDrag(); lastClick = double.NegativeInfinity;
                }
                bool anyDrag = false;
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i].WasPressedThisFrame()) lastGroup = 0;
                    gestures[i].Track(current, PointerGesture.Radius(Screen.height));
                    anyDrag |= gestures[i].Dragging;
                    if (gestures[i].Dragging)
                    {
                        lastClick = double.NegativeInfinity;
                        if (i == 0 && !control.IsPressed() && !alt.IsPressed()) ShowBox();
                        if (i == (Modern ? 2 : 1) && !shift.IsPressed() && !control.IsPressed() && !alt.IsPressed())
                        {
                            cameraController.DragPan(panStarted ? previous : gestures[i].Start, current);
                            panStarted = true;
                        }
                    }
                    if (!gestures[i].Held || !buttons[i].WasReleasedThisFrame()) continue;
                    if (gestures[i].Dragging)
                    {
                        if (i == 0 && !control.IsPressed() && !alt.IsPressed())
                        {
                            Vector2 a = view.ScreenToViewportPoint(gestures[i].Start), b = view.ScreenToViewportPoint(current);
                            driver.Simulation.QueueSelection(SelectionRequest.Box(view.projectionMatrix * view.worldToCameraMatrix,
                                Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), shift.IsPressed()));
                        }
                    }
                    else if (!overlay.ContainsScreenPoint(current) && view.pixelRect.Contains(current)) Click(i, now);
                    gestures[i].Cancel();
                    if (i == (Modern ? 2 : 1)) panStarted = false;
                    if (i == 0) box.gameObject.SetActive(false);
                }
                cameraController.SuppressEdgePan = anyDrag;
                previous = current;
            }
        }

        private void Click(int button, double now)
        {
            if (control.IsPressed() || alt.IsPressed()) { lastClick = double.NegativeInfinity; return; }
            bool additive = shift.IsPressed();
            if (button == 1 && !Modern && !additive)
                driver.Simulation.QueueSelection(SelectionRequest.Clear());
            else if (button == 0 || (button == 1 && Modern))
            {
                float radius = PointerGesture.Radius(Screen.height);
                bool doubleClick = button == 0 && now - lastClick <= PointerGesture.DoublePressInterval &&
                    PointerGesture.WithinRadius(lastClickPosition, current, radius);
                driver.Simulation.QueueSelection(SelectionRequest.PointerClick(view.ScreenPointToRay(current),
                    view.projectionMatrix * view.worldToCameraMatrix, selectUnit: button == 0,
                    moveGround: Modern ? button == 1 : button == 0,
                    clearGround: Modern && button == 0 && !additive, additive: additive, sameType: doubleClick));
                lastClick = button == 0 && !doubleClick ? now : double.NegativeInfinity;
                lastClickPosition = current;
            }
            else lastClick = double.NegativeInfinity;
            if (button != 0) lastClick = double.NegativeInfinity;
        }

        private void ShowBox()
        {
            var parent = (RectTransform)box.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, gestures[0].Start, view, out Vector2 a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, current, view, out Vector2 b);
            box.anchoredPosition = Vector2.Min(a, b) - parent.rect.min;
            box.sizeDelta = Vector2.Max(a, b) - Vector2.Min(a, b);
            box.gameObject.SetActive(true);
        }

        private void CancelDrag()
        {
            for (int i = 0; i < gestures.Length; i++) gestures[i].Cancel();
            panStarted = false;
            box.gameObject.SetActive(false);
            cameraController.SuppressEdgePan = false;
        }

        private void LateUpdate() => CompleteFrame();

        public void CompleteFrame()
        {
            if (centreAfterFrame)
            {
                centreAfterFrame = false;
                if (driver.Simulation.SelectedCount > 0) cameraController.CentreOn(driver.Simulation.SelectedCentre);
            }
            if (driver.Simulation.MoveMarkerVersion == 0) return;
            moveMarker.Show(driver.Simulation.MoveMarkerPosition, Time.unscaledTimeAsDouble);
        }

        private void OnDisable()
        {
            CancelDrag(); centreAfterFrame = false; lastGroup = 0; lastClick = double.NegativeInfinity;
            if (moveMarker != null)
            {
                if (Application.isPlaying) Destroy(moveMarker.gameObject);
                else DestroyImmediate(moveMarker.gameObject);
            }
            if (runtimeActions == null) return;
            runtimeActions.Disable();
            if (Application.isPlaying) Destroy(runtimeActions);
            else DestroyImmediate(runtimeActions);
            runtimeActions = null;
            InputSystem.settings = previousSettings;
            previousSettings.hideFlags = previousSettingsFlags;
            if (Application.isPlaying) Destroy(runtimeSettings);
            else DestroyImmediate(runtimeSettings);
            runtimeSettings = null;
        }
    }
}
