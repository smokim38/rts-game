using System.Globalization;
using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Northwatch.Presentation
{
    [DefaultExecutionOrder(100)]
    public sealed class DebugOverlay : MonoBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Text[] values;
        [SerializeField] private MatchDriver driver;
        [SerializeField] private Camera view;
        private InputActionAsset runtimeActions;
        private InputAction toggle, sideA, sideB, small, large, place, point, backspace;
        private readonly InputAction[] digits = new InputAction[10];
        private string[] integers, milliseconds;
        private FrameRateWindow frameRate;
        private bool spawnMode, replaceCount = true;
        private Side side;
        private UnitSize size;
        private int count = 1, placementVersion;
        private float refresh;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Northwatch.SandboxOverlay");
        public float FramesPerSecond => frameRate.FramesPerSecond;
        public float FrameMilliseconds => frameRate.Milliseconds;
        public int DisplayedFramesPerSecond { get; private set; }
        public bool SpawnMode => spawnMode;

        public bool ContainsScreenPoint(Vector2 position) =>
            RectTransformUtility.RectangleContainsScreenPoint(panel, position, view);

        public void Configure(InputActionAsset input, Canvas surface, RectTransform background, Text[] fields)
        {
            actions = input; canvas = surface; panel = background; values = fields;
        }

        public void Bind(MatchDriver match, Camera camera) { driver = match; view = camera; }

        private void OnEnable()
        {
            integers = new string[10000];
            milliseconds = new string[10000];
            for (int i = 0; i < integers.Length; i++)
            {
                integers[i] = i.ToString("0000", CultureInfo.InvariantCulture);
                milliseconds[i] = (i * 0.1f).ToString("000.0", CultureInfo.InvariantCulture);
            }
            frameRate = new FrameRateWindow();
            runtimeActions = Instantiate(actions);
            InputActionMap map = runtimeActions.FindActionMap("Sandbox", true);
            toggle = map.FindAction("ToggleSpawn", true);
            sideA = map.FindAction("SideA", true); sideB = map.FindAction("SideB", true);
            small = map.FindAction("Small", true); large = map.FindAction("Large", true);
            place = map.FindAction("Place", true); point = map.FindAction("Point", true);
            backspace = map.FindAction("Backspace", true);
            for (int i = 0; i < digits.Length; i++) digits[i] = map.FindAction("Digit" + i, true);
            map.Enable();
            canvas.worldCamera = view;
            values[0].font.RequestCharactersInTexture("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz :.-/", 18);
            values[7].text = "Large";
            values[9].text = "Block occupied: choose clear ground.";
            Canvas.ForceUpdateCanvases();
            values[9].text = "";
        }

        private void Update()
        {
            using (Marker.Auto())
            {
                frameRate.Advance(Time.unscaledDeltaTime);
                if (toggle.WasPressedThisFrame()) { spawnMode = !spawnMode; replaceCount = true; }
                UnitSimulation simulation = driver.Simulation;
                if (simulation.PlacementVersion != placementVersion)
                {
                    placementVersion = simulation.PlacementVersion;
                    values[9].text = simulation.PlacementResult < 0 ? "Block occupied: choose clear ground." : "Placed.";
                }
                if (spawnMode)
                {
                    if (sideA.WasPressedThisFrame()) side = Side.First;
                    if (sideB.WasPressedThisFrame()) side = Side.Second;
                    if (small.WasPressedThisFrame()) size = UnitSize.Small;
                    if (large.WasPressedThisFrame()) size = UnitSize.Large;
                    for (int digit = 0; digit < digits.Length; digit++)
                        if (digits[digit].WasPressedThisFrame())
                        {
                            int next = (replaceCount ? 0 : count) * 10 + digit;
                            if (next < 1 || next > UnitSimulation.Capacity) continue;
                            count = next; replaceCount = false;
                        }
                    if (backspace.WasPressedThisFrame())
                    {
                        int shortened = count / 10;
                        count = Mathf.Max(1, shortened);
                        replaceCount = shortened == 0;
                    }
                    if (place.WasPressedThisFrame()) TryPlace(point.ReadValue<Vector2>());
                }
                values[0].text = integers[simulation.DrawCount(Side.First)];
                values[1].text = integers[simulation.DrawCount(Side.Second)];
                values[2].text = integers[simulation.Count];
                refresh -= Time.unscaledDeltaTime;
                if (refresh <= 0)
                {
                    refresh = 0.1f;
                    DisplayedFramesPerSecond = Mathf.Clamp(Mathf.RoundToInt(FramesPerSecond), 0, 9999);
                    values[3].text = integers[DisplayedFramesPerSecond];
                    values[4].text = milliseconds[Mathf.Clamp(Mathf.RoundToInt(FrameMilliseconds * 10), 0, 9999)];
                }
                values[5].text = spawnMode ? "ON" : "OFF";
                values[6].text = side == Side.First ? "A" : "B";
                values[7].text = size == UnitSize.Small ? "Small" : "Large";
                values[8].text = integers[count];
                values[10].text = integers[simulation.RemainingCapacity];
            }
        }

        public bool TryPlace(Vector2 screenPoint)
        {
            if (!spawnMode || RectTransformUtility.RectangleContainsScreenPoint(panel, screenPoint, view)) return false;
            Ray ray = view.ScreenPointToRay(screenPoint);
            if (!new Plane(Vector3.up, 0f).Raycast(ray, out float distance)) return false;
            Vector3 ground = ray.GetPoint(distance);
            bool queued = driver.Simulation.QueueBlock(side, size, ground, count);
            values[9].text = queued ? "Placement queued." : count > driver.Simulation.RemainingCapacity
                ? "Capacity exceeded. Still fit:" : "Click inside the map.";
            replaceCount = true;
            return queued;
        }

        private void OnDisable()
        {
            if (runtimeActions == null) return;
            runtimeActions.Disable();
            if (Application.isPlaying) Destroy(runtimeActions);
            else DestroyImmediate(runtimeActions);
        }
    }
}
