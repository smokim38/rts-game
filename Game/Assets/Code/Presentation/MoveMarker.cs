using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Northwatch.Presentation
{
    public sealed class MoveMarker : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;
        private MeshRenderer drawing;
        private Transform ring;
        private static readonly ProfilerMarker Marker = new ProfilerMarker("Northwatch.MoveMarker");

        public void Initialize(Material source)
        {
            ring = transform;
            const int segments = 48;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * (Mathf.PI * 2f / segments);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * 0.5f;
                vertices[i * 2 + 1] = direction * 0.3f;
                int next = (i + 1) % segments * 2, index = i * 6;
                triangles[index] = i * 2; triangles[index + 1] = i * 2 + 1; triangles[index + 2] = next;
                triangles[index + 3] = next; triangles[index + 4] = i * 2 + 1; triangles[index + 5] = next + 1;
            }
            mesh = new Mesh { name = "Move ring", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.UploadMeshData(false);
            // Reuse a shader already referenced by the scene so Player stripping cannot remove it.
            material = new Material(source) { name = "Move ring" };
            material.SetColor("_BaseColor", Color.magenta);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            drawing = gameObject.AddComponent<MeshRenderer>();
            drawing.sharedMaterial = material;
            drawing.shadowCastingMode = ShadowCastingMode.Off; drawing.receiveShadows = false;
            drawing.enabled = false;
        }

        public void Show(Vector3 point, double seconds)
        {
            using (Marker.Auto())
            {
                // Lift just above the plane to avoid depth fighting; the order's X/Z stay exact.
                ring.position = point + Vector3.up * 0.02f;
                float scale = 1f + 0.15f * Mathf.Sin((float)(seconds % 0.5) * (Mathf.PI * 4f));
                ring.localScale = Vector3.one * scale;
                drawing.enabled = true;
            }
        }

        private void OnDestroy()
        {
            if (Application.isPlaying) { Destroy(mesh); Destroy(material); }
            else { DestroyImmediate(mesh); DestroyImmediate(material); }
        }
    }
}
