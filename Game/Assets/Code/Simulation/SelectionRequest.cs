using Unity.Mathematics;
using UnityEngine;

namespace Northwatch.Simulation
{
    public struct SelectionRequest
    {
        internal byte Kind;
        internal bool Additive;
        internal int GroupIndex;
        internal float4x4 Projection;
        internal float4 Bounds;
        internal float3 Origin, Direction;

        // Bounds are in viewport coordinates. Both edges belong to the box.
        public static SelectionRequest Box(Matrix4x4 worldToClip, Rect viewportBox, bool additive = false)
            => new SelectionRequest { Kind = 1, Projection = worldToClip,
                Bounds = new float4(viewportBox.xMin, viewportBox.yMin, viewportBox.xMax, viewportBox.yMax), Additive = additive };

        public static SelectionRequest Click(Ray ray, bool additive = false)
            => new SelectionRequest { Kind = 2, Origin = ray.origin, Direction = ray.direction, Additive = additive };

        public static SelectionRequest Group(int number, bool save)
        {
            if (number < 1 || number > 9) throw new System.ArgumentOutOfRangeException(nameof(number));
            return new SelectionRequest { Kind = save ? (byte)3 : (byte)4, GroupIndex = number - 1 };
        }
    }
}
