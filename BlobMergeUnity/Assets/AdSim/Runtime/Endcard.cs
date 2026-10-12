using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AdSim
{
    /// <summary>
    /// An endcard as data: layers (image / colour / text) with keyframed animations, a hand script and sound cues.
    /// Positions are fractions of the frame with y measured from the TOP (like design tools); sizes are fractions of
    /// the frame width W. See Documentation~/README.md ("Endcards") for the format and Endcards/*.json for examples.
    /// </summary>
    [Serializable]
    public class EndcardSpec
    {
        public string name = "endcard";
        public float duration = 4f;
        public string tapSfx = "";            // played on every hand press
        public float tapVolume = 1f;
        public string font = "";              // text font asset path (e.g. Assets/AdSim/Fonts/Poppins-Bold.ttf); "" = OS Arial Black
        public EndcardLayer[] layers = new EndcardLayer[0];
        public EndcardHandStep[] hand = new EndcardHandStep[0];
        public EndcardSfx[] sfx = new EndcardSfx[0];

        public static EndcardSpec FromJson(string json) => JsonUtility.FromJson<EndcardSpec>(json);

        public IEnumerable<string> ImagePaths()
        {
            foreach (var l in layers)
                if (!string.IsNullOrEmpty(l.image)) yield return l.image;
        }
    }

    [Serializable]
    public class EndcardLayer
    {
        public string id = "";
        public string image = "";             // texture asset path; empty + no text = solid colour quad
        public string color = "#FFFFFF";      // image tint / fill colour / text colour
        public bool fullscreen;               // cover the whole frame (aspect fill)
        public string text = "";              // "\n" for line breaks
        public float textSize = 0.06f;        // cap height, fraction of W
        public float lineSpacing = 0.9f;      // multi-line text: line pitch multiplier
        public string align = "center";       // center | left | right: which side of the text sits at pos
        public string outline = "";           // text outline colour ("" = none)
        public float outlineWidth = 0.006f;   // fraction of W
        public Vector2 pos = new Vector2(0.5f, 0.5f);
        public float width = 0.5f;            // image width (opaque content), fraction of W
        public string anchor = "bounds";      // bounds: pos = centre of the opaque box; mass: pos = visual centre (alpha centroid)
        public bool pressable = true;         // squish while the hand presses it
        public EndcardAnim[] anims = new EndcardAnim[0];
    }

    /// <summary>
    /// One tween. Before a property's first tween starts, the property holds that tween's <see cref="from"/>
    /// (so things can start hidden); afterwards the latest started tween wins. "punch" returns to from.
    /// </summary>
    [Serializable]
    public class EndcardAnim
    {
        public float t;
        public float d = 0.3f;
        public string prop = "scale";         // x | y | scale | alpha | rot | chars (fraction of the text typed)
        public float from;
        public float to = 1f;
        public string ease = "outCubic";      // linear inCubic outCubic inOutCubic outBack outElastic punch
    }

    [Serializable]
    public class EndcardHandStep
    {
        public float t;                       // starts no earlier than this (after the previous step)
        public string action = "moveTo";      // moveTo | tap | hover | exit
        public string target = "";            // layer id; empty = pos
        public Vector2 pos;                   // frame fraction, y from top (when no target)
        public Vector2 offset;                // added to the point, fraction of W (y down)
        public float hold = 0.15f;            // tap: press length; hover: duration
        public float speed = 1f;              // travel speed, x the reference pace (1.3 W/s) whatever the persona
        public float duration;                // > 0: moveTo/tap travel takes exactly this long (frame-timed endcards), no correction
    }

    [Serializable]
    public class EndcardSfx
    {
        public float t;
        public string clip;
        public float volume = 1f;
        public float maxLength;               // > 0: trim to this length with a short fade-out
    }

    /// <summary>Builds an endcard in front of the capture camera and plays it, driving the shared hand.</summary>
    public class EndcardPlayer
    {
        class Layer
        {
            public EndcardLayer Spec;
            public Transform T;
            public SpriteRenderer Sprite;
            public TextMesh Text;
            public TextMesh[] Outline;
            public Vector3[] OutlineOffsets;
            public Color Color, OutlineColor;
            public float BaseScale = 1f, Press;
            public Vector2 FullscreenScale;
        }

        static readonly Vector2[] OutlineDirs =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7f, 0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, -0.7f),
        };

        const int SortBase = 31000;          // above the game, below the hand (32000)
        const int RenderQueue = 3900;        // above game transparents, below the hand (4000)
        const int FontPx = 96;

        readonly EndcardSpec _spec;
        readonly Camera _cam;
        readonly HumanMotor _motor;
        readonly Func<string, Texture2D> _load;
        readonly Func<string, Font> _loadFont;
        readonly Action<string, float, float> _cue; // clip, volume, maxLength (at "now")
        readonly float _w, _h;
        readonly List<Layer> _layers = new List<Layer>();
        readonly Dictionary<string, Layer> _byId = new Dictionary<string, Layer>();
        GameObject _root;
        Material _spriteMat;
        Layer _pressTarget;
        float _t;
        Font _font;
        bool _customFont;

        public EndcardPlayer(EndcardSpec spec, Camera cam, HumanMotor motor, Func<string, Texture2D> loadTexture,
                             Func<string, Font> loadFont, Action<string, float, float> cue)
        {
            _loadFont = loadFont;
            _spec = spec;
            _cam = cam;
            _motor = motor;
            _load = loadTexture;
            _cue = cue;
            _w = motor.W;
            _h = motor.H;
        }

        float WorldPerW => _cam.orthographicSize * 2f * _cam.aspect;

        public IEnumerator Play()
        {
            Build();
            var hand = new IteratorRunner();
            hand.Start(HandRoutine());
            int nextSfx = 0;
            var sfx = new List<EndcardSfx>(_spec.sfx);
            sfx.Sort((a, b) => a.t.CompareTo(b.t));
            for (_t = 0f; _t < _spec.duration; _t += Time.deltaTime)
            {
                while (nextSfx < sfx.Count && sfx[nextSfx].t <= _t)
                {
                    var s = sfx[nextSfx++];
                    _cue(s.clip, s.volume, s.maxLength);
                }
                hand.Step();
                Apply(Time.deltaTime);
                yield return null;
            }
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            _root = new GameObject("AdSim Endcard");
            _spriteMat = new Material(Shader.Find("Sprites/Default")) { renderQueue = RenderQueue };
            for (int i = 0; i < _spec.layers.Length; i++)
            {
                var s = _spec.layers[i];
                var l = new Layer { Spec = s, Color = ParseColor(s.color, Color.white) };
                var go = new GameObject(string.IsNullOrEmpty(s.id) ? "layer" + i : s.id);
                go.transform.SetParent(_root.transform, false);
                l.T = go.transform;
                int order = SortBase + i * 10;
                if (!string.IsNullOrEmpty(s.text)) BuildText(l, order);
                else BuildSprite(l, order);
                _layers.Add(l);
                if (!string.IsNullOrEmpty(s.id)) _byId[s.id] = l;
            }
            Apply(0f);
        }

        void BuildSprite(Layer l, int order)
        {
            var s = l.Spec;
            Texture2D tex = string.IsNullOrEmpty(s.image) ? Texture2D.whiteTexture : _load?.Invoke(s.image);
            if (tex == null)
            {
                Debug.LogWarning("[AdSim] endcard image not found: " + s.image);
                tex = Texture2D.whiteTexture;
            }
            var rect = string.IsNullOrEmpty(s.image) ? new Rect(0, 0, tex.width, tex.height) : OpaqueRect(tex);
            var pivot = s.anchor == "mass" ? MassPivot(tex, rect) : new Vector2(0.5f, 0.5f);
            var sprite = Sprite.Create(tex, rect, pivot, 100f, 0, SpriteMeshType.FullRect);
            l.Sprite = l.T.gameObject.AddComponent<SpriteRenderer>();
            l.Sprite.sprite = sprite;
            // one material per layer: SpriteRenderers sharing a material here all sampled the first sprite's texture
            l.Sprite.sharedMaterial = new Material(_spriteMat) { mainTexture = tex };
            l.Sprite.sortingOrder = order;
            float spriteW = rect.width / 100f, spriteH = rect.height / 100f;
            if (s.fullscreen)
            {
                float worldW = WorldPerW, worldH = _cam.orthographicSize * 2f;
                float cover = Mathf.Max(worldW / spriteW, worldH / spriteH) * 1.01f;
                l.FullscreenScale = string.IsNullOrEmpty(s.image)
                    ? new Vector2(worldW / spriteW * 1.01f, worldH / spriteH * 1.01f) // solid fill: stretch
                    : new Vector2(cover, cover);
            }
            else l.BaseScale = s.width * WorldPerW / spriteW;
        }

        void BuildText(Layer l, int order)
        {
            var s = l.Spec;
            if (_font == null && !string.IsNullOrEmpty(_spec.font))
            {
                _font = _loadFont?.Invoke(_spec.font);
                _customFont = _font != null;
                if (_font == null) Debug.LogWarning("[AdSim] endcard font not found: " + _spec.font);
            }
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont(new[] { "Arial Black", "Segoe UI Black", "Arial Bold", "Arial" }, FontPx);
            var mat = new Material(_font.material) { renderQueue = RenderQueue };
            var anchor = s.align == "left" ? TextAnchor.MiddleLeft : s.align == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            var align = s.align == "left" ? TextAlignment.Left : s.align == "right" ? TextAlignment.Right : TextAlignment.Center;
            _font.RequestCharactersInTexture("H", FontPx);
            float capPx = _font.GetCharacterInfo('H', out var ci, FontPx) ? ci.glyphHeight : FontPx * 0.72f;
            // TextMesh: one font pixel = 0.1 world units at characterSize 1
            float charSize = s.textSize * WorldPerW / (Mathf.Max(1f, capPx) * 0.1f);

            TextMesh Make(Transform parent, string name, int sortingOrder)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var tm = go.AddComponent<TextMesh>();
                tm.font = _font;
                tm.fontSize = FontPx;
                tm.fontStyle = _customFont ? FontStyle.Normal : FontStyle.Bold; // a bold font file is already bold
                tm.characterSize = charSize;
                tm.anchor = anchor;
                tm.alignment = align;
                tm.lineSpacing = s.lineSpacing;
                tm.richText = false;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.sortingOrder = sortingOrder;
                return tm;
            }

            if (!string.IsNullOrEmpty(s.outline))
            {
                l.OutlineColor = ParseColor(s.outline, Color.black);
                l.Outline = new TextMesh[OutlineDirs.Length];
                l.OutlineOffsets = new Vector3[OutlineDirs.Length];
                for (int k = 0; k < OutlineDirs.Length; k++)
                {
                    l.Outline[k] = Make(l.T, "outline", order);
                    l.OutlineOffsets[k] = (Vector3)(OutlineDirs[k] * (s.outlineWidth * WorldPerW));
                }
            }
            l.Text = Make(l.T, "text", order + 1);
        }

        static Rect OpaqueRect(Texture2D tex)
        {
            if (!tex.isReadable) return new Rect(0, 0, tex.width, tex.height);
            try
            {
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height, x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (px[y * w + x].a < 8) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
                if (x1 >= x0 && y1 >= y0) return new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            }
            catch (Exception) { /* unreadable data: use the whole image */ }
            return new Rect(0, 0, tex.width, tex.height);
        }

        /// <summary>Alpha-weighted centre of the image inside <paramref name="rect"/>, as a sprite pivot (0..1).</summary>
        static Vector2 MassPivot(Texture2D tex, Rect rect)
        {
            if (!tex.isReadable) return new Vector2(0.5f, 0.5f);
            var px = tex.GetPixels32();
            double sx = 0, sy = 0, sw = 0;
            int w = tex.width;
            for (int y = (int)rect.yMin; y < (int)rect.yMax; y++)
            for (int x = (int)rect.xMin; x < (int)rect.xMax; x++)
            {
                float a = px[y * w + x].a / 255f;
                sx += x * a;
                sy += y * a;
                sw += a;
            }
            if (sw <= 0) return new Vector2(0.5f, 0.5f);
            return new Vector2((float)((sx / sw + 0.5 - rect.xMin) / rect.width), (float)((sy / sw + 0.5 - rect.yMin) / rect.height));
        }

        // ---------------------------------------------------------------- animate

        void Apply(float dt)
        {
            float depth = _cam.nearClipPlane + 2f; // behind the hand (near + 1)
            foreach (var l in _layers)
            {
                var s = l.Spec;
                float x = Eval(s, "x", s.pos.x), y = Eval(s, "y", s.pos.y);
                float scale = Eval(s, "scale", 1f), alpha = Eval(s, "alpha", 1f), rot = Eval(s, "rot", 0f);

                bool pressed = _motor.Pointer.Down && _pressTarget == l && s.pressable;
                l.Press = Mathf.MoveTowards(l.Press, pressed ? 1f : 0f, dt / 0.06f);
                scale *= Mathf.Lerp(1f, 0.92f, l.Press);

                var p = _cam.ViewportToWorldPoint(new Vector3(x, 1f - y, depth));
                l.T.position = p;
                l.T.rotation = Quaternion.Euler(0f, 0f, rot);
                if (l.Sprite != null)
                {
                    l.T.localScale = l.FullscreenScale != Vector2.zero
                        ? new Vector3(l.FullscreenScale.x * scale, l.FullscreenScale.y * scale, 1f)
                        : Vector3.one * (l.BaseScale * scale);
                    var c = l.Color;
                    c.a *= Mathf.Clamp01(alpha);
                    l.Sprite.color = c;
                    l.Sprite.enabled = c.a > 0.001f && scale > 0.001f;
                }
                else if (l.Text != null)
                {
                    l.T.localScale = Vector3.one * scale;
                    float typed = Mathf.Clamp01(Eval(s, "chars", 1f));
                    int n = Mathf.RoundToInt(typed * s.text.Length);
                    string text = s.text.Substring(0, Mathf.Clamp(n, 0, s.text.Length));
                    var c = l.Color;
                    c.a *= Mathf.Clamp01(alpha);
                    l.Text.text = text;
                    l.Text.color = c;
                    if (l.Outline != null)
                    {
                        var oc = l.OutlineColor;
                        oc.a *= Mathf.Clamp01(alpha);
                        for (int k = 0; k < l.Outline.Length; k++)
                        {
                            l.Outline[k].text = text;
                            l.Outline[k].color = oc;
                            l.Outline[k].transform.localPosition = l.OutlineOffsets[k] / Mathf.Max(0.001f, scale);
                        }
                    }
                }
            }
        }

        float Eval(EndcardLayer s, string prop, float fallback)
        {
            EndcardAnim first = null, current = null;
            foreach (var a in s.anims)
            {
                if (a.prop != prop) continue;
                if (first == null || a.t < first.t) first = a;
                if (a.t <= _t && (current == null || a.t >= current.t)) current = a;
            }
            if (current == null) return first != null ? first.from : fallback;
            float u = current.d <= 0f ? 1f : Mathf.Clamp01((_t - current.t) / current.d);
            return current.ease == "punch"
                ? current.from + (current.to - current.from) * Mathf.Sin(Mathf.PI * u)
                : Mathf.LerpUnclamped(current.from, current.to, Ease(current.ease, u));
        }

        static float Ease(string ease, float u)
        {
            switch (ease)
            {
                case "linear": return u;
                case "inCubic": return u * u * u;
                case "inOutCubic": return u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f;
                case "outBack":
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    float v = u - 1f;
                    return 1f + c3 * v * v * v + c1 * v * v;
                }
                case "outElastic":
                    if (u <= 0f || u >= 1f) return u;
                    return Mathf.Pow(2f, -10f * u) * Mathf.Sin((u * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                default: // outCubic
                {
                    float v = 1f - u;
                    return 1f - v * v * v;
                }
            }
        }

        // ---------------------------------------------------------------- hand

        IEnumerator HandRoutine()
        {
            foreach (var step in _spec.hand)
            {
                while (_t < step.t) yield return null;
                switch (step.action)
                {
                    case "exit":
                        _motor.Pointer.Down = false;
                        yield return _motor.MoveTo(new Vector2(_w * 1.25f, -_h * 0.15f), Pace(step), false);
                        break;
                    case "hover":
                        yield return _motor.Hover(step.hold);
                        break;
                    case "tap":
                    {
                        var target = Point(step);
                        if (step.duration > 0f) yield return TimedMove(target, step.duration);
                        else if (Vector2.Distance(_motor.Base, target) > 0.02f * _w) yield return _motor.MoveTo(target, Pace(step));
                        _byId.TryGetValue(step.target ?? "", out _pressTarget);
                        _motor.Pointer.Down = true;
                        if (!string.IsNullOrEmpty(_spec.tapSfx)) _cue(_spec.tapSfx, _spec.tapVolume, 0f);
                        yield return _motor.Wait(step.hold);
                        _motor.Pointer.Down = false;
                        yield return null;
                        break;
                    }
                    default: // moveTo
                        if (step.duration > 0f) yield return TimedMove(Point(step), step.duration);
                        else yield return _motor.MoveTo(Point(step), Pace(step));
                        break;
                }
            }
        }

        /// <summary>Minimum-jerk travel that lands exactly after <paramref name="seconds"/> (frame-timed endcards).</summary>
        IEnumerator TimedMove(Vector2 target, float seconds)
        {
            Vector2 from = _motor.Base;
            for (float t = 0f; ; )
            {
                t += Time.deltaTime;
                _motor.Base = Vector2.Lerp(from, target, HumanMotor.MinJerk(t / seconds));
                if (t >= seconds) break;
                yield return null;
            }
            _motor.Base = target;
        }

        /// <summary>Endcards are scripted: the hand keeps the same pace whatever the gameplay persona.</summary>
        float Pace(EndcardHandStep step) => step.speed * ReferenceTravelSpeed / Mathf.Max(0.05f, _motor.TravelSpeed);

        const float ReferenceTravelSpeed = 1.3f;

        Vector2 Point(EndcardHandStep step)
        {
            float x = step.pos.x, y = step.pos.y;
            if (!string.IsNullOrEmpty(step.target) && _byId.TryGetValue(step.target, out var l))
            {
                x = Eval(l.Spec, "x", l.Spec.pos.x);
                y = Eval(l.Spec, "y", l.Spec.pos.y);
            }
            return new Vector2(x * _w + step.offset.x * _w, (1f - y) * _h - step.offset.y * _w);
        }

        static Color ParseColor(string hex, Color fallback) =>
            !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
    }
}
