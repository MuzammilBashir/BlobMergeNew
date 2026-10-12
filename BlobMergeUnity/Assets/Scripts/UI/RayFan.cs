using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Level-complete unlock burst rays (mock burst-v1/v2): one generated fan of tapered triangles from the centre,
    /// alternating long/short with random widths, each twinkling in length; alpha ~0.55 at the centre → 0.22 at 60 % →
    /// 0 at the tip. <see cref="blur"/> 0 = sharp, 1 = burst-v2 "blurry" (1.8× wider, soft edges, 1.25× brighter).
    /// Rotation = the RectTransform's; drawn with the additive UI material.
    /// </summary>
    public class RayFan : MaskableGraphic
    {
        struct Ray { public float a, len, w, tw; }
        readonly List<Ray> rays = new List<Ray>();

        /// <summary>Full ray radius (reference units, before each ray's length factor).</summary>
        [System.NonSerialized] public float radius;
        /// <summary>Burst-in 0..1 (scales the radius and alpha).</summary>
        [System.NonSerialized] public float burst;
        /// <summary>Seconds, for the per-ray length twinkle.</summary>
        [System.NonSerialized] public float time;
        [System.NonSerialized] public float blur;
        [System.NonSerialized] public Color rayColor = Color.white;

        public int Count => rays.Count;

        public void Generate(int n)
        {
            rays.Clear();
            for (int i = 0; i < n; i++)
                rays.Add(new Ray
                {
                    a = i / (float)n * Mathf.PI * 2f + (Random.value - 0.5f) * 0.08f,
                    len = (i % 2 == 1 ? 0.45f : 0.75f) + Random.value * 0.45f,
                    w = (i % 2 == 1 ? 0.012f : 0.022f) + Random.value * 0.018f,
                    tw = Random.value * 6f,
                });
        }

        void Update() => SetVerticesDirty(); // twinkle + burst-in change every frame

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (burst <= 0f) return;
            float b = Mathf.Clamp01(blur);
            float widthMul = 1f + 0.8f * b, bright = 1f + 0.25f * b;
            float edge = 1f - b; // across the width: spine full, edges fade with blur (soft edges)
            Color32 C(float alpha)
            {
                var c = rayColor;
                c.a = Mathf.Clamp01(alpha);
                return c;
            }
            float a0 = 0.55f * burst * bright, aMid = 0.22f * burst * bright;
            foreach (var r in rays)
            {
                float tw = 0.75f + 0.25f * Mathf.Sin(time * 1.7f + r.tw);
                float L = radius * burst * r.len * tw;
                float w = r.w * widthMul;
                int i0 = vh.currentVertCount;
                vh.AddVert(Vector3.zero, C(a0), Vector4.zero);                                   // 0 centre
                vh.AddVert(Dir(r.a - w) * L * 0.6f, C(aMid * edge), Vector4.zero);              // 1 mid left
                vh.AddVert(Dir(r.a) * L * 0.6f, C(aMid), Vector4.zero);                         // 2 mid spine
                vh.AddVert(Dir(r.a + w) * L * 0.6f, C(aMid * edge), Vector4.zero);              // 3 mid right
                vh.AddVert(Dir(r.a - w) * L, C(0f), Vector4.zero);                              // 4 tip left
                vh.AddVert(Dir(r.a) * L, C(0f), Vector4.zero);                                  // 5 tip spine
                vh.AddVert(Dir(r.a + w) * L, C(0f), Vector4.zero);                              // 6 tip right
                vh.AddTriangle(i0, i0 + 1, i0 + 2);
                vh.AddTriangle(i0, i0 + 2, i0 + 3);
                vh.AddTriangle(i0 + 1, i0 + 4, i0 + 5);
                vh.AddTriangle(i0 + 1, i0 + 5, i0 + 2);
                vh.AddTriangle(i0 + 2, i0 + 5, i0 + 6);
                vh.AddTriangle(i0 + 2, i0 + 6, i0 + 3);
            }
        }

        static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
    }
}
