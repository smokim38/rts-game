using System;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    public struct MovementParameters
    {
        public const float TickDuration = 1f / 30f;
        public const float DefaultSlotSpacing = 1.25f;
        public float SmallSpeed, LargeSpeed, Acceleration, Deceleration, TurnSpeed;
        public float SlotSpacing;
        public int MoveCheckPasses;
        public int FieldCapacity, BuildSlots, BuildLatencyTicks;
        public float MinLease, DirectSeekRange;

        public void ValidateFields(int width, int height)
        {
            long cells = (long)width * height;
            if (width < 1 || height < 1 || cells > 87381 || FieldCapacity < 200 ||
                cells * FieldCapacity * 3 > 64L * 1024 * 1024 || BuildSlots < 2 || BuildSlots > 4 ||
                BuildLatencyTicks < 1 || !math.isfinite(MinLease) || MinLease <= 0 ||
                !math.isfinite(DirectSeekRange) || DirectSeekRange <= 0)
                throw new ArgumentException("Flow-field settings violate ADR 0011; match cannot start.");
        }

        public void Validate()
        {
            if (!math.all(math.isfinite(new float4(SmallSpeed, LargeSpeed, Acceleration, Deceleration))) ||
                !math.isfinite(TurnSpeed) || !math.isfinite(SlotSpacing) ||
                SmallSpeed <= 0 || SmallSpeed > 30 * .25f || LargeSpeed <= 0 || LargeSpeed > 30 * .5f ||
                Acceleration <= 0 || Deceleration < 2 * math.max(SmallSpeed, LargeSpeed) || TurnSpeed <= 0 ||
                SlotSpacing < 1.05f || SlotSpacing > 1.3f || MoveCheckPasses < 1 || MoveCheckPasses > 4)
                throw new ArgumentException("Movement settings violate ADR 0008 rule 6 or ADR 0012 rule 15; match cannot start.");
        }

        internal float Speed(UnitSize size) => size == UnitSize.Small ? SmallSpeed : LargeSpeed;
        // Includes integration and the capped overlap correction. Clamps that exceed this
        // envelope keep the old position, so queries against the old grid remain complete.
        internal float StepBound => math.max(SmallSpeed, LargeSpeed) * TickDuration + .5f * Footprint.LargeRadius;
    }
}
