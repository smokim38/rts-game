using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Northwatch.Simulation
{
    [BurstCompile]
    internal struct SelectionJob : IJob
    {
        [ReadOnly] internal UnitState State;
        [ReadOnly] internal NativeArray<SelectionRequest> Requests;
        internal int RequestCount;
        internal NativeArray<uint> Selected, Groups;
        [WriteOnly] internal NativeArray<float4> Summary;

        public void Execute()
        {
            for (int slot = 0; slot < Selected.Length; slot++)
            {
                uint generation = State.Alive[slot] != 0 && State.Sides[slot] == Side.First ? State.Generations[slot] : 0;
                if (Selected[slot] != generation) Selected[slot] = 0;
                for (int group = 0; group < 9; group++)
                {
                    int index = group * Selected.Length + slot;
                    if (Groups[index] != generation) Groups[index] = 0;
                }
            }
            for (int request = 0; request < RequestCount; request++) Apply(Requests[request]);
            float3 centre = 0;
            int count = 0;
            for (int slot = 0; slot < Selected.Length; slot++)
                if (Selected[slot] != 0) { centre += State.Positions[slot]; count++; }
            Summary[0] = new float4(count == 0 ? float3.zero : centre / count, count);
        }

        private void Apply(SelectionRequest request)
        {
            int picked = -1;
            float nearest = float.PositiveInfinity;
            if (request.Kind == 2)
                for (int slot = 0; slot < Selected.Length; slot++)
                {
                    if (State.Alive[slot] == 0) continue;
                    float width = State.Sizes[slot] == UnitSize.Small ? 0.5f : 1f;
                    float3 centre = State.Positions[slot] + new float3(0, width * 0.5f, 0);
                    float3 minimum = centre - width * 0.5f, maximum = centre + width * 0.5f;
                    bool3 parallel = math.abs(request.Direction) < 1e-7f;
                    if (math.any(parallel & ((request.Origin < minimum) | (request.Origin > maximum)))) continue;
                    float3 direction = math.select(request.Direction, new float3(1), parallel);
                    float3 a = (minimum - request.Origin) / direction, b = (maximum - request.Origin) / direction;
                    float enter = math.cmax(math.select(math.min(a, b), new float3(float.NegativeInfinity), parallel));
                    float exit = math.cmin(math.select(math.max(a, b), new float3(float.PositiveInfinity), parallel));
                    float distance = math.max(0, enter);
                    if (exit < distance || distance >= nearest) continue;
                    nearest = distance; picked = slot;
                }
            for (int slot = 0; slot < Selected.Length; slot++)
            {
                if (request.Kind >= 3)
                {
                    int index = request.GroupIndex * Selected.Length + slot;
                    if (request.Kind == 3) Groups[index] = Selected[slot];
                    else Selected[slot] = Groups[index];
                    continue;
                }
                bool hit = false;
                if (State.Alive[slot] != 0 && State.Sides[slot] == Side.First)
                {
                    if (request.Kind == 2) hit = slot == picked;
                    else
                    {
                        float height = State.Sizes[slot] == UnitSize.Small ? 0.25f : 0.5f;
                        float4 clip = math.mul(request.Projection, new float4(State.Positions[slot] + new float3(0, height, 0), 1));
                        float2 point = clip.xy / clip.w * 0.5f + 0.5f;
                        hit = clip.w > 0 && math.abs(clip.z) <= clip.w &&
                            math.all(point >= request.Bounds.xy) && math.all(point <= request.Bounds.zw);
                    }
                }
                if (!request.Additive) Selected[slot] = 0;
                if (hit) Selected[slot] = request.Kind == 2 && request.Additive && Selected[slot] != 0
                    ? 0 : State.Generations[slot];
            }
        }
    }
}
