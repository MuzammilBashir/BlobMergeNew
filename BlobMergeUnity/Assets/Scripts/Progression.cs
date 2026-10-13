using System;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Fish scene progress (spec FISH_PROGRESSION.md §0/§0b): the creature bar shows the 11 tiers [WindowStart … WindowStart + 10].
    /// <see cref="Highest"/> = biggest creature ever made (saved in PlayerPrefs, not in ad takes; decides first-ever
    /// celebrations). <see cref="WindowStart"/> = smallest creature still in THIS game: every game starts at 0 and shifts
    /// when this game makes the bar's last creature (user decision 2026-10-12; never saved, a scene reload resets it).
    /// </summary>
    [Serializable]
    public class Progression
    {
        [Tooltip("Creature bar + lifetime progress (Fish scene only). Off = endless game like the other scenes.")]
        public bool enabled = true;
        [Tooltip("Creatures on the bar. Making the last one for the first time shifts the bar one creature along (the smallest stops spawning).")]
        [Range(2, 17)] public int windowSize = 11;

        /// <summary>The lane spawns 1/2/4, so the seahorse (tier 2) counts as already made.</summary>
        public const int StartHighest = 2;
        public const string DefaultKeyPrefix = "BlobMerge.Fish.";

        [NonSerialized] public int Highest = StartHighest;
        /// <summary>Per game (not saved): the bar's first creature.</summary>
        [NonSerialized] public int WindowStart;
        /// <summary>Per game: biggest creature made in this game (what the shift checks).</summary>
        [NonSerialized] public int GameHighest;
        /// <summary>PlayerPrefs keys: prefix + "Highest" (self-tests use their own prefix). "WindowStart" is the old v2 key,
        /// ignored and deleted.</summary>
        [NonSerialized] public string KeyPrefix = DefaultKeyPrefix;

        public string HighestKey => KeyPrefix + "Highest";
        public string WindowStartKey => KeyPrefix + "WindowStart";
        public int WindowSize => Mathf.Clamp(windowSize, 2, ProgressionMath.TopTier + 1);
        /// <summary>Last creature on the bar.</summary>
        public int WindowEnd => WindowStart + WindowSize - 1;

        public struct Events
        {
            public bool newFish; // a tier > Highest appeared (it is the new Highest): first-ever celebration
            public bool shift;   // the bar's last creature, first time this game: WindowStart moved up one
        }

        /// <summary>A value appeared (lane spawn or merge result). Updates Highest (saved by the caller), GameHighest and
        /// WindowStart (this game).</summary>
        public Events OnValue(int value)
        {
            int tier = Mathf.Min(GameController.Tier(value), ProgressionMath.TopTier); // 131072+ = whales too
            var e = new Events
            {
                newFish = ProgressionMath.IsNewFish(tier, Highest),
                shift = ProgressionMath.ShiftDue(tier, GameHighest, WindowStart, WindowSize),
            };
            if (e.newFish) Highest = tier;
            if (tier > GameHighest) GameHighest = tier;
            if (e.shift) WindowStart++;
            return e;
        }

        public void Load()
        {
            Highest = Mathf.Clamp(PlayerPrefs.GetInt(HighestKey, StartHighest), StartHighest, ProgressionMath.TopTier);
            if (PlayerPrefs.HasKey(WindowStartKey)) PlayerPrefs.DeleteKey(WindowStartKey); // old saved window: ignored
            NewGame();
        }

        /// <summary>A fresh game: the bar starts at the fry again (the lane spawns 1/2/4 unscaled).</summary>
        public void NewGame()
        {
            WindowStart = 0;
            GameHighest = 0;
        }

        public void Save()
        {
            PlayerPrefs.SetInt(HighestKey, Highest);
            PlayerPrefs.Save();
        }

        /// <summary>Forgets the saved progress (bot "-fishreset", self-tests).</summary>
        public void DeleteSaved()
        {
            PlayerPrefs.DeleteKey(HighestKey);
            PlayerPrefs.DeleteKey(WindowStartKey);
            PlayerPrefs.Save();
        }
    }
}
