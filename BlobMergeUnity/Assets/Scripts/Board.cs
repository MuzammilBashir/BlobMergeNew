using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Builds the playfield visuals (purple "aquarium" look: rounded bevelled frame, vignetted field,
    /// wavy water line under the lane, drifting bubbles, danger bar) and registers collision geometry.
    /// </summary>
    public class Board
    {
        public float HalfW, HalfH, FloorY, TopY, DangerY, LaneY, WaterY;

        readonly GameController g;
        readonly Transform root;
        readonly MeshRenderer[] doors = new MeshRenderer[2];
        readonly float[] doorFlash = new float[2];
        Mesh liveMesh;               // water band + wave line + bubbles + danger bar (rebuilt each frame)
        float dangerLevel;
        readonly List<Vector3> lv = new List<Vector3>(2048);
        readonly List<Color> lc = new List<Color>(2048);
        readonly List<int> lt = new List<int>(4096);

        struct Bubble { public Vector2 pos; public float r, speed, phase; }
        Bubble[] bubbles;
        Bubble[] stars;   // Stars mode (pos fixed, speed/phase = twinkle, r = size)
        Color[] starTint;
        struct Cloud { public Vector2 pos, vel; public float rx, ry, rot, phase; public int col; }
        Cloud[] clouds;
        Mesh spaceMesh;
        readonly List<Vector3> sv = new List<Vector3>(4096);
        readonly List<Color> sc = new List<Color>(4096);
        readonly List<int> st = new List<int>(8192);
        float nextShootAt = 4f, shootT = -1f;
        Vector2 shootFrom, shootDir;
        int builtStars = -1, builtClouds = -1, builtDebris = -1;
        Color shootCol;
        struct Rock { public Vector2 pos, vel; public float rot, spin, size, phase; public Vector2[] pts; }
        Rock[] rocks;
        static readonly List<Vector2> tmpPoly = new List<Vector2>(16);

        // palette: this scene's BoardTheme (defaults = the purple art reference)
        BoardTheme Th => g.Theme;
        Color BgCenter => Th.backgroundCenter;
        Color BgEdge => Th.backgroundEdge;
        Color FrameDark => Th.frameDark;
        Color FrameMain => Th.frameMain;
        Color FrameHighlight => Th.frameHighlight;
        Color InnerLip => Th.innerLip;
        Color FieldCenter => Th.fieldCenter;
        Color FieldEdge => Th.fieldEdge;
        Color LaneShade => Th.laneShade;
        Color WaveLine => Th.waveLine;
        Color BubbleCol => Th.bubbles;
        Color DangerCol => Th.danger;
        Color DangerHot => Th.dangerHot;

        public Board(GameController game, SoftBodyWorld world)
        {
            g = game;
            HalfW = g.boardWidth * 0.5f;
            HalfH = g.boardHeight * 0.5f;
            FloorY = -HalfH;
            TopY = HalfH;
            DangerY = FloorY + g.dangerLineFromFloor;
            LaneY = TopY - g.laneFromTop;
            WaterY = LaneY - g.waterLineBelowLane;

            root = new GameObject("Board").transform;
            BuildVisuals();

            float ch = g.chamfer;
            world.AddPlane(Vector2.right, new Vector2(-HalfW, 0));
            world.AddPlane(Vector2.left, new Vector2(HalfW, 0));
            world.AddPlane(Vector2.up, new Vector2(0, FloorY));
            world.AddPlane(Vector2.down, new Vector2(0, TopY));
            world.AddPlane(new Vector2(1, 1), new Vector2(-HalfW, FloorY + ch));
            world.AddPlane(new Vector2(-1, 1), new Vector2(HalfW, FloorY + ch));
            if (g.sideBumpers)
            {
                float by = FloorY + g.bumperHeightFromFloor;
                world.AddCircle(new Vector2(-HalfW, by), g.bumperRadius);
                world.AddCircle(new Vector2(HalfW, by), g.bumperRadius);
            }
        }

        /// <summary>Container outline grown outward by 'grow', with rounded corners.</summary>
        List<Vector2> Outline(float grow, Vector2 offset = default)
        {
            float ch = g.chamfer;
            float x = HalfW + grow, top = TopY + grow, bot = FloorY - grow;
            float c = ch + grow * 0.41f;
            var pts = new[]
            {
                new Vector2(-x, top), new Vector2(x, top), new Vector2(x, bot + c),
                new Vector2(x - c, bot), new Vector2(-x + c, bot), new Vector2(-x, bot + c)
            };
            var r = MeshUtil.Rounded(pts, g.frameCornerRadius + grow, 8);
            if (offset != Vector2.zero) for (int i = 0; i < r.Count; i++) r[i] += offset;
            return r;
        }

        MeshRenderer Add(string name, Mesh mesh, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = g.material;
            r.sortingOrder = order;
            return r;
        }

        void BuildVisuals()
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float ft = g.frameThickness;

            // backdrop vignette behind everything
            var big = new List<Vector2>();
            for (int i = 0; i < 32; i++)
            {
                float a = i / 32f * Mathf.PI * 2f;
                big.Add(new Vector2(Mathf.Cos(a) * 18f, Mathf.Sin(a) * 24f));
            }
            if (Th.backdrop == BackdropStyle.Gradient) AddGradient(v, c, t, 34f, 44f);
            else MeshUtil.AddFan(v, c, t, new Vector2(0, 1f), big, BgCenter, BgEdge);
            Add("Backdrop", MeshUtil.Build(v, c, t, "Backdrop"), -20);
            v.Clear(); c.Clear(); t.Clear();

            // every frame layer is a band around the field opening (not a filled slab), so a translucent field is glass
            var hole = Outline(0f);
            // soft drop shadow under the container
            MeshUtil.AddBand(v, c, t, Outline(ft + 0.35f, new Vector2(0, -0.35f)), hole, Th.frameShadow);
            MeshUtil.AddBand(v, c, t, Outline(ft + 0.15f, new Vector2(0, -0.25f)), hole, Th.frameShadow);
            // bevelled tube frame: dark base, main body, lighter top-left highlight
            MeshUtil.AddBand(v, c, t, Outline(ft), hole, FrameDark);
            MeshUtil.AddBand(v, c, t, Outline(ft - 0.07f, new Vector2(0f, 0.03f)), hole, FrameMain);
            MeshUtil.AddBand(v, c, t, Outline(ft * 0.55f, new Vector2(-0.02f, 0.06f)), hole, FrameHighlight);
            MeshUtil.AddBand(v, c, t, Outline(ft * 0.38f, new Vector2(0f, 0.02f)), hole, FrameMain);
            // dark inner lip, then the vignetted field
            MeshUtil.AddBand(v, c, t, Outline(0.08f), hole, InnerLip);
            MeshUtil.AddFan(v, c, t, new Vector2(0f, (FloorY + WaterY) * 0.5f), Outline(0f), FieldCenter, FieldEdge);

            // optional bumpers
            if (g.sideBumpers)
            {
                float by = FloorY + g.bumperHeightFromFloor, br = g.bumperRadius;
                MeshUtil.AddDisc(v, c, t, new Vector2(-HalfW, by), br, br, 0f, 24, FrameMain, -Mathf.PI * 0.5f, Mathf.PI, FrameDark);
                MeshUtil.AddDisc(v, c, t, new Vector2(HalfW, by), br, br, 0f, 24, FrameMain, Mathf.PI * 0.5f, Mathf.PI, FrameDark);
            }
            Add("Frame", MeshUtil.Build(v, c, t, "Frame"), 0);

            // entry doors on the walls at lane height
            for (int s = 0; s < 2; s++)
            {
                v.Clear(); c.Clear(); t.Clear();
                float x = s == 0 ? -HalfW + 0.05f : HalfW - 0.05f;
                MeshUtil.AddPolygon(v, c, t, MeshUtil.Rounded(new[]
                {
                    new Vector2(-0.05f, -0.65f), new Vector2(-0.05f, 0.65f), new Vector2(0.05f, 0.65f), new Vector2(0.05f, -0.65f)
                }, 0.05f, 4), Th.doors);
                doors[s] = Add("Door", MeshUtil.Build(v, c, t, "Door"), 3);
                doors[s].transform.localPosition = new Vector3(x, LaneY, 0);
            }

            liveMesh = new Mesh { name = "BoardLive" };
            liveMesh.MarkDynamic();
            Add("WaterAndBubbles", liveMesh, 1);

            bubbles = new Bubble[g.bubbleCount];
            for (int i = 0; i < bubbles.Length; i++) bubbles[i] = NewBubble(Random.Range(FloorY + 0.3f, WaterY - 0.3f));
            spaceMesh = new Mesh { name = "Space" };
            spaceMesh.MarkDynamic();
            Add("Space", spaceMesh, -15); // behind the frame, seen through the glass field
        }

        Bubble NewBubble(float y) => new Bubble
        {
            pos = new Vector2(Random.Range(-HalfW + 0.4f, HalfW - 0.4f), y),
            r = Random.Range(0.07f, 0.2f),
            speed = Random.Range(0.15f, 0.45f),
            phase = Random.value * 10f
        };

        /// <summary>Linear multi-stop gradient over a big rectangle (grid so every stop is exact).</summary>
        void AddGradient(List<Vector3> v, List<Color> c, List<int> t, float w, float h)
        {
            var stops = Th.gradient;
            if (stops == null || stops.Length == 0) stops = new[] { BgCenter, BgEdge };
            float ang = Th.gradientAngle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)); // top -> bottom, tilted
            Vector2 cen = new Vector2(0f, 1f);
            // the gradient spans the visible area (board + HUD), not the whole oversized rect
            float span = (g.boardHeight + g.hudSpace + 4f) * 0.5f;
            const int cols = 12, rows = 48;
            int b = v.Count;
            for (int r = 0; r <= rows; r++)
                for (int k = 0; k <= cols; k++)
                {
                    Vector2 p = cen + new Vector2((k / (float)cols - 0.5f) * w, (0.5f - r / (float)rows) * h);
                    float u = Mathf.Clamp01((Vector2.Dot(p - cen, dir) / span + 1f) * 0.5f) * (stops.Length - 1);
                    int i0 = Mathf.Min((int)u, stops.Length - 1), i1 = Mathf.Min(i0 + 1, stops.Length - 1);
                    v.Add(p); c.Add(MeshUtil.Lin(Color.Lerp(stops[i0], stops[i1], Mathf.SmoothStep(0f, 1f, u - i0))));
                }
            for (int r = 0; r < rows; r++)
                for (int k = 0; k < cols; k++)
                {
                    int a0 = b + r * (cols + 1) + k, a1 = a0 + 1, b0 = a0 + cols + 1, b1 = b0 + 1;
                    t.Add(a0); t.Add(a1); t.Add(b1);
                    t.Add(a0); t.Add(b1); t.Add(b0);
                }
        }

        /// <summary>Backdrop space layer (behind the frame, seen through the glass): nebula, distant galaxy, stars, shooting stars.</summary>
        void TickSpace(float time, float dt)
        {
            sv.Clear(); sc.Clear(); st.Clear();
            var th = Th;
            float viewTop = TopY + g.hudSpace + 1.5f, viewBot = FloorY - 2.5f, viewX = HalfW + 2.5f;

            // nebula clouds: big soft ellipses, drifting and breathing very slowly
            if (builtClouds != th.nebulaClouds || clouds == null)
            {
                builtClouds = th.nebulaClouds;
                clouds = new Cloud[th.nebulaClouds];
                for (int i = 0; i < clouds.Length; i++)
                    clouds[i] = new Cloud
                    {
                        pos = new Vector2(Random.Range(-viewX, viewX), Random.Range(viewBot, viewTop)),
                        vel = Random.insideUnitCircle * 0.06f,
                        rx = Random.Range(2.5f, 5.5f), ry = Random.Range(1.4f, 3f),
                        rot = Random.Range(0f, Mathf.PI), phase = Random.value * 10f, col = i
                    };
            }
            var nc = th.nebulaColors;
            for (int i = 0; i < clouds.Length && nc != null && nc.Length > 0; i++)
            {
                ref var cl = ref clouds[i];
                float sp = th.nebulaSpeed;
                cl.pos += cl.vel * sp * dt;
                if (cl.pos.x < -viewX - 2f || cl.pos.x > viewX + 2f) cl.vel.x = -cl.vel.x;
                if (cl.pos.y < viewBot - 2f || cl.pos.y > viewTop + 2f) cl.vel.y = -cl.vel.y;
                float breathe = 1f + 0.06f * Mathf.Sin(time * 0.25f * sp + cl.phase);
                var col = nc[cl.col % nc.Length];
                col.a *= 0.8f + 0.2f * Mathf.Sin(time * 0.18f * sp + cl.phase * 1.7f);
                var clear = col; clear.a = 0f;
                MeshUtil.AddDisc(sv, sc, st, cl.pos, cl.rx * breathe, cl.ry * breathe, cl.rot + time * 0.01f * sp, 28, col, edgeColor: clear);
            }

            // distant spiral galaxy: soft core + two arms of faint dots, rotating very slowly
            if (th.distantGalaxy)
            {
                Vector2 gp = th.galaxyPosition; float gs = th.galaxySize;
                var gc = th.galaxyColor; var clear = gc; clear.a = 0f;
                const float tilt = 0.45f, skew = 0.5f;
                float spin = time * 0.03f, cs = Mathf.Cos(skew), sn = Mathf.Sin(skew);
                var halo = gc; halo.a *= 0.35f;
                MeshUtil.AddDisc(sv, sc, st, gp, gs * 0.9f, gs * 0.9f * tilt, skew, 24, halo, edgeColor: clear);
                MeshUtil.AddDisc(sv, sc, st, gp, gs * 0.22f, gs * 0.22f * tilt, skew, 16, gc, edgeColor: clear);
                for (int arm = 0; arm < 2; arm++)
                    for (int k = 0; k < 26; k++)
                    {
                        float f = k / 26f;
                        float a = spin + arm * Mathf.PI + f * 4.2f;
                        float rad = gs * (0.12f + f * 0.8f);
                        Vector2 q = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad * tilt);
                        q = new Vector2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
                        var dc = gc; dc.a *= 0.55f * (1f - f);
                        var dclear = dc; dclear.a = 0f;
                        float ds = gs * 0.07f * (1f - f * 0.5f);
                        MeshUtil.AddDisc(sv, sc, st, gp + q, ds * 1.6f, ds, a, 8, dc, edgeColor: dclear);
                    }
            }

            // underwater light shafts: tapered quads from above the tank, fading downward, swaying slowly
            for (int i = 0; i < th.lightRays; i++)
            {
                float u = (i + 0.5f) / th.lightRays;
                float sway = Mathf.Sin(time * th.lightRaySpeed + i * 1.7f) * 0.6f;
                float x0 = Mathf.Lerp(-viewX, viewX, u) + sway;
                float topY = viewTop, botY = FloorY + 1f;
                float wTop = 0.25f + 0.2f * Mathf.Sin(i * 2.3f), wBot = wTop * 3.2f;
                float lean = 1.8f + 0.6f * Mathf.Sin(i * 1.3f);
                var cTop = th.lightRayColor;
                cTop.a *= 0.75f + 0.25f * Mathf.Sin(time * th.lightRaySpeed * 1.3f + i * 2.1f);
                var cBot = cTop; cBot.a = 0f;
                var lt0 = MeshUtil.Lin(cTop); var lb0 = MeshUtil.Lin(cBot);
                int b0 = sv.Count;
                sv.Add(new Vector2(x0 - wTop, topY)); sc.Add(lt0);
                sv.Add(new Vector2(x0 + wTop, topY)); sc.Add(lt0);
                sv.Add(new Vector2(x0 + lean + wBot, botY)); sc.Add(lb0);
                sv.Add(new Vector2(x0 + lean - wBot, botY)); sc.Add(lb0);
                st.Add(b0); st.Add(b0 + 1); st.Add(b0 + 2);
                st.Add(b0); st.Add(b0 + 2); st.Add(b0 + 3);
            }

            // floating debris: flat angular rocks with a darker facet and a lit rim, drifting and spinning slowly
            if (builtDebris != th.debrisCount || rocks == null)
            {
                builtDebris = th.debrisCount;
                rocks = new Rock[th.debrisCount];
                for (int i = 0; i < rocks.Length; i++)
                {
                    int n = Random.Range(5, 8);
                    var pts = new Vector2[n];
                    float a0 = Random.value * 6.28f;
                    for (int k = 0; k < n; k++)
                    {
                        float a = a0 + k / (float)n * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f);
                        pts[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(0.7f, 1f);
                    }
                    rocks[i] = new Rock
                    {
                        pos = new Vector2(Random.Range(-viewX, viewX), Random.Range(viewBot, viewTop)),
                        vel = Random.insideUnitCircle.normalized * Random.Range(0.04f, 0.12f),
                        rot = Random.value * 6.28f, spin = Random.Range(-0.25f, 0.25f),
                        size = Mathf.Lerp(th.debrisMinSize, th.debrisMaxSize, Random.value * Random.value),
                        phase = Random.value * 6.28f, pts = pts
                    };
                }
            }
            for (int i = 0; i < rocks.Length; i++)
            {
                ref var rk = ref rocks[i];
                float sp = th.debrisSpeed;
                rk.pos += rk.vel * sp * dt;
                rk.rot += rk.spin * sp * dt;
                // wrap around the view so the field never empties
                if (rk.pos.x < -viewX - 1f) rk.pos.x = viewX + 1f; else if (rk.pos.x > viewX + 1f) rk.pos.x = -viewX - 1f;
                if (rk.pos.y < viewBot - 1f) rk.pos.y = viewTop + 1f; else if (rk.pos.y > viewTop + 1f) rk.pos.y = viewBot - 1f;
                Vector2 p = rk.pos + new Vector2(0f, Mathf.Sin(time * 0.6f * sp + rk.phase) * 0.06f); // light bob
                float cr = Mathf.Cos(rk.rot), sr = Mathf.Sin(rk.rot);
                tmpPoly.Clear();
                for (int k = 0; k < rk.pts.Length; k++)
                {
                    Vector2 q = rk.pts[k] * rk.size;
                    tmpPoly.Add(p + new Vector2(q.x * cr - q.y * sr, q.x * sr + q.y * cr));
                }
                // rim: the outline shifted toward the light, drawn first so only its lit edge shows
                Vector2 lit = new Vector2(-0.6f, 0.8f) * (rk.size * 0.14f);
                for (int k = 0; k < tmpPoly.Count; k++) tmpPoly[k] += lit;
                MeshUtil.AddFan(sv, sc, st, p + lit, tmpPoly, th.debrisRim, th.debrisRim);
                for (int k = 0; k < tmpPoly.Count; k++) tmpPoly[k] -= lit;
                MeshUtil.AddFan(sv, sc, st, p, tmpPoly, th.debrisColor, th.debrisColor);
                // shadow facet: the half facing away from the light
                int h = tmpPoly.Count / 2, b0 = sv.Count;
                var shade = MeshUtil.Lin(th.debrisShade);
                sv.Add(p); sc.Add(shade);
                int start = 0; float best = float.MaxValue;
                for (int k = 0; k < tmpPoly.Count; k++)
                {
                    float d = Vector2.Dot(tmpPoly[k] - p, new Vector2(-0.6f, 0.8f));
                    if (d < best) { best = d; start = k; }
                }
                for (int k = -h / 2 - 1; k <= h / 2 + 1; k++)
                {
                    sv.Add(tmpPoly[((start + k) % tmpPoly.Count + tmpPoly.Count) % tmpPoly.Count]); sc.Add(shade);
                }
                int cnt = sv.Count - b0 - 1;
                for (int k = 0; k < cnt - 1; k++) { st.Add(b0); st.Add(b0 + 1 + k); st.Add(b0 + 2 + k); }
            }

            // stars: small soft dots, low opacity, gentle twinkle, slightly tinted
            if (th.particles == FieldParticles.Stars)
            {
                int total = th.starCount + th.backdropStarCount;
                if (builtStars != total || stars == null)
                {
                    builtStars = total;
                    stars = new Bubble[total];
                    starTint = new Color[total];
                    for (int i = 0; i < total; i++)
                    {
                        Vector2 p = i < th.starCount
                            ? new Vector2(Random.Range(-HalfW, HalfW), Random.Range(FloorY, TopY))
                            : new Vector2(Random.Range(-viewX, viewX), Random.Range(viewBot, viewTop));
                        float big = Random.value;
                        stars[i] = new Bubble
                        {
                            pos = p,
                            r = big > 0.93f ? Random.Range(0.03f, 0.045f) : Random.Range(0.008f, 0.024f),
                            speed = Random.Range(0.5f, 1.5f),
                            phase = Random.value * 10f
                        };
                        float tint = Random.value;
                        starTint[i] = tint < 0.2f ? new Color(0.75f, 0.85f, 1f) : tint < 0.3f ? new Color(1f, 0.82f, 0.92f) : Color.white;
                    }
                }
                for (int i = 0; i < stars.Length; i++)
                {
                    ref var s0 = ref stars[i];
                    float wave = 0.5f + 0.5f * Mathf.Sin(time * th.twinkleSpeed * s0.speed * 2f + s0.phase);
                    // blink: sharpen the wave so stars stay steady most of the time and dip briefly
                    if (th.blinkSharpness > 0f) wave = Mathf.Pow(wave, 1f + th.blinkSharpness * 7f);
                    float tw = 1f - th.twinkleAmount * wave;
                    var col = BubbleCol * starTint[i];
                    col.a = BubbleCol.a * th.starOpacity * tw;
                    var clear = col; clear.a = 0f;
                    float r = s0.r * th.starSize;
                    bool sparkle = (s0.phase * 0.1f) % 1f < th.sparkleShare; // stable per star
                    if (sparkle)
                    {
                        // small flat 4-point diamond sparkle (vector style)
                        float R = r * 3.2f, ri = R * 0.22f;
                        tmpPoly.Clear();
                        for (int k = 0; k < 8; k++)
                        {
                            float a = k * Mathf.PI * 0.25f + Mathf.PI * 0.5f;
                            float rr = (k & 1) == 0 ? R : ri;
                            tmpPoly.Add(s0.pos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr);
                        }
                        MeshUtil.AddFan(sv, sc, st, s0.pos, tmpPoly, col, col);
                    }
                    else
                    {
                        var glow = col; glow.a *= 0.22f;
                        MeshUtil.AddDisc(sv, sc, st, s0.pos, r * 2.6f, r * 2.6f, 0f, 10, glow, edgeColor: clear);
                        MeshUtil.AddDisc(sv, sc, st, s0.pos, r, r, 0f, 8, col, edgeColor: Color.Lerp(col, clear, 0.6f));
                    }
                }
            }

            // a faint shooting star now and then
            if (th.shootingStarEvery > 0f)
            {
                bool cross = th.shootingStarCrossTime > 0f;
                float dur = cross ? th.shootingStarCrossTime : 0.9f;
                float travel = cross ? (viewX + 2f) * 2f : 7f;
                if (shootT < 0f && time >= nextShootAt)
                {
                    shootT = 0f;
                    var cs = th.shootingStarColors;
                    shootCol = cs != null && cs.Length > 0 ? cs[Random.Range(0, cs.Length)] : th.shootingStarColor;
                    if (cross)
                    {
                        // from one side of the screen to the other, slightly downward
                        float side = Random.value < 0.5f ? -1f : 1f;
                        shootFrom = new Vector2(side * (viewX + 1.5f), Random.Range(FloorY + 2f, viewTop - 0.5f));
                        float a = Random.Range(0.12f, 0.38f);
                        shootDir = new Vector2(-side * Mathf.Cos(a), -Mathf.Sin(a));
                    }
                    else
                    {
                        shootFrom = new Vector2(Random.Range(-viewX, viewX * 0.3f), Random.Range(TopY - 2f, viewTop));
                        float a = Random.Range(-0.75f, -0.35f);
                        shootDir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    }
                    nextShootAt = time + th.shootingStarEvery * Random.Range(0.7f, 1.3f);
                }
                if (shootT >= 0f)
                {
                    shootT += dt;
                    float k = shootT / dur;
                    if (k >= 1f) shootT = -1f;
                    else
                    {
                        Vector2 head = shootFrom + shootDir * (k * travel);
                        float fade = cross ? Mathf.Clamp01(Mathf.Min(k, 1f - k) * 8f) : Mathf.Sin(k * Mathf.PI);
                        var col = shootCol; col.a *= fade;
                        var clear = col; clear.a = 0f;
                        float len = th.shootingStarLength, w = th.shootingStarWidth;
                        // tapered streak: round head, tail narrowing to a point and fading out
                        Vector2 nrm = new Vector2(-shootDir.y, shootDir.x) * (w * 0.5f);
                        Vector2 tail = head - shootDir * len;
                        var lcol = MeshUtil.Lin(col); var lclear = MeshUtil.Lin(clear);
                        int b0 = sv.Count;
                        sv.Add(head + nrm); sc.Add(lcol);
                        sv.Add(head - nrm); sc.Add(lcol);
                        sv.Add(tail); sc.Add(lclear);
                        st.Add(b0); st.Add(b0 + 1); st.Add(b0 + 2);
                        MeshUtil.AddDisc(sv, sc, st, head, w * 0.6f, w * 0.6f, 0f, 10, col);
                        var glow = col; glow.a *= 0.25f;
                        MeshUtil.AddDisc(sv, sc, st, head, w * 2f, w * 2f, 0f, 10, glow, edgeColor: clear);
                    }
                }
            }

            spaceMesh.Clear();
            spaceMesh.SetVertices(sv);
            spaceMesh.SetColors(sc);
            spaceMesh.SetTriangles(st, 0);
            spaceMesh.RecalculateBounds();
        }

        public void FlashDoor(int side) => doorFlash[side < 0 ? 0 : 1] = 1f;

        public void SetDanger(float level) => dangerLevel = level;

        float Wave(float x, float time) =>
            WaterY + 0.05f * Mathf.Sin(x * 1.9f + time * 1.4f) + 0.03f * Mathf.Sin(x * 3.6f - time * 2.1f);

        public void Tick(float dt)
        {
            float time = GameTime.Unscaled;
            for (int s = 0; s < 2; s++)
            {
                doorFlash[s] = Mathf.Max(0f, doorFlash[s] - dt * 2.5f);
                float sy = 0.55f + doorFlash[s] * 0.9f;
                doors[s].transform.localScale = new Vector3(1f + doorFlash[s] * 1.5f, sy, 1f);
            }

            TickSpace(time, dt);
            lv.Clear(); lc.Clear(); lt.Clear();

            // bubbles drifting up through the water
            for (int i = 0; i < bubbles.Length && Th.particles == FieldParticles.Bubbles; i++)
            {
                ref var b = ref bubbles[i];
                b.pos.y += b.speed * dt;
                float x = b.pos.x + Mathf.Sin(time * 1.3f + b.phase) * 0.12f;
                if (b.pos.y > WaterY - 0.25f) { b = NewBubble(FloorY + 0.2f); continue; }
                float fade = Mathf.Clamp01((WaterY - 0.25f - b.pos.y) * 2f) * Mathf.Clamp01((b.pos.y - FloorY) * 2f);
                var col = BubbleCol; col.a *= fade;
                MeshUtil.AddRing(lv, lc, lt, new Vector2(x, b.pos.y), b.r, 0.028f, 20, col);
                var hl = new Color(1f, 1f, 1f, 0.25f * fade);
                MeshUtil.AddDisc(lv, lc, lt, new Vector2(x - b.r * 0.35f, b.pos.y + b.r * 0.35f), b.r * 0.18f, b.r * 0.18f, 0f, 8, hl);
            }

            // darker band above the water line (the lane), with a wavy bottom edge
            int n = 40;
            float x0 = -HalfW + 0.02f, x1 = HalfW - 0.02f, top = TopY - 0.02f;
            int bi = lv.Count;
            var shade = MeshUtil.Lin(LaneShade);
            var shadeTop = MeshUtil.Lin(new Color(LaneShade.r, LaneShade.g, LaneShade.b, LaneShade.a * 1.3f));
            for (int i = 0; i <= n; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)n);
                lv.Add(new Vector3(x, Wave(x, time))); lc.Add(shade);
                lv.Add(new Vector3(x, top)); lc.Add(shadeTop);
            }
            for (int i = 0; i < n; i++)
            {
                int a = bi + i * 2;
                lt.Add(a); lt.Add(a + 1); lt.Add(a + 3);
                lt.Add(a); lt.Add(a + 3); lt.Add(a + 2);
            }
            // the wave line itself, with a faint glow
            AddWaveStrip(x0, x1, n, time, 0.16f, new Color(WaveLine.r, WaveLine.g, WaveLine.b, 0.12f));
            AddWaveStrip(x0, x1, n, time, 0.045f, WaveLine);

            // danger bar: solid rounded lavender bar, pulses pink when blobs rest above it
            float pulse = dangerLevel > 0f ? 0.5f + 0.5f * Mathf.Sin(time * 14f) : 0f;
            var dcol = Color.Lerp(DangerCol, DangerHot, pulse * dangerLevel);
            dcol.a = 0.85f;
            float hw = HalfW * 0.9f, hh = 0.05f + 0.03f * dangerLevel;
            MeshUtil.AddPolygon(lv, lc, lt, MeshUtil.Rounded(new[]
            {
                new Vector2(-hw, DangerY - hh), new Vector2(-hw, DangerY + hh), new Vector2(hw, DangerY + hh), new Vector2(hw, DangerY - hh)
            }, hh, 4), dcol);

            liveMesh.Clear();
            liveMesh.SetVertices(lv);
            liveMesh.SetColors(lc);
            liveMesh.SetTriangles(lt, 0);
            liveMesh.RecalculateBounds();
        }

        void AddWaveStrip(float x0, float x1, int n, float time, float width, Color color)
        {
            var lcol = MeshUtil.Lin(color);
            int bi = lv.Count;
            for (int i = 0; i <= n; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / (float)n);
                float y = Wave(x, time);
                lv.Add(new Vector3(x, y - width * 0.5f)); lc.Add(lcol);
                lv.Add(new Vector3(x, y + width * 0.5f)); lc.Add(lcol);
            }
            for (int i = 0; i < n; i++)
            {
                int a = bi + i * 2;
                lt.Add(a); lt.Add(a + 1); lt.Add(a + 3);
                lt.Add(a); lt.Add(a + 3); lt.Add(a + 2);
            }
        }
    }
}
