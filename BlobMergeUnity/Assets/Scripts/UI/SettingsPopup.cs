using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// The Settings card (Assets/Resources/UI/Settings.prefab), opened by the HUD's gear: a Sound and a Haptics toggle
    /// and Continue. A toggle's icon stays put; while its setting is off the "Disable" slash over it is shown, clipped
    /// to the round button by the Mask on the button. Opening freezes the game (<see cref="GameController.TryPauseForSettings"/>),
    /// Continue resumes it. The values live in <see cref="GameSettings"/>.
    /// </summary>
    public class SettingsPopup : MonoBehaviour
    {
        [Tooltip("Pops in.")]
        public RectTransform card;
        public Button soundButton, hapticsButton;
        [Tooltip("Closes the card and resumes play.")]
        public Button closeButton;
        [Tooltip("The Disable slash, shown while Sound / Haptics is off.")]
        public GameObject soundOff, hapticsOff;
        [Header("Pop-in")]
        public float popDuration = 0.45f;
        public float popFrom = 0.6f;

        GameController game;
        float t = -1f;

        /// <summary>Spawns the prefab under the HUD, hidden, and makes the HUD's gear open it. Nothing happens without a
        /// gear or a prefab, so a HUD without them still works.</summary>
        public static SettingsPopup Install(GameController game, HudView hud)
        {
            if (hud == null || hud.settingsButton == null) return null;
            var prefab = Resources.Load<SettingsPopup>("UI/Settings");
            if (prefab == null) return null;
            var v = Instantiate(prefab, hud.transform, false);
            v.name = "SettingsPopup";
            v.game = game;
            if (v.soundButton != null) v.soundButton.onClick.AddListener(() => { GameSettings.SoundOn = !GameSettings.SoundOn; v.Refresh(); });
            if (v.hapticsButton != null) v.hapticsButton.onClick.AddListener(() => { GameSettings.HapticsOn = !GameSettings.HapticsOn; v.Refresh(); });
            if (v.closeButton != null) v.closeButton.onClick.AddListener(v.Close);
            hud.settingsButton.onClick.AddListener(v.Open);
            v.gameObject.SetActive(false);
            return v;
        }

        void Open()
        {
            if (game != null && !game.TryPauseForSettings()) return;
            gameObject.SetActive(true);
            if (Sfx.Current != null) Sfx.Current.PopUp(); // here, not OnEnable: Install briefly enables the instance at startup
        }

        void Close()
        {
            gameObject.SetActive(false);
            if (game != null) game.ResumeFromSettings();
        }

        void OnEnable()
        {
            t = 0f;
            transform.SetAsLastSibling(); // over the creature bar
            Refresh();
            Update();
        }

        void Refresh()
        {
            if (soundOff != null) soundOff.SetActive(!GameSettings.SoundOn);
            if (hapticsOff != null) hapticsOff.SetActive(!GameSettings.HapticsOn);
        }

        void Update()
        {
            if (t < 0f || card == null) return;
            t += GameTime.UnscaledDelta; // the game is frozen while this is open
            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, popDuration));
            card.localScale = Vector3.one * Mathf.LerpUnclamped(popFrom, 1f, LevelCompleteView.Bezier(0.34f, 1.56f, 0.64f, 1f, u));
            if (u >= 1f) t = -1f;
        }
    }
}
