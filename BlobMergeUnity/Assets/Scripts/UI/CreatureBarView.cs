using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Fish scene lower HUD (spec FISH_PROGRESSION.md §0, mock ladder-v2.html): the creature bar — the tiers
    /// [WindowStart … WindowStart + 10] in full colour on a stepped path (6 on the lower row, 5 on the upper), plus the
    /// new-fish celebration (the creature flies from the merge point into its slot, the slot pops with a ring, an
    /// additive glow flash and sparkles, a "NAME!" label floats over the tank) and the shift (the first icon fades out,
    /// the others slide one slot along, the next creature pops in at the end). Layout lives in CreatureBar_Fish.prefab
    /// (path points on PathOuter/PathInner, one marker per bar position under Slots, the icon prefab); without a prefab
    /// <see cref="BuildDefault"/> makes the same thing in code. The fly-in, bursts and label stay procedural. All
    /// animation runs on <see cref="GameTime"/> so it records correctly.
    /// </summary>
    public class CreatureBarView : MonoBehaviour
    {
        // default layout (BuildDefault): the mock ×3 in 1080 reference units; the strip is 276 tall, slots relative to its centre
        const float Width = 1080f, Height = 276f, IconSize = 78f, Step = 99f, RowY = 66f, LowerX0 = -474f, UpperX0 = 99f;
        const int LowerCount = 6, DefaultSlots = 11;
        const float OuterStroke = 72f, InnerStroke = 54f, TagY = 468f, TagSize = 54f, EnterFromX = 90f;
        static readonly Vector2[] PathPoints = { new Vector2(-474f, -66f), new Vector2(21f, -66f), new Vector2(75f, 66f), new Vector2(495f, 66f) };
        // timings (mock CSS / JS)
        // room each side for an end icon's pop (1.7x grows it ~27 beyond its slot). Kept small: on narrow phones (Samsung A56,
        // 9:19.5) every unit here shrinks the whole strip, and the pop is brief
        const float PopMargin = 24f;
        const float FlyDur = 0.7f, FlySize = 192f, PopAt = 0.72f, PopDur = 0.55f, ShiftDelay = 0.7f;
        const float SlideDur = 0.6f, FadeDur = 0.4f, TagDur = 1.4f;
        const float GlowDur = 0.9f, GlowSize = 180f, RingDur = 0.6f, SparkDur = 0.7f, SparkSize = 15f, SparkDist = 78f;
        const int Sparks = 8;

        // user's bar reference (2026-10-13): purple path with a white border (20 % thinner than the reference's), round
        // icons with a thin navy edge + white ring; locked creatures are bluish-grey silhouettes, the next unlock a green ring
        [Header("Icon colours per state")]
        public Color ringColor = new Color32(0xFB, 0xF6, 0xEC, 0xFF);
        [Tooltip("Ring of the next creature to unlock.")]
        public Color nextRingColor = new Color32(0x5F, 0xD4, 0x3A, 0xFF);
        [Tooltip("Extra thickness of the next-unlock ring (the icon prefab's ring is the normal one).")]
        public float nextRingExtra = 2f;
        public Color openFill = new Color32(0x07, 0x32, 0x80, 0xFF);
        public Color lockedFill = new Color32(0x07, 0x32, 0x80, 0xFF);
        [Tooltip("Tint of a locked creature's silhouette.")]
        public Color silhouette = new Color32(0x7E, 0x92, 0xC4, 0xFF);

        [Header("Parts (move the Slots markers to change the layout)")]
        public CreatureIconView iconPrefab;
        [Tooltip("One marker per bar position, first to last. Positions past the last continue its direction.")]
        public RectTransform[] slots;
        public RectTransform iconLayer, burstLayer;

        GameController game;
        Canvas canvas;
        RectTransform root, fxLayer;
        float iconSize;
        TMP_Text tag;
        float clock, tagT = -1f;

        class Icon { public int tier; public RectTransform rt; public CanvasGroup group; public float popT = -1f; public bool open; public CreatureIconView view; public Vector2 fillSize; }
        readonly Dictionary<int, Icon> icons = new Dictionary<int, Icon>();
        Icon leaving, entering;
        int shownStart = -1;
        bool iconsAnimating = true;                        // false = every icon sits still on its slot (loop skipped)
        float slideT = -1f;                                // seconds into the current shift slide (< 0 = none)
        readonly List<float> shiftsDue = new List<float>(); // clock times when queued shifts may start

        class Flight { public int tier; public Vector2 from; public float t; public bool shiftAfter; public RectTransform rt; public Image fish; }
        readonly List<Flight> flights = new List<Flight>();
        readonly Stack<Flight> flightPool = new Stack<Flight>();

        class Burst { public RectTransform rt; public Image glow, ring; public Image[] sparks; public float t; }
        readonly List<Burst> bursts = new List<Burst>();
        readonly Stack<Burst> burstPool = new Stack<Burst>();

        /// <summary>The strip's root (the orca celebration keeps "tap to continue" above it).</summary>
        public RectTransform Root => root;
        /// <summary>(new window start) — a shift slide begins.</summary>
        public event System.Action<int> ShiftStarted;
        /// <summary>(dropped tier) — the slide finished; that creature is gone from the game.</summary>
        public event System.Action<int> ShiftDone;

        /// <summary>The bar from <paramref name="prefab"/> (or the code default), under the HUD canvas.</summary>
        public static CreatureBarView Create(GameController g, HudView hud, CreatureBarView prefab)
        {
            var canvas = hud.GetComponent<Canvas>();
            if (canvas == null) canvas = hud.GetComponentInChildren<Canvas>();
            var v = prefab != null ? Instantiate(prefab, canvas.transform, false) : BuildDefault(canvas.transform, null);
            v.name = "CreatureBar";
            v.game = g;
            v.canvas = canvas;
            v.root = (RectTransform)v.transform;
            v.root.anchorMin = v.root.anchorMax = new Vector2(0.5f, 0f); // Place() measures from the screen bottom
            v.iconSize = v.iconPrefab.Rect.sizeDelta.x;

            // flights and the label move over the whole screen: a full-canvas layer above the bar
            v.fxLayer = Layer(canvas.transform, "CreatureBarFx");
            v.fxLayer.anchorMin = Vector2.zero;
            v.fxLayer.anchorMax = Vector2.one;
            v.fxLayer.offsetMin = v.fxLayer.offsetMax = Vector2.zero;
            NestedCanvas(v.fxLayer.gameObject);
            v.tag = new GameObject("NewFishLabel", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            v.tag.rectTransform.SetParent(v.fxLayer, false);
            v.tag.rectTransform.sizeDelta = new Vector2(1000f, TagSize * 1.4f);
            TextStyle.Apply(v.tag, Color.white);
            v.tag.fontSize = TagSize;
            v.tag.alignment = TextAlignmentOptions.Center;
            v.tag.raycastTarget = false;
            v.tag.alpha = 0f;
            return v;
        }

        /// <summary>The default bar (the prefab builder saves this): path strokes, icon/burst layers, 11 slot markers.
        /// <paramref name="icon"/> null = an inactive icon template child.</summary>
        public static CreatureBarView BuildDefault(Transform parent, CreatureIconView icon)
        {
            var go = new GameObject("CreatureBar", typeof(RectTransform));
            var v = go.AddComponent<CreatureBarView>();
            var root = (RectTransform)go.transform;
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(Width, Height);
            NestedCanvas(go); // the animated bar rebuilds its own batch, not the whole HUD canvas

            // the path: a white outer stroke under a purple inner one, round caps and joins
            Stroke(root, "PathOuter", MeshUtil.Hex("FBF6EC"), OuterStroke);
            Stroke(root, "PathInner", MeshUtil.Hex("5660A8"), InnerStroke);
            v.iconLayer = Layer(root, "Icons");
            v.burstLayer = Layer(root, "Bursts");
            var slotRoot = Layer(root, "Slots");
            v.slots = new RectTransform[DefaultSlots];
            for (int i = 0; i < DefaultSlots; i++)
            {
                v.slots[i] = Layer(slotRoot, "Slot" + i);
                v.slots[i].anchoredPosition = i < LowerCount
                    ? new Vector2(LowerX0 + i * Step, -RowY)
                    : new Vector2(UpperX0 + (i - LowerCount) * Step, RowY);
            }
            if (icon == null)
            {
                icon = BuildDefaultIcon(root);
                icon.gameObject.SetActive(false); // template only
            }
            v.iconPrefab = icon;
            return v;
        }

        /// <summary>The default icon: navy outline, white ring, navy fill, fish (CreatureIcon.prefab is saved from this).</summary>
        public static CreatureIconView BuildDefaultIcon(Transform parent)
        {
            var edge = Img((RectTransform)parent, "CreatureIcon", UiSprites.Circle, MeshUtil.Hex("14306B"), IconSize);
            var v = edge.gameObject.AddComponent<CreatureIconView>();
            v.edge = edge;
            v.ring = Img(edge.rectTransform, "Ring", UiSprites.Circle, MeshUtil.Hex("FBF6EC"), IconSize - 3f);
            v.fill = Img(edge.rectTransform, "Fill", UiSprites.Circle, MeshUtil.Hex("073280"), IconSize - 9f);
            v.fish = Img(edge.rectTransform, "Fish", null, Color.white, IconSize * 0.8f);
            v.fish.preserveAspect = true;
            edge.gameObject.AddComponent<CanvasGroup>();
            return v;
        }

        /// <summary>Own canvas (no sorting override: draws in hierarchy order inside the HUD canvas).</summary>
        static void NestedCanvas(GameObject go)
        {
            var c = go.AddComponent<Canvas>();
            c.overrideSorting = false;
        }

        static RectTransform Layer(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }

        static void Stroke(RectTransform parent, string name, Color col, float width)
        {
            var s = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<PathStroke>(); // explicit: the Editor reported PathOuter without one (no draw)
            s.rectTransform.SetParent(parent, false);
            s.rectTransform.sizeDelta = new Vector2(Width, Height); // real rect (a zero-size one may be culled on Android); points stay in local units
            s.raycastTarget = false;
            s.color = col;
            s.width = width;
            s.points.AddRange(PathPoints);
            s.SetVerticesDirty();
        }

        static Image Img(RectTransform parent, string name, Sprite sprite, Color color, float size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(size, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Slot centre (strip-local) of bar position <paramref name="index"/>; past the last marker continues its direction.</summary>
        public Vector2 SlotPos(int index)
        {
            int n = slots.Length;
            if (index < n) return root.InverseTransformPoint(slots[index].position);
            Vector2 last = root.InverseTransformPoint(slots[n - 1].position), prev = root.InverseTransformPoint(slots[n - 2].position);
            return last + (last - prev) * (index - n + 1);
        }

        /// <summary>Where a creature's slot is right now, in the HUD canvas's local space (the fly-in target).</summary>
        public Vector2 SlotCanvasPos(int tier) => CanvasPos(SlotPos(tier - shownStart));

        Vector2 CanvasPos(Vector2 stripLocal) => fxLayer.InverseTransformPoint(root.TransformPoint(stripLocal));

        Icon MakeIcon(int tier)
        {
            var view = Instantiate(iconPrefab, iconLayer, false);
            view.name = "Creature" + tier;
            view.gameObject.SetActive(true);
            var group = view.GetComponent<CanvasGroup>();
            if (group == null) group = view.gameObject.AddComponent<CanvasGroup>();
            var icon = new Icon { tier = tier, rt = view.Rect, group = group, view = view, fillSize = view.fill.rectTransform.sizeDelta };
            icons[tier] = icon;
            // a creature whose fly-in is still under way stays a silhouette until its pop
            SetOpen(icon, tier <= game.progression.Highest && !FlightWaitingFor(tier));
            iconsAnimating = true;
            return icon;
        }

        /// <summary>Coloured (made at least once, ever) or a bluish-grey silhouette; then re-marks the next unlock.</summary>
        void SetOpen(Icon icon, bool open)
        {
            icon.open = open;
            icon.view.fish.sprite = open ? game.FishSprite(icon.tier) : Silhouette(icon.tier);
            icon.view.fish.color = open ? Color.white : silhouette;
            icon.view.fill.color = open ? openFill : lockedFill;
            int next = int.MaxValue;
            foreach (var i in icons.Values) if (!i.open && i.tier < next) next = i.tier;
            foreach (var i in icons.Values)
            {
                bool n = i.tier == next;
                i.view.ring.color = n ? nextRingColor : ringColor;
                i.view.fill.rectTransform.sizeDelta = i.fillSize - Vector2.one * (n ? 2f * nextRingExtra : 0f);
            }
        }

        static readonly Dictionary<int, Sprite> silhouettes = new Dictionary<int, Sprite>();

        /// <summary>The creature's shape in white (alpha kept), tinted by the Image. Read back through a RenderTexture
        /// because the fish textures aren't CPU-readable.</summary>
        Sprite Silhouette(int tier)
        {
            if (silhouettes.TryGetValue(tier, out var s) && s != null) return s;
            var src = game.FishSprite(tier);
            if (src == null) return null;
            var f = src.textureRect; // tight-packed sprites can have fractional rects
            var r = new RectInt(Mathf.RoundToInt(f.x), Mathf.RoundToInt(f.y), Mathf.FloorToInt(f.width), Mathf.FloorToInt(f.height));
            var rt = RenderTexture.GetTemporary(src.texture.width, src.texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src.texture, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, px[i].a);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, r.width, r.height), new Vector2(0.5f, 0.5f), src.pixelsPerUnit);
            silhouettes[tier] = s;
            return s;
        }

        /// <summary>The bar's last creature was made again (not a first-ever one, so no fly-in): shift after the usual delay.</summary>
        public void QueueShift() => shiftsDue.Add(clock + ShiftDelay);

        /// <summary>A new creature was made at <paramref name="worldPos"/>: fly it into its slot. shiftAfter = it was the
        /// bar's last creature (the shift follows its pop).</summary>
        public void Celebrate(int tier, Vector2 worldPos, bool shiftAfter)
        {
            var f = flightPool.Count > 0 ? flightPool.Pop() : NewFlight();
            f.tier = tier;
            f.t = 0f;
            f.shiftAfter = shiftAfter;
            f.fish.sprite = game.FishSprite(tier);
            if (icons.TryGetValue(tier, out var slot)) SetOpen(slot, false);
            f.rt.gameObject.SetActive(true);
            f.rt.SetAsLastSibling();
            Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(fxLayer, game.SteadyToScreen(worldPos), uiCam, out f.from);
            f.rt.anchoredPosition = f.from;
            f.rt.sizeDelta = Vector2.one * FlySize;
            flights.Add(f);

            // the label restarts for every new creature (one at a time reads best)
            tag.text = FishLook.Name(tier).ToUpperInvariant() + "!";
            tagT = 0f;
            tag.rectTransform.SetAsLastSibling();
        }

        Flight NewFlight()
        {
            var rt = Layer(fxLayer, "NewFish");
            var glow = Img(rt, "Glow", GlowSprite, new Color(0.78f, 0.96f, 1f, 0.9f), 1f);
            glow.material = LevelCompleteView.Additive;
            Stretch(glow.rectTransform, 1.5f);
            var fish = Img(rt, "Fish", null, Color.white, 1f);
            fish.preserveAspect = true;
            Stretch(fish.rectTransform, 1f);
            return new Flight { rt = rt, fish = fish };
        }

        /// <summary>Child fills its parent × <paramref name="k"/> (centred).</summary>
        static void Stretch(RectTransform rt, float k)
        {
            rt.anchorMin = new Vector2(0.5f - k * 0.5f, 0.5f - k * 0.5f);
            rt.anchorMax = new Vector2(0.5f + k * 0.5f, 0.5f + k * 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        void Pop(Vector2 slot, Icon icon)
        {
            if (icon != null) { icon.popT = 0f; iconsAnimating = true; if (!icon.open && icon.tier <= game.progression.Highest) SetOpen(icon, true); }
            var b = burstPool.Count > 0 ? burstPool.Pop() : NewBurst();
            b.t = 0f;
            b.rt.anchoredPosition = slot;
            b.rt.gameObject.SetActive(true);
            bursts.Add(b);
        }

        Burst NewBurst()
        {
            var b = new Burst { rt = Layer(burstLayer, "Burst"), sparks = new Image[Sparks] };
            b.glow = Img(b.rt, "Flash", FlashSprite, Color.white, GlowSize);
            b.glow.material = LevelCompleteView.Additive;
            b.ring = Img(b.rt, "Ring", RingSprite, Color.white, iconSize);
            for (int i = 0; i < Sparks; i++)
            {
                b.sparks[i] = Img(b.rt, "Spark", GlowSprite, Color.white, SparkSize * 2.2f);
                b.sparks[i].material = LevelCompleteView.Additive;
            }
            return b;
        }

        // ------------------------------------------------------------------ animation
        void LateUpdate()
        {
            if (!game.ProgressionLive) return; // live toggle
            float dt = GameTime.UnscaledDelta;
            clock += dt;
            Place();
            var p = game.progression;
            int size = p.WindowSize;

            if (shownStart < 0)
            {
                shownStart = p.WindowStart; // this game's window (0 unless a debug start); later changes arrive as animated shifts
                for (int t = shownStart; t < shownStart + size && t <= ProgressionMath.TopTier; t++) MakeIcon(t);
            }

            // shifts: one at a time, each after the celebration that caused it
            if (slideT < 0f && shiftsDue.Count > 0 && clock >= shiftsDue[0])
            {
                shiftsDue.RemoveAt(0);
                StartShift(size);
            }
            if (slideT >= 0f)
            {
                slideT += dt;
                if (slideT >= SlideDur)
                {
                    // the next creature pops in at the end (unless its own fly-in is waiting there: that pop follows)
                    if (entering != null && !FlightWaitingFor(entering.tier)) Pop(SlotPos(size - 1), entering);
                    EndShift();
                }
            }

            // icons on their slots (sliding during a shift: cubic-bezier(.4,0,.2,1) over 0.6 s)
            if (iconsAnimating) AnimateIcons(dt);
            AnimateFlights(dt);
            AnimateBursts(dt);
            AnimateTag(dt);
        }

        /// <summary>Icons on their slots: sliding during a shift, punch-scaling after a pop. Idle frames skip it.</summary>
        void AnimateIcons(float dt)
        {
            iconsAnimating = slideT >= 0f;
            float slide = slideT >= 0f ? LevelCompleteView.Bezier(0.4f, 0f, 0.2f, 1f, slideT / SlideDur) : 1f; // 1 when idle
            foreach (var icon in icons.Values)
            {
                int idx = icon.tier - shownStart;
                Vector2 to = SlotPos(idx);
                Vector2 from = icon == entering ? to + new Vector2(EnterFromX, 0f) : SlotPos(idx + 1);
                if (icon == leaving) from = to = SlotPos(0);
                icon.rt.anchoredPosition = Vector2.LerpUnclamped(from, to, slide);
                float scale = PopScale(icon.popT);
                if (icon.popT >= PopDur) icon.popT = -1f; // this frame drew scale 1
                else if (icon.popT >= 0f) { icon.popT += dt; iconsAnimating = true; }
                if (icon == leaving)
                {
                    float k = LevelCompleteView.Bezier(0.25f, 0.1f, 0.25f, 1f, slideT / FadeDur);
                    icon.group.alpha = 1f - k;
                    scale *= Mathf.Lerp(1f, 0.2f, k);
                }
                else icon.group.alpha = icon == entering ? Mathf.Clamp01(slideT / FadeDur) : 1f;
                icon.rt.localScale = Vector3.one * scale;
            }
        }

        void StartShift(int size)
        {
            leaving = icons.TryGetValue(shownStart, out var l) ? l : null;
            shownStart++;
            int next = shownStart + size - 1;
            entering = next <= ProgressionMath.TopTier && !icons.ContainsKey(next) ? MakeIcon(next) : null;
            slideT = 0f;
            iconsAnimating = true;
            ShiftStarted?.Invoke(shownStart);
        }

        void EndShift()
        {
            int dropped = shownStart - 1;
            if (leaving != null) { icons.Remove(leaving.tier); Destroy(leaving.rt.gameObject); }
            leaving = entering = null;
            slideT = -1f;
            ShiftDone?.Invoke(dropped);
        }

        void AnimateFlights(float dt)
        {
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var f = flights[i];
                f.t += dt;
                // back-to-back shifts: a creature past the shown bar's end (the previous shift hasn't slid yet) aims at
                // the end slot and waits there until the slide has brought its slot in
                bool waiting = Waiting(f.tier);
                int idx = Mathf.Min(f.tier - shownStart, game.progression.WindowSize - 1); // never past the bar's end
                Vector2 to = CanvasPos(SlotPos(idx)); // follows the bar (placement, a running shift)
                float u = Mathf.Min(f.t / FlyDur, 1f);
                float x = Mathf.LerpUnclamped(f.from.x, to.x, LevelCompleteView.Bezier(0.5f, 0f, 0.6f, 1f, u));
                float y = Mathf.LerpUnclamped(f.from.y, to.y, LevelCompleteView.Bezier(0.3f, -0.4f, 0.6f, 1f, u)); // lifts first
                if (waiting && f.t >= FlyDur) y += Mathf.Sin((f.t - FlyDur) * 6f) * 6f * root.localScale.x; // queued: gentle bob
                f.rt.anchoredPosition = new Vector2(x, y);
                f.rt.sizeDelta = Vector2.one * Mathf.Lerp(FlySize, iconSize * root.localScale.x, LevelCompleteView.Bezier(0.25f, 0.1f, 0.25f, 1f, u));
                if (f.t < PopAt || waiting) continue;

                // arrived: the slot pops; the bar's last creature then shifts the bar
                Pop(SlotPos(idx), icons.TryGetValue(f.tier, out var icon) && icon != leaving ? icon : null);
                if (f.shiftAfter) shiftsDue.Add(clock + ShiftDelay);
                f.rt.gameObject.SetActive(false);
                flights.RemoveAt(i);
                flightPool.Push(f);
            }
        }

        /// <summary>The creature's slot isn't on the shown bar yet (a shift is pending or still sliding it in).</summary>
        bool Waiting(int tier)
        {
            var p = game.progression;
            int shownEnd = shownStart + p.WindowSize - 1;
            return (tier > shownEnd && tier <= p.WindowEnd) || (slideT >= 0f && entering != null && entering.tier == tier);
        }

        /// <summary>A fly-in for this creature is under way (its arrival does the pop).</summary>
        bool FlightWaitingFor(int tier)
        {
            foreach (var f in flights) if (f.tier == tier) return true;
            return false;
        }

        void AnimateBursts(float dt)
        {
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                var b = bursts[i];
                b.t += dt;
                if (b.t >= GlowDur) { b.rt.gameObject.SetActive(false); bursts.RemoveAt(i); burstPool.Push(b); continue; }
                // additive glow flash: scale .3 → 1.4, fades in over the first 25 %, out after (ease-out)
                float u = b.t / GlowDur;
                b.glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.4f, EaseOut(u));
                b.glow.color = new Color(1f, 1f, 1f, u < 0.25f ? EaseOut(u / 0.25f) : 1f - EaseOut((u - 0.25f) / 0.75f));
                // white ring: scale .6 → 2.6, fading out
                float r = EaseOut(b.t / RingDur);
                b.ring.enabled = b.t < RingDur;
                b.ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.6f, r);
                b.ring.color = new Color(1f, 1f, 1f, 1f - r);
                // 8 sparkles flying out
                float s = EaseOut(b.t / SparkDur);
                for (int k = 0; k < Sparks; k++)
                {
                    var sp = b.sparks[k];
                    sp.enabled = b.t < SparkDur;
                    float a = k / (float)Sparks * Mathf.PI * 2f;
                    sp.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * SparkDist * s;
                    sp.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.3f, s);
                    sp.color = new Color(1f, 1f, 1f, 1f - s);
                }
            }
        }

        /// <summary>Mock .tag: rises in from 30 below at 60 %, holds, then drifts 42 up while fading (1.4 s).</summary>
        void AnimateTag(float dt)
        {
            if (tagT < 0f) return;
            tagT += dt;
            float u = tagT / TagDur, alpha, dy, scale = 1f;
            if (u < 0.15f) { float e = EaseOut(u / 0.15f); alpha = e; dy = -30f * (1f - e); scale = Mathf.Lerp(0.6f, 1f, e); }
            else if (u < 0.75f) { alpha = 1f; dy = 0f; }
            else { float e = EaseOut(Mathf.Clamp01((u - 0.75f) / 0.25f)); alpha = 1f - e; dy = 42f * e; }
            tag.alpha = alpha;
            tag.rectTransform.anchoredPosition = CanvasPos(new Vector2(0f, TagY)) + new Vector2(0f, dy);
            tag.rectTransform.localScale = Vector3.one * scale;
            if (u >= 1f) { tagT = -1f; tag.alpha = 0f; }
        }

        static float EaseOut(float x) => LevelCompleteView.Bezier(0f, 0f, 0.58f, 1f, x);

        /// <summary>Mock "pop": scale 1 → 1.7 (at 35 %) → 1 over 0.55 s, each leg eased cubic-bezier(.34,1.56,.64,1).</summary>
        static float PopScale(float t)
        {
            if (t < 0f || t >= PopDur) return 1f;
            float u = t / PopDur;
            return u < 0.35f
                ? Mathf.LerpUnclamped(1f, 1.7f, LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, u / 0.35f))
                : Mathf.LerpUnclamped(1.7f, 1f, LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, (u - 0.35f) / 0.65f));
        }

        /// <summary>Centred between the container's bottom edge and the screen bottom, measured every frame in the canvas's
        /// own space (overlay = screen pixels; the AdSim ScreenSpaceCamera canvas = the capture camera's pixels).</summary>
        void Place()
        {
            var canvasRt = (RectTransform)canvas.transform;
            Vector2 px = game.SteadyToScreen(new Vector2(0f, game.Board.FloorY - game.frameThickness));
            Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, px, uiCam, out Vector2 local))
            {
                // anchors are bottom-centre: local y measured from the canvas's bottom edge, halved = the midpoint
                float fromBottom = local.y - canvasRt.rect.yMin;
                // never lower than the bottom icon row's clearance above the screen edge (the band under the container is
                // barely taller than the strip, so centring alone left the lower row touching / cut by the screen bottom)
                float s = Mathf.Min(1f, canvasRt.rect.width / (root.sizeDelta.x + 2f * PopMargin));
                root.anchoredPosition = new Vector2(0f, Mathf.Max(fromBottom * 0.5f, BelowCentre() * s));
            }
            // the strip is a full 1080 wide plus pop headroom at both ends: narrower canvases shrink it to fit (centred)
            root.localScale = Vector3.one * Mathf.Min(1f, canvasRt.rect.width / (root.sizeDelta.x + 2f * PopMargin));
        }

        const float EdgeMargin = 24f; // canvas units kept free under the lowest icon

        /// <summary>How far the lowest icon's edge (+ <see cref="EdgeMargin"/>) reaches below the strip's centre, in the strip's own units.</summary>
        float BelowCentre()
        {
            float low = 0f;
            foreach (var s in slots)
                if (s != null) low = Mathf.Min(low, root.InverseTransformPoint(s.position).y);
            return -low + iconSize * 0.5f + EdgeMargin;
        }

        // ------------------------------------------------------------------ procedural sprites
        static Sprite glow, flash, ring;

        static Sprite Radial(int size, System.Func<float, Color> fn)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = fn(new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f).magnitude);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Soft white dot (spark glow, fly-in halo): white at the centre → light cyan → 0.</summary>
        static Sprite GlowSprite => glow != null ? glow : glow = Radial(64, r =>
            r < 0.3f ? Color.white : Color.Lerp(new Color(0.85f, 0.97f, 1f, 0.8f), new Color(0.7f, 0.94f, 1f, 0f), Mathf.Clamp01((r - 0.3f) / 0.7f)));

        /// <summary>Mock .glowp: white .95 → cyan .5 at 35 % → 0 at 70 %.</summary>
        static Sprite FlashSprite => flash != null ? flash : flash = Radial(128, r =>
            r < 0.35f ? Color.Lerp(new Color(1f, 1f, 1f, 0.95f), new Color(0.67f, 0.92f, 1f, 0.5f), r / 0.35f)
                      : Color.Lerp(new Color(0.67f, 0.92f, 1f, 0.5f), new Color(0.59f, 0.86f, 1f, 0f), Mathf.Clamp01((r - 0.35f) / 0.35f)));

        /// <summary>Mock .ring: a white circle border, 3 of 26 px thick.</summary>
        static Sprite RingSprite => ring != null ? ring : ring = Radial(128, r =>
        {
            const float aa = 2f / 128f, inner = 1f - 2f * 3f / 26f;
            return new Color(1f, 1f, 1f, Mathf.Clamp01((1f - r) / aa) * Mathf.Clamp01((r - inner) / aa));
        });
    }
}
