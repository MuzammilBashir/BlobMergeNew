using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Fish game-over popup — its own prefab (Assets/UI/Prefabs/GameOverPopup_Fish.prefab, pointed at by
    /// BoardTheme.gameOverPrefab; older HUDs keep it baked in as a child instead): YOUR SCORE, HIGH SCORE (crown +
    /// number) and Play again. <see cref="Install"/> hands the score text and the button to the HUD, GameController
    /// hooks Play again up to Restart.
    /// On show it moves above the creature bar, copies the HUD's best and pops the card in (GameTime: game over freezes time).
    /// </summary>
    public class GameOverPopup : MonoBehaviour
    {
        [Tooltip("Pops in.")]
        public RectTransform card;
        [Tooltip("Gets the final score (through HudView.gameOverText / gameOverFormat).")]
        public TMP_Text score;
        [Tooltip("Restarts the run.")]
        public Button playAgain;
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

        /// <summary>Spawns <paramref name="prefab"/> under the HUD (replacing a popup baked into the HUD prefab) and
        /// points the HUD's gameOverPanel / gameOverText / playAgainButton at it, hidden. Nothing happens without a
        /// prefab, so a HUD with the popup baked in still works. Call before Play again gets its listener.</summary>
        public static void Install(HudView hud, GameOverPopup prefab)
        {
            if (hud == null || prefab == null) return;
            if (hud.gameOverPanel != null) Destroy(hud.gameOverPanel);
            var v = Instantiate(prefab, hud.transform, false);
            v.name = "GameOverPopup";
            v.Wire(hud);
        }

        /// <summary>Hands this popup's parts to the HUD and hides it.</summary>
        void Wire(HudView hud)
        {
            hud.gameOverPanel = gameObject;
            if (score != null) hud.gameOverText = score;
            if (playAgain != null) hud.playAgainButton = playAgain;
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ default layout (the prefab builder saves this)
        static readonly Color Ink = new Color32(0x14, 0x30, 0x6B, 0xFF); // body text on the white card (white would vanish)
        static readonly Vector2 Half = new Vector2(0.5f, 0.5f);

        /// <summary>Builds the popup under <paramref name="hud"/> and assigns its gameOverPanel / gameOverText /
        /// playAgainButton. <paramref name="cardSprite"/> should be 9-sliced (wave header in the top border).</summary>
        public static GameOverPopup BuildInto(HudView hud, Sprite cardSprite, Sprite buttonSprite, Sprite crownSprite)
        {
            var v = BuildDefault(hud.transform, cardSprite, buttonSprite, crownSprite);
            hud.gameOverFormat = "{0}";
            v.Wire(hud);
            return v;
        }

        /// <summary>The default card under <paramref name="parent"/>, self-contained (no HUD needed).</summary>
        public static GameOverPopup BuildDefault(Transform parent, Sprite cardSprite, Sprite buttonSprite, Sprite crownSprite)
        {
            const float w = 680f, h = 760f;
            var dim = Child(parent, "GameOverPopup", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0.02f, 0.08f, 0.2f, 0.75f); // blocks taps on the board
            var v = dim.gameObject.AddComponent<GameOverPopup>();

            v.card = Child(dim, "Card", Half, Half, Vector2.zero, new Vector2(w, h));
            Img(v.card, cardSprite);

            var title = Txt(v.card, "Title", "GAME OVER", 76f, new Vector2(0f, h * 0.5f - 88f), new Vector2(w - 80f, 110f), Color.white);
            title.characterSpacing = 2f;
            Txt(v.card, "ScoreLabel", "YOUR SCORE", 40f, new Vector2(0f, 118f), new Vector2(560f, 60f), Ink);
            v.score = Txt(v.card, "Score", "0", 120f, new Vector2(0f, 32f), new Vector2(560f, 140f), Ink);
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
            v.playAgain = btn;
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
