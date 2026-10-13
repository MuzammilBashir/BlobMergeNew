using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Fish game-over popup (a child of HUD_Fish.prefab, assigned as HudView.gameOverPanel): YOUR SCORE (HudView's
    /// gameOverText), HIGH SCORE (crown + number) and Play again (HudView.playAgainButton; GameController hooks it up).
    /// On show it moves above the creature bar, copies the HUD's best and pops the card in (GameTime: game over freezes time).
    /// </summary>
    public class GameOverPopup : MonoBehaviour
    {
        [Tooltip("Pops in.")]
        public RectTransform card;
        [Tooltip("Gets the HUD's best score.")]
        public TMP_Text best;
        [Tooltip("Optional: kept just left of the best number, the pair centred.")]
        public RectTransform crown;
        public float crownGap = 14f;
        [Header("Pop-in")]
        public float popDuration = 0.45f;
        public float popFrom = 0.6f;

        float t = -1f;

        void OnEnable()
        {
            t = 0f;
            transform.SetAsLastSibling(); // over the creature bar
            var hud = GetComponentInParent<HudView>();
            if (best != null && hud != null && hud.bestText != null) best.text = hud.bestText.text;
            CentreBestRow();
            Update();
        }

        void CentreBestRow()
        {
            if (best == null || crown == null) return;
            float w = best.GetPreferredValues(best.text).x, cw = crown.rect.width;
            float left = -(cw + crownGap + w) * 0.5f;
            crown.anchoredPosition = new Vector2(left + cw * 0.5f, crown.anchoredPosition.y);
            best.alignment = TextAlignmentOptions.Left;
            best.rectTransform.pivot = new Vector2(0f, 0.5f);
            best.rectTransform.anchoredPosition = new Vector2(left + cw + crownGap, best.rectTransform.anchoredPosition.y);
        }

        void Update()
        {
            if (t < 0f || card == null) return;
            t += GameTime.UnscaledDelta;
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, popDuration));
            card.localScale = Vector3.one * Mathf.LerpUnclamped(popFrom, 1f, LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, u));
            if (u >= 1f) t = -1f;
        }

        // ------------------------------------------------------------------ default layout (the prefab builder bakes this into HUD_Fish)
        static readonly Color Ink = new Color32(0x14, 0x30, 0x6B, 0xFF); // body text on the white card (white would vanish)
        static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

        /// <summary>Builds the popup under <paramref name="hud"/> and assigns its gameOverPanel / gameOverText /
        /// playAgainButton. <paramref name="cardSprite"/> should be 9-sliced (wave header in the top border).</summary>
        public static GameOverPopup BuildInto(HudView hud, Sprite cardSprite, Sprite buttonSprite, Sprite crownSprite)
        {
            const float w = 680f, h = 760f;
            var dim = Child(hud.transform, "GameOverPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.08f, 0.2f, 0.75f); // blocks taps on the board
            var v = dim.gameObject.AddComponent<GameOverPopup>();

            v.card = Child(dim, "Card", Half, Half, Vector2.zero, new Vector2(w, h));
            Img(v.card, cardSprite);

            var title = Txt(v.card, "Title", "GAME OVER", 76f, new Vector2(0f, h * 0.5f - 88f), new Vector2(w - 80f, 110f), Color.white);
            title.characterSpacing = 2f;
            Txt(v.card, "ScoreLabel", "YOUR SCORE", 40f, new Vector2(0f, 118f), new Vector2(560f, 60f), Ink);
            hud.gameOverText = Txt(v.card, "Score", "0", 120f, new Vector2(0f, 32f), new Vector2(560f, 140f), Ink);
            hud.gameOverFormat = "{0}";
            Txt(v.card, "BestLabel", "HIGH SCORE", 40f, new Vector2(0f, -62f), new Vector2(560f, 60f), Ink);
            var row = Child(v.card, "Best", Half, Half, new Vector2(0f, -122f), new Vector2(560f, 80f));
            v.best = Txt(row, "Value", "0", 64f, Vector2.zero, new Vector2(400f, 80f), Ink);
            if (crownSprite != null)
            {
                v.crown = Child(row, "Crown", Half, Half, new Vector2(0f, 4f), new Vector2(64f, 64f));
                Img(v.crown, crownSprite).preserveAspect = true;
            }

            float bw = 480f, bh = buttonSprite != null ? bw * buttonSprite.rect.height / buttonSprite.rect.width : 135f;
            var btnRt = Child(v.card, "PlayAgain", Half, Half, new Vector2(0f, -h * 0.5f + 125f), new Vector2(bw, bh));
            var btnImg = Img(btnRt, buttonSprite);
            btnImg.raycastTarget = true;
            var btn = btnRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            Txt(btnRt, "Label", "PLAY AGAIN", 56f, new Vector2(0f, 6f), new Vector2(460f, 120f), Color.white);
            hud.playAgainButton = btn;

            hud.gameOverPanel = dim.gameObject;
            dim.gameObject.SetActive(false);
            return v;
        }

        static RectTransform Child(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform rt, Sprite sprite)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        static TMP_Text Txt(RectTransform parent, string name, string text, float size, Vector2 pos, Vector2 dim, Color face)
        {
            var t = Child(parent, name, Half, Half, pos, dim).gameObject.AddComponent<TextMeshProUGUI>();
            TextStyle.Apply(t, face);
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.raycastTarget = false;
            return t;
        }
    }
}
