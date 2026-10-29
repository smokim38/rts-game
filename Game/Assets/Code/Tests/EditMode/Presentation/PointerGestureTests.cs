using Northwatch.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class PointerGestureTests
    {
        [TestCase(720f)]
        [TestCase(1080f)]
        [TestCase(2160f)]
        public void SixScaledPixelsIsClickAndDragStaysLatchedUntilCancelled(float height)
        {
            var gesture = new PointerGesture();
            gesture.Begin(new Vector2(100, 100));
            float radius = PointerGesture.Radius(height);
            gesture.Track(gesture.Start + Vector2.right * radius, radius);
            Assert.That(gesture.Dragging, Is.False);
            gesture.Track(gesture.Start + Vector2.right * (radius + 0.1f), radius);
            Assert.That(gesture.Dragging, Is.True);
            gesture.Track(gesture.Start, radius);
            Assert.That(gesture.Dragging, Is.True);
            gesture.Cancel();
            Assert.That(gesture.Held || gesture.Dragging, Is.False);
            gesture.Begin(Vector2.zero);
            gesture.Track(Vector2.one * radius * 0.75f, radius);
            Assert.That(gesture.Dragging, Is.True, "Distance is radial, not per axis.");
        }
    }
}
