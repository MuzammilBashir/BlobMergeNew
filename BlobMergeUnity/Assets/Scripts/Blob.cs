using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace BlobMerge
{
    public enum BlobState { Lane, Aiming, Free, Merging }

    /// <summary>
    /// A numbered soft blob: owns its SoftBody, renders it as a smooth glossy bubble with a centred label,
    /// and animates its size with a spring (spawn / merge pop).
    /// </summary>
    public class Blob : MonoBehaviour
    {
        public SoftBody Body { get; private set; }
        public int Value { get; private set; }
        public int Tier { get; private set; }
        public float Radius { get; private set; }
        public Color BaseColor { get; private set; }
        /// <summary>Hexagon theme piece (regular hexagon, flat soft-shaded look).</summary>
        public bool Hex { get; private set; }
        /// <summary>Fish theme piece: clear bubble with a sea creature inside.</summary>
        public bool Fish { get; private set; }
        Transform fishTr;                        // creature (animated grid) + its eyelids as a child
        MeshRenderer fishRenderer, lidRenderer;
        Mesh fishMesh, lidMesh;
        MaterialPropertyBlock fishProps;
        float nextBlink, blinkAt = -10f;
        static readonly List<Vector2> UV = new List<Vector2>(256);
        MeshFilter glossFilter;
        MeshRenderer glossRenderer;
        Mesh glossMesh;
        float fishFacing = 1f, fishTarget = 1f, fishPhase;

        // hexagon texture: pattern polygons in rest space (already clipped to the hexagon), each dark (true) or light
        readonly List<Vector2[]> texShapes = new List<Vector2[]>();
        readonly List<bool> texDark = new List<bool>();
        HexTexture texKind;

        public BlobState State;
        public float StateTime;      // seconds in current state
        public float SlowInLane;     // Free: seconds spent slow with its top up in the lane band
        public bool DropThrough;     // Free: sinking through the lane (the queue never holds a blob up)
        public bool InTank;          // Fish: joined the pile. Cannot rise back into the queue.
        public float AreaTarget = 1f;
        public float Flash;          // 0..1 white flash
        public bool Selected;
        public float LastImpactFx;   // time of last impact effect (cooldown)
        public int Chain;            // merges in a row that produced this blob
        public bool Growing;         // tweening from lane scale to landed scale after a throw
        public bool Landed;          // hexagon touched the floor/pile: held (and drawn) as an exact hexagon
        public float LandedTime;     // seconds since landing (the snap-back blend)
        public float LandSpeed;      // downward speed into the landing (0 = popped/placed, no rebound cap)
        public Vector2 LastVel;      // body velocity after the previous physics tick
        public float GrowT, GrowFrom, LaunchY;

        /// <summary>Blob + body state for what-if simulations (GameController.PreviewThrow).</summary>
        public sealed class SimSnapshot
        {
            public readonly SoftBody.Snapshot Body = new SoftBody.Snapshot();
            public BlobState State;
            public float StateTime, AreaTarget, AreaVel, GrowT, GrowFrom, LaunchY;
            public bool Growing, InTank;
        }

        public void SaveSim(SimSnapshot s)
        {
            Body.Save(s.Body);
            s.State = State; s.StateTime = StateTime; s.AreaTarget = AreaTarget; s.AreaVel = areaVel;
            s.GrowT = GrowT; s.GrowFrom = GrowFrom; s.LaunchY = LaunchY; s.Growing = Growing; s.InTank = InTank;
        }

        public void LoadSim(SimSnapshot s)
        {
            Body.Load(s.Body);
            State = s.State; StateTime = s.StateTime; AreaTarget = s.AreaTarget; areaVel = s.AreaVel;
            GrowT = s.GrowT; GrowFrom = s.GrowFrom; LaunchY = s.LaunchY; Growing = s.Growing; InTank = s.InTank;
        }

        /// <summary>Set size directly (tweens), bypassing the pop spring.</summary>
        public void SetAreaDirect(float area)
        {
            Body.AreaScale = area;
            AreaTarget = area;
            areaVel = 0f;
        }

        /// <summary>Radius right now (lane blobs are scaled down via AreaScale).</summary>
        public float CurrentRadius => Radius * Mathf.Sqrt(Mathf.Max(0.01f, Body.AreaScale));

        float areaVel;
        GameController game;

        MeshFilter fillFilter, outlineFilter;
        MeshRenderer fillRenderer, outlineRenderer;
        Mesh fillMesh, outlineMesh;
        TextMeshPro label;

        static readonly List<Vector3> V = new List<Vector3>(256);
        static readonly List<Color> C = new List<Color>(256);
        static readonly List<int> T = new List<int>(768);
        static Vector2[] outline = new Vector2[256];
        static Vector2[] ideal = new Vector2[64];
        static Vector2[] normals = new Vector2[256];

        const int Subdiv = 3;

        void Awake()
        {
            fillFilter = gameObject.AddComponent<MeshFilter>();
            fillRenderer = gameObject.AddComponent<MeshRenderer>();
            fillMesh = new Mesh { name = "BlobFill" };
            fillMesh.MarkDynamic();
            fillFilter.sharedMesh = fillMesh;
            fillRenderer.sortingOrder = 10;

            var o = new GameObject("Outline");
            o.transform.SetParent(transform, false);
            outlineFilter = o.AddComponent<MeshFilter>();
            outlineRenderer = o.AddComponent<MeshRenderer>();
            outlineMesh = new Mesh { name = "BlobOutline" };
            outlineMesh.MarkDynamic();
            outlineFilter.sharedMesh = outlineMesh;
            outlineRenderer.sortingOrder = 9;

            var lo = new GameObject("Label");
            lo.transform.SetParent(transform, false);
            label = lo.AddComponent<TextMeshPro>();
            label.sortingOrder = 20;
        }

        public void Init(GameController g, int value, Vector2 pos, float startArea, int id)
        {
            game = g;
            Value = value;
            Tier = GameController.Tier(value);
            Radius = g.RadiusForTier(Tier);
            BaseColor = g.ColorForTier(Tier);

            Hex = g.Hex;
            Fish = g.FishMode;
            SetupFish(g);
            Body = new SoftBody(g.PointsPerPiece, Radius, g.silhouetteNoise, Random.Range(0, 100000), pos, g.density, g.elongation,
                                Hex ? g.hexCornerRound : -1f)
            {
                Owner = this,
                Id = id,
                ShapeStiffness = g.ShapeStiffnessForTier(Tier),
                Pressure = g.PressureForTier(Tier),
                MaxSquash = g.MaxSquashNow,
                Rigidity = g.RigidityNow,
                AreaScale = startArea
            };
            Body.Place(pos, Mathf.Sqrt(startArea));
            BuildTexture(g, id);

            State = BlobState.Lane;
            StateTime = 0f;
            AreaTarget = 1f;
            areaVel = 0f;
            Flash = 0f;
            Selected = false;
            LastImpactFx = 0f;
            Chain = 0;
            Growing = false;
            GrowT = 0f;
            InTank = false;
            Landed = false;
            LandedTime = 0f;
            LandSpeed = 0f;
            LastVel = Vector2.zero;

            var mr = fillRenderer;
            mr.sharedMaterial = g.material;
            outlineRenderer.sharedMaterial = g.material;

            TextStyle.Apply(label);
            label.alignment = TextAlignmentOptions.Center;
            label.text = value.ToString();
            FitLabel();
        }

        public void SetState(BlobState s)
        {
            State = s;
            StateTime = 0f;
        }

        /// <summary>Area spring for pop / squash-in animations (runs at physics rate).</summary>
        public void TickPhysics(float h)
        {
            float k = game.popSpring, c = game.popDamping;
            float a = k * (AreaTarget - Body.AreaScale) - c * areaVel;
            areaVel += a * h;
            Body.AreaScale = Mathf.Max(0.05f, Body.AreaScale + areaVel * h);
        }

        public void TickVisual(float dt)
        {
            StateTime += dt;
            Flash = Mathf.Max(0f, Flash - dt * 3.5f);
            Render();
        }

        // TMP font size that gives 1 world unit of digit height (measured once from the actual font + style)
        static float fontSizePerUnit;

        /// <summary>
        /// Same number size on every blob: a fixed digit height in world units (GameController.blobLabelHeight),
        /// independent of value, digit count and blob size. No auto-sizing.
        /// </summary>
        void FitLabel()
        {
            label.enableAutoSizing = false;
            label.rectTransform.sizeDelta = new Vector2(6f, 2f); // generous box; text overflows centred, never wraps
            if (fontSizePerUnit <= 0f)
            {
                string keep = label.text;
                label.fontSize = 10f;
                label.text = "8";
                label.ForceMeshUpdate();
                float h = label.textBounds.size.y;
                fontSizePerUnit = h > 1e-4f ? 10f / h : 10f;
                label.text = keep;
            }
            label.fontSize = game.blobLabelHeight * fontSizePerUnit;
            label.ForceMeshUpdate();
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        void Render()
        {
            var b = Body;
            int n = b.N, m = n * Subdiv;
            if (outline.Length < m) { outline = new Vector2[m]; normals = new Vector2[m]; }

            var src = b.P;
            if (Hex && Landed && game.hexRigidWhenLanded)
            {
                // straight sides once landed: draw the exact hexagon at the body's pose (blended in over the settle time)
                if (ideal.Length < n) ideal = new Vector2[n];
                float w = game.hexSettleTime > 0f ? Mathf.Clamp01(LandedTime / game.hexSettleTime) : 1f;
                float th = b.FitRotation(), s = Mathf.Sqrt(Mathf.Max(0.01f, b.AreaScale));
                float cs = Mathf.Cos(th) * s, sn = Mathf.Sin(th) * s;
                Vector2 cen = b.Centroid();
                for (int i = 0; i < n; i++)
                {
                    Vector2 q = b.RestPoint(i);
                    ideal[i] = Vector2.Lerp(b.P[i], cen + new Vector2(cs * q.x - sn * q.y, sn * q.x + cs * q.y), w);
                }
                src = ideal;
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = src[(i + n - 1) % n], p1 = src[i], p2 = src[(i + 1) % n], p3 = src[(i + 2) % n];
                for (int s = 0; s < Subdiv; s++) outline[i * Subdiv + s] = CatmullRom(p0, p1, p2, p3, s / (float)Subdiv);
            }
            Vector2 c = Vector2.zero;
            for (int i = 0; i < m; i++) c += outline[i];
            c /= m;
            for (int i = 0; i < m; i++)
            {
                Vector2 t = outline[(i + 1) % m] - outline[(i + m - 1) % m];
                normals[i] = new Vector2(t.y, -t.x).normalized;
            }

            if (Hex) { RenderHex(c, m); RenderSelectionAndLabel(c, m); return; }
            if (Fish) { RenderBubble(c, m); RenderSelectionAndLabel(c, m); return; }

            Color baseCol = Color.Lerp(BaseColor, Color.white, Flash * 0.85f);
            Color centreCol = Color.Lerp(baseCol, Color.white, 0.16f);
            Color rimCol = Color.Lerp(baseCol, Color.black, 0.12f);

            // ---- fill: centre -> inner ring -> rim ----
            V.Clear(); C.Clear(); T.Clear();
            Vector2 hl = HighlightAnchor(c, m, out float extent);
            Vector2 centre = c + (hl - c) * 0.18f;
            V.Add(centre); C.Add(MeshUtil.Lin(centreCol));
            var lb = MeshUtil.Lin(baseCol);
            var lr = MeshUtil.Lin(rimCol);
            for (int i = 0; i < m; i++) { V.Add(c + (outline[i] - c) * 0.84f); C.Add(lb); }
            for (int i = 0; i < m; i++) { V.Add(outline[i]); C.Add(lr); }
            for (int i = 0; i < m; i++)
            {
                int i1 = (i + 1) % m;
                T.Add(0); T.Add(1 + i); T.Add(1 + i1);
                int a0 = 1 + i, a1 = 1 + i1, r0 = 1 + m + i, r1 = 1 + m + i1;
                T.Add(a0); T.Add(r0); T.Add(r1);
                T.Add(a0); T.Add(r1); T.Add(a1);
            }
            // glossy highlight (water-bubble sheen)
            Vector2 hd = (hl - c);
            float ang = Mathf.Atan2(hd.y, hd.x) + Mathf.PI * 0.5f;
            float hs = Mathf.Clamp(extent, 0.2f, 2f);
            MeshUtil.AddDisc(V, C, T, c + hd * 0.62f, hs * 0.26f, hs * 0.11f, ang, 14, new Color(1, 1, 1, 0.42f), edgeColor: new Color(1, 1, 1, 0.0f));
            MeshUtil.AddDisc(V, C, T, c + hd * 0.38f + new Vector2(hd.y, -hd.x) * 0.35f, hs * 0.06f, hs * 0.06f, 0f, 8, new Color(1, 1, 1, 0.35f));

            fillMesh.Clear();
            fillMesh.SetVertices(V);
            fillMesh.SetColors(C);
            fillMesh.SetTriangles(T, 0);
            fillMesh.RecalculateBounds();

            RenderSelectionAndLabel(c, m);
        }

        /// <summary>
        /// Flat soft-shaded hexagon (matte, no sheen): soft top-light gradient, a slightly lighter bevel band inside the
        /// edge, a thin darker rim of the same hue and a soft drop shadow.
        /// </summary>
        void RenderHex(Vector2 c, int m)
        {
            Color baseCol = Color.Lerp(BaseColor, Color.white, Flash * 0.85f);
            Color top = Color.Lerp(baseCol, Color.white, 0.10f), bottom = Color.Lerp(baseCol, Color.black, 0.10f);
            Color bevel = Color.Lerp(baseCol, Color.white, 0.18f);
            Color rim = Color.Lerp(baseCol, Color.black, 0.28f);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < m; i++) { minY = Mathf.Min(minY, outline[i].y); maxY = Mathf.Max(maxY, outline[i].y); }
            float invH = 1f / Mathf.Max(1e-4f, maxY - minY);
            Color Shade(Vector2 p, Color lo, Color hi) => Color.Lerp(lo, hi, (p.y - minY) * invH);

            float r = CurrentRadius;
            float rimW = Mathf.Clamp(r * 0.07f, 0.03f, 0.07f), bevelW = rimW * 1.6f;
            V.Clear(); C.Clear(); T.Clear();

            // drop shadow (offset copy, faint)
            Vector2 so = new Vector2(0.03f, -0.07f) * Mathf.Clamp(r, 0.4f, 1.6f);
            var sh = MeshUtil.Lin(new Color(0.03f, 0.01f, 0.1f, 0.28f));
            V.Add(c + so); C.Add(sh);
            for (int i = 0; i < m; i++) { V.Add(outline[i] + so); C.Add(sh); }
            for (int i = 0; i < m; i++) { T.Add(0); T.Add(1 + i); T.Add(1 + (i + 1) % m); }

            // body: centre -> inner fill ring -> bevel ring (lighter at the top) -> rim (crisp, darker)
            int o = V.Count;
            V.Add(c); C.Add(MeshUtil.Lin(Color.Lerp(top, bottom, 0.5f)));
            for (int i = 0; i < m; i++) { Vector2 p = outline[i] - normals[i] * (rimW + bevelW); V.Add(p); C.Add(MeshUtil.Lin(Shade(p, bottom, top))); }
            for (int i = 0; i < m; i++)
            {
                Vector2 p = outline[i] - normals[i] * rimW;
                float up = Mathf.Clamp01(Vector2.Dot(normals[i], new Vector2(-0.35f, 0.94f)));
                V.Add(p); C.Add(MeshUtil.Lin(Color.Lerp(Shade(p, bottom, top), bevel, up * 0.8f)));
            }
            for (int i = 0; i < m; i++) { V.Add(outline[i] - normals[i] * rimW); C.Add(MeshUtil.Lin(rim)); }
            for (int i = 0; i < m; i++) { V.Add(outline[i]); C.Add(MeshUtil.Lin(rim)); }
            for (int i = 0; i < m; i++)
            {
                int i1 = (i + 1) % m;
                T.Add(o); T.Add(o + 1 + i); T.Add(o + 1 + i1);
                for (int ring = 0; ring < 3; ring += 2) // fill->bevel and rimInner->rimOuter (bevel->rimInner is the crisp edge)
                {
                    int a0 = o + 1 + ring * m + i, a1 = o + 1 + ring * m + i1, b0 = a0 + m, b1 = a1 + m;
                    T.Add(a0); T.Add(b0); T.Add(b1);
                    T.Add(a0); T.Add(b1); T.Add(a1);
                }
            }
            AddTexture(baseCol);
            fillMesh.Clear();
            fillMesh.SetVertices(V);
            fillMesh.SetColors(C);
            fillMesh.SetTriangles(T, 0);
            fillMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ hexagon texture
        void BuildTexture(GameController g, int id)
        {
            texShapes.Clear(); texDark.Clear();
            texKind = Hex ? g.hexTexture : HexTexture.None;
            if (texKind == HexTexture.MixByValue)
                texKind = (HexTexture)(1 + Tier % 3); // camo, stripes, craters by value
            if (texKind == HexTexture.None) return;

            // clip region: the rest hexagon shrunk so the pattern stays inside the rim
            var clip = new Vector2[Body.N];
            for (int i = 0; i < Body.N; i++) clip[i] = Body.RestPoint(i) * 0.86f;
            var rnd = new System.Random(id * 7919 + Value);
            float R() => (float)rnd.NextDouble();
            float r = Radius;

            switch (texKind)
            {
                case HexTexture.Stripes:
                {
                    // gas-giant bands across the piece (dark, light, dark), slightly tilted
                    float tilt = (R() - 0.5f) * 0.5f;
                    float[] ys = { 0.42f, 0.02f, -0.4f };
                    float[] hs = { 0.16f, 0.1f, 0.2f };
                    for (int k = 0; k < 3; k++)
                    {
                        float y = (ys[k] + (R() - 0.5f) * 0.1f) * r, h = hs[k] * r;
                        AddTexShape(Rotate(new[] { new Vector2(-2f * r, y - h), new Vector2(2f * r, y - h), new Vector2(2f * r, y + h), new Vector2(-2f * r, y + h) }, tilt), clip, k != 1);
                    }
                    break;
                }
                case HexTexture.Camo:
                {
                    // planet patches: a few irregular blobs, alternating darker / lighter
                    int count = 4 + rnd.Next(2);
                    for (int k = 0; k < count; k++)
                    {
                        float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * 0.72f;
                        Vector2 cen = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d * r;
                        float size = (0.2f + R() * 0.22f) * r, p1 = R() * 6.28f, p2 = R() * 6.28f;
                        var pts = new Vector2[12];
                        for (int i = 0; i < pts.Length; i++)
                        {
                            float t = i / (float)pts.Length * Mathf.PI * 2f;
                            float rr = size * (1f + 0.22f * Mathf.Sin(t * 2f + p1) + 0.12f * Mathf.Sin(t * 3f + p2));
                            pts[i] = cen + new Vector2(Mathf.Cos(t) * rr * 1.3f, Mathf.Sin(t) * rr);
                        }
                        AddTexShape(pts, clip, k % 3 != 2);
                    }
                    break;
                }
                case HexTexture.Craters:
                {
                    // moon craters: dark disc + a base-coloured disc offset up-left = dark crescent at the lower right
                    int count = 3 + rnd.Next(2);
                    for (int k = 0; k < count; k++)
                    {
                        float a = R() * Mathf.PI * 2f, d = Mathf.Sqrt(R()) * 0.6f;
                        Vector2 cen = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d * r;
                        float size = (0.1f + R() * 0.15f) * r;
                        AddTexShape(Circle(cen, size, 14), clip, true);
                        AddTexShape(Circle(cen + new Vector2(-0.22f, 0.22f) * size, size * 0.82f, 14), clip, false);
                    }
                    break;
                }
            }
        }

        static Vector2[] Rotate(Vector2[] pts, float a)
        {
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2(pts[i].x * c - pts[i].y * s, pts[i].x * s + pts[i].y * c);
            return pts;
        }

        static Vector2[] Circle(Vector2 c, float r, int n)
        {
            var p = new Vector2[n];
            for (int i = 0; i < n; i++) { float t = i / (float)n * Mathf.PI * 2f; p[i] = c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r; }
            return p;
        }

        /// <summary>Clip a CCW pattern polygon to the (convex, CCW) hexagon and keep it (Sutherland–Hodgman).</summary>
        void AddTexShape(Vector2[] poly, Vector2[] clip, bool dark)
        {
            var output = new List<Vector2>(poly);
            for (int e = 0; e < clip.Length && output.Count > 0; e++)
            {
                Vector2 a = clip[e], b = clip[(e + 1) % clip.Length];
                var input = output;
                output = new List<Vector2>(input.Count + 4);
                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 p = input[i], q = input[(i + 1) % input.Count];
                    bool pin = Cross(b - a, p - a) >= 0f, qin = Cross(b - a, q - a) >= 0f;
                    if (pin) output.Add(p);
                    if (pin != qin)
                    {
                        float tp = Cross(b - a, p - a), tq = Cross(b - a, q - a);
                        output.Add(Vector2.Lerp(p, q, tp / (tp - tq)));
                    }
                }
            }
            if (output.Count < 3) return;
            texShapes.Add(output.ToArray());
            texDark.Add(dark);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        /// <summary>Pattern polygons mapped through the body's current affine fit, so they turn and squash with it.</summary>
        void AddTexture(Color baseCol)
        {
            if (texShapes.Count == 0) return;
            Body.FitAffine(out Vector2 c, out Vector4 m);
            float k = game.hexTextureContrast;
            var dark = MeshUtil.Lin(Color.Lerp(baseCol, Color.black, k));
            var light = MeshUtil.Lin(Color.Lerp(baseCol, Color.white, k * 0.9f));
            var plain = MeshUtil.Lin(baseCol);
            for (int s = 0; s < texShapes.Count; s++)
            {
                var pts = texShapes[s];
                // craters: the second disc of each pair is drawn in the plain colour (cuts the crescent)
                var col = texKind == HexTexture.Craters ? (texDark[s] ? dark : plain) : (texDark[s] ? dark : light);
                int b = V.Count;
                Vector2 cen = Vector2.zero;
                for (int i = 0; i < pts.Length; i++) cen += pts[i];
                cen /= pts.Length;
                V.Add(c + new Vector2(m.x * cen.x + m.y * cen.y, m.z * cen.x + m.w * cen.y)); C.Add(col);
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector2 q = pts[i];
                    V.Add(c + new Vector2(m.x * q.x + m.y * q.y, m.z * q.x + m.w * q.y)); C.Add(col);
                }
                for (int i = 0; i < pts.Length; i++) { T.Add(b); T.Add(b + 1 + i); T.Add(b + 1 + (i + 1) % pts.Length); }
            }
        }

        // ------------------------------------------------------------------ fish bubble
        void SetupFish(GameController g)
        {
            if (Fish && fishRenderer == null)
            {
                var fo = new GameObject("Fish");
                fo.transform.SetParent(transform, false);
                fishTr = fo.transform;
                fo.AddComponent<MeshFilter>().sharedMesh = fishMesh = new Mesh { name = "FishBody" };
                fishMesh.MarkDynamic();
                fishRenderer = fo.AddComponent<MeshRenderer>();
                fishRenderer.sortingOrder = 11; // bubble 10 < creature 11 < lids 12 < gloss 13 < number 20
                var lo = new GameObject("Lids");
                lo.transform.SetParent(fo.transform, false); // shares the creature's pose and flip
                lo.AddComponent<MeshFilter>().sharedMesh = lidMesh = new Mesh { name = "FishLids" };
                lidMesh.MarkDynamic();
                lidRenderer = lo.AddComponent<MeshRenderer>();
                lidRenderer.sortingOrder = 12;
                fishProps = new MaterialPropertyBlock();

                var go = new GameObject("Gloss");
                go.transform.SetParent(transform, false);
                glossFilter = go.AddComponent<MeshFilter>();
                glossRenderer = go.AddComponent<MeshRenderer>();
                glossMesh = new Mesh { name = "BubbleGloss" };
                glossMesh.MarkDynamic();
                glossFilter.sharedMesh = glossMesh;
                glossRenderer.sortingOrder = 13;
            }
            if (fishRenderer != null)
            {
                fishRenderer.enabled = lidRenderer.enabled = glossRenderer.enabled = Fish;
                if (Fish)
                {
                    var sp = g.FishSprite(Tier);
                    if (g.FishMaterial != null) fishRenderer.sharedMaterial = g.FishMaterial;
                    if (sp != null) { fishProps.SetTexture("_MainTex", sp.texture); fishRenderer.SetPropertyBlock(fishProps); }
                    lidRenderer.sharedMaterial = g.material;
                    glossRenderer.sharedMaterial = g.material;
                    fishPhase = Random.value * 6.28f;
                    fishFacing = fishTarget = Random.value < 0.5f ? -1f : 1f;
                    nextBlink = GameTime.Unscaled + Random.Range(0.5f, 4f);
                    blinkAt = -10f;
                }
            }
        }

        /// <summary>Eyelid closure now: blinks every 2-5 s (sometimes twice), 0.16 s each.</summary>
        float BlinkClose(float t)
        {
            // the recorder switches the clock's base when capture starts: re-spread blinks scheduled on the old clock
            if (nextBlink - t > game.fishBlinkMax + 1f || t < blinkAt - 0.5f) { nextBlink = t + Random.Range(0.2f, game.fishBlinkMax); blinkAt = -10f; }
            if (t >= nextBlink)
            {
                blinkAt = t;
                nextBlink = t + (Random.value < 0.15f ? 0.3f : Random.Range(game.fishBlinkMin, game.fishBlinkMax));
            }
            float k = (t - blinkAt) / 0.16f;
            if (k < 0f || k > 1f) return 0f;
            return k < 0.4f ? k / 0.4f : k < 0.6f ? 1f : (1f - k) / 0.4f;
        }

        /// <summary>
        /// Clear soap-bubble look: faint tinted body, brighter glassy rim, a creature inside that bobs and faces the
        /// way it moves, and a gloss layer (top-left sheen + small spark) drawn over the creature.
        /// </summary>
        void RenderBubble(Vector2 c, int m)
        {
            Color tint = Color.Lerp(BaseColor, Color.white, 0.35f);
            if (Flash > 0f) tint = Color.Lerp(tint, Color.white, Flash * 0.8f);
            float a = game.bubbleTint;
            var cCentre = MeshUtil.Lin(new Color(tint.r, tint.g, tint.b, a * 0.55f));
            var cInner = MeshUtil.Lin(new Color(tint.r, tint.g, tint.b, a));
            var cEdge = MeshUtil.Lin(new Color(tint.r, tint.g, tint.b, Mathf.Min(1f, a * 2.2f)));
            var rimCol = MeshUtil.Lin(new Color(0.85f, 0.97f, 1f, game.bubbleRim));
            float r = CurrentRadius, rimW = Mathf.Clamp(r * 0.06f, 0.025f, 0.06f);

            V.Clear(); C.Clear(); T.Clear();
            V.Add(c); C.Add(cCentre);
            for (int i = 0; i < m; i++) { V.Add(c + (outline[i] - c) * 0.78f); C.Add(cInner); }
            for (int i = 0; i < m; i++) { V.Add(outline[i] - normals[i] * rimW); C.Add(cEdge); }
            for (int i = 0; i < m; i++) { V.Add(outline[i] - normals[i] * rimW); C.Add(rimCol); }
            for (int i = 0; i < m; i++) { V.Add(outline[i]); C.Add(rimCol); }
            for (int i = 0; i < m; i++)
            {
                int i1 = (i + 1) % m;
                T.Add(0); T.Add(1 + i); T.Add(1 + i1);
                for (int ring = 0; ring < 3; ring += 2) // body: inner -> edge, rim: crisp band
                {
                    int a0 = 1 + ring * m + i, a1 = 1 + ring * m + i1, b0 = a0 + m, b1 = a1 + m;
                    T.Add(a0); T.Add(b0); T.Add(b1);
                    T.Add(a0); T.Add(b1); T.Add(a1);
                }
            }
            fillMesh.Clear();
            fillMesh.SetVertices(V);
            fillMesh.SetColors(C);
            fillMesh.SetTriangles(T, 0);
            fillMesh.RecalculateBounds();

            // creature: centred a little high (number below), bobbing, facing its travel direction, tilting with it
            float t = GameTime.Unscaled;
            Vector2 vel = Body.Velocity;
            // turn only when really travelling (a throw / a fall sideways), and turn fast: drifting in the lane or
            // settling in the pile must not flip it (mid-flip it is a thin sliver)
            if (State == BlobState.Free && Mathf.Abs(vel.x) > 2.5f) fishTarget = Mathf.Sign(vel.x);
            fishFacing = Mathf.MoveTowards(fishFacing, fishTarget, Time.deltaTime * 12f);
            // fits the bubble as it is now: a squashed bubble shrinks its creature instead of letting it poke out
            float span = Mathf.Min(Body.Max.x - Body.Min.x, Body.Max.y - Body.Min.y);
            float size = Mathf.Min(2f * r, span) * game.fishSize;
            Vector2 fp = c + new Vector2(0f, r * 0.08f + Mathf.Sin(t * game.fishBobSpeed + fishPhase) * r * game.fishBob);
            fishTr.position = new Vector3(fp.x, fp.y, -0.05f);
            float tilt = Mathf.Clamp(vel.y * 2.5f, -25f, 25f) * Mathf.Sign(fishFacing) + Mathf.Sin(t * 1.1f + fishPhase) * 4f;
            fishTr.rotation = Quaternion.Euler(0f, 0f, tilt);
            float sx = Mathf.Abs(fishFacing) < 0.15f ? 0.15f * Mathf.Sign(fishFacing == 0f ? 1f : fishFacing) : fishFacing;
            fishTr.localScale = new Vector3(size * sx, size, 1f);
            // alive: swim wave through the body (tail/fins, tentacles, flippers) + eye blinks
            Color body = Flash > 0f ? Color.Lerp(Color.white, new Color(1.6f, 1.6f, 1.6f), Flash) : Color.white;
            FishLook.BuildBody(fishMesh, V, UV, C, T, Tier, t, fishPhase, game.fishSwim, body);
            FishLook.BuildLids(lidMesh, V, C, T, Tier, BlinkClose(t), t, fishPhase, game.fishSwim);

            // gloss over the creature: soft top-left sheen, a small spark, faint lower-right reflection
            V.Clear(); C.Clear(); T.Clear();
            Vector2 hl = HighlightAnchor(c, m, out float extent);
            Vector2 hd = hl - c;
            float ang = Mathf.Atan2(hd.y, hd.x) + Mathf.PI * 0.5f;
            float hs = Mathf.Clamp(extent, 0.2f, 2.5f);
            MeshUtil.AddDisc(V, C, T, c + hd * 0.66f, hs * 0.34f, hs * 0.09f, ang, 16, new Color(1, 1, 1, 0.55f), edgeColor: new Color(1, 1, 1, 0f));
            MeshUtil.AddDisc(V, C, T, c + hd * 0.42f + new Vector2(hd.y, -hd.x) * 0.42f, hs * 0.05f, hs * 0.05f, 0f, 8, new Color(1, 1, 1, 0.7f));
            MeshUtil.AddDisc(V, C, T, c - hd * 0.7f, hs * 0.26f, hs * 0.05f, ang, 14, new Color(1, 1, 1, 0.18f), edgeColor: new Color(1, 1, 1, 0f));
            glossMesh.Clear();
            glossMesh.SetVertices(V);
            glossMesh.SetColors(C);
            glossMesh.SetTriangles(T, 0);
            glossMesh.RecalculateBounds();
        }

        void RenderSelectionAndLabel(Vector2 c, int m)
        {
            // ---- selection outline ----
            outlineRenderer.enabled = Selected;
            if (Selected)
            {
                V.Clear(); C.Clear(); T.Clear();
                float w = 0.1f + 0.025f * Mathf.Sin(Time.time * 10f);
                // fish bubbles are see-through: a full-white selection behind them reads as a solid white bubble
                var white = MeshUtil.Lin(new Color(1f, 1f, 1f, Fish ? 0.2f : 1f));
                V.Add(c); C.Add(white);
                for (int i = 0; i < m; i++) { V.Add(outline[i] + normals[i] * w); C.Add(white); }
                for (int i = 0; i < m; i++) { T.Add(0); T.Add(1 + i); T.Add(1 + (i + 1) % m); }
                outlineMesh.Clear();
                outlineMesh.SetVertices(V);
                outlineMesh.SetColors(C);
                outlineMesh.SetTriangles(T, 0);
                outlineMesh.RecalculateBounds();
            }

            // ---- label: centred, upright, constant size (optionally follows blob size) ----
            label.enabled = !Fish || game.fishShowNumbers; // fish theme: the creature is the value (user)
            Vector2 lp = Fish ? c + Vector2.down * (CurrentRadius * game.fishLabelDrop) : c; // fish: number under the creature
            label.transform.position = new Vector3(lp.x, lp.y, -0.1f);
            float labelScale = 1f;
            if (game.blobLabelFollowsSize)
                labelScale = Mathf.Min(Mathf.Sqrt(Mathf.Max(0.05f, Body.AreaScale)), 1.6f);
            else if (Body.AreaScale < 0.3f * game.LaneAreaFor(Value))
                labelScale = Mathf.Sqrt(Body.AreaScale / (0.3f * game.LaneAreaFor(Value))); // only while popping in from nothing
            label.transform.localScale = Vector3.one * labelScale;
            if (!Mathf.Approximately(label.fontSize, game.blobLabelHeight * fontSizePerUnit)) FitLabel(); // live tweak
        }

        /// <summary>Outline point furthest toward the upper-left light, used to place the sheen.</summary>
        Vector2 HighlightAnchor(Vector2 c, int m, out float extent)
        {
            Vector2 dir = new Vector2(-0.55f, 0.83f);
            float best = float.MinValue;
            Vector2 bp = c;
            for (int i = 0; i < m; i++)
            {
                float d = Vector2.Dot(outline[i] - c, dir);
                if (d > best) { best = d; bp = outline[i]; }
            }
            extent = Mathf.Max(0.05f, best);
            return bp;
        }
    }
}
