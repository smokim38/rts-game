using UnityEditor;
using UnityEngine;

namespace Northwatch.EditorTools
{
    public static class TankMeshBuilder
    {
        public const string MeshPath = "Assets/Data/TankMesh.asset";

        public static void Build()
        {
            var vertices = new Vector3[72];
            var normals = new Vector3[72];
            var triangles = new int[108];
            // The existing matrices put local Y=-.5 on the ground and local +X along heading.
            // Keep the entire silhouette inside the same unit square used by Footprint.
            Box(0, new Vector3(-.5f, -.5f, -.5f), new Vector3(.5f, -.18f, .5f));
            Box(24, new Vector3(-.28f, -.18f, -.22f), new Vector3(.12f, .16f, .22f));
            Box(48, new Vector3(.12f, .02f, -.055f), new Vector3(.5f, .11f, .055f));
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "TankMesh";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, MeshPath);
            else EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);

            void Box(int offset, Vector3 min, Vector3 max)
            {
                Face(offset, new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z), new Vector3(max.x, min.y, max.z));
                Face(offset + 4, new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z),
                    new Vector3(min.x, max.y, min.z), new Vector3(min.x, min.y, min.z));
                Face(offset + 8, new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z));
                Face(offset + 12, new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z),
                    new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z));
                Face(offset + 16, new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z),
                    new Vector3(min.x, max.y, max.z), new Vector3(min.x, min.y, max.z));
                Face(offset + 20, new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z),
                    new Vector3(max.x, max.y, min.z), new Vector3(max.x, min.y, min.z));
            }

            void Face(int offset, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                vertices[offset] = a; vertices[offset + 1] = b; vertices[offset + 2] = c; vertices[offset + 3] = d;
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                for (int i = 0; i < 4; i++) normals[offset + i] = normal;
                int index = offset / 4 * 6;
                triangles[index] = offset; triangles[index + 1] = offset + 1; triangles[index + 2] = offset + 2;
                triangles[index + 3] = offset; triangles[index + 4] = offset + 2; triangles[index + 5] = offset + 3;
            }
        }
    }
}
