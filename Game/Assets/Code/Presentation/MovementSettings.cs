using Northwatch.Simulation.Movement;
using UnityEngine;

namespace Northwatch.Presentation
{
    [CreateAssetMenu(menuName = "Northwatch/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [SerializeField] private float smallSpeed = 4f;
        [SerializeField] private float largeSpeed = 2.5f;
        [SerializeField] private float acceleration = 16f;
        [SerializeField] private float deceleration = 16f;
        [SerializeField] private float turnSpeed = 6.2831853f;
        [SerializeField] private float slotSpacing = MovementParameters.DefaultSlotSpacing;
        [SerializeField] private int moveCheckPasses = 1;
        [SerializeField] private int fieldCapacity = 256;
        [SerializeField] private int buildSlots = 2;
        [SerializeField] private int buildLatencyTicks = 1;
        [SerializeField] private float minLease = 3f;
        [SerializeField] private float directSeekRange = 12f;

        public MovementParameters Snapshot()
        {
            var values = new MovementParameters
            {
                SmallSpeed = smallSpeed, LargeSpeed = largeSpeed, Acceleration = acceleration,
                Deceleration = deceleration, TurnSpeed = turnSpeed, SlotSpacing = slotSpacing,
                MoveCheckPasses = moveCheckPasses, FieldCapacity = fieldCapacity, BuildSlots = buildSlots,
                BuildLatencyTicks = buildLatencyTicks, MinLease = minLease, DirectSeekRange = directSeekRange
            };
            values.Validate();
            values.ValidateFields(256, 256);
            return values;
        }
    }
}
