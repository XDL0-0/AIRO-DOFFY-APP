using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RoundedPanel : MaskableGraphic
    {
        [SerializeField, Min(0)] private float radius = 16;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect r = rectTransform.rect;
            float corner = Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2);
            mesh.AddVert(r.center, color, Vector2.one * .5f);
            const int segments = 8;
            for (int c = 0; c < 4; c++)
            {
                Vector2 center = new Vector2(c < 2 ? r.xMax - corner : r.xMin + corner,
                    c == 0 || c == 3 ? r.yMax - corner : r.yMin + corner);
                for (int i = 0; i <= segments; i++)
                {
                    float angle = (90 - c * 90 - i * 90f / segments) * Mathf.Deg2Rad;
                    Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * corner;
                    mesh.AddVert(p, color, Vector2.zero);
                }
            }
            int count = 4 * (segments + 1);
            for (int i = 1; i <= count; i++) mesh.AddTriangle(0, i, i == count ? 1 : i + 1);
        }
    }
}
