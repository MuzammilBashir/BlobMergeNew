using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Fish Fantasy art pass (2026-10-13, Higgsfield art in Assets/Art/UI, referenced from the scene's BoardTheme):
    /// restyles the HUD prefab's score panel / crown / settings button, replaces the game-over card with the popup
    /// (sad fish in a bubble, score plate, Play again button) and shows the splash once per launch. Scenes whose
    /// theme has no UI art are untouched. Animations run on <see cref="GameTime"/> (game over sets timeScale 0).
    /// </summary>
    public class FishUi : MonoBehaviour
    {
        const float PopupWidth = 680f, PopDur = 0.45f;
        const float SplashHold = 2.2f, SplashFade = 0.35f, LogoIn = 0.15f, LogoDur = 0.5f;

        static bool splashShown; // once per app launch (Restart reloads the scene)
        static FishUi active;

        RectTransform card, crownIcon;
        TMP_Text bestValue;
        float cardT = -1f;
        static readonly Color Ink = new Color32(0x14, 0x30, 0x6B, 0xFF);

        CanvasGroup splash;
        RectTransform logo;
        TMP_Text loading;
        float splashT = -1f;
        System.Action splashDone;

        public static void Apply(GameController game, HudView hud, BoardTheme theme, bool showSplash, System.Action splashDone)
        {
            if (theme.uiPopup == null && theme.splashBackground == null && theme.uiScorePlate == null) return;
            var ui = hud.gameObject.AddComponent<FishUi>();
            active = ui;
            Restyle(hud, theme);
            if (theme.uiPopup != null) ui.BuildGameOver(game, hud, theme);
            if (showSplash && !splashShown && theme.splashBackground != null)
            {
                splashShown = true;
                ui.BuildSplash(hud, theme, splashDone);
            }
            else splashDone?.Invoke();
        }

        // ------------------------------------------------------------------ HUD
        static void Restyle(HudView hud, BoardTheme theme)
        {
            if (theme.uiScorePlate != null && Find(hud.transform, "ScorePanel") is RectTransform panel)
            {
                var img = panel.GetComponent<Image>();
                Swap(img, theme.uiScorePlate);
                panel.sizeDelta = new Vector2(400f, 400f / Aspect(theme.uiScorePlate));
                foreach (var s in panel.GetComponents<Shadow>()) s.enabled = false;
                if (Find(panel, "Tab") is RectTransform tab)
                {
                    tab.GetComponent<Image>().enabled = false; // the plate has its own tab; the SCORE label stays
                    tab.anchoredPosition = new Vector2(0f, -4f);
                }
                if (hud.scoreText != null) hud.scoreText.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            }
            if (hud.bestText != null) hud.bestText.enableWordWrapping = false; // 5-digit bests wrapped to "4453 / 0"
            if (theme.uiCrown != null && Find(hud.transform, "Crown") is RectTransform crown)
            {
                Swap(crown.GetComponent<Image>(), theme.uiCrown);
                crown.sizeDelta = new Vector2(64f, 64f);
                foreach (var s in crown.GetComponents<Shadow>()) s.enabled = false;
            }
            if (theme.uiSettingsButton != null && Find(hud.transform, "SettingsButton") is RectTransform gear)
            {
                Swap(gear.GetComponent<Image>(), theme.uiSettingsButton);
                gear.sizeDelta = new Vector2(100f, 100f);
                foreach (var s in gear.GetComponents<Shadow>()) s.enabled = false;
                if (Find(gear, "Gear") is RectTransform icon) icon.gameObject.SetActive(false);
            }
        }

        static void Swap(Image img, Sprite sprite)
        {
            if (img == null) return;
            img.sprite = sprite;
            img.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            img.preserveAspect = true;
            img.color = Color.white;
        }

        static float Aspect(Sprite s) => s.rect.width / s.rect.height;

        static RectTransform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ------------------------------------------------------------------ game over popup
        void BuildGameOver(GameController game, HudView hud, BoardTheme theme)
        {
            if (hud.gameOverPanel != null) Destroy(hud.gameOverPanel);
            var dim = Rect("GameOverPopup", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0.02f, 0.08f, 0.2f, 0.75f); // blocks taps on the board

            // the card is 9-sliced (wave header in the top border), so its height follows the content
            const float h = 760f;
            card = Rect("Card", dim, Half, Half, Vector2.zero, new Vector2(PopupWidth, h));
            Img(card, theme.uiPopup);

            var title = Txt(card, "Title", "GAME OVER", 76f, new Vector2(0f, h * 0.5f - 88f), new Vector2(PopupWidth - 80f, 110f));
            title.characterSpacing = 2f;

            // body text sits on the white card: navy face (white would vanish), same font and outline
            Txt(card, "ScoreLabel", "YOUR SCORE", 40f, new Vector2(0f, 118f), new Vector2(560f, 60f), Ink);
            hud.gameOverText = Txt(card, "Score", "0", 120f, new Vector2(0f, 32f), new Vector2(560f, 140f), Ink);
            hud.gameOverFormat = "{0}";

            Txt(card, "BestLabel", "HIGH SCORE", 40f, new Vector2(0f, -62f), new Vector2(560f, 60f), Ink);
            var bestRow = Rect("Best", card, Half, Half, new Vector2(0f, -122f), new Vector2(560f, 80f));
            bestValue = Txt(bestRow, "Value", "0", 64f, Vector2.zero, new Vector2(400f, 80f), Ink);
            bestValue.alignment = TextAlignmentOptions.Left;
            if (theme.uiCrown != null)
            {
                crownIcon = Rect("Crown", bestRow, Half, Half, Vector2.zero, new Vector2(64f, 64f));
                Img(crownIcon, theme.uiCrown).preserveAspect = true;
            }

            var btnRt = Rect("PlayAgain", card, Half, Half, new Vector2(0f, -h * 0.5f + 125f), new Vector2(480f, 480f / Aspect(theme.uiButton)));
            var btnImg = Img(btnRt, theme.uiButton);
            btnImg.raycastTarget = true;
            var btn = btnRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.onClick.AddListener(game.Restart);
            Txt(btnRt, "Label", "PLAY AGAIN", 56f, new Vector2(0f, 6f), new Vector2(460f, 120f));
            hud.playAgainButton = btn;

            hud.gameOverPanel = dim.gameObject;
            dim.gameObject.SetActive(false);
        }

        static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = Half;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform rt, Sprite sprite)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        static TMP_Text Txt(RectTransform parent, string name, string text, float size, Vector2 pos, Vector2 dim, Color? face = null)
        {
            var t = Rect(name, parent, Half, Half, pos, dim).gameObject.AddComponent<TextMeshProUGUI>();
            TextStyle.Apply(t, face);
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return t;
        }

        // ------------------------------------------------------------------ splash
        void BuildSplash(HudView hud, BoardTheme theme, System.Action done)
        {
            splashDone = done;
            // a nested canvas on top of the HUD (it shares the HUD's scaler and capture-camera handling)
            var root = Rect("Splash", hud.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var c = root.gameObject.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 1000;
            root.gameObject.AddComponent<GraphicRaycaster>();
            splash = root.gameObject.AddComponent<CanvasGroup>();
            var black = Rect("Back", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            black.gameObject.AddComponent<Image>().color = MeshUtil.Hex("0B5FA8"); // blocks input while it shows
            var bg = Rect("Art", root, Half, Half, Vector2.zero, new Vector2(1080f, 1920f));
            Img(bg, theme.splashBackground);
            var fit = bg.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = Aspect(theme.splashBackground);

            if (theme.splashLogo != null)
            {
                // top 30 % of the art is kept calm for the logo
                logo = Rect("Logo", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -330f), new Vector2(760f, 760f / Aspect(theme.splashLogo)));
                Img(logo, theme.splashLogo);
                logo.localScale = Vector3.zero;
            }
            loading = Txt(root, "Loading", "Loading...", 64f, Vector2.zero, new Vector2(700f, 100f));
            var lrt = loading.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 190f);
            splashT = 0f;
        }

        // ------------------------------------------------------------------ animation
        void Update()
        {
            float dt = GameTime.UnscaledDelta;
            var hud = GetComponent<HudView>();
            if (card != null)
            {
                bool shown = hud.gameOverPanel != null && hud.gameOverPanel.activeInHierarchy;
                if (!shown) cardT = -1f;
                else
                {
                    if (cardT < 0f)
                    {
                        cardT = 0f;
                        hud.gameOverPanel.transform.SetAsLastSibling(); // over the creature bar
                        // high score = the HUD's (kept up to date while playing); crown + number centred as one row
                        bestValue.text = hud.bestText != null ? hud.bestText.text : "0";
                        float w = bestValue.GetPreferredValues(bestValue.text).x, gap = 14f, crownW = crownIcon != null ? 64f : 0f;
                        float left = -(crownW + gap + w) * 0.5f;
                        if (crownIcon != null) crownIcon.anchoredPosition = new Vector2(left + crownW * 0.5f, 4f);
                        bestValue.rectTransform.anchoredPosition = new Vector2(left + crownW + gap + 200f, 0f); // rect is 400 wide, text left-aligned
                    }
                    cardT += dt;
                    float u = Mathf.Clamp01(cardT / PopDur);
                    card.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, u));
                }
            }
            if (splashT >= 0f) AnimateSplash(Mathf.Min(dt, 0.05f)); // the first frame after load reports ~1.4 s
        }

        /// <summary>Ad takes start straight into gameplay: drop a showing splash at once.</summary>
        public static void DismissSplash()
        {
            if (active == null || active.splashT < 0f) return;
            active.splashT = SplashHold + SplashFade;
            active.AnimateSplash(0f);
        }

        void AnimateSplash(float dt)
        {
            splashT += dt;
            if (logo != null)
            {
                float u = Mathf.Clamp01((splashT - LogoIn) / LogoDur);
                logo.localScale = Vector3.one * LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, u);
            }
            int dots = 1 + (int)(splashT * 3f) % 3;
            loading.text = "Loading" + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots); // hidden dots keep it centred
            float fade = (splashT - SplashHold) / SplashFade;
            splash.alpha = 1f - Mathf.Clamp01(fade);
            if (fade < 1f) return;
            splashT = -1f;
            Destroy(splash.gameObject);
            splashDone?.Invoke();
        }
    }
}
