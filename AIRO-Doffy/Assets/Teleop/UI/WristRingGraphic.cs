using UnityEngine;
using UnityEngine.UI;

namespace Doffy.UI
{
    /// <summary>An actual annulus: transparent centre, including UI hit testing.</summary>
    public sealed class WristRingGraphic : MaskableGraphic
    {
        public float InnerRadius = 180;
        public float OuterRadius = 202;
        private const int Segments = 96;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * 2 * Mathf.PI / Segments;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(dir * InnerRadius, color, Vector2.zero);
                vh.AddVert(dir * OuterRadius, color, Vector2.one);
                if (i == 0) continue;
                int n = i * 2;
                vh.AddTriangle(n - 2, n - 1, n);
                vh.AddTriangle(n - 1, n + 1, n);
            }
        }

        public override bool Raycast(Vector2 sp, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, sp, eventCamera, out Vector2 p)) return false;
            return p.sqrMagnitude >= InnerRadius * InnerRadius &&
                   p.sqrMagnitude <= OuterRadius * OuterRadius && base.Raycast(sp, eventCamera);
        }
    }
}
