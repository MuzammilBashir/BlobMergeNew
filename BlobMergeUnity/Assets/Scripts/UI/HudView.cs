using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// The in-game HUD. Lives on the root of HUD.prefab (Assets/Resources/UI/HUD.prefab) — open that prefab to
    /// restyle/move anything; the game only talks to the references below, so rearrange freely but keep them assigned.
    /// </summary>
    public class HudView : MonoBehaviour
    {
        [Header("References (keep assigned when editing the prefab)")]
        [Tooltip("Gets fitted to the device safe area (notches) at runtime. Put on-screen HUD inside it.")]
        public RectTransform safeArea;
        [Tooltip("Parent of the score panel / best score / settings button; shifted by GameController.hudOffsetY.")]
        public RectTransform topBar;
        public TMP_Text scoreText;
        public TMP_Text bestText;
        public Button settingsButton;
        public GameObject gameOverPanel;
        public TMP_Text gameOverText;
        [Tooltip("Optional: set = game over restarts only from this button (no tap-anywhere).")]
        public Button playAgainButton;

        [Header("Style source for in-game text")]
        [Tooltip("Blob numbers and floating +score texts copy this text's font, material (outline), colour and Bold/Italic. " +
                 "Empty = use the Score text.")]
        public TMP_Text worldTextStyle;

        public TMP_Text WorldTextStyle => worldTextStyle != null ? worldTextStyle : scoreText;

        [Header("Feel")]
        [Tooltip("How much the score number pops (scale) when it changes.")]
        public float scorePunchScale = 0.35f;
        public float scorePunchDecay = 4f;
        [Tooltip("{0} = final score.")]
        [TextArea] public string gameOverFormat = "GAME OVER\n{0}\n<size=55%>tap to restart</size>";

        float punch;

        void Awake()
        {
            if (safeArea != null && Application.isPlaying)
            {
                var sa = Screen.safeArea;
                safeArea.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
                safeArea.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
                safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
            }
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            // buttons need an EventSystem in the scene
            if (settingsButton != null && FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public void SetOffsetY(float y)
        {
            if (topBar != null) topBar.anchoredPosition += new Vector2(0f, y);
        }

        public void SetScore(int score, bool punchIt)
        {
            if (scoreText != null) scoreText.text = score.ToString();
            if (punchIt) punch = 1f;
        }

        public void SetBest(int best)
        {
            if (bestText != null) bestText.text = best.ToString();
        }

        public void ShowGameOver(int score)
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(true);
            if (gameOverText != null) gameOverText.text = string.Format(gameOverFormat, score);
        }

        void Update()
        {
            punch = Mathf.Max(0f, punch - GameTime.UnscaledDelta * scorePunchDecay);
            if (scoreText != null) scoreText.transform.localScale = Vector3.one * (1f + punch * scorePunchScale);
        }

        // ====================================================================== default layout
        /// <summary>
        /// Builds the default HUD hierarchy. Used by the editor to generate HUD.prefab (with saved sprite assets),
        /// and at runtime as a fallback when the prefab doesn't exist yet.
        /// </summary>
        public static HudView Build(Sprite rounded, Sprite crownSprite, Sprite gearSprite)
        {
            var root = new GameObject("HUD", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();
            var hud = root.AddComponent<HudView>();

            var safe = Rect("SafeArea", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            hud.safeArea = safe;

            // top bar: stretches across the top of the safe area
            var topBar = Rect("TopBar", safe, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 260f));
            hud.topBar = topBar;
            var top = new Vector2(0.5f, 1f);

            // score panel with "SCORE" tab
            var panel = Img(topBar, "ScorePanel", rounded, MeshUtil.Hex("1E1240"), top, new Vector2(0, -175), new Vector2(330, 135), true);
            Shadow(panel.gameObject, new Color(0.05f, 0.02f, 0.12f, 0.6f), new Vector2(0, -6));
            var tab = Img(panel.rectTransform, "Tab", rounded, MeshUtil.Hex("5B37B4"), top, new Vector2(0, 6), new Vector2(170, 52), true);
            Txt(tab.rectTransform, "Label", "SCORE", 30, TextAlignmentOptions.Center, new Vector2(0, -1), new Vector2(160, 50));
            hud.scoreText = Txt(panel.rectTransform, "Score", "0", 72, TextAlignmentOptions.Center, new Vector2(0, -8), new Vector2(320, 100));

            // best: crown + number (top-left)
            var crown = Img(topBar, "Crown", crownSprite, MeshUtil.Hex("FFC63A"), new Vector2(0f, 1f), new Vector2(70, -170), new Vector2(54, 54), false);
            Shadow(crown.gameObject, new Color(0.6f, 0.35f, 0f, 0.6f), new Vector2(0, -3));
            var best = Txt(topBar, "Best", "0", 40, TextAlignmentOptions.Left, Vector2.zero, new Vector2(240, 60));
            var brt = best.rectTransform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 0.5f);
            brt.anchoredPosition = new Vector2(108, -170);
            hud.bestText = best;

            // settings button (top-right) — hook its OnClick in the prefab
            var gearBg = Img(topBar, "SettingsButton", rounded, MeshUtil.Hex("5B37B4"), new Vector2(1f, 1f), new Vector2(-80, -170), new Vector2(84, 84), true);
            gearBg.raycastTarget = true;
            Shadow(gearBg.gameObject, new Color(0.08f, 0.03f, 0.2f, 0.7f), new Vector2(0, -5));
            hud.settingsButton = gearBg.gameObject.AddComponent<Button>();
            Img(gearBg.rectTransform, "Gear", gearSprite, MeshUtil.Hex("D9CCFF"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52, 52), false);

            // game over overlay
            var dim = Rect("GameOverPanel", root.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var dimImg = dim.gameObject.AddComponent<Image>();
            dimImg.color = new Color(0.07f, 0.03f, 0.17f, 0.82f);
            dimImg.raycastTarget = false;
            var card = Img(dim, "Card", rounded, MeshUtil.Hex("2A1A5C"), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 520), true);
            var o1 = card.gameObject.AddComponent<Outline>(); o1.effectColor = MeshUtil.Hex("6A3AE0"); o1.effectDistance = new Vector2(6, -6);
            var o2 = card.gameObject.AddComponent<Outline>(); o2.effectColor = MeshUtil.Hex("6A3AE0"); o2.effectDistance = new Vector2(6, 6);
            hud.gameOverText = Txt(card.rectTransform, "GameOverText", "GAME OVER", 84, TextAlignmentOptions.Center, Vector2.zero, new Vector2(740, 500));
            hud.gameOverPanel = dim.gameObject;
            dim.gameObject.SetActive(false);
            return hud;
        }

        static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform parent, string name, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size, bool sliced)
        {
            var rt = Rect(name, parent, anchor, anchor, new Vector2(0.5f, 0.5f), pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        static TMP_Text Txt(RectTransform parent, string name, string text, float size, TextAlignmentOptions align, Vector2 pos, Vector2 dim)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, dim);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            TextStyle.Apply(t);
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        static void Shadow(GameObject go, Color color, Vector2 offset)
        {
            var s = go.AddComponent<Shadow>();
            s.effectColor = color;
            s.effectDistance = offset;
        }
    }
}
