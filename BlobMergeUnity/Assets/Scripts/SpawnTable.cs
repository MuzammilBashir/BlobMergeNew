using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// Pure maths for the Fish lane queue (spec FISH_PROGRESSION.md §0b, covered by <see cref="SelfTests"/>): the base
    /// spawn table driven by R (biggest tier made this game) and the sawtooth difficulty D the director refuses with.
    /// Mirrors the table mock's draw() (.superpowers/brainstorm/…/spawn-table.html).
    /// </summary>
    public static class SpawnTable
    {
        /// <summary>Shape of the base table (the mock's six sliders).</summary>
        public struct Shape
        {
            public int start;     // pool size at game start
            public int lag;       // top spawn = R - lag
            public int cap;       // max pool size
            public float decay;   // next tier up = decay × the one below
            public int fadeFrom;  // small-fade starts when R passes this tier
            public float fade;    // small-fade strength per tier below

            public static Shape Default => new Shape { start = 3, lag = 3, cap = 6, decay = 0.55f, fadeFrom = 6, fade = 0.6f };
        }

        /// <summary>Biggest tier that may spawn: clamp(max(ws+start-1, R-lag), ≤ ws+cap-1, ≤ orca).</summary>
        public static int PoolTop(int r, int windowStart, Shape s)
        {
            int top = Mathf.Max(windowStart + Mathf.Max(1, s.start) - 1, r - s.lag); // pool never empty
            return Mathf.Max(windowStart, Mathf.Min(top, windowStart + Mathf.Max(1, s.cap) - 1, ProgressionMath.TopTier));
        }

        /// <summary>
        /// Normalised spawn chance per tier (index = tier, 0..12; 0 outside [windowStart, PoolTop]).
        /// Weight(t) = decay^(t-ws) × fade^max(0, (R-fadeFrom)-t).
        /// </summary>
        public static float[] Weights(int r, int windowStart, Shape s, float[] into = null)
        {
            var w = into != null && into.Length == ProgressionMath.TopTier + 1 ? into : new float[ProgressionMath.TopTier + 1];
            System.Array.Clear(w, 0, w.Length);
            int top = PoolTop(r, windowStart, s);
            float sum = 0f;
            for (int t = windowStart; t <= top; t++)
            {
                w[t] = Mathf.Pow(s.decay, t - windowStart) * Mathf.Pow(s.fade, Mathf.Max(0, (r - s.fadeFrom) - t));
                sum += w[t];
            }
            if (sum > 0f) for (int t = 0; t < w.Length; t++) w[t] /= sum;
            return w;
        }

        /// <summary>
        /// Tier picked by <paramref name="u"/> in [0,1) from (unnormalised) weights, skipping <paramref name="excludeA"/> /
        /// <paramref name="excludeB"/> (-1 = none) unless nothing else is left. -1 if every weight is 0.
        /// </summary>
        public static int Sample(float[] w, float u, int excludeA = -1, int excludeB = -1)
        {
            float total = 0f;
            for (int t = 0; t < w.Length; t++) if (t != excludeA && t != excludeB) total += w[t];
            if (total <= 0f)
            {
                if (excludeA < 0 && excludeB < 0) return -1;
                return Sample(w, u); // only excluded tiers have weight: take one of them anyway
            }
            float x = Mathf.Clamp01(u) * total;
            int last = -1;
            for (int t = 0; t < w.Length; t++)
            {
                if (t == excludeA || t == excludeB || w[t] <= 0f) continue;
                last = t;
                if ((x -= w[t]) < 0f) return t;
            }
            return last; // u == 1 / rounding
        }

        /// <summary>Base difficulty for the biggest tier made this game: lerp(min, max, (R-2)/10).</summary>
        public static float Base(int r, float min, float max) => Mathf.Lerp(min, max, (r - 2) / 10f);

        /// <summary>
        /// Sawtooth difficulty: <paramref name="baseD"/> minus <paramref name="relaxDepth"/> for <paramref name="relaxSeconds"/>
        /// after the last new R, then a linear climb back to base over <paramref name="climbSeconds"/>; capped at
        /// <paramref name="graceMax"/> for the first <paramref name="graceSeconds"/> of a game. Result 0..1.
        /// </summary>
        public static float Difficulty(float baseD, float sinceNewR, float sinceStart, float relaxDepth, float relaxSeconds,
                                       float climbSeconds, float graceSeconds, float graceMax)
        {
            float climb = climbSeconds > 0f ? Mathf.Clamp01((sinceNewR - relaxSeconds) / climbSeconds) : 1f;
            float relax = sinceNewR < relaxSeconds ? 1f : 1f - climb;
            float d = Mathf.Clamp01(baseD - relaxDepth * relax);
            if (sinceStart < graceSeconds) d = Mathf.Min(d, graceMax);
            return d;
        }

        /// <summary>Spawns a lock lasts: round(lerp(min, max, D)).</summary>
        public static int LockLength(float d, int min, int max) => Mathf.RoundToInt(Mathf.Lerp(min, max, d));
    }
}
