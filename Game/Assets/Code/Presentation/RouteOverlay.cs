#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using Northwatch.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Northwatch.Presentation
{
    // Created only on F8. All cell and arrow storage is cached while the overlay is visible.
    internal sealed class RouteOverlay : IDisposable
    {
        private const int Colours = 12, Cells = 256 * 256;
        private readonly Mesh cube;
        private readonly Matrix4x4[] regions = new Matrix4x4[Cells];
        private readonly int[] counts = new int[Colours], offsets = new int[Colours], written = new int[Colours];
        private readonly RenderParams[] parameters = new RenderParams[Colours + 1];
        private readonly Material[] materials = new Material[Colours + 1];
        private readonly Matrix4x4[] arrows = new Matrix4x4[Cells * 3];
        private int lastField = int.MinValue, lastClass = -1, arrowCount;
        private ulong lastPublished;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Northwatch.RouteOverlay");

        internal RouteOverlay(Mesh cube, Material source)
        {
            this.cube = cube;
            for (int i = 0; i <= Colours; i++)
            {
                materials[i] = new Material(source);
                materials[i].SetColor("_BaseColor", i == Colours ? Color.white : Color.HSVToRGB(i / (float)Colours, .55f, .65f));
                parameters[i] = new RenderParams(materials[i]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            }
        }

        internal void Draw(UnitSimulation simulation)
        {
            using (Marker.Auto())
            {
                simulation.TrySelectedRoute(out int field, out byte size, out ulong published);
                if (size != lastClass)
                {
                    Array.Clear(counts, 0, counts.Length);
                    Array.Clear(written, 0, written.Length);
                    for (int cell = 0; cell < Cells; cell++)
                    {
                        ushort region = simulation.RouteRegion(cell, size);
                        if (region != 0) counts[(region - 1) % Colours]++;
                    }
                    for (int colour = 1; colour < Colours; colour++) offsets[colour] = offsets[colour - 1] + counts[colour - 1];
                    for (int cell = 0; cell < Cells; cell++)
                    {
                        ushort region = simulation.RouteRegion(cell, size);
                        if (region == 0) continue;
                        int colour = (region - 1) % Colours;
                        Vector3 point = new Vector3(cell % 256 - 127.5f + size * .5f, .025f, cell / 256 - 127.5f + size * .5f);
                        regions[offsets[colour] + written[colour]++] = Matrix4x4.TRS(point, Quaternion.identity, new Vector3(.94f, .01f, .94f));
                    }
                }
                if (field != lastField || size != lastClass || published != lastPublished)
                {
                    arrowCount = 0;
                    if (field >= 0)
                        for (int cell = 0; cell < Cells; cell++)
                        {
                            Vector3 point = new Vector3(cell % 256 - 127.5f + size * .5f, .06f, cell / 256 - 127.5f + size * .5f);
                            if (!simulation.RouteDirection(field, point, out Vector3 direction)) continue;
                            Vector3 tip = point + direction * .3f;
                            Vector3 side = new Vector3(-direction.z, 0, direction.x);
                            Line(point - direction * .3f, tip);
                            Line(tip, tip - direction * .16f + side * .12f);
                            Line(tip, tip - direction * .16f - side * .12f);
                        }
                }
                lastField = field; lastClass = size; lastPublished = published;
                for (int colour = 0; colour < Colours; colour++) Render(regions, offsets[colour], counts[colour], ref parameters[colour]);
                Render(arrows, 0, arrowCount, ref parameters[Colours]);
            }
        }

        private void Line(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            arrows[arrowCount++] = Matrix4x4.TRS((from + to) * .5f, Quaternion.LookRotation(delta), new Vector3(.025f, .015f, delta.magnitude));
        }

        private void Render(Matrix4x4[] matrices, int offset, int count, ref RenderParams render)
        {
            for (int start = 0; start < count; start += MatchDriver.InstancesPerDraw)
                Graphics.RenderMeshInstanced(render, cube, 0, matrices, Mathf.Min(MatchDriver.InstancesPerDraw, count - start), offset + start);
        }

        public void Dispose()
        {
            foreach (Material material in materials)
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
#endif
