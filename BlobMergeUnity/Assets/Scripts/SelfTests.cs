using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>
    /// "-blobselftest": checks the pure progression / queue maths inside a player build (this project has no Unity
    /// test runner, and the user's Editor must never be batch-opened). Logs "[BlobMerge] selftest PASS|FAIL name".
    /// </summary>
    public static class SelfTests
    {
        static int pass, fail;

        static void Check(string name, bool ok)
        {
            if (ok) pass++; else fail++;
            Debug.Log($"[BlobMerge] selftest {(ok ? "PASS" : "FAIL")} {name}");
        }

        public static void RunAll()
        {
            pass = fail = 0;
            MathTests();
            SpawnTableTests();
            ProgressionTests();
            Debug.Log($"[BlobMerge] selftest summary: pass={pass} fail={fail}");
        }

        static void MathTests()
        {
            Check("new fish only above the highest", ProgressionMath.IsNewFish(3, 2) && !ProgressionMath.IsNewFish(2, 2) &&
                                                     !ProgressionMath.IsNewFish(1, 5));
            Check("shift when the bar's last creature is new", ProgressionMath.ShiftDue(10, 9, 0, 11) &&
                                                             ProgressionMath.ShiftDue(11, 10, 1, 11));
            Check("no shift below the last creature", !ProgressionMath.ShiftDue(9, 8, 0, 11));
            Check("no shift when it is not new", !ProgressionMath.ShiftDue(10, 10, 0, 11));
            Check("no shift past the whale (tier-16 end)", !ProgressionMath.ShiftDue(16, 15, 6, 11) &&
                                                           ProgressionMath.MaxWindowStart(11) == 6);
            Check("spawn floor = 2^windowStart", ProgressionMath.SpawnFloor(0) == 1 && ProgressionMath.SpawnFloor(1) == 2 &&
                                                 ProgressionMath.SpawnFloor(2) == 4);

            Check("scale by spawn floor", QueueRules.Scale(4, 4, 64) == 16 && QueueRules.Scale(1, 2, 32) == 2);
            Check("scale capped at max", QueueRules.Scale(16, 2, 16) == 16);
            Check("scale floor 1 is identity", QueueRules.Scale(8, 1, 16) == 8);
            var recent = new List<int> { 4, 2, 2 };
            Check("streak blocks a third 2", QueueRules.Streak(2, recent, 2));
            Check("streak allows a 4", !QueueRules.Streak(4, recent, 2));
            Check("streak off at 0", !QueueRules.Streak(2, recent, 0));
        }

        /// <summary>Whole percent per tier, as the table mock prints it.</summary>
        static bool Row(float[] w, int from, params int[] pct)
        {
            for (int t = 0; t < w.Length; t++)
            {
                bool inPool = t >= from && t < from + pct.Length;
                if (inPool ? Mathf.RoundToInt(w[t] * 100f) != pct[t - from] : w[t] != 0f) return false;
            }
            return true;
        }

        static void SpawnTableTests()
        {
            var s = SpawnTable.Shape.Default;
            Check("pool new game = fry..seahorse", SpawnTable.PoolTop(2, 0, s) == 2 && SpawnTable.PoolTop(5, 0, s) == 2);
            Check("pool grows with R - lag", SpawnTable.PoolTop(6, 0, s) == 3 && SpawnTable.PoolTop(7, 0, s) == 4);
            Check("pool capped at ws + cap - 1", SpawnTable.PoolTop(9, 0, s) == 5 && SpawnTable.PoolTop(12, 2, s) == 7 &&
                                                 SpawnTable.PoolTop(10, 1, s) == 6);
            Check("pool follows the window start", SpawnTable.PoolTop(2, 2, s) == 4);
            var bad = s; bad.start = 0; bad.cap = 0;
            Check("pool never empty (start/cap clamped to 1)", SpawnTable.PoolTop(2, 0, bad) == 0 && SpawnTable.PoolTop(9, 3, bad) == 3 &&
                                                               SpawnTable.Weights(9, 3, bad)[3] == 1f);
            Check("table new game 54/30/16", Row(SpawnTable.Weights(2, 0, s), 0, 54, 30, 16));
            Check("table R=6 row 50/27/15/8", Row(SpawnTable.Weights(6, 0, s), 0, 50, 27, 15, 8));
            Check("table R=9 row 24/22/20/18/10/6", Row(SpawnTable.Weights(9, 0, s), 0, 24, 22, 20, 18, 10, 6));
            Check("table R=12 row (floor 2) 22/20/18/17/15/8", Row(SpawnTable.Weights(12, 2, s), 2, 22, 20, 18, 17, 15, 8));
            float sum = 0f; foreach (var x in SpawnTable.Weights(11, 1, s)) sum += x;
            Check("table normalised", Mathf.Abs(sum - 1f) < 1e-4f);
            var w = new float[13]; w[0] = 0.5f; w[1] = 0.3f; w[2] = 0.2f;
            Check("sample walks the weights", SpawnTable.Sample(w, 0f) == 0 && SpawnTable.Sample(w, 0.6f) == 1 &&
                                              SpawnTable.Sample(w, 0.95f) == 2 && SpawnTable.Sample(w, 1f) == 2);
            Check("sample skips excluded tiers", SpawnTable.Sample(w, 0f, 0) == 1 && SpawnTable.Sample(w, 0.1f, 0, 1) == 2);
            var w1 = new float[13]; w1[3] = 1f;
            Check("sample falls back when only excluded tiers remain", SpawnTable.Sample(w1, 0.5f, 3) == 3 &&
                                                                       SpawnTable.Sample(new float[13], 0.5f) == -1);
            Check("base difficulty 0.15..0.85", Mathf.Approximately(SpawnTable.Base(2, 0.15f, 0.85f), 0.15f) &&
                                                Mathf.Approximately(SpawnTable.Base(16, 0.15f, 0.85f), 0.85f) &&
                                                Mathf.Abs(SpawnTable.Base(9, 0.15f, 0.85f) - 0.5f) < 1e-5f);
            // R = 7 (base 0.5): relax 0.3 for 20 s, climb back over 40 s, grace 90 s at <= 0.1
            float D(float sinceR, float sinceStart) => SpawnTable.Difficulty(0.5f, sinceR, sinceStart, 0.3f, 20f, 40f, 90f, 0.1f);
            Check("grace caps D at 0.1", Mathf.Approximately(D(200f, 30f), 0.1f) && Mathf.Approximately(D(5f, 89f), 0.1f));
            Check("relief right after a new R", Mathf.Abs(D(0f, 300f) - 0.2f) < 1e-5f && Mathf.Abs(D(19.9f, 300f) - 0.2f) < 1e-5f);
            Check("climb back to base", Mathf.Abs(D(40f, 300f) - 0.35f) < 1e-5f && Mathf.Abs(D(60f, 300f) - 0.5f) < 1e-5f &&
                                        Mathf.Abs(D(500f, 300f) - 0.5f) < 1e-5f);
            Check("D clamped to 0..1", SpawnTable.Difficulty(0.1f, 0f, 300f, 0.3f, 20f, 40f, 90f, 0.1f) == 0f &&
                                       SpawnTable.Difficulty(1.4f, 999f, 999f, 0.3f, 20f, 40f, 90f, 0.1f) == 1f);
            Check("lock length 3..10", SpawnTable.LockLength(0f, 3, 10) == 3 && SpawnTable.LockLength(1f, 3, 10) == 10 &&
                                       SpawnTable.LockLength(0.6f, 3, 10) == 7 && SpawnTable.LockLength(0.2f, 3, 10) == 4);
        }

        static void ProgressionTests()
        {
            var p = new Progression { KeyPrefix = "BlobMerge.SelfTest." }; // test-only keys, deleted afterwards
            p.DeleteSaved();
            p.Load();
            Check("fresh progress: seahorse made, bar from fry", p.Highest == 2 && p.WindowStart == 0 && p.WindowEnd == 10);
            var e = p.OnValue(4);
            Check("known fish is no event", !e.newFish && !e.shift && p.Highest == 2);
            e = p.OnValue(8);
            Check("clownfish is a new fish", e.newFish && !e.shift && p.Highest == 3);
            e = p.OnValue(1024);
            Check("dolphin (11th) shifts the bar", e.newFish && e.shift && p.Highest == 10 && p.WindowStart == 1);
            e = p.OnValue(1024);
            Check("a second dolphin this game: no shift", !e.newFish && !e.shift && p.WindowStart == 1);
            e = p.OnValue(2048);
            Check("shark shifts again", e.newFish && e.shift && p.WindowStart == 2 && p.WindowEnd == 12);
            p.OnValue(4096); p.OnValue(8192); p.OnValue(16384);
            e = p.OnValue(32768);
            Check("narwhal shifts to the end", e.newFish && e.shift && p.WindowStart == 6 && p.WindowEnd == 16);
            e = p.OnValue(65536);
            Check("whale is new but never shifts", e.newFish && !e.shift && p.Highest == 16 && p.WindowStart == 6);
            e = p.OnValue(131072);
            Check("nothing above the whale", !e.newFish && !e.shift && p.Highest == 16 && p.WindowStart == 6);

            p.Save();
            PlayerPrefs.SetInt(p.WindowStartKey, 2); // an old v2 saved window
            var q = new Progression { KeyPrefix = p.KeyPrefix };
            q.Load();
            Check("next game: lifetime highest kept, bar back to the fry", q.Highest == 16 && q.WindowStart == 0 &&
                                                                           q.GameHighest == 0 &&
                                                                           PlayerPrefs.GetInt("BlobMerge.SelfTest.Highest") == 16);
            Check("old saved window key ignored and deleted", !PlayerPrefs.HasKey(q.WindowStartKey));
            e = q.OnValue(1024);
            Check("known dolphin shifts this game's bar (no new fish)", !e.newFish && e.shift && q.WindowStart == 1);
            q.NewGame();
            Check("new game resets the window", q.WindowStart == 0 && q.GameHighest == 0 && q.Highest == 16);
            Check("default key", new Progression().HighestKey == "BlobMerge.Fish.Highest");
            q.DeleteSaved();
        }
    }
}
