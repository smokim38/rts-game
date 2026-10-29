using UnityEngine;

namespace Northwatch.Presentation
{
    public struct PointerGesture
    {
        public Vector2 Start { get; private set; }
        public bool Held { get; private set; }
        public bool Dragging { get; private set; }

        public static float Radius(float screenHeight) => 6f * screenHeight / 1080f;

        public static bool WithinRadius(Vector2 first, Vector2 second, float radius)
        {
            // Subtracting screen coordinates can round the scaled boundary upwards.
            float boundary = radius + 0.0001f;
            return (second - first).sqrMagnitude <= boundary * boundary;
        }

        public void Begin(Vector2 position)
        {
            Start = position;
            Held = true;
            Dragging = false;
        }

        public void Track(Vector2 position, float radius)
        {
            Dragging |= Held && !WithinRadius(Start, position, radius);
        }

        public void Cancel() { Held = Dragging = false; }
    }
}
