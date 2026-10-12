using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>An eye on a fish sprite: centre and size in sprite-local units (sprite = 1×1, centre 0,0), lid colour.</summary>
    public readonly struct FishEye
    {
        public readonly Vector2 Center, Size;
        public readonly Color Skin;
        public FishEye(float x, float y, float w, float h, Color skin) { Center = new Vector2(x, y); Size = new Vector2(w, h); Skin = skin; }
    }

    /// <summary>
    /// Idle life for the Fish theme's creatures (Resources/Fish/fish_00..12, all facing right): the sprite is drawn as
    /// a small grid that a travelling wave bends (tail + fins / tentacles / flippers), and skin-coloured eyelids close
    /// over the eyes for a blink. Eye positions were measured on the sprites (Branding/themes/fish, 2026-10-08;
    /// tiers 10-12 from sprites/r2, 2026-10-09; tier 1 neon tetra re-measured 2026-10-12).
    /// </summary>
    public static class FishLook
    {
        public enum Swim { Tail, Tentacles, Flippers }

        // 1 fry, 2 neon tetra, 4 clownfish, 8 pufferfish, 16 angelfish, 32 turtle, 64 octopus, 128 dolphin, 256 shark, 512 sting ray,
        // 1024 hammerhead (eye on each hammer tip), 2048 orca, 4096 whale (user reorder 2026-10-12)
        public static readonly FishEye[][] Eyes =
        {
            /* 0 */ new[] { new FishEye(0.258f, 0.027f, 0.250f, 0.234f, new Color(0.99f, 0.44f, 0.00f)) },
            /* 1 */ new[] { new FishEye(0.264f, 0.027f, 0.207f, 0.195f, new Color(0.96f, 0.82f, 0.66f)) },
            /* 2 */ new[] { new FishEye(0.312f, 0.039f, 0.152f, 0.145f, new Color(0.99f, 0.56f, 0.00f)) },
            /* 3 */ new[] { new FishEye(0.158f, 0.082f, 0.199f, 0.195f, new Color(0.99f, 0.85f, 0.11f)), new FishEye(0.424f, 0.070f, 0.090f, 0.180f, new Color(0.99f, 0.85f, 0.11f)) },
            /* 4 */ new[] { new FishEye(0.287f, 0.021f, 0.113f, 0.137f, new Color(0.98f, 0.86f, 0.33f)) },
            /* 5 */ new[] { new FishEye(0.314f, 0.217f, 0.121f, 0.129f, new Color(0.62f, 0.83f, 0.18f)) },
            /* 6 */ new[] { new FishEye(0.223f, 0.172f, 0.094f, 0.156f, new Color(0.67f, 0.04f, 0.77f)), new FishEye(-0.006f, 0.096f, 0.152f, 0.160f, new Color(0.59f, 0.02f, 0.74f)) },
            /* 7 */ new[] { new FishEye(0.102f, 0.299f, 0.102f, 0.137f, new Color(0.56f, 0.69f, 0.84f)) },
            /* 8 */ new[] { new FishEye(0.234f, 0.090f, 0.125f, 0.125f, new Color(0.30f, 0.51f, 0.72f)) },
            /* 9 */ new[] { new FishEye(0.285f, 0.059f, 0.098f, 0.102f, new Color(0.06f, 0.38f, 0.64f)) },
            /* 10 */ new[] { new FishEye(0.064f, 0.016f, 0.098f, 0.102f, new Color(0.65f, 0.59f, 0.49f)), new FishEye(0.455f, 0.111f, 0.062f, 0.098f, new Color(0.55f, 0.49f, 0.39f)) },
            /* 11 */ new[] { new FishEye(0.238f, 0.092f, 0.090f, 0.102f, new Color(0.14f, 0.16f, 0.22f)) },
            /* 12 */ new[] { new FishEye(0.197f, -0.045f, 0.098f, 0.121f, new Color(0.04f, 0.49f, 0.97f)) },
        };

        /// <summary>Display name per tier 0..12 (level-complete ribbon "DOLPHIN UNLOCKED!"; upper-cased on screen).</summary>
        public static readonly string[] Names =
        {
            "Fry", "Neon Tetra", "Clownfish", "Pufferfish", "Angelfish", "Turtle", "Octopus", "Dolphin", "Shark", "Sting Ray",
            "Hammerhead", "Orca", "Whale",
        };

        public static string Name(int tier) => Names[Mathf.Clamp(tier, 0, Names.Length - 1)];

        static readonly Swim[] Mode = { Swim.Tail, Swim.Tail, Swim.Tail, Swim.Tail, Swim.Tail, Swim.Flippers, Swim.Tentacles, Swim.Tail, Swim.Tail, Swim.Flippers, Swim.Tail, Swim.Tail, Swim.Tail };   // ray: wings flap like the turtle's flippers
        static readonly float[] Amp = { 0.035f, 0.045f, 0.035f, 0.02f, 0.03f, 0.03f, 0.04f, 0.03f, 0.03f, 0.035f, 0.03f, 0.025f, 0.025f };   // sprite units
        static readonly float[] Speed = { 3.0f, 3.2f, 2.8f, 2.2f, 2.4f, 1.6f, 2.0f, 2.0f, 2.2f, 1.5f, 1.8f, 1.5f, 1.4f };                  // rad/s

        const int N = 12; // grid cells per side

        /// <summary>Where sprite-local point <paramref name="p"/> is drawn this frame.</summary>
        public static Vector2 Deform(int tier, Vector2 p, float t, float phase, float strength)
        {
            int i = Mathf.Clamp(tier, 0, Mode.Length - 1);
            float a = Amp[i] * strength, w = Speed[i];
            switch (Mode[i])
            {
                case Swim.Tail:
                {
                    // body wave growing toward the tail (left), fins at the top/bottom edges flutter a little
                    float u = p.x + 0.5f;
                    float tail = Mathf.Pow(Mathf.Clamp01(1f - u), 1.6f);
                    float dy = a * Mathf.Sin(t * w + phase - u * 4f) * tail;
                    float edge = Mathf.Clamp01((Mathf.Abs(p.y) - 0.18f) / 0.3f);
                    float dx = a * 0.6f * Mathf.Sin(t * w * 1.7f + phase + p.y * 6f) * edge;
                    return p + new Vector2(dx, dy);
                }
                case Swim.Tentacles:
                {
                    // head still, tentacles (lower part) wave sideways
                    float v = p.y + 0.5f;
                    float low = Mathf.Pow(Mathf.Clamp01(1f - v / 0.65f), 1.3f);
                    float dx = a * Mathf.Sin(t * w + phase + v * 7f + p.x * 3f) * low;
                    float dy = a * 0.4f * Mathf.Sin(t * w * 0.8f + phase + p.x * 5f) * low;
                    return p + new Vector2(dx, dy);
                }
                default:
                {
                    // flippers: the outer ring paddles slowly, shell/head stay
                    float r = p.magnitude / 0.5f;
                    float outer = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.95f, r));
                    float ang = Mathf.Atan2(p.y, p.x);
                    float dy = a * Mathf.Sin(t * w + phase + ang * 2f) * outer;
                    return p + new Vector2(0f, dy);
                }
            }
        }

        /// <summary>The creature as a deformed, textured grid (local units, 1×1 centred), tinted by <paramref name="tint"/>.</summary>
        public static void BuildBody(Mesh mesh, List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tris,
                                     int tier, float t, float phase, float strength, Color tint)
        {
            v.Clear(); uv.Clear(); c.Clear(); tris.Clear();
            for (int y = 0; y <= N; y++)
                for (int x = 0; x <= N; x++)
                {
                    Vector2 p = new Vector2(x / (float)N - 0.5f, y / (float)N - 0.5f);
                    v.Add(Deform(tier, p, t, phase, strength));
                    uv.Add(new Vector2(x / (float)N, y / (float)N));
                    c.Add(tint);
                }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int a = y * (N + 1) + x, b = a + 1, d = a + N + 1, e = d + 1;
                    tris.Add(a); tris.Add(d); tris.Add(e);
                    tris.Add(a); tris.Add(e); tris.Add(b);
                }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetColors(c);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Eyelids for a blink of <paramref name="close"/> (0 open … 1 shut), following the body wave.</summary>
        public static void BuildLids(Mesh mesh, List<Vector3> v, List<Color> c, List<int> tris, int tier, float close,
                                     float t, float phase, float strength)
        {
            v.Clear(); c.Clear(); tris.Clear();
            var eyes = Eyes[Mathf.Clamp(tier, 0, Eyes.Length - 1)];
            if (close > 0.02f)
                foreach (var e in eyes)
                {
                    Vector2 cen = Deform(tier, e.Center, t, phase, strength);
                    // lids meet in the middle: a skin ellipse that grows vertically over the eye
                    MeshUtil.AddDisc(v, c, tris, cen, e.Size.x * 0.56f, e.Size.y * 0.56f * close, 0f, 18, e.Skin);
                    if (close > 0.6f) // lash line once it's nearly shut
                    {
                        Color lash = Color.Lerp(e.Skin, new Color(0.08f, 0.05f, 0.15f), 0.75f);
                        MeshUtil.AddDisc(v, c, tris, cen + Vector2.down * e.Size.y * 0.04f, e.Size.x * 0.48f, e.Size.y * 0.06f, 0f, 14, lash);
                    }
                }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
