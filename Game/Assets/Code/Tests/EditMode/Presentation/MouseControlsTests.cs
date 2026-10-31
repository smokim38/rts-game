using System;
using System.Linq;
using System.Reflection;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using Northwatch.Presentation.Cameras;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class MouseControlsTests : InputTestFixture
    {
        private MatchDriver driver;
        private DebugOverlay overlay;
        private SelectionController selection;
        private Camera view;
        private CameraController camera;
        private Mouse mouse;
        private Keyboard keyboard;
        private Vector2 own, ground, enemy;
        private double now;
        private Action updateOverlay;
        private UnitSimulation Simulation => driver.Simulation;

        [SetUp]
        public void OpenSandbox()
        {
            var roots = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath).GetRootGameObjects();
            driver = roots.Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            overlay = roots.Select(root => root.GetComponent<DebugOverlay>()).Single(value => value != null);
            selection = overlay.GetComponent<SelectionController>();
            view = roots.Select(root => root.GetComponent<Camera>()).Single(value => value != null);
            camera = view.GetComponent<CameraController>();
            mouse = InputSystem.AddDevice<Mouse>(); keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Call(driver, "OnEnable"); Call(camera, "OnEnable"); Call(overlay, "OnEnable"); Call(selection, "OnEnable");
            view.orthographicSize = 20;
            updateOverlay = (Action)typeof(DebugOverlay).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .CreateDelegate(typeof(Action), overlay);
            Simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
            Simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(4, 0, 0));
            Simulation.QueueAdd(Side.Second, UnitSize.Small, new Vector3(-4, 0, 0));
            Simulation.ScheduleFrame(1); Simulation.CompleteFrame();
            own = view.WorldToScreenPoint(new Vector3(0, 0.25f, 0));
            enemy = view.WorldToScreenPoint(new Vector3(-4, 0.25f, 0));
            ground = view.ViewportToScreenPoint(new Vector3(0.8f, 0.2f));
            now = 1;
        }

        private void Step(Vector2 position, ushort buttons = 0, bool focused = true, params Key[] keys)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = buttons });
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update(); updateOverlay(); selection.Advance(focused, now); camera.Advance(0.01f, focused);
            Simulation.ScheduleFrame(0); Simulation.CompleteFrame(); selection.CompleteFrame(); now += 0.05;
        }

        private void Click(Vector2 position, ushort button = 1, params Key[] keys)
        { Step(position, button, true, keys); Step(position, 0, true, keys); }

        private void Switch(bool modern)
        {
            Assert.That(selection.Modern, Is.False);
            if (modern) { Step(own, 0, true, Key.F2); Step(own); }
            Assert.That(selection.Modern, Is.EqualTo(modern));
            Assert.That(overlay.transform.Find("Panel/SelectionHelp").GetComponent<Text>().text,
                Does.StartWith(modern ? "Modern (F2 test switch)" : "Classic (F2 test switch)"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClicksActOnReleaseAndGroundRulesRespectOptionAndModifiers(bool modern)
        {
            Switch(modern);
            Click(ground, modern ? (ushort)2 : (ushort)1);
            Assert.That(Simulation.MoveMarkerVersion, Is.Zero, "No selected units means no order.");
            Assert.That(driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>().enabled, Is.False);
            Step(own, 1);
            Assert.That(Simulation.SelectedCount, Is.Zero, "A press has no click effect.");
            Step(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(1));
            ushort move = modern ? (ushort)2 : (ushort)1;
            Step(ground, move);
            Assert.That(Simulation.MoveMarkerVersion, Is.Zero);
            float radius = PointerGesture.Radius(Screen.height);
            Step(ground + Vector2.right * radius);
            Assert.That(Simulation.MoveMarkerVersion, Is.EqualTo(1), "Exactly six scaled pixels is a click.");
            Ray ray = view.ScreenPointToRay(ground + Vector2.right * radius);
            new Plane(Vector3.up, 0).Raycast(ray, out float distance);
            Assert.That(Vector3.Distance(Simulation.MoveMarkerPosition, ray.GetPoint(distance)), Is.LessThan(0.001f));
            Assert.That(driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>().enabled, Is.True);
            Click(ground, 1, Key.LeftCtrl); Click(ground, 1, Key.LeftAlt);
            Assert.That(Simulation.MoveMarkerVersion, Is.EqualTo(1));
            Click(ground, 1, Key.LeftShift);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(1));
            Assert.That(Simulation.MoveMarkerVersion, Is.EqualTo(modern ? 1 : 2));
            Click(enemy, move);
            Assert.That(Simulation.MoveMarkerVersion, Is.EqualTo(modern ? 2 : 3), "Enemy counts as ground.");
            Vector3 before = view.transform.position;
            Click(own, modern ? (ushort)1 : (ushort)2);
            if (modern) Click(ground);
            Assert.That(Simulation.SelectedCount, Is.Zero);
            Assert.That(view.transform.position, Is.EqualTo(before));
            Click(own); Step(own, 0, true, Key.Escape);
            Assert.That(Simulation.SelectedCount, Is.Zero);
            Assert.That(driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>().enabled, Is.True,
                "The last marker persists even after deselection.");
        }

        [Test]
        public void MarkerHidesOnFirstDrawAfterLastArrivalAndEmptySelectionCannotShowIt()
        {
            Click(own); Click(ground);
            var drawing = driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>();
            Assert.That(drawing.enabled, Is.True);
            for (int tick = 0; tick < 1200 && Simulation.MoveMarkerCount > 0; tick++)
            {
                Simulation.ScheduleFrame(1); Simulation.CompleteFrame(); selection.CompleteFrame();
                Assert.That(drawing.enabled, Is.EqualTo(Simulation.State.Movement[0].HasMoveOrder));
            }
            Assert.That(drawing.enabled, Is.False);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 256; frame++) selection.CompleteFrame();
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
            Step(own, 0, true, Key.Escape); Click(ground);
            Assert.That(drawing.enabled, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void NewOrderMidwayReusesSingleRingAtNewPoint(bool modern)
        {
            Switch(modern); Click(own);
            ushort button = modern ? (ushort)2 : (ushort)1;
            Click(ground, button);
            Simulation.ScheduleFrame(1); Simulation.CompleteFrame(); selection.CompleteFrame();
            var marker = driver.transform.Find("MoveMarker").GetComponent<MeshRenderer>();
            Vector2 next = view.WorldToScreenPoint(new Vector3(20, 0, 0));
            Click(next, button);
            Assert.That(driver.GetComponentsInChildren<MoveMarker>().Length, Is.EqualTo(1));
            Assert.That(marker.enabled, Is.True);
            Assert.That(Vector3.Distance(marker.transform.position, new Vector3(20, .02f, 0)), Is.LessThan(.001f));
            Assert.That(Simulation.MoveMarkerCount, Is.EqualTo(1));
        }

        [Test]
        public void MarkerIsAGroundRingPulsingTwicePerSecondWithoutAllocations()
        {
            MoveMarker marker = driver.transform.Find("MoveMarker").GetComponent<MoveMarker>();
            Mesh mesh = marker.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Assert.That(vertices.Length, Is.EqualTo(96));
            foreach (Vector3 vertex in vertices)
            {
                Assert.That(vertex.y, Is.Zero);
                Assert.That(vertex.magnitude, Is.InRange(0.2999f, 0.5001f));
            }
            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                Assert.That(Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                    vertices[triangles[i + 2]] - vertices[triangles[i]]).y, Is.GreaterThan(0), "The ground ring faces up.");
            var point = new Vector3(30, 0, -20);
            marker.Show(point, 0.125);
            Assert.That(marker.transform.position, Is.EqualTo(point + Vector3.up * 0.02f));
            Assert.That(marker.transform.localScale.x, Is.EqualTo(1.15f).Within(0.0001f));
            marker.Show(point, 0.375);
            Assert.That(marker.transform.localScale.x, Is.EqualTo(1f).Within(0.0001f));
            marker.Show(point, 0.625);
            Assert.That(marker.transform.localScale.x, Is.EqualTo(1.15f).Within(0.0001f));
            Material material = marker.GetComponent<MeshRenderer>().sharedMaterial;
            Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(Color.green));
            Assert.That(material, Is.Not.SameAs(driver.SelectionMaterial), "Team colours must remain unchanged.");
            Click(own); Click(ground);
            for (int i = 0; i < 16; i++) selection.CompleteFrame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) selection.CompleteFrame();
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        [TestCase(20f)]
        [TestCase(200f)]
        public void MarkerKeepsMinimumScreenDiameterThroughPulseZoomAndPan(float zoom)
        {
            MoveMarker marker = driver.transform.Find("MoveMarker").GetComponent<MoveMarker>();
            view.orthographicSize = zoom;
            var point = new Vector3(30, 0, -20);
            for (int pan = 0; pan < 2; pan++)
            {
                if (pan == 1) camera.DragPan(ground, ground + Vector2.right * 100);
                for (int sample = 0; sample <= 32; sample++)
                {
                    marker.Show(point, sample / 64.0);
                    Vector3 horizontal = view.transform.right * (marker.transform.localScale.x * 0.5f);
                    float pixels = Vector3.Distance(view.WorldToScreenPoint(point + horizontal), view.WorldToScreenPoint(point - horizontal));
                    Assert.That(pixels, Is.GreaterThanOrEqualTo(24f * Screen.height / 1080f - 0.01f));
                    Assert.That(marker.transform.position.x, Is.EqualTo(point.x));
                    Assert.That(marker.transform.position.z, Is.EqualTo(point.z));
                    if (zoom == 20f) Assert.That(marker.transform.localScale.x, Is.InRange(1f, 1.15f));
                    else Assert.That(pixels, Is.LessThanOrEqualTo(27.6f * Screen.height / 1080f + 0.01f));
                }
            }
        }

        [TestCase(false, 1)]
        [TestCase(false, 2)]
        [TestCase(false, 4)]
        [TestCase(true, 1)]
        [TestCase(true, 2)]
        [TestCase(true, 4)]
        public void EachButtonUsesSameDragThresholdAndOnlyPanButtonMovesCamera(bool modern, int button)
        {
            Switch(modern); Click(own);
            int version = Simulation.MoveMarkerVersion;
            float radius = PointerGesture.Radius(Screen.height);
            Vector3 before = view.transform.position;
            Step(ground, (ushort)button); Step(ground + Vector2.right * radius, (ushort)button);
            Assert.That(view.transform.position, Is.EqualTo(before));
            Step(ground + Vector2.right * (radius + 1), (ushort)button);
            bool pans = button == (modern ? 4 : 2);
            Assert.That(view.transform.position != before, Is.EqualTo(pans));
            Assert.That(camera.SuppressEdgePan, Is.True);
            Step(ground);
            Assert.That(Simulation.MoveMarkerVersion, Is.EqualTo(version), "Returning to origin remains a drag.");
            if (button != 1) Assert.That(Simulation.SelectedCount, Is.EqualTo(1));
            Step(own, (ushort)button); Step(new Vector2(view.pixelRect.xMax - 1, own.y), (ushort)button);
            Assert.That(camera.SuppressEdgePan, Is.True);
            Vector3 atEdge = view.transform.position;
            Step(new Vector2(view.pixelRect.xMax - 1, own.y), (ushort)button);
            Assert.That(view.transform.position, Is.EqualTo(atEdge), "No edge pan during any drag.");
            Step(ground, 0, false);
            Assert.That(camera.SuppressEdgePan, Is.False, "Focus loss cancels the drag.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DoubleClickSelectsTypeAndHonoursTimeDistanceAndShift(bool modern)
        {
            Switch(modern);
            Click(own); now += 0.4; Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(1));
            Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(2));
            now += 0.4;
            Click(own, 1, Key.LeftShift); Click(own, 1, Key.LeftShift);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(2), "Shift double-click adds without toggling the type.");
            now += 0.4;
            Click(own);
            now += 0.24; // Keep the 0.34 s release interval clear of floating-point rounding at the boundary.
            Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(2));
            now += 0.4;
            Click(ground);
            Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(1), "Distant clicks cannot form a double-click.");
        }

        [TestCase(false, 0.34, 2)]
        [TestCase(false, 0.36, 1)]
        [TestCase(true, 0.34, 2)]
        [TestCase(true, 0.36, 1)]
        public void DoubleClickUsesDesignInterval(bool modern, double interval, int expectedCount)
        {
            Switch(modern);
            Click(own);
            now += interval - 0.1;
            Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(expectedCount));
        }

        [TestCase(0.34, true)]
        [TestCase(0.36, false)]
        public void GroupDoubleTapUsesSameDesignInterval(double interval, bool centres)
        {
            Click(own);
            Step(own, 0, true, Key.LeftCtrl, Key.Digit1); Step(own);
            camera.CentreOn(new Vector3(80, 0, 80));
            Vector3 before = view.transform.position;
            Step(own, 0, true, Key.Digit1); Step(own);
            Assert.That(view.transform.position, Is.EqualTo(before));
            now += interval - 0.1;
            Step(own, 0, true, Key.Digit1);
            Assert.That(view.transform.position != before, Is.EqualTo(centres));
        }

        [Test]
        public void ExcursionAndReleaseWithinOneInputUpdateRemainsADrag()
        {
            Click(own);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ground, buttons = 1 });
            InputSystem.QueueStateEvent(mouse, new MouseState
                { position = ground + Vector2.right * (PointerGesture.Radius(Screen.height) + 1), buttons = 1 });
            Step(ground);
            Assert.That(Simulation.MoveMarkerVersion, Is.Zero);
            Assert.That(Simulation.SelectedCount, Is.Zero);
        }

        [Test]
        public void TestSwitchAndEscapeRemainAvailableWhileSpawnModeIsActive()
        {
            Click(own);
            Step(own, 0, true, Key.F1); Step(own);
            Assert.That(overlay.SpawnMode, Is.True);
            Step(own, 0, true, Key.F2); Step(own);
            Assert.That(selection.Modern, Is.True);
            Step(own, 0, true, Key.Escape);
            Assert.That(Simulation.SelectedCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PanBeginsAtPressPositionWhenSeveralPointerEventsArriveInOneFrame(bool modern)
        {
            Switch(modern);
            Ray ray = view.ScreenPointToRay(ground);
            new Plane(Vector3.up, 0).Raycast(ray, out float distance);
            Vector3 anchor = ray.GetPoint(distance);
            ushort button = modern ? (ushort)4 : (ushort)2;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = ground, buttons = button });
            Vector2 destination = ground + Vector2.right * 100;
            Step(destination, button);
            Vector2 screen = view.WorldToScreenPoint(anchor);
            Assert.That(Vector2.Distance(screen, destination), Is.LessThan(0.01f), "Ground follows the pointer one to one.");
        }

        [TearDown]
        public void CloseSandbox()
        {
            Call(selection, "OnDisable"); Call(overlay, "OnDisable"); Call(camera, "OnDisable"); Call(driver, "OnDisable");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void Call(object target, string method) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
