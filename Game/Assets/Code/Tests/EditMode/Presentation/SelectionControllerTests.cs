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
    public sealed class SelectionControllerTests : InputTestFixture
    {
        [Test]
        public void PointerModifiersGroupsAndSpawnModeUseBuiltPrefabBindings()
        {
            var scene = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath);
            GameObject[] roots = scene.GetRootGameObjects();
            MatchDriver driver = roots.Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            DebugOverlay overlay = roots.Select(root => root.GetComponent<DebugOverlay>()).Single(value => value != null);
            SelectionController selection = overlay.GetComponent<SelectionController>();
            Camera view = roots.Select(root => root.GetComponent<Camera>()).Single(value => value != null);
            CameraController camera = view.GetComponent<CameraController>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            InputSettings.UpdateMode previous = InputSystem.settings.updateMode;
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                Call(driver, "OnEnable"); Call(camera, "OnEnable"); Call(overlay, "OnEnable"); Call(selection, "OnEnable");
                var updateOverlay = (Action)typeof(DebugOverlay).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                    .CreateDelegate(typeof(Action), overlay);
                UnitSimulation simulation = driver.Simulation;
                simulation.QueueAdd(Side.First, UnitSize.Small, Vector3.zero);
                simulation.QueueAdd(Side.First, UnitSize.Small, new Vector3(-20, 0, 0));
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                Vector2 a = view.WorldToScreenPoint(new Vector3(0, 0.25f, 0));
                Vector2 b = view.WorldToScreenPoint(new Vector3(-20, 0.25f, 0));
                double now = 1;
                void Step(Vector2 position, bool down = false, bool focused = true, params Key[] keys)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = down ? (ushort)1 : (ushort)0 });
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                    InputSystem.Update(); updateOverlay(); selection.Advance(focused, now);
                    simulation.ScheduleFrame(0); simulation.CompleteFrame(); selection.CompleteFrame(); now += 0.05;
                }
                Step(a, true); Step(a);
                Assert.That(simulation.SelectedCount, Is.EqualTo(1));
                Step(b, true, true, Key.LeftShift); Step(b, false, true, Key.LeftShift);
                Assert.That(simulation.SelectedCount, Is.EqualTo(2));
                Step(a, true, true, Key.RightShift); Step(a, false, true, Key.RightShift);
                Assert.That(simulation.SelectedCount, Is.EqualTo(1));
                Step(a + Vector2.one * 8, true); Step(a - Vector2.one * 8, true);
                RectTransform box = overlay.transform.Find("SelectionBox").GetComponent<RectTransform>();
                Assert.That(box.gameObject.activeSelf, Is.True);
                Vector2 lower = RectTransformUtility.WorldToScreenPoint(view, box.TransformPoint(box.rect.min));
                Assert.That(Vector2.Distance(lower, a - Vector2.one * 8), Is.LessThan(0.1f));
                Step(a - Vector2.one * 8, false, true, Key.LeftShift);
                Assert.That(box.gameObject.activeSelf, Is.False);
                Assert.That(simulation.SelectedCount, Is.EqualTo(2), "Shift drag adds without toggling.");
                Step(a, false, true, Key.LeftCtrl, Key.Digit9); Step(a);
                Step(new Vector2(view.pixelWidth - 10, 10), true); Step(new Vector2(view.pixelWidth - 10, 10));
                Assert.That(simulation.SelectedCount, Is.EqualTo(2), "Classic ground clicks keep selection.");
                Step(a, false, true, Key.Escape); Step(a);
                Assert.That(simulation.SelectedCount, Is.Zero);
                camera.CentreOn(new Vector3(100, 0, 100));
                Vector3 beforeRecall = view.transform.position;
                Step(a, false, true, Key.Digit9); Step(a);
                Assert.That(simulation.SelectedCount, Is.EqualTo(2));
                Assert.That(view.transform.position, Is.EqualTo(beforeRecall), "First press only recalls.");
                Step(a, false, true, Key.Digit9); Step(a);
                Assert.That(view.transform.position, Is.Not.EqualTo(beforeRecall));
                Ray centreRay = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                new Plane(Vector3.up, 0).Raycast(centreRay, out float distance);
                Assert.That(Vector3.Distance(centreRay.GetPoint(distance), new Vector3(-10, 0, 0)), Is.LessThan(0.001f));
                Step(a, false, true, Key.F1); Step(a);
                Step(b, true); Step(b); Step(b, false, true, Key.Digit9);
                Assert.That(simulation.SelectedCount, Is.EqualTo(2), "Spawn input must not change selection or recall a group.");
                Step(a, false, true, Key.F1); Step(a);
                Vector2 empty = new Vector2(view.pixelWidth - 10, 10);
                Step(empty, true); Step(empty, false, false); Step(empty);
                Assert.That(simulation.SelectedCount, Is.EqualTo(2), "Losing focus cancels a gesture.");
                camera.CentreOn(new Vector3(1000, 0, -1000));
                Ray boundRay = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
                new Plane(Vector3.up, 0).Raycast(boundRay, out distance);
                Vector3 bound = boundRay.GetPoint(distance);
                Assert.That(bound.x, Is.EqualTo(UnitSimulation.MapHalfWidth).Within(0.001f));
                Assert.That(bound.z, Is.EqualTo(-UnitSimulation.MapHalfWidth).Within(0.001f));
            }
            finally
            {
                Call(selection, "OnDisable"); Call(overlay, "OnDisable"); Call(camera, "OnDisable"); Call(driver, "OnDisable");
                InputSystem.settings.updateMode = previous;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static void Call(object target, string method) => target.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
