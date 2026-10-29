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
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClicksActOnReleaseAndGroundRulesRespectOptionAndModifiers(bool modern)
        {
            Switch(modern);
            Click(ground, modern ? (ushort)2 : (ushort)1);
            Assert.That(Simulation.MoveMarkerVersion, Is.Zero, "No selected units means no order.");
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
            Assert.That(overlay.transform.Find("MoveMarker").gameObject.activeSelf, Is.True);
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
            now += 0.25; // The next release is exactly 0.35 s after the first.
            Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(2));
            now += 0.4;
            Click(ground);
            Click(own);
            Assert.That(Simulation.SelectedCount, Is.EqualTo(1), "Distant clicks cannot form a double-click.");
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
