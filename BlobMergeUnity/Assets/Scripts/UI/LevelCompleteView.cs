using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>Fish: the orca (first ever) celebration tunables (on the Game object, Progression block).</summary>
    [System.Serializable]
    public class LevelCelebration
    {
        [Tooltip("Full-screen celebration when the orca is made for the first time (hero fish, letters, ribbon, confetti). Off = none.")]
        public bool enabled = true;
        [Tooltip("Confetti rain over the panel.")]
        public bool celebrationConfetti = true;
        [Tooltip("Confetti pieces in the rain.")]
        [Range(0, 80)] public int confettiPieces = 30;
        [Tooltip("Goal merge slow-mo: game speed (0.3 = 30 %) ...")]
        [Range(0.05f, 1f)] public float slowMoScale = 0.3f;
        [Tooltip("... for this many real seconds after the goal merge.")]
        [Range(0f, 2f)] public float slowMoSeconds = 0.4f;
        [Tooltip("Real seconds after the goal merge when gameplay freezes (time scale 0). The panel's own animation " +
                 "runs on fixed timings from the merge (dim at 0.7 s, hero at 0.8 s ...) regardless of this value.")]
        [Range(0.2f, 2f)] public float panelAt = 0.7f;
        [Tooltip("Real seconds after the goal merge before a tap continues (\"tap to continue\" appears then).")]
        [Range(0.5f, 5f)] public float tapAfter = 2.3f;
        [Tooltip("How dark the board gets behind the panel (alpha of the dark-blue dim).")]
        [Range(0f, 1f)] public float dimAlpha = 0.82f;

        [Header("Unlock burst behind the hero fish (mock burst-v1/v2)")]
        [Tooltip("Seconds per full turn of the rays.")]
        [Range(5f, 120f)] public float rayTurnSeconds = 49f;
        [Tooltip("Number of rays (alternating long / short).")]
        [Range(6, 72)] public int rayCount = 29;
        [Tooltip("Ray colour (white / cyan = ocean).")]
        public Color rayColor = new Color(200f / 255f, 245f / 255f, 1f, 1f);
        [Tooltip("0 = sharp rays, 1 = blurry (1.8x wider, soft edges, 1.25x brighter).")]
        [Range(0f, 1f)] public float rayBlur = 0.4f;
        [Tooltip("Soft additive glow circles flaring around the centre, per second.")]
        [Range(0f, 60f)] public float glowParticlesPerSecond = 22f;
        [Tooltip("Confetti pieces in the burst from the centre.")]
        [Range(0, 200)] public int burstConfetti = 72;
        [Tooltip("Bubbles rising behind the fish, per second.")]
        [Range(0f, 20f)] public float bubblesPerSecond = 4f;
    }

    /// <summary>
    /// Fish: the full-screen "ALL CREATURES / FOUND!" + "ORCA UNLOCKED!" celebration, played once when the top creature
    /// is made for the first time (approved mock level-complete-juice.html, user revision: the hero fish punch-scales in
    /// place, no fly-in; tap resumes the same game). The layout is Celebration_Fish.prefab (set on the BoardTheme; edit
    /// positions, sizes, sprites and colours there; the texts are filled in at runtime, the big word letter by letter
    /// from the LetterTemplate); without a prefab <see cref="BuildDefault"/> makes the same panel in code. Built hidden
    /// before it's needed; everything is timed on <see cref="GameController.WinClock"/> (real seconds since the merge,
    /// GameTime based, so it animates while the game is frozen and records correctly).
    /// </summary>
    public class LevelCompleteView : MonoBehaviour
    {
        // timeline (seconds after the goal merge), from the mock's CSS
        const float DimAt = 0.70f, DimDur = 0.3f;
        const float HeroAt = 0.80f, HeroDur = 0.55f, HeroFrom = 0.25f, BobAt = 1.4f, BobPeriod = 1.6f, BobPx = 18f;
        const float LevelAt = 1.05f, LevelDur = 0.4f, DropPx = 90f;
        const float LettersAt = 1.20f, LetterGap = 0.05f, LetterDur = 0.35f;
        const float RibbonAt = 1.55f, RibbonDur = 0.4f, BlinkPeriod = 1.1f;
        const float RainFirst = 0.9f, RainSpread = 2.2f;

        // default layout (BuildDefault; reference units, y up from the screen centre; the hero sits at the centre, the
        // texts keep the juice mock's spacing around it)
        const float HeroSize = 450f, LevelY = 576f, CompleteY = 458f, RibbonY = -367f, RibbonW = 840f,
                    RibbonH = 132f, TapY = -811f, TapBarGap = 70f;
        static readonly Vector3[] corners = new Vector3[4];

        [Header("Parts (assigned by the prefab)")]
        public Image dim;
        [Tooltip("Rays, core, glows, bubbles and confetti burst from here (behind the hero).")]
        public RectTransform burstRt;
        public RayFan rayFan;
        public Image core;
        public RectTransform glowLayer, bubbleLayer, confettiLayer;
        [Tooltip("The glass bubble; its child heroFish gets the creature sprite.")]
        public RectTransform hero;
        public Image heroFish;
        [Tooltip("Small top line (drops in).")]
        public TMP_Text titleText;
        [Tooltip("Inactive template: one copy per letter of the big word, centred on its position.")]
        public TMP_Text letterTemplate;
        public float letterSpacing = 3f;
        public RectTransform ribbon;
        public TMP_Text ribbonLabel;
        [Tooltip("\"tap to continue\" (kept at least 70 above the creature bar).")]
        public TMP_Text tapText;

        [Header("Particles")]
        public Sprite glowSprite;
        public Sprite risingBubbleSprite;
        public Color[] confettiColors =
        {
            MeshUtil.Hex("FF6A3D"), MeshUtil.Hex("FFD23F"), MeshUtil.Hex("3D7DF2"), MeshUtil.Hex("B04AE0"),
            MeshUtil.Hex("5CC94A"), MeshUtil.Hex("7FB8E8"), MeshUtil.Hex("FF4FA0"),
        };
        public Color[] burstColors =
        {
            MeshUtil.Hex("FF6A3D"), MeshUtil.Hex("FFD23F"), MeshUtil.Hex("3D7DF2"), MeshUtil.Hex("B04AE0"),
            MeshUtil.Hex("5CC94A"), MeshUtil.Hex("7FE0FF"), MeshUtil.Hex("FF4FA0"),
        };

        GameController game;
        LevelCelebration cfg;
        RectTransform root, heroFishRt, levelRt, ribbonRt;
        CanvasGroup heroGroup, ribbonGroup;
        TMP_Text levelText;
        float levelY, tapY0;
        readonly List<TMP_Text> letters = new List<TMP_Text>();

        class Piece { public RectTransform rt; public Image img; public Vector2 from, delta; public float spin, dur, delay; }
        readonly List<Piece> rain = new List<Piece>();

        /// <summary>
        /// The panel from <paramref name="prefab"/> (or the code default), hidden (call before it's needed: textures and
        /// ~200 UI objects are made then, so the merge frame only activates and animates). <see cref="Play"/> starts it.
        /// <paramref name="title"/> = the small top line, <paramref name="word"/> = the big line popping in letter by
        /// letter, <paramref name="ribbonText"/> = the green ribbon's text.
        /// </summary>
        public static LevelCompleteView Create(GameController g, HudView hud, LevelCelebration cfg, string title, string word,
                                               Sprite fish, string ribbonText, LevelCompleteView prefab = null)
        {
            var canvas = hud.GetComponent<Canvas>();
            if (canvas == null) canvas = hud.GetComponentInChildren<Canvas>();
            var v = prefab != null ? Instantiate(prefab, canvas.transform, false) : BuildDefault(canvas.transform);
            v.name = "OrcaCelebration";
            v.game = g;
            v.cfg = cfg;
            v.root = (RectTransform)v.transform;
            v.root.anchorMin = Vector2.zero;
            v.root.anchorMax = Vector2.one;
            v.root.offsetMin = v.root.offsetMax = Vector2.zero;
            v.root.SetAsLastSibling(); // above the HUD and the creature bar

            var c = v.dim.color; c.a = 0f; v.dim.color = c;
            v.heroGroup = Group(v.hero);
            v.heroFish.sprite = fish;
            v.heroFishRt = v.heroFish.rectTransform;
            v.levelText = v.titleText;
            v.levelText.text = title;
            v.levelRt = v.levelText.rectTransform;
            v.levelY = v.levelRt.anchoredPosition.y;
            v.ribbonRt = v.ribbon;
            v.ribbonGroup = Group(v.ribbon);
            v.ribbonLabel.text = ribbonText;
            v.tapY0 = v.tapText.rectTransform.anchoredPosition.y;
            v.BuildLetters(word);
            v.BuildBurst();
            if (cfg.celebrationConfetti) v.BuildRain();
            v.gameObject.SetActive(false);
            return v;
        }

        static CanvasGroup Group(RectTransform rt)
        {
            var g = rt.GetComponent<CanvasGroup>();
            return g != null ? g : rt.gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>The big word: one TMP per letter (copies of the template) so each can pop on its own.</summary>
        void BuildLetters(string word)
        {
            var tpl = letterTemplate;
            tpl.gameObject.SetActive(false);
            Vector2 at = tpl.rectTransform.anchoredPosition;
            var widths = new float[word.Length];
            float total = 0f;
            for (int i = 0; i < word.Length; i++)
            {
                var t = Instantiate(tpl, tpl.transform.parent, false);
                t.name = "Letter" + i;
                t.gameObject.SetActive(true);
                t.text = word[i].ToString();
                widths[i] = t.GetPreferredValues(t.text).x + letterSpacing;
                total += widths[i];
                letters.Add(t);
            }
            float x = at.x - total * 0.5f;
            for (int i = 0; i < word.Length; i++)
            {
                letters[i].rectTransform.anchoredPosition = new Vector2(x + widths[i] * 0.5f, at.y);
                letters[i].rectTransform.sizeDelta = new Vector2(widths[i] + 20f, tpl.rectTransform.sizeDelta.y);
                x += widths[i];
            }
        }

        /// <summary>The top creature's merge: shows the panel and starts its timeline (<see cref="GameController.WinClock"/>).</summary>
        public void Play()
        {
            root.SetAsLastSibling();
            gameObject.SetActive(true);
            PlaceRain();
            LateUpdate();
        }

        // ------------------------------------------------------------------ default layout (the prefab builder saves this)
        public static LevelCompleteView BuildDefault(Transform parent)
        {
            var go = new GameObject("OrcaCelebration", typeof(RectTransform));
            var v = go.AddComponent<LevelCompleteView>();
            var root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            // own (nested) canvas: the per-frame ray mesh and moving pieces re-batch only this panel, not the whole HUD
            go.AddComponent<Canvas>();

            v.dim = Img(root, "Dim", null, new Color(7f / 255f, 20f / 255f, 45f / 255f, 1f), Vector2.zero, Vector2.zero);
            v.dim.rectTransform.anchorMin = Vector2.zero;
            v.dim.rectTransform.anchorMax = Vector2.one;
            v.dim.rectTransform.offsetMin = v.dim.rectTransform.offsetMax = Vector2.zero;

            v.burstRt = Child(root, "Burst");
            v.rayFan = new GameObject("Rays", typeof(RectTransform)).AddComponent<RayFan>();
            v.rayFan.rectTransform.SetParent(v.burstRt, false);
            v.rayFan.rectTransform.sizeDelta = Vector2.zero;
            v.rayFan.raycastTarget = false;
            v.core = Img(v.burstRt, "Core", CoreSprite, Color.white, Vector2.zero, Vector2.one);
            v.glowLayer = Child(v.burstRt, "Glows");
            v.bubbleLayer = Child(v.burstRt, "Bubbles");
            v.confettiLayer = Child(v.burstRt, "Confetti");
            v.glowSprite = GlowDotSprite;
            v.risingBubbleSprite = RiseBubbleSprite;

            // hero: the unlocked fish in a glass bubble, punch-scales in place
            v.hero = Img(root, "Hero", BubbleSprite, Color.white, Vector2.zero, new Vector2(HeroSize, HeroSize)).rectTransform;
            v.heroFish = Img(v.hero, "Fish", null, Color.white, Vector2.zero, new Vector2(HeroSize * 0.76f, HeroSize * 0.76f));
            v.heroFish.preserveAspect = true;

            v.titleText = Text(root, "Title", "ALL CREATURES", 78f, new Vector2(0f, LevelY));
            v.letterTemplate = Text(root, "LetterTemplate", "A", 132f, new Vector2(0f, CompleteY));
            v.letterTemplate.rectTransform.sizeDelta = new Vector2(150f, 180f);

            // green ribbon ("ORCA UNLOCKED!")
            v.ribbon = Child(root, "Ribbon");
            v.ribbon.anchoredPosition = new Vector2(0f, RibbonY);
            v.ribbon.sizeDelta = new Vector2(RibbonW, RibbonH);
            Img(v.ribbon, "Shadow", CapsuleSprite(false), MeshUtil.Hex("145A1B"), new Vector2(0f, -12f), new Vector2(RibbonW, RibbonH));
            Img(v.ribbon, "Body", CapsuleSprite(true), Color.white, Vector2.zero, new Vector2(RibbonW, RibbonH));
            v.ribbonLabel = Text(v.ribbon, "Label", "ORCA UNLOCKED!", 51f, Vector2.zero);

            v.tapText = Text(root, "Tap", "tap to continue", 48f, new Vector2(0f, TapY));
            return v;
        }

        static RectTransform Child(RectTransform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        // ------------------------------------------------------------------ unlock burst (isolated: swap freely)
        // Mock burst-v1/v2 (canvas JS = spec), canvas px x1.5 = reference units. Burst clock bt = WinClock - BurstOffset
        // (the mock's t: burst-in 0.4 -> 0.9 s ease-out-cubic, hero punch at 0.45 s = our HeroAt). Rays, core and glow
        // particles are additive; bubbles and confetti alpha-blended; all behind the hero.
        const float BurstOffset = HeroAt - 0.45f, MockPx = 1.5f;
        class Glow { public Image img; public float life, max; }
        class Bubble { public Image img; public Vector2 pos; public float speed, phase; }
        class Bit { public Image img; public Vector2 pos, vel; public float rot, spin, delay; }
        readonly List<Glow> glows = new List<Glow>();
        readonly List<Bubble> bubbles = new List<Bubble>();
        readonly List<Bit> bits = new List<Bit>();
        // pooled entries (Image + state), pre-made at build; grow only if a tunable is raised a lot
        readonly Stack<Glow> glowPool = new Stack<Glow>();
        readonly Stack<Bubble> bubblePool = new Stack<Bubble>();
        float glowDue, bubbleDue;
        const int GlowPrewarm = 40, BubblePrewarm = 30;

        static Material additive;
        /// <summary>Additive UI material (Resources/Shaders/UIAdditive), shared with the creature bar's glow flashes.</summary>
        internal static Material Additive
        {
            get
            {
                if (additive != null) return additive;
                var sh = Resources.Load<Shader>("Shaders/UIAdditive");
                if (sh == null) { Debug.LogWarning("[BlobMerge] UIAdditive shader missing: burst drawn alpha-blended"); return null; }
                return additive = new Material(sh) { name = "UI Additive (runtime)" };
            }
        }

        void BuildBurst()
        {
            rayFan.material = Additive;
            rayFan.radius = 560f * MockPx;
            rayFan.Generate(cfg.rayCount);
            core.material = Additive;
            core.color = new Color(1f, 1f, 1f, 0f);
            for (int i = 0; i < GlowPrewarm; i++) glowPool.Push(NewGlow());
            for (int i = 0; i < BubblePrewarm; i++) bubblePool.Push(NewBubble());

            // confetti burst from the centre (gravity + drag + flip-spin)
            for (int i = 0; i < cfg.burstConfetti; i++)
            {
                float a = Random.value * Mathf.PI * 2f, sp = 300f + Random.value * 700f;
                var img = Img(confettiLayer, "BurstConfetti", null, burstColors[i % burstColors.Length], Vector2.zero,
                              new Vector2(10f + Random.value * 8f, 16f + Random.value * 10f) * MockPx);
                img.enabled = false;
                bits.Add(new Bit
                {
                    img = img,
                    vel = new Vector2(Mathf.Cos(a) * sp, -(Mathf.Sin(a) * sp - 350f)) * MockPx, // the mock is y-down
                    rot = Random.value * 6f, spin = (Random.value - 0.5f) * 14f, delay = 0.5f + Random.value * 0.25f,
                });
            }
        }

        Glow NewGlow()
        {
            var img = Img(glowLayer, "Glow", glowSprite != null ? glowSprite : GlowDotSprite, Color.clear, Vector2.zero, Vector2.one);
            img.material = Additive;
            img.enabled = false;
            return new Glow { img = img };
        }

        Bubble NewBubble()
        {
            var img = Img(bubbleLayer, "Bubble", risingBubbleSprite != null ? risingBubbleSprite : RiseBubbleSprite, Color.white, Vector2.zero, Vector2.one);
            img.enabled = false;
            return new Bubble { img = img };
        }

        void AnimateBurst(float t, float dt)
        {
            float bt = t - BurstOffset;
            float k = Mathf.Clamp01((bt - 0.4f) / 0.5f), e = 1f - (1f - k) * (1f - k) * (1f - k);

            if (rayFan.Count != cfg.rayCount) rayFan.Generate(cfg.rayCount);
            rayFan.burst = e;
            rayFan.time = Mathf.Max(0f, bt);
            rayFan.blur = cfg.rayBlur;
            rayFan.rayColor = cfg.rayColor;
            rayFan.rectTransform.localEulerAngles = new Vector3(0f, 0f, -360f * Mathf.Max(0f, bt) / Mathf.Max(1f, cfg.rayTurnSeconds));

            // hot core, breathing
            float cr = (150f + 14f * Mathf.Sin(bt * 2.4f)) * e * MockPx;
            core.rectTransform.sizeDelta = Vector2.one * cr * 1.9f * 2f;
            core.color = new Color(1f, 1f, 1f, e);

            // additive glow circles flaring at random spots around the centre
            if (bt > 0.5f && cfg.glowParticlesPerSecond > 0f)
            {
                glowDue += dt * cfg.glowParticlesPerSecond;
                for (; glowDue >= 1f; glowDue -= 1f)
                {
                    var g = glowPool.Count > 0 ? glowPool.Pop() : NewGlow();
                    g.img.enabled = true;
                    g.img.color = Color.clear;
                    float a = Random.value * 7f, d = Random.value * 190f * MockPx;
                    g.img.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    g.img.rectTransform.sizeDelta = Vector2.one * (18f + Random.value * 46f) * MockPx * 2f;
                    g.life = 0f;
                    g.max = 0.6f + Random.value * 0.9f;
                    glows.Add(g);
                }
            }
            for (int i = glows.Count - 1; i >= 0; i--)
            {
                var g = glows[i];
                g.life += dt;
                if (g.life >= g.max) { g.img.enabled = false; glowPool.Push(g); glows.RemoveAt(i); continue; }
                g.img.color = new Color(1f, 1f, 1f, Mathf.Sin(Mathf.PI * g.life / g.max));
            }

            // bubbles rising from below the hero with a sway (mock: start 260 canvas px below, gone 500 px above)
            float startY = -260f * MockPx, endY = 500f * MockPx;
            if (bt > 0.6f && cfg.bubblesPerSecond > 0f)
            {
                bubbleDue += dt * cfg.bubblesPerSecond;
                for (; bubbleDue >= 1f; bubbleDue -= 1f)
                {
                    var b = bubblePool.Count > 0 ? bubblePool.Pop() : NewBubble();
                    b.img.enabled = true;
                    b.img.color = Color.clear;
                    b.img.rectTransform.sizeDelta = Vector2.one * (6f + Random.value * 12f) * MockPx * 2f;
                    b.pos = new Vector2((Random.value - 0.5f) * 360f * MockPx, startY);
                    b.speed = (120f + Random.value * 120f) * MockPx;
                    b.phase = Random.value * 7f;
                    bubbles.Add(b);
                }
            }
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var b = bubbles[i];
                b.pos.y += b.speed * dt;
                if (b.pos.y > endY + 20f * MockPx) { b.img.enabled = false; bubblePool.Push(b); bubbles.RemoveAt(i); continue; }
                b.img.rectTransform.anchoredPosition = new Vector2(b.pos.x + Mathf.Sin(bt * 2f + b.phase) * 12f * MockPx, b.pos.y);
                float al = Mathf.Min(1f, (b.pos.y - startY) / (80f * MockPx)) * Mathf.Clamp01((endY - b.pos.y) / (200f * MockPx));
                b.img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(al));
            }

            // confetti burst
            float drag = Mathf.Pow(0.985f, dt * 60f);
            foreach (var c in bits)
            {
                if (bt < c.delay) continue;
                c.img.enabled = c.pos.y > -2500f;
                if (!c.img.enabled) continue;
                c.vel.y -= 900f * MockPx * dt;
                c.vel *= drag;
                c.pos += c.vel * dt;
                c.rot += c.spin * dt;
                var rt = c.img.rectTransform;
                rt.anchoredPosition = c.pos;
                rt.localEulerAngles = new Vector3(0f, 0f, -c.rot * Mathf.Rad2Deg);
                rt.localScale = new Vector3(1f, Mathf.Abs(Mathf.Cos(c.rot * 1.3f)) + 0.15f, 1f);
            }
        }

        // ------------------------------------------------------------------ confetti rain
        void BuildRain()
        {
            for (int i = 0; i < cfg.confettiPieces; i++)
            {
                var img = Img(root, "Confetti", null, confettiColors[i % confettiColors.Length], Vector2.zero, new Vector2(24f, 36f));
                rain.Add(new Piece
                {
                    rt = img.rectTransform, img = img,
                    spin = Random.Range(0f, 720f),
                    dur = 2.6f + Random.Range(0f, 1.6f),
                    delay = RainFirst + Random.Range(0f, RainSpread),
                });
                img.enabled = false;
            }
        }

        /// <summary>Rain start/fall from the canvas size at play time (the screen may have rotated/resized since build).</summary>
        void PlaceRain()
        {
            var size = root.rect.size;
            if (size.x <= 1f) size = new Vector2(1080f, 1920f);
            foreach (var p in rain)
            {
                p.from = new Vector2(Random.Range(-0.5f, 0.5f) * size.x, size.y * 0.5f + 60f);
                p.delta = new Vector2(Random.Range(-90f, 90f), -size.y * 1.04f);
            }
        }

        // ------------------------------------------------------------------ animation
        void LateUpdate()
        {
            float t = game.WinClock;

            float dt = GameTime.UnscaledDelta;
            dim.color = new Color(dim.color.r, dim.color.g, dim.color.b, cfg.dimAlpha * Mathf.Clamp01((t - DimAt) / DimDur));
            AnimateBurst(t, dt);

            // hero: appears at HeroAt and punch-scales 25 % → overshoot → 100 % (ease-out-back), then the fish bobs
            heroGroup.alpha = t >= HeroAt ? 1f : 0f;
            float hk = Bezier(0.34f, 1.56f, 0.64f, 1f, (t - HeroAt) / HeroDur);
            hero.localScale = Vector3.one * Mathf.LerpUnclamped(HeroFrom, 1f, hk);
            heroFishRt.anchoredPosition = new Vector2(0f, BobPx * PingPong(t, BobAt, BobPeriod));

            Drop(levelText, levelRt, levelY, Bezier(0.34f, 1.56f, 0.64f, 1f, (t - LevelAt) / LevelDur), t >= LevelAt);

            for (int i = 0; i < letters.Count; i++)
            {
                float u = (t - LettersAt - i * LetterGap) / LetterDur;
                float e = Bezier(0.34f, 1.8f, 0.64f, 1f, u);
                letters[i].alpha = u <= 0f ? 0f : Mathf.Clamp01(e);
                letters[i].rectTransform.localScale = Vector3.one * Mathf.Max(0f, e);
                letters[i].rectTransform.localEulerAngles = new Vector3(0f, 0f, 20f * (1f - e));
            }

            float r = Bezier(0.34f, 1.56f, 0.64f, 1f, (t - RibbonAt) / RibbonDur);
            ribbonGroup.alpha = t <= RibbonAt ? 0f : Mathf.Clamp01(r);
            ribbonRt.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, r);

            float tap = Mathf.Clamp01((t - cfg.tapAfter) / 0.3f);
            tapText.alpha = tap * Mathf.Lerp(1f, 0.35f, PingPong(t, cfg.tapAfter + 0.3f, BlinkPeriod));
            // never on the creature bar: at least TapBarGap above its top (16:9 leaves little room below the tank)
            float tapY = tapY0;
            var bar = game.CreatureBar;
            if (bar != null)
            {
                bar.Root.GetWorldCorners(corners);
                tapY = Mathf.Max(tapY, root.InverseTransformPoint(corners[1]).y + TapBarGap);
            }
            tapText.rectTransform.anchoredPosition = new Vector2(tapText.rectTransform.anchoredPosition.x, tapY);

            foreach (var p in rain)
            {
                float u = (t - p.delay) / p.dur;
                p.img.enabled = u > 0f && u < 1f;
                if (!p.img.enabled) continue;
                float e = Bezier(0.2f, 0.6f, 0.4f, 1f, u);
                p.rt.anchoredPosition = p.from + p.delta * e;
                p.rt.localEulerAngles = new Vector3(0f, 0f, -p.spin * e);
                var c = p.img.color; c.a = 1f - e; p.img.color = c;
            }
        }

        /// <summary>CSS "drop": from 30 px (mock) above, fading in.</summary>
        static void Drop(TMP_Text text, RectTransform rt, float y, float e, bool started)
        {
            text.alpha = started ? Mathf.Clamp01(e) : 0f;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y + DropPx * (1f - (started ? e : 0f)));
        }

        /// <summary>Looping 0 → 1 → 0 (ease-in-out each half) starting at <paramref name="start"/>; 0 before it.</summary>
        static float PingPong(float t, float start, float period)
        {
            if (t <= start) return 0f;
            float p = ((t - start) / period) % 1f;
            float h = p < 0.5f ? p * 2f : 2f - p * 2f;
            return Bezier(0.42f, 0f, 0.58f, 1f, h);
        }

        /// <summary>CSS cubic-bezier(x1, y1, x2, y2) easing at progress x (clamped to 0..1).</summary>
        internal static float Bezier(float x1, float y1, float x2, float y2, float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            float s = x;
            for (int i = 0; i < 10; i++)
            {
                float f = Cub(s, x1, x2) - x;
                float d = 3f * x1 * (1f - s) * (1f - s) + 6f * (x2 - x1) * (1f - s) * s + 3f * (1f - x2) * s * s;
                if (Mathf.Abs(f) < 1e-5f || Mathf.Abs(d) < 1e-6f) break;
                s = Mathf.Clamp01(s - f / d);
            }
            return Cub(s, y1, y2);
        }

        static float Cub(float s, float a, float b) => 3f * a * (1f - s) * (1f - s) * s + 3f * b * (1f - s) * s * s + s * s * s;

        // ------------------------------------------------------------------ building blocks
        static Image Img(RectTransform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TMP_Text Text(RectTransform parent, string name, string s, float size, Vector2 pos)
        {
            var t = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.rectTransform.SetParent(parent, false);
            t.rectTransform.anchoredPosition = pos;
            t.rectTransform.sizeDelta = new Vector2(1000f, size * 1.4f);
            TextStyle.Apply(t, Color.white);
            t.text = s;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }

        // ------------------------------------------------------------------ procedural sprites (white unless noted; tint with Image.color)
        static Sprite bubble, core0, glowDot, riseBubble, capsule, capsuleFlat;

        delegate Color Shade(Vector2 p, float aa); // p in -1..1 (y up), aa = one pixel in p units

        static Sprite Make(string name, int w, int h, Shade fn)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            float aa = 2f / Mathf.Min(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(new Vector2((x + 0.5f) / w * 2f - 1f, (y + 0.5f) / h * 2f - 1f), aa);
            tex.SetPixels(px);
            tex.Apply(false, false); // readable: the UI prefab builder saves it as a PNG
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            sp.name = name;
            return sp;
        }

        /// <summary>Glass bubble (mock .hero): light rim + radial highlight at the upper left.</summary>
        static Sprite BubbleSprite => bubble != null ? bubble : bubble = Make("CelebrationBubble", 256, 256, (p, aa) =>
        {
            float r = p.magnitude;
            float edge = Mathf.Clamp01((1f - r) / aa);
            float g = Mathf.Clamp01((p - new Vector2(-0.3f, 0.4f)).magnitude / 1.91f);
            Color fill = g < 0.6f
                ? Color.Lerp(new Color(1f, 1f, 1f, 0.5f), new Color(0.47f, 0.78f, 1f, 0.2f), g / 0.6f)
                : Color.Lerp(new Color(0.47f, 0.78f, 1f, 0.2f), new Color(1f, 1f, 1f, 0.1f), (g - 0.6f) / 0.4f);
            float rim = Mathf.Clamp01((r - 0.95f) / aa);
            Color c = Color.Lerp(fill, new Color(0.86f, 0.96f, 1f, 0.9f), rim);
            c.a *= edge;
            return c;
        });

        /// <summary>Radial gradient through colour stops (pos 0..1 of the radius), like a canvas createRadialGradient.</summary>
        static Color Radial(float r, (float pos, Color col)[] stops)
        {
            if (r <= stops[0].pos) return stops[0].col;
            for (int i = 1; i < stops.Length; i++)
                if (r <= stops[i].pos)
                    return Color.Lerp(stops[i - 1].col, stops[i].col, (r - stops[i - 1].pos) / (stops[i].pos - stops[i - 1].pos));
            return stops[stops.Length - 1].col;
        }

        /// <summary>Burst core (mock): white .95 → warm white .7 → faint cyan .25 → 0. Additive; alpha = burst-in.</summary>
        static Sprite CoreSprite => core0 != null ? core0 : core0 = Make("CelebrationCore", 256, 256, (p, aa) => Radial(p.magnitude, CoreStops));
        static readonly (float, Color)[] CoreStops =
        {
            (0f, new Color(1f, 1f, 1f, 0.95f)), (0.25f, new Color(1f, 0.98f, 0.88f, 0.7f)),
            (0.55f, new Color(0.59f, 0.86f, 1f, 0.25f)), (1f, new Color(0.47f, 0.78f, 1f, 0f)),
        };

        /// <summary>Glow particle (mock): white .85 → cyan .45 → 0. Additive; alpha = its sin fade.</summary>
        static Sprite GlowDotSprite => glowDot != null ? glowDot : glowDot = Make("CelebrationGlow", 64, 64, (p, aa) => Radial(p.magnitude, GlowStops));
        static readonly (float, Color)[] GlowStops =
        {
            (0f, new Color(1f, 1f, 1f, 0.85f)), (0.4f, new Color(0.75f, 0.94f, 1f, 0.45f)), (1f, new Color(0.59f, 0.86f, 1f, 0f)),
        };

        /// <summary>Rising bubble (mock): light cyan ring + a small white highlight at the upper left.</summary>
        static Sprite RiseBubbleSprite => riseBubble != null ? riseBubble : riseBubble = Make("CelebrationRisingBubble", 64, 64, (p, aa) =>
        {
            float r = p.magnitude;
            float ring = Mathf.Clamp01((1f - r) / aa) * Mathf.Clamp01((r - 0.78f) / aa);
            float hl = Mathf.Clamp01((0.25f - (p - new Vector2(-0.35f, 0.35f)).magnitude) / aa);
            Color c = new Color(0.86f, 0.98f, 1f, 0.9f * ring);
            return hl > 0f ? Color.Lerp(c, new Color(1f, 1f, 1f, 0.7f), hl) : c;
        });

        /// <summary>Ribbon capsule (fully round ends), 840x132 aspect. gradient = baked mock colours #3cc23a → #21862a.</summary>
        static Sprite CapsuleSprite(bool gradient)
        {
            if (gradient && capsule != null) return capsule;
            if (!gradient && capsuleFlat != null) return capsuleFlat;
            const int w = 510, h = 80;
            float hw = w / 2f, hh = h / 2f;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            Color top = MeshUtil.Hex("3CC23A"), bottom = MeshUtil.Hex("21862A");
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float qx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - hw) - (hw - hh));
                    float qy = y + 0.5f - hh;
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - (hh - 1f);
                    Color c = gradient ? Color.Lerp(bottom, top, (y + 0.5f) / h) : Color.white;
                    c.a = Mathf.Clamp01(0.5f - d);
                    px[y * w + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(false, false); // readable: the UI prefab builder saves it as a PNG
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            s.name = gradient ? "CelebrationRibbon" : "CelebrationRibbonShadow";
            if (gradient) capsule = s; else capsuleFlat = s;
            return s;
        }
    }
}
