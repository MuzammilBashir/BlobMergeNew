using UnityEngine;

namespace BlobMerge
{
    /// <summary>Pure maths for the Fish creature bar (covered by <see cref="SelfTests"/>).</summary>
    public static class ProgressionMath
    {
        /// <summary>Highest creature tier (4096, the orca).</summary>
        public const int TopTier = 12;

        /// <summary>A creature never made before.</summary>
        public static bool IsNewFish(int tier, int highest) => tier > highest;

        /// <summary>
        /// Making the bar's last creature (<paramref name="windowStart"/> + size - 1) for the first time (above
        /// <paramref name="highest"/> = this game's biggest so far) shifts the bar one creature along, as long as a next
        /// creature exists (the window may not run past <see cref="TopTier"/>).
        /// </summary>
        public static bool ShiftDue(int tier, int highest, int windowStart, int windowSize) =>
            IsNewFish(tier, highest) && tier >= windowStart + windowSize - 1 && windowStart + windowSize <= TopTier;

        /// <summary>Smallest lane value while the bar starts at <paramref name="windowStart"/>: the dropped tiers never spawn.</summary>
        public static int SpawnFloor(int windowStart) => 1 << Mathf.Clamp(windowStart, 0, TopTier);

        /// <summary>Largest valid window start (the bar's last creature is the orca).</summary>
        public static int MaxWindowStart(int windowSize) => Mathf.Max(0, TopTier + 1 - windowSize);
    }
}
