using System;
using System.Reflection;
using Northwatch.Presentation.Cameras;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Northwatch.Tests.EditMode.Presentation.Cameras
{
    public sealed class CameraControllerTests : InputTestFixture
    {
        [Test]
        public void RigRespondsToArrowsEdgesWheelAndFocusWithoutRotatingOrAllocating()
        {
            GameObject rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/CameraRig.prefab"));
            Camera view = rig.GetComponent<Camera>();
            CameraController controller = rig.GetComponent<CameraController>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                Assert.That(controller, Is.Not.Null);
                typeof(CameraController).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Quaternion rotation = rig.transform.rotation;
                Rect viewport = view.pixelRect;
                Vector2 middle = viewport.center;
                Assert.That(Quaternion.Angle(rotation, Quaternion.Euler(35.26439f, 45f, 0f)), Is.LessThan(0.001f));
                Key[] keys = { Key.RightArrow, Key.LeftArrow, Key.UpArrow, Key.DownArrow };
                Vector2[] directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
                for (int i = 0; i < keys.Length; i++)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys[i]));
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = middle });
                    InputSystem.Update();
                    Vector3 before = rig.transform.position;
                    controller.Advance(0.01f, true);
                    Vector3 local = Quaternion.Inverse(rotation) * (rig.transform.position - before);
                    Assert.That(Vector2.Dot(new Vector2(local.x, local.y), directions[i]), Is.GreaterThan(0f), "Arrow " + keys[i]);
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Vector2[] edges = { new Vector2(viewport.xMax - 1, middle.y), new Vector2(viewport.xMin + 1, middle.y),
                    new Vector2(middle.x, viewport.yMax - 1), new Vector2(middle.x, viewport.yMin + 1) };
                for (int i = 0; i < edges.Length; i++)
                {
                    InputSystem.QueueStateEvent(mouse, new MouseState { position = edges[i] });
                    InputSystem.Update();
                    Vector3 before = rig.transform.position;
                    controller.Advance(0.01f, true);
                    Vector3 local = Quaternion.Inverse(rotation) * (rig.transform.position - before);
                    Assert.That(Vector2.Dot(new Vector2(local.x, local.y), directions[i]), Is.GreaterThan(0f),
                        "Edge " + edges[i] + " in " + view.pixelRect + "; pointer " + mouse.position.ReadValue());
                }
                InputSystem.QueueStateEvent(mouse, new MouseState { position = Vector2.zero });
                InputSystem.Update();
                Vector3 atOrigin = rig.transform.position;
                controller.Advance(0.01f, true);
                Vector3 originDelta = Quaternion.Inverse(rotation) * (rig.transform.position - atOrigin);
                Assert.That(originDelta.x, Is.LessThan(0f));
                Assert.That(originDelta.y, Is.LessThan(0f));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightArrow));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = edges[0], scroll = new Vector2(0, 10000) });
                InputSystem.Update();
                Vector3 unfocused = rig.transform.position;
                controller.Advance(1f, false);
                Assert.That(rig.transform.position, Is.EqualTo(unfocused));
                Assert.That(view.orthographicSize, Is.EqualTo(200f));
                controller.Advance(1f, true);
                Assert.That(view.orthographicSize, Is.EqualTo(20f));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = middle, scroll = new Vector2(0, -10000) });
                InputSystem.Update();
                controller.Advance(100f, true);
                Assert.That(view.orthographicSize, Is.EqualTo(200f));
                Vector2[] corners = { new Vector2(128, -128), new Vector2(-128, 128), new Vector2(128, 128), new Vector2(-128, -128) };
                foreach (float scroll in new[] { 10000f, -10000f })
                    for (int i = 0; i < keys.Length; i++)
                    {
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys[i]));
                        InputSystem.QueueStateEvent(mouse, new MouseState { position = middle, scroll = new Vector2(0, scroll) });
                        InputSystem.Update();
                        controller.Advance(100f, true);
                        Vector3 centre = GroundCentre(view);
                        Assert.That(centre.x, Is.EqualTo(corners[i].x).Within(0.001f));
                        Assert.That(centre.z, Is.EqualTo(corners[i].y).Within(0.001f));
                    }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.UpArrow));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = middle });
                InputSystem.Update();
                for (int i = 0; i < 100; i++) controller.Advance(0.001f, true);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) controller.Advance(0.001f, true);
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Assert.That(allocated, Is.Zero, "Warmed-up camera frames must allocate 0 B.");
                Assert.That(rig.transform.rotation, Is.EqualTo(rotation));
                TestContext.WriteLine("Camera: 1000 warmed-up updates, " + allocated + " B managed allocation.");
            }
            finally
            {
                if (controller != null)
                    typeof(CameraController).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Object.DestroyImmediate(rig);
            }
        }

        private static Vector3 GroundCentre(Camera view)
        {
            Ray ray = view.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            Assert.That(new Plane(Vector3.up, 0).Raycast(ray, out float distance), Is.True);
            return ray.GetPoint(distance);
        }
    }
}
