using Northwatch.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Northwatch.Tests.EditMode.Presentation
{
    public sealed class PointerGestureTests
    {
        [TestCase(720f, 4f)]
        [TestCase(1080f, 6f)]
        [TestCase(2160f, 12f)]
        public void SixScaledPixelsIsClickAndDragStaysLatchedUntilCancelled(float height, float expectedRadius)
        {
            var gesture = new PointerGesture();
            gesture.Begin(new Vector2(100, 100));
            float radius = PointerGesture.Radius(height);
            Assert.That(radius, Is.EqualTo(expectedRadius));
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
