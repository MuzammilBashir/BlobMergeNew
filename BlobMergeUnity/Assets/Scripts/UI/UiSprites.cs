using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Procedurally rasterised UI sprites (anti-aliased signed-distance shapes), so the HUD needs no image assets.
    /// All sprites are white; tint them with Image.color.
    /// </summary>
    public static class UiSprites
    {
        static Sprite roundedRect, crown, gear, circle;

        delegate float Sdf(Vector2 p); // signed distance in pixels, negative inside

        static Texture2D Raster(int w, int h, Sdf sdf)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = sdf(new Vector2(x + 0.5f, y + 0.5f));
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    px[y * w + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false); // stays readable so the editor can save it as a PNG asset
            return tex;
        }

        static float RoundBox(Vector2 p, Vector2 center, Vector2 half, float r)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(r, r);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        /// <summary>9-sliced rounded rectangle (corner radius ~24 px at 100 px/unit).</summary>
        public static Sprite RoundedRect
        {
            get
            {
                if (roundedRect != null) return roundedRect;
                const int s = 64, r = 24;
                var tex = Raster(s, s, p => RoundBox(p, new Vector2(s / 2f, s / 2f), new Vector2(s / 2f, s / 2f), r));
                roundedRect = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
                return roundedRect;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (circle != null) return circle;
                const int s = 64;
                circle = Sprite.Create(Raster(s, s, p => (p - new Vector2(s / 2f, s / 2f)).magnitude - (s / 2f - 1f)), new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
                return circle;
            }
        }

        public static Sprite Crown
        {
            get
            {
                if (crown != null) return crown;
                const int s = 96;
                // crown polygon (y up), three spikes with balls on top
                Vector2[] poly =
                {
                    new Vector2(14, 18), new Vector2(82, 18), new Vector2(88, 66), new Vector2(66, 44),
                    new Vector2(48, 74), new Vector2(30, 44), new Vector2(8, 66)
                };
                float Poly(Vector2 p)
                {
                    float d = float.MaxValue; bool inside = false;
                    for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                    {
                        Vector2 a = poly[j], b = poly[i], e = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, e) / e.sqrMagnitude);
                        d = Mathf.Min(d, (p - (a + e * t)).magnitude);
                        if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                    }
                    return inside ? -d : d;
                }
                float Ball(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;
                var tex = Raster(s, s, p =>
                {
                    float d = Poly(p) - 3f; // slightly rounded
                    d = Mathf.Min(d, Ball(p, new Vector2(8, 70), 7f));
                    d = Mathf.Min(d, Ball(p, new Vector2(48, 80), 7f));
                    d = Mathf.Min(d, Ball(p, new Vector2(88, 70), 7f));
                    d = Mathf.Min(d, RoundBox(p, new Vector2(48, 13), new Vector2(36, 6), 4f)); // base band
                    return d;
                });
                crown = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
                return crown;
            }
        }

        public static Sprite Gear
        {
            get
            {
                if (gear != null) return gear;
                const int s = 96;
                Vector2 c = new Vector2(s / 2f, s / 2f);
                var tex = Raster(s, s, p =>
                {
                    Vector2 d = p - c;
                    float r = d.magnitude, a = Mathf.Atan2(d.y, d.x);
                    // 8 rounded teeth: radius alternates between body and tooth tip
                    float tooth = Mathf.Clamp01((Mathf.Cos(a * 8f) - 0.15f) * 2.2f);
                    float outer = Mathf.Lerp(30f, 42f, tooth);
                    float body = r - outer;
                    float hole = 13f - r;
                    return Mathf.Max(body, hole);
                });
                gear = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
                return gear;
            }
        }
    }
}
