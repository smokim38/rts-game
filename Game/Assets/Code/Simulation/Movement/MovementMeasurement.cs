using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation.Movement
{
    // Diagnostic API exposes samples, never unit arrays (ADR 0005).
    public sealed class MovementMeasurement : IDisposable
    {
        public const int SampleCapacity = 8192;
        private UnitState state;
        private MovementPipeline pipeline;
        private NativeArray<byte> map;
        private NativeArray<long> timestamps;
        private JobHandle pending;
        private float2 goal;
        public int TickCount { get; private set; }

        public MovementMeasurement(MovementParameters settings, int scene)
        {
            settings.Validate();
            if (scene < 0 || scene > 2) throw new ArgumentOutOfRangeException(nameof(scene));
            state = new UnitState(2000);
            map = new NativeArray<byte>(256 * 256, Allocator.Persistent);
            timestamps = new NativeArray<long>(SampleCapacity * 7, Allocator.Persistent);
            new SetupJob { State = state, Map = map, Scene = scene }.Schedule().Complete();
            goal = scene == 0 ? float2.zero : new float2(2, scene == 2 ? 2 : 0);
            pipeline = new MovementPipeline(2000, settings, map);
        }

        public void Schedule(int ticks)
        {
            if (ticks < 0 || TickCount + ticks > SampleCapacity) throw new ArgumentOutOfRangeException(nameof(ticks));
            for (int i = 0; i < ticks; i++)
            {
                pending = new InputJob { Positions = state.Positions, Units = state.Movement, Goal = goal }.Schedule(2000, 64, pending);
                pending = pipeline.Schedule(state, pending, 64, timestamps, TickCount++ * 7);
            }
        }

        public void Complete() => pending.Complete();
        public long Timestamp(int tick, int boundary) => timestamps[tick * 7 + boundary];

        public void Dispose()
        {
            Complete(); pipeline.Dispose(); state.Dispose(); map.Dispose(); timestamps.Dispose();
        }

        [BurstCompile]
        private struct SetupJob : IJob
        {
            internal UnitState State;
            internal NativeArray<byte> Map;
            internal int Scene;
            public void Execute()
            {
                for (int i = 0; i < Map.Length; i++) Map[i] = 1;
                if (Scene > 0) for (int y = 0; y < 256; y++) Map[y * 256 + 128] = 0;
                if (Scene == 2) for (int x = 0; x < 128; x++) Map[128 * 256 + x] = 0;
                for (int i = 0; i < State.Alive.Length; i++)
                {
                    float2 position = Scene == 0 ? new float2((i % 50 - 25) * .72f, (i / 50 - 20) * .72f) :
                        new float2(-.72f - (i % 50) * .72f, Scene == 2 ? -.72f - (i / 50) * .72f : (i / 50 - 20) * .72f);
                    State.Positions[i] = new float3(position.x, 0, position.y);
                    State.Alive[i] = 1;
                    State.Movement[i] = new MovementUnit
                    {
                        Mode = MovementMode.Moving, Home = position,
                        Previous = new MovementPose { Position = State.Positions[i] }
                    };
                }
            }
        }

        [BurstCompile]
        private struct InputJob : IJobParallelFor
        {
            [ReadOnly] internal NativeArray<float3> Positions;
            internal NativeArray<MovementUnit> Units;
            internal float2 Goal;
            public void Execute(int i)
            {
                MovementUnit unit = Units[i];
                unit.PreferredVelocity = math.normalizesafe(Goal - Positions[i].xz) * 4;
                unit.NewVelocity = unit.PreferredVelocity;
                Units[i] = unit;
            }
        }
    }
}
