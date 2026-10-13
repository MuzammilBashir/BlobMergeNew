using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Launch splash (Splash_Fish.prefab, set on the scene's BoardTheme): full-screen art, the logo popping in and an
    /// animated "Loading..." line, then a fade. Shown once per app launch (Restart reloads the scene) as a nested
    /// canvas on top of the HUD. Runs on <see cref="GameTime"/>.
    /// </summary>
    public class SplashView : MonoBehaviour
    {
        [Tooltip("Fades the whole splash out.")]
        public CanvasGroup group;
        [Tooltip("Pops in (optional).")]
        public RectTransform logo;
        [Tooltip("Gets 1-3 animated dots after its text (optional).")]
        public TMP_Text loading;

        [Header("Timing (seconds)")]
        public float hold = 2.2f;
        public float fade = 0.35f;
        public float logoIn = 0.15f;
        public float logoDuration = 0.5f;

        static bool shown;
        static SplashView active;
        float t;
        string loadingText;
        System.Action done;

        /// <summary>Shows the splash (first call per launch only). Returns false (and doesn't call <paramref name="onDone"/>)
        /// when nothing is shown.</summary>
        public static bool Show(SplashView prefab, Transform hudCanvas, System.Action onDone)
        {
            if (prefab == null || shown) return false;
            shown = true;
            active = Instantiate(prefab, hudCanvas, false);
            active.name = "Splash";
            active.done = onDone;
            var rt = (RectTransform)active.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            return true;
        }

        /// <summary>Seconds the showing splash has run (its own clock), -1 when none (bot screenshots).</summary>
        public static float Elapsed => active != null ? active.t : -1f;

        /// <summary>Ad takes start straight into gameplay: drop a showing splash at once.</summary>
        public static void Dismiss()
        {
            if (active != null) active.Finish();
        }

        void Awake()
        {
            if (loading != null) loadingText = loading.text.TrimEnd('.');
            if (logo != null) logo.localScale = Vector3.zero;
        }

        void Update()
        {
            t += Mathf.Min(GameTime.UnscaledDelta, 0.05f); // the first frame after load reports ~1.4 s
            if (logo != null) logo.localScale = Vector3.one * LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, (t - logoIn) / logoDuration);
            if (loading != null)
            {
                int dots = 1 + (int)(t * 3f) % 3;
                loading.text = loadingText + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots); // hidden dots keep it centred
            }
            float f = (t - hold) / Mathf.Max(0.01f, fade);
            if (group != null) group.alpha = 1f - Mathf.Clamp01(f);
            if (f >= 1f) Finish();
        }

        void Finish()
        {
            if (active == this) active = null;
            Destroy(gameObject);
            var d = done;
            done = null;
            d?.Invoke();
        }

        // ------------------------------------------------------------------ default layout (the prefab builder saves this)
        /// <summary>Background (covers the screen), logo in the top area, Loading near the bottom.</summary>
        public static SplashView BuildDefault(Transform parent, Sprite background, Sprite logoSprite)
        {
            var root = new GameObject("Splash", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var c = root.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 1000;
            root.gameObject.AddComponent<GraphicRaycaster>();
            var v = root.gameObject.AddComponent<SplashView>();
            v.group = root.gameObject.AddComponent<CanvasGroup>();

            var back = Child(root, "Back", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            back.gameObject.AddComponent<Image>().color = MeshUtil.Hex("0B5FA8"); // blocks input while it shows
            if (background != null)
            {
                var art = Child(root, "Art", Half, Half, Vector2.zero, new Vector2(1080f, 1920f));
                art.gameObject.AddComponent<Image>().sprite = background;
                var fit = art.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = background.rect.width / background.rect.height;
            }
            if (logoSprite != null)
            {
                // the art keeps its top 30 % calm for the logo
                v.logo = Child(root, "Logo", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -330f),
                               new Vector2(760f, 760f * logoSprite.rect.height / logoSprite.rect.width));
                v.logo.gameObject.AddComponent<Image>().sprite = logoSprite;
            }
            var lrt = Child(root, "Loading", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(700f, 100f));
            v.loading = lrt.gameObject.AddComponent<TextMeshProUGUI>();
            TextStyle.Apply(v.loading);
            v.loading.text = "Loading...";
            v.loading.fontSize = 64f;
            v.loading.alignment = TextAlignmentOptions.Center;
            v.loading.raycastTarget = false;
            return v;
        }

        static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

        static RectTransform Child(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }
    }
}
