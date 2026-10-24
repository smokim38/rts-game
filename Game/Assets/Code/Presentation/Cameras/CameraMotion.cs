using UnityEngine;

namespace Northwatch.Presentation.Cameras
{
    public static class CameraMotion
    {
        public static Vector3 PanDelta(Vector2 input, Quaternion angle, float zoom, float speed, float seconds)
        {
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 up = Vector3.ProjectOnPlane(angle * Vector3.up, Vector3.up);
            // Undo the fixed pitch's foreshortening so both axes travel equally on screen.
            up /= up.sqrMagnitude;
            return (angle * Vector3.right * input.x + up * input.y) * (2f * zoom * speed * seconds);
        }

        public static Vector2 EdgePan(Vector2 pointer, Rect viewport, float distance)
        {
            if (pointer.x < viewport.xMin || pointer.x > viewport.xMax ||
                pointer.y < viewport.yMin || pointer.y > viewport.yMax) return Vector2.zero;
            float x = pointer.x < viewport.xMin + distance ? -1f : pointer.x > viewport.xMax - distance ? 1f : 0f;
            float y = pointer.y < viewport.yMin + distance ? -1f : pointer.y > viewport.yMax - distance ? 1f : 0f;
            return new Vector2(x, y);
        }

        public static float Zoom(float current, float scroll, float sensitivity, float nearest, float farthest) =>
            Mathf.Clamp(current * Mathf.Exp(-scroll * sensitivity), nearest, farthest);

        public static Vector3 ClampCentre(Vector3 centre, float halfWidth) =>
            new Vector3(Mathf.Clamp(centre.x, -halfWidth, halfWidth), 0f, Mathf.Clamp(centre.z, -halfWidth, halfWidth));
    }
}
