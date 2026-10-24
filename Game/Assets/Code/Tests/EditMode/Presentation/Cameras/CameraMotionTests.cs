using Northwatch.Presentation.Cameras;
using Northwatch.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Presentation.Cameras
{
    public sealed class CameraMotionTests
    {
        private static readonly Quaternion Angle = Quaternion.Euler(35.26439f, 45f, 0f);

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void PanCoversSameGroundPerSecond(int frames)
        {
            Vector3 travelled = Vector3.zero;
            for (int i = 0; i < frames; i++)
                travelled += CameraMotion.PanDelta(new Vector2(1, 1), Angle, 20f, 0.5f, 1f / frames);
            Vector3 expected = CameraMotion.PanDelta(new Vector2(1, 1), Angle, 20f, 0.5f, 1f);
            Assert.That(Vector3.Distance(travelled, expected), Is.LessThan(0.0001f));
            Assert.That(travelled.y, Is.EqualTo(0f).Within(0.0001f));
        }

        [TestCase(20f, 1f, 0f)]
        [TestCase(100f, 0f, 1f)]
        [TestCase(200f, 1f, 1f)]
        public void PanHasSameProjectedScreenSpeedAtEveryZoom(float zoom, float x, float y)
        {
            Vector3 delta = CameraMotion.PanDelta(new Vector2(x, y), Angle, zoom, 0.5f, 1f);
            Vector3 local = Quaternion.Inverse(Angle) * delta;
            Assert.That(new Vector2(local.x, local.y).magnitude / (2f * zoom), Is.EqualTo(0.5f).Within(0.00001f));
        }

        [Test]
        public void ZoomClampsBothLimitsAndReversesWithoutSticking()
        {
            const float sensitivity = 0.18f;
            float zoom = 200f;
            for (int notch = 0; notch < 13; notch++)
                zoom = CameraMotion.Zoom(zoom, 1f, sensitivity, 20, 200);
            Assert.That(zoom, Is.EqualTo(20));
            Assert.That(CameraMotion.Zoom(zoom, 1f, sensitivity, 20, 200), Is.EqualTo(20));
            Assert.That(CameraMotion.Zoom(zoom, -1f, sensitivity, 20, 200), Is.InRange(23f, 25f));
            for (int notch = 0; notch < 13; notch++)
                zoom = CameraMotion.Zoom(zoom, -1f, sensitivity, 20, 200);
            Assert.That(zoom, Is.EqualTo(200));
            Assert.That(CameraMotion.Zoom(zoom, -1f, sensitivity, 20, 200), Is.EqualTo(200));
            Assert.That(CameraMotion.Zoom(zoom, 1f, sensitivity, 20, 200), Is.InRange(165f, 170f));
            float split = CameraMotion.Zoom(CameraMotion.Zoom(100, 0.5f, sensitivity, 20, 200), 0.5f, sensitivity, 20, 200);
            Assert.That(split, Is.EqualTo(CameraMotion.Zoom(100, 1f, sensitivity, 20, 200)).Within(0.00001f));
        }

        [TestCase(-500, 0, -128, 0)]
        [TestCase(500, 0, 128, 0)]
        [TestCase(0, -500, 0, -128)]
        [TestCase(0, 500, 0, 128)]
        [TestCase(-500, -500, -128, -128)]
        [TestCase(-500, 500, -128, 128)]
        [TestCase(500, -500, 128, -128)]
        [TestCase(500, 500, 128, 128)]
        public void CentreClampsEachMapEdgeAndCorner(float x, float z, float expectedX, float expectedZ)
        {
            Assert.That(CameraMotion.ClampCentre(new Vector3(x, 0, z), UnitSimulation.MapHalfWidth),
                Is.EqualTo(new Vector3(expectedX, 0, expectedZ)));
        }

        [TestCase(0, 300, -1, 0)]
        [TestCase(800, 300, 1, 0)]
        [TestCase(400, 0, 0, -1)]
        [TestCase(400, 600, 0, 1)]
        [TestCase(799, 599, 1, 1)]
        [TestCase(400, 300, 0, 0)]
        [TestCase(-1, 300, 0, 0)]
        [TestCase(801, 300, 0, 0)]
        [TestCase(400, -1, 0, 0)]
        [TestCase(400, 601, 0, 0)]
        public void EdgePanUsesOnlyPointerInsideTheViewport(float x, float y, float expectedX, float expectedY)
        {
            Assert.That(CameraMotion.EdgePan(new Vector2(x, y), new Rect(0, 0, 800, 600), 16f),
                Is.EqualTo(new Vector2(expectedX, expectedY)));
        }
    }
}
