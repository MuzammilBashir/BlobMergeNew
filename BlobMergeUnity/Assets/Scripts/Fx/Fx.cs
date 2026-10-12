using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Lightweight pooled 2D effects rendered in one dynamic mesh: droplets, sparkles, shockwave rings.
    /// Also floating score texts and camera shake (trauma model).
    /// </summary>
    public class Fx : MonoBehaviour
    {
        public int maxParticles = 700;
        // shake tuning (driven from GameController's inspector each frame)
        [System.NonSerialized] public float MaxShakeOffset = 0.5f;   // world units at full trauma
        [System.NonSerialized] public float MaxShakeAngle = 2.5f;    // degrees of camera roll at full trauma
        [System.NonSerialized] public float ShakeDecay = 1.8f;       // trauma lost per second
        [System.NonSerialized] public float ShakeFrequency = 32f;
        [System.NonSerialized] public float KillBelowY = float.MinValue; // droplets vanish below the floor

        enum Kind : byte { Droplet, Ring, Sparkle, Bubble }

        struct Particle
        {
            public Vector2 pos, vel;
            public float size, life, maxLife, drag, gravity, width;
            public Color col;
            public Kind kind;
        }

        Particle[] parts;
        int count;
        Mesh mesh;
        readonly List<Vector3> v = new List<Vector3>(4096);
        readonly List<Color> c = new List<Color>(4096);
        readonly List<int> t = new List<int>(8192);

        class FloatText { public TextMeshPro tm; public float life, max; public Vector2 pos; public Color col; public float size; }
        const float FloatTextFontSize = 6f; // TMP world-space size for the "+N" score popups
        readonly List<FloatText> texts = new List<FloatText>();

        float trauma, sustain;
        public Vector2 ShakeOffset { get; private set; }
        public float ShakeAngle { get; private set; }

        public void Init(Material material)
        {
            parts = new Particle[maxParticles];
            var go = new GameObject("FxMesh");
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Fx" };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.sortingOrder = 30;
        }

        void Emit(Particle p)
        {
            if (count >= parts.Length) return;
            parts[count++] = p;
        }

        /// <summary>Radial burst of liquid droplets.</summary>
        public void Splash(Vector2 pos, Color col, int n, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity = 14f, Vector2 bias = default)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Emit(new Particle
                {
                    kind = Kind.Droplet,
                    pos = pos + d * Random.Range(0f, 0.15f),
                    vel = d * Random.Range(speedMin, speedMax) + bias,
                    size = Random.Range(sizeMin, sizeMax),
                    maxLife = Random.Range(0.35f, 0.7f),
                    drag = 2.2f,
                    gravity = gravity,
                    col = Color.Lerp(col, Color.white, Random.value * 0.25f)
                });
            }
        }

        public void Sparkles(Vector2 pos, int n, float speed, float radius)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Emit(new Particle
                {
                    kind = Kind.Sparkle,
                    pos = pos + d * radius * Random.Range(0.5f, 1f),
                    vel = d * speed * Random.Range(0.5f, 1.2f),
                    size = Random.Range(0.04f, 0.09f),
                    maxLife = Random.Range(0.25f, 0.5f),
                    drag = 4f,
                    col = Color.white
                });
            }
        }

        public void Ring(Vector2 pos, Color col, float r0, float r1, float life, float width)
        {
            Emit(new Particle { kind = Kind.Ring, pos = pos, size = r0, vel = new Vector2(r1, 0), maxLife = life, width = width, col = col });
        }

        // ---------------------------------------------------------------- Fish theme: water & bubbles
        static readonly Color WaterCol = new Color(0.78f, 0.95f, 1f, 0.8f);

        /// <summary>Small air bubbles: hollow rings with a spark, rising and wobbling (buoyancy instead of gravity).</summary>
        public void Bubbles(Vector2 pos, int n, float radius, float speed, float sizeMin = 0.05f, float sizeMax = 0.12f)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Emit(new Particle
                {
                    kind = Kind.Bubble,
                    pos = pos + d * radius * Random.Range(0.2f, 1f),
                    vel = d * speed * Random.Range(0.3f, 1f) + Vector2.up * Random.Range(0.5f, 1.5f),
                    size = Random.Range(sizeMin, sizeMax),
                    maxLife = Random.Range(0.7f, 1.3f),
                    drag = 3f,
                    gravity = -Random.Range(2f, 4f), // rises
                    width = Random.value * 6.28f,    // wobble phase
                    col = new Color(0.9f, 0.98f, 1f, 0.85f)
                });
            }
        }

        /// <summary>Clear water droplets bursting out (bubble pop / splash).</summary>
        public void WaterSplash(Vector2 pos, int n, float speedMin, float speedMax, float sizeMin, float sizeMax) =>
            Splash(pos, WaterCol, n, speedMin, speedMax, sizeMin, sizeMax, 9f);

        /// <summary>One small bubble left behind a thrown bubble.</summary>
        public void BubbleTrail(Vector2 pos, float size) =>
            Emit(new Particle
            {
                kind = Kind.Bubble, pos = pos, vel = Random.insideUnitCircle * 0.4f, size = size * Random.Range(0.6f, 1.1f),
                maxLife = 0.55f, drag = 2f, gravity = -2.5f, width = Random.value * 6.28f, col = new Color(0.9f, 0.98f, 1f, 0.7f)
            });

        /// <summary>Single trailing droplet (flight trail).</summary>
        public void Trail(Vector2 pos, Color col, float size)
        {
            Emit(new Particle { kind = Kind.Droplet, pos = pos, vel = Random.insideUnitCircle * 0.3f, size = size, maxLife = 0.35f, drag = 1f, col = new Color(col.r, col.g, col.b, 0.55f) });
        }

        public void FloatingText(string s, Vector2 pos, Color col, float size)
        {
            FloatText f = null;
            foreach (var x in texts) if (x.life >= x.max) { f = x; break; }
            if (f == null)
            {
                var go = new GameObject("FloatText");
                go.transform.SetParent(transform, false);
                var tm = go.AddComponent<TextMeshPro>();
                TextStyle.Apply(tm);
                tm.alignment = TextAlignmentOptions.Center;
                tm.rectTransform.sizeDelta = new Vector2(4f, 1.2f);
                tm.fontSize = FloatTextFontSize;
                tm.sortingOrder = 50;
                f = new FloatText { tm = tm };
                texts.Add(f);
            }
            f.tm.gameObject.SetActive(true);
            f.tm.text = s;
            f.pos = pos;
            f.col = col;
            f.size = size;
            f.life = 0f;
            f.max = 0.9f;
        }

        /// <summary>Add a one-off shake impulse (0..1 trauma; shake strength = trauma²).</summary>
        public void Shake(float amount) => trauma = Mathf.Min(1f, trauma + amount);

        /// <summary>Hold a minimum shake level this frame (call every frame while it should rumble).</summary>
        public void Sustain(float amount) => sustain = Mathf.Max(sustain, amount);

        void Update()
        {
            float dt = Time.deltaTime;

            trauma = Mathf.Max(0f, trauma - dt * ShakeDecay);
            float tr = Mathf.Max(trauma, sustain);
            sustain = 0f;
            float strength = tr * tr;
            float tt = GameTime.Unscaled * ShakeFrequency;
            ShakeOffset = new Vector2((Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * 2f, (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * 2f) * (strength * MaxShakeOffset);
            ShakeAngle = (Mathf.PerlinNoise(tt * 0.8f, 5.1f) - 0.5f) * 2f * strength * MaxShakeAngle;

            v.Clear(); c.Clear(); t.Clear();
            for (int i = 0; i < count; i++)
            {
                ref var p = ref parts[i];
                p.life += dt;
                if (p.life >= p.maxLife) { parts[i] = parts[--count]; i--; continue; }
                float k = p.life / p.maxLife;

                if (p.kind == Kind.Ring)
                {
                    float e = 1f - (1f - k) * (1f - k) * (1f - k);
                    float r = Mathf.Lerp(p.size, p.vel.x, e);
                    var col = p.col; col.a *= (1f - k);
                    MeshUtil.AddRing(v, c, t, p.pos, r, p.width * (1f - k * 0.7f), 40, col);
                    continue;
                }

                p.vel *= Mathf.Clamp01(1f - p.drag * dt);
                p.vel.y -= p.gravity * dt;
                p.pos += p.vel * dt;
                if (p.pos.y < KillBelowY) { parts[i] = parts[--count]; i--; continue; }
                if (p.kind == Kind.Bubble)
                {
                    // hollow bubble: thin rim + small spark, grows a little, wobbles sideways, fades out at the end
                    float br = p.size * (1f + 0.25f * k);
                    Vector2 bp = p.pos + new Vector2(Mathf.Sin(p.life * 9f + p.width) * 0.04f, 0f);
                    var bc = p.col; bc.a *= Mathf.Clamp01((1f - k) * 2.5f) * Mathf.Clamp01(p.life * 12f);
                    MeshUtil.AddRing(v, c, t, bp, br, Mathf.Max(0.012f, br * 0.22f), 14, bc);
                    var hc = bc; hc.a *= 0.9f;
                    MeshUtil.AddDisc(v, c, t, bp + new Vector2(-0.35f, 0.35f) * br, br * 0.22f, br * 0.22f, 0f, 6, hc);
                    continue;
                }
                float size = p.size * (p.kind == Kind.Sparkle ? (1f - k) : Mathf.Lerp(1f, 0.25f, k));
                var cc = p.col; cc.a *= p.kind == Kind.Sparkle ? 1f : Mathf.Clamp01((1f - k) * 1.6f);
                // droplets stretch along their velocity
                float sp = p.vel.magnitude;
                float stretch = 1f + Mathf.Min(sp * 0.04f, 0.8f);
                float ang = Mathf.Atan2(p.vel.y, p.vel.x);
                MeshUtil.AddDisc(v, c, t, p.pos, size * stretch, size, ang, p.kind == Kind.Sparkle ? 4 : 8, cc);
            }
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();

            foreach (var f in texts)
            {
                if (f.life >= f.max) continue;
                f.life += dt;
                float k = Mathf.Clamp01(f.life / f.max);
                if (k >= 1f) { f.tm.gameObject.SetActive(false); continue; }
                float pop = k < 0.15f ? Mathf.Lerp(0.4f, 1.25f, k / 0.15f) : Mathf.Lerp(1.25f, 1f, (k - 0.15f) / 0.2f);
                f.tm.transform.position = new Vector3(f.pos.x, f.pos.y + k * 1.3f, -0.3f);
                f.tm.transform.localScale = Vector3.one * f.size * pop;
                var col = f.col; col.a = 1f - Mathf.Clamp01((k - 0.55f) / 0.45f);
                f.tm.color = col;
            }
        }
    }
}
