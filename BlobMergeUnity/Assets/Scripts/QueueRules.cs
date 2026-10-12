using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>Pure lane-queue rules for the Fish progression (the spawn table itself: <see cref="SpawnTable"/>) (covered by <see cref="SelfTests"/>).</summary>
    public static class QueueRules
    {
        /// <summary>Shifts a value rolled from the 1..16 odds table up by the spawn floor (2^WindowStart), capped at <paramref name="spawnMax"/>.</summary>
        public static int Scale(int rolled, int spawnMin, int spawnMax) => Mathf.Min(rolled * Mathf.Max(1, spawnMin), spawnMax);

        /// <summary>True when the newest <paramref name="maxSameInRow"/> spawns already equal <paramref name="v"/> (0 = rule off).</summary>
        public static bool Streak(int v, IReadOnlyList<int> recent, int maxSameInRow)
        {
            if (maxSameInRow <= 0) return false;
            int n = 0;
            for (int i = recent.Count - 1; i >= 0 && recent[i] == v; i--) n++;
            return n >= maxSameInRow;
        }
    }
}
