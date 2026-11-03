using System;
using System.Linq;
using System.Reflection;
using Northwatch.EditorTools;
using Northwatch.Presentation;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class DebugOverlayTests : InputTestFixture
    {
        [Test]
        public void InputControlsSpawnOnlyInModeAndCounterTracksTwoThousandUnits()
        {
            var scene = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath);
            GameObject[] roots = scene.GetRootGameObjects();
            MatchDriver driver = roots.Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            DebugOverlay overlay = roots.Select(root => root.GetComponent<DebugOverlay>()).Single(value => value != null);
            Camera camera = roots.Select(root => root.GetComponent<Camera>()).Single(value => value != null);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            InputSettings.UpdateMode previousMode = InputSystem.settings.updateMode;
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                Call(driver, "OnEnable"); Call(overlay, "OnEnable");
                var fields = (UnityEngine.UI.Text[])typeof(DebugOverlay).GetField("values", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(overlay);
                var update = (Action)typeof(DebugOverlay).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                    .CreateDelegate(typeof(Action), overlay);
                Canvas.ForceUpdateCanvases();
                Vector2 first = camera.WorldToScreenPoint(new Vector3(-40, 0, 0));
                Vector2 second = camera.WorldToScreenPoint(new Vector3(-40, 0, 45));
                Click(mouse, update, first);
                Assert.That(driver.Simulation.RemainingCapacity, Is.EqualTo(2000));
                Press(keyboard, update, Key.F1);
                Assert.That(overlay.SpawnMode, Is.True);
                Press(keyboard, update, Key.Digit2); Press(keyboard, update, Key.Backspace);
                Press(keyboard, update, Key.Digit5);
                Assert.That(fields[8].text, Is.EqualTo("0005"), "A digit replaces the count after Backspace removes its last digit.");
                Press(keyboard, update, Key.Backspace); Press(keyboard, update, Key.Backspace);
                Assert.That(fields[8].text, Is.EqualTo("0001"), "The effective count stays valid while its input is cleared.");
                Press(keyboard, update, Key.Digit0);
                Assert.That(fields[8].text, Is.EqualTo("0001"), "Zero cannot become the placement count.");
                Press(keyboard, update, Key.Digit1);
                Press(keyboard, update, Key.Digit0); Press(keyboard, update, Key.Digit0); Press(keyboard, update, Key.Digit0);
                Assert.That(fields[8].text, Is.EqualTo("1000"));
                Click(mouse, update, first);
                Assert.That(driver.Simulation.RemainingCapacity, Is.EqualTo(1000));
                Press(keyboard, update, Key.B); Press(keyboard, update, Key.L);
                Press(keyboard, update, Key.Digit1); Press(keyboard, update, Key.Digit0);
                Press(keyboard, update, Key.Digit0); Press(keyboard, update, Key.Digit0);
                Click(mouse, update, second);
                Assert.That(driver.Simulation.RemainingCapacity, Is.Zero);
                driver.Simulation.ScheduleFrame(1); driver.Simulation.CompleteFrame(); update();
                Assert.That(driver.Simulation.Count, Is.EqualTo(2000));
                Assert.That(driver.Simulation.DrawCount(Side.First), Is.EqualTo(1000));
                Assert.That(driver.Simulation.DrawCount(Side.Second), Is.EqualTo(1000));
                Assert.That(fields[2].text, Is.EqualTo("2000"));
                Click(mouse, update, Vector2.one * 500);
                Assert.That(fields[9].text, Does.Contain("Capacity exceeded"));
                Assert.That(fields[10].text, Is.EqualTo("0000"));
                Press(keyboard, update, Key.F1);
                Assert.That(overlay.TryPlace(first), Is.False);
            }
            finally
            {
                Call(overlay, "OnDisable"); Call(driver, "OnDisable");
                InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                InputSystem.settings.updateMode = previousMode;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [TestCase(UnitSize.Small)] [TestCase(UnitSize.Large)]
        public void RouteOverlayKeepsF8AcrossInputUpdatesAndOnlyTintsCutOffRegions(UnitSize size)
        {
            var scene = EditorSceneManager.OpenScene(SandboxSceneBuilder.ScenePath);
            MatchDriver driver = scene.GetRootGameObjects().Select(root => root.GetComponent<MatchDriver>()).Single(value => value != null);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            InputSettings.UpdateMode previousMode = InputSystem.settings.updateMode;
            try
            {
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                Call(driver, "OnEnable");
                Assert.That(Field<object>(driver, "routeOverlay"), Is.Null, "Off by default.");
                UnitSimulation simulation = driver.Simulation;
                Assert.That(simulation.QueueBlock(Side.First, size, new Vector3(60, 0, 30), 1), Is.True);
                simulation.ScheduleFrame(1); simulation.CompleteFrame();
                simulation.QueueSelection(SelectionRequest.Box(new Matrix4x4 { m00 = .001f, m12 = .001f, m33 = 1 }, new Rect(0, 0, 1, 1)));
                simulation.ScheduleFrame(0); simulation.CompleteFrame();
                Vector3 goal = new Vector3(88, 0, 30);
                simulation.QueueSelection(SelectionRequest.PointerClick(new Ray(goal + Vector3.up * 10, Vector3.down),
                    Matrix4x4.identity, true, true, false, false, false));
                simulation.ScheduleFrame(2); simulation.CompleteFrame();
                // The harness pumps input again before LateUpdate; the press must survive it.
                Press(keyboard, () => { }, Key.F8);
                Call(driver, "LateUpdate"); simulation.CompleteFrame();
                object route = Field<object>(driver, "routeOverlay");
                Assert.That(route, Is.Not.Null);
                var sizes = new int[ushort.MaxValue + 1];
                for (int cell = 0; cell < 65536; cell++) sizes[simulation.RouteRegion(cell, (byte)size)]++;
                int mainRegion = Enumerable.Range(1, ushort.MaxValue).OrderByDescending(region => sizes[region]).First();
                int expected = 65536 - sizes[0] - sizes[mainRegion];
                Assert.That(expected, Is.GreaterThan(0), "The closed ring has a separate interior.");
                Assert.That(Field<int[]>(route, "counts").Sum(), Is.EqualTo(expected));
                Matrix4x4[] regions = Field<Matrix4x4[]>(route, "regions");
                for (int i = 0; i < expected; i++)
                {
                    int x = Mathf.RoundToInt(regions[i].m03 + 127.5f - (byte)size * .5f);
                    int z = Mathf.RoundToInt(regions[i].m23 + 127.5f - (byte)size * .5f);
                    Assert.That(simulation.RouteRegion(z * 256 + x, (byte)size), Is.Not.EqualTo(mainRegion).And.Not.Zero);
                }
                Material[] materials = Field<Material[]>(route, "materials");
                foreach (Material tint in materials.Where((_, i) => i != 12))
                {
                    Assert.That(tint.GetColor("_BaseColor").a, Is.InRange(.01f, .15f));
                    Assert.That(tint.GetFloat("_Surface"), Is.EqualTo(1));
                    Assert.That(tint.GetFloat("_ZWrite"), Is.Zero);
                    Assert.That(tint.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                }
                Assert.That(Field<int>(route, "arrowCount"), Is.GreaterThan(0));
                Assert.That(Field<Matrix4x4[]>(route, "arrows")[0].GetColumn(0).magnitude, Is.EqualTo(.075f).Within(.00001f));
                Press(keyboard, () => { }, Key.F8);
                Call(driver, "LateUpdate"); simulation.CompleteFrame();
                Assert.That(Field<object>(driver, "routeOverlay"), Is.Null, "F8 toggles it off again.");
            }
            finally
            {
                Call(driver, "OnDisable"); InputSystem.RemoveDevice(keyboard);
                InputSystem.settings.updateMode = previousMode;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static T Field<T>(object target, string name) => (T)target.GetType()
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static void Press(Keyboard keyboard, Action update, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update(); update();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); update();
        }

        private static void Click(Mouse mouse, Action update, Vector2 position)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 1 }); InputSystem.Update(); update();
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }); InputSystem.Update(); update();
        }

        private static void Call(object target, string method) => target.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
    }
}
