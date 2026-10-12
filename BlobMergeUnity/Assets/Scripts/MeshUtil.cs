using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>Procedural vertex-coloured geometry. All colours are given in sRGB and converted here.</summary>
    public static class MeshUtil
    {
        static int linear = -1;

        public static Color Lin(Color c)
        {
            if (linear < 0) linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0;
            return linear == 1 ? c.linear : c;
        }

        public static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        public static Mesh Build(List<Vector3> v, List<Color> c, List<int> t, string name = "Mesh")
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetColors(c);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Convex polygon fan.</summary>
        public static void AddPolygon(List<Vector3> v, List<Color> c, List<int> t, IList<Vector2> pts, Color color)
        {
            var lc = Lin(color);
            int b = v.Count;
            for (int i = 0; i < pts.Count; i++) { v.Add(pts[i]); c.Add(lc); }
            for (int i = 1; i < pts.Count - 1; i++) { t.Add(b); t.Add(b + i); t.Add(b + i + 1); }
        }

        /// <summary>Fan from a centre vertex: centre colour fades to the rim colour (radial gradient / vignette).</summary>
        public static void AddFan(List<Vector3> v, List<Color> c, List<int> t, Vector2 center, IList<Vector2> rim, Color centerColor, Color rimColor)
        {
            int b = v.Count;
            v.Add(center); c.Add(Lin(centerColor));
            var lr = Lin(rimColor);
            for (int i = 0; i < rim.Count; i++) { v.Add(rim[i]); c.Add(lr); }
            for (int i = 0; i < rim.Count; i++) { t.Add(b); t.Add(b + 1 + i); t.Add(b + 1 + (i + 1) % rim.Count); }
        }

        /// <summary>Band between an outer and an inner outline with the same point count (a frame with a hole).</summary>
        public static void AddBand(List<Vector3> v, List<Color> c, List<int> t, IList<Vector2> outer, IList<Vector2> inner, Color color)
        {
            var lc = Lin(color);
            int b = v.Count, n = outer.Count;
            for (int i = 0; i < n; i++) { v.Add(outer[i]); c.Add(lc); v.Add(inner[i]); c.Add(lc); }
            for (int i = 0; i < n; i++)
            {
                int o0 = b + i * 2, i0 = o0 + 1, o1 = b + ((i + 1) % n) * 2, i1 = o1 + 1;
                t.Add(o0); t.Add(o1); t.Add(i1);
                t.Add(o0); t.Add(i1); t.Add(i0);
            }
        }

        /// <summary>Solid convex polygon filled from its centroid.</summary>
        public static void AddFilled(List<Vector3> v, List<Color> c, List<int> t, IList<Vector2> pts, Color color)
        {
            Vector2 cen = Vector2.zero;
            for (int i = 0; i < pts.Count; i++) cen += pts[i];
            AddFan(v, c, t, cen / pts.Count, pts, color, color);
        }

        /// <summary>Rounds every corner of a polygon with an arc of the given radius (fillet).</summary>
        public static List<Vector2> Rounded(IList<Vector2> pts, float radius, int segPerCorner = 8)
        {
            var o = new List<Vector2>(pts.Count * (segPerCorner + 1));
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = pts[(i + n - 1) % n], p1 = pts[i], p2 = pts[(i + 1) % n];
                Vector2 d0 = (p0 - p1), d1 = (p2 - p1);
                float l0 = d0.magnitude, l1 = d1.magnitude;
                if (radius <= 0f || l0 < 1e-5f || l1 < 1e-5f) { o.Add(p1); continue; }
                d0 /= l0; d1 /= l1;
                float ang = Mathf.Acos(Mathf.Clamp(Vector2.Dot(d0, d1), -1f, 1f));
                if (ang < 1e-3f || ang > Mathf.PI - 1e-3f) { o.Add(p1); continue; }
                float tanHalf = Mathf.Tan(ang * 0.5f);
                float tLen = Mathf.Min(radius / tanHalf, Mathf.Min(l0, l1) * 0.5f);
                float r = tLen * tanHalf;
                Vector2 bis = (d0 + d1).normalized;
                Vector2 center = p1 + bis * (r / Mathf.Sin(ang * 0.5f));
                Vector2 s = p1 + d0 * tLen, e = p1 + d1 * tLen;
                float a0 = Mathf.Atan2(s.y - center.y, s.x - center.x), a1 = Mathf.Atan2(e.y - center.y, e.x - center.x);
                float da = Mathf.DeltaAngle(a0 * Mathf.Rad2Deg, a1 * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                for (int k = 0; k <= segPerCorner; k++)
                {
                    float a = a0 + da * k / segPerCorner;
                    o.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            return o;
        }

        public static void AddQuad(List<Vector3> v, List<Color> c, List<int> t, Vector2 center, Vector2 size, Color color)
        {
            Vector2 h = size * 0.5f;
            AddPolygon(v, c, t, new[]
            {
                center + new Vector2(-h.x, -h.y), center + new Vector2(-h.x, h.y),
                center + new Vector2(h.x, h.y), center + new Vector2(h.x, -h.y)
            }, color);
        }

        /// <summary>Ellipse (or arc slice when arc &lt; 2π) with optional rotation.</summary>
        public static void AddDisc(List<Vector3> v, List<Color> c, List<int> t, Vector2 center, float rx, float ry,
            float rotation, int seg, Color color, float arcStart = 0f, float arc = Mathf.PI * 2f, Color? edgeColor = null)
        {
            var lc = Lin(color);
            var le = Lin(edgeColor ?? color);
            bool full = arc >= Mathf.PI * 2f - 0.001f;
            int b = v.Count;
            v.Add(center); c.Add(lc);
            float cs = Mathf.Cos(rotation), sn = Mathf.Sin(rotation);
            int count = full ? seg : seg + 1;
            for (int i = 0; i < count; i++)
            {
                float a = arcStart + arc * i / seg;
                float x = Mathf.Cos(a) * rx, y = Mathf.Sin(a) * ry;
                v.Add(center + new Vector2(x * cs - y * sn, x * sn + y * cs)); c.Add(le);
            }
            for (int i = 0; i < seg; i++)
            {
                int i1 = full ? (i + 1) % seg : i + 1;
                t.Add(b); t.Add(b + 1 + i); t.Add(b + 1 + i1);
            }
        }

        public static void AddRing(List<Vector3> v, List<Color> c, List<int> t, Vector2 center, float radius, float width, int seg, Color color)
        {
            var lc = Lin(color);
            int b = v.Count;
            float r0 = Mathf.Max(0f, radius - width * 0.5f), r1 = radius + width * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float a = Mathf.PI * 2f * i / seg;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                v.Add(center + d * r0); c.Add(lc);
                v.Add(center + d * r1); c.Add(lc);
            }
            for (int i = 0; i < seg; i++)
            {
                int a0 = b + i * 2, a1 = b + ((i + 1) % seg) * 2;
                t.Add(a0); t.Add(a0 + 1); t.Add(a1 + 1);
                t.Add(a0); t.Add(a1 + 1); t.Add(a1);
            }
        }

        /// <summary>Arrow from 'from' along unit 'dir'.</summary>
        public static void AddArrow(List<Vector3> v, List<Color> c, List<int> t, Vector2 from, Vector2 dir, float length,
            float shaftWidth, float headLength, float headWidth, Color color)
        {
            var lc = Lin(color);
            Vector2 n = new Vector2(-dir.y, dir.x);
            float sl = Mathf.Max(0f, length - headLength);
            int b = v.Count;
            Vector2 s0 = from, s1 = from + dir * sl, tip = from + dir * length;
            v.Add(s0 - n * shaftWidth * 0.5f); v.Add(s0 + n * shaftWidth * 0.5f);
            v.Add(s1 + n * shaftWidth * 0.5f); v.Add(s1 - n * shaftWidth * 0.5f);
            v.Add(s1 - n * headWidth * 0.5f); v.Add(s1 + n * headWidth * 0.5f); v.Add(tip);
            for (int i = 0; i < 7; i++) c.Add(lc);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            t.Add(b + 4); t.Add(b + 5); t.Add(b + 6);
            // rounded tail
            AddDisc(v, c, t, from, shaftWidth * 0.5f, shaftWidth * 0.5f, 0f, 10, color);
        }

        /// <summary>Same arrow, its opacity fading from <paramref name="startAlpha"/> (tail) to <paramref name="endAlpha"/> (tip).</summary>
        public static void AddArrowFaded(List<Vector3> v, List<Color> c, List<int> t, Vector2 from, Vector2 dir, float length,
            float shaftWidth, float headLength, float headWidth, Color color, float startAlpha, float endAlpha)
        {
            Vector2 n = new Vector2(-dir.y, dir.x);
            float sl = Mathf.Max(0f, length - headLength);
            Color A(float along) { var k = color; k.a = color.a * Mathf.Lerp(startAlpha, endAlpha, length > 0f ? along / length : 0f); return Lin(k); }
            int b = v.Count;
            Vector2 s0 = from, s1 = from + dir * sl, tip = from + dir * length;
            v.Add(s0 - n * shaftWidth * 0.5f); c.Add(A(0f));
            v.Add(s0 + n * shaftWidth * 0.5f); c.Add(A(0f));
            v.Add(s1 + n * shaftWidth * 0.5f); c.Add(A(sl));
            v.Add(s1 - n * shaftWidth * 0.5f); c.Add(A(sl));
            v.Add(s1 - n * headWidth * 0.5f); c.Add(A(sl));
            v.Add(s1 + n * headWidth * 0.5f); c.Add(A(sl));
            v.Add(tip); c.Add(A(length));
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            t.Add(b + 4); t.Add(b + 5); t.Add(b + 6);
            var tail = color; tail.a *= startAlpha;
            AddDisc(v, c, t, from, shaftWidth * 0.5f, shaftWidth * 0.5f, 0f, 10, tail);
        }
    }
}
