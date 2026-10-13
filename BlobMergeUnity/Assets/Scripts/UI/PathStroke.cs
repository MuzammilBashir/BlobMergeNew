using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// A polyline drawn as one thick stroke with round caps and round joins (like an SVG path with
    /// stroke-linecap/linejoin round). Points are in the RectTransform's local units. Used under the creature bar
    /// (CreatureBar_Fish.prefab: PathOuter / PathInner).
    /// </summary>
    public class PathStroke : MaskableGraphic
    {
        [Tooltip("Path corners in this RectTransform's local units (edit freely; it redraws live).")]
        public List<Vector2> points = new List<Vector2>();
        public float width = 10f;
        const int CapSegments = 20;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2) return;
            float hw = width * 0.5f;
            Color32 c = color;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector2 a = points[i], b = points[i + 1], d = b - a;
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector2 n = new Vector2(-d.y, d.x).normalized * hw;
                int k = vh.currentVertCount;
                vh.AddVert(a + n, c, Vector4.zero);
                vh.AddVert(b + n, c, Vector4.zero);
                vh.AddVert(b - n, c, Vector4.zero);
                vh.AddVert(a - n, c, Vector4.zero);
                vh.AddTriangle(k, k + 1, k + 2);
                vh.AddTriangle(k, k + 2, k + 3);
            }
            // a disc on every point = round caps and joins (the colour is opaque, so overlaps don't show)
            foreach (var p in points)
            {
                int k = vh.currentVertCount;
                vh.AddVert(p, c, Vector4.zero);
                for (int s = 0; s < CapSegments; s++)
                {
                    float a = s / (float)CapSegments * Mathf.PI * 2f;
                    vh.AddVert(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * hw, c, Vector4.zero);
                }
                for (int s = 0; s < CapSegments; s++) vh.AddTriangle(k, k + 1 + s, k + 1 + (s + 1) % CapSegments);
            }
        }
    }
}
