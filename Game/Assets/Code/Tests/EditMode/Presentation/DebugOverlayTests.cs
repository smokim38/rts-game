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
                Vector2 second = camera.WorldToScreenPoint(new Vector3(40, 0, 0));
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
