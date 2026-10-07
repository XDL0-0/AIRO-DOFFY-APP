using UnityEngine;

namespace Doffy.UI
{
    /// <summary>A hollow cuff around the wrist's Z axis, with no disk across the hand.</summary>
    public sealed class BraceletBody : MonoBehaviour
    {
        private Mesh mesh;
        private Material material;

        public static void Create(Transform parent, float radius, float distalZ, float proximalZ)
        {
            var body = new GameObject("Hollow bracelet body", typeof(MeshFilter), typeof(MeshRenderer));
            body.transform.SetParent(parent, false);
            var owner = body.AddComponent<BraceletBody>();
            const int segments = 64;
            const int sides = 4;
            var vertices = new Vector3[segments * sides * 4];
            var triangles = new int[segments * sides * 6];
            var colors = new Color[vertices.Length];
            float inner = radius - .003f;
            // Cross-section order produces outward-facing outer wall, lip, inner wall, lip.
            var profile = new Vector2[] {
                new Vector2(radius, proximalZ), new Vector2(radius, distalZ),
                new Vector2(inner, distalZ), new Vector2(inner, proximalZ)
            };
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                float b = (i + 1) * Mathf.PI * 2 / segments;
                for (int side = 0; side < sides; side++)
                {
                    Vector2 start = profile[side], end = profile[(side + 1) % sides];
                    int v = (i * sides + side) * 4, t = (i * sides + side) * 6;
                    vertices[v] = At(a, start);
                    vertices[v + 1] = At(b, start);
                    vertices[v + 2] = At(b, end);
                    vertices[v + 3] = At(a, end);
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                    Color color = side == 1 || side == 3 ? WorkspaceTheme.Accent : WorkspaceTheme.Background;
                    for (int c = 0; c < 4; c++) colors[v + c] = color;
                }
            }
            owner.mesh = new Mesh { name = "Hollow wrist cuff", vertices = vertices, triangles = triangles, colors = colors };
            owner.mesh.RecalculateNormals();
            owner.mesh.RecalculateBounds();
            body.GetComponent<MeshFilter>().sharedMesh = owner.mesh;
            owner.material = new Material(Resources.Load<Shader>("BraceletBody"));
            body.GetComponent<MeshRenderer>().sharedMaterial = owner.material;
        }

        private static Vector3 At(float angle, Vector2 section) =>
            new Vector3(Mathf.Cos(angle) * section.x, Mathf.Sin(angle) * section.x, section.y);

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                if (mesh != null) Destroy(mesh);
                if (material != null) Destroy(material);
            }
            else
            {
                if (mesh != null) DestroyImmediate(mesh);
                if (material != null) DestroyImmediate(material);
            }
        }
    }
}
