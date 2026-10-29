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
        [SerializeField] private float slotSpacing = 1.25f;
        [SerializeField] private int maxSerialFallbacks = 32;

        public MovementParameters Snapshot()
        {
            var values = new MovementParameters
            {
                SmallSpeed = smallSpeed, LargeSpeed = largeSpeed, Acceleration = acceleration,
                Deceleration = deceleration, TurnSpeed = turnSpeed, SlotSpacing = slotSpacing,
                MaxSerialFallbacks = maxSerialFallbacks
            };
            values.Validate();
            return values;
        }
    }
}
