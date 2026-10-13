using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// The player's Sound and Haptics switches (Settings popup), saved in PlayerPrefs, both on by default.
    /// Sound mutes the whole mix through the AudioListener, so the ambience loop goes quiet too; haptics are checked
    /// by <see cref="Sfx"/> before every vibration.
    /// </summary>
    public static class GameSettings
    {
        const string SoundKey = "blobmerge.sound", HapticsKey = "blobmerge.haptics";
        static int sound = -1, haptics = -1; // -1 = not read yet

        public static bool SoundOn
        {
            get { if (sound < 0) sound = PlayerPrefs.GetInt(SoundKey, 1); return sound == 1; }
            set
            {
                sound = value ? 1 : 0;
                PlayerPrefs.SetInt(SoundKey, sound);
                PlayerPrefs.Save();
                ApplySound();
            }
        }

        public static bool HapticsOn
        {
            get { if (haptics < 0) haptics = PlayerPrefs.GetInt(HapticsKey, 1); return haptics == 1; }
            set
            {
                haptics = value ? 1 : 0;
                PlayerPrefs.SetInt(HapticsKey, haptics);
                PlayerPrefs.Save();
            }
        }

        // before the first scene: restores the saved mute (and re-reads the prefs, in case Enter Play Mode skips the domain reload)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            sound = haptics = -1;
            ApplySound();
        }

        static void ApplySound() => AudioListener.volume = SoundOn ? 1f : 0f;
    }
}
