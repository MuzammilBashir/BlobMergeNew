# Fish Progression & Queue Balancing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Fish scene 8 levels with a goal fish (Octopus 32 … new 4096 creature), a lower-HUD progress bar
(current > next > goal), a level-complete / retry flow, and a fairer lane queue (spawn range per level, no streaks, pity).

**Architecture:** Pure maths lives in two static classes (`ProgressionMath`, `QueueRules`) that a player-side self-test
checks (`-blobselftest`), because this project has no Unity test runner and the user's Editor must never be batch-opened.
`Progression` (serializable tunables + level state) hangs off `GameController` like `LaneSpawner` does; everything is gated
by `GameController.ProgressionOn` (= Fish scene + `progression.enabled`), so Game/Hexa behave exactly as before. The bar
(`ProgressBarView`) is built at runtime under the HUD canvas; level change = scene reload with the level in PlayerPrefs.

**Tech Stack:** Unity 6000.0.67f1, C# (LF line endings), uGUI + TextMeshPro, Python 3 + Pillow (placeholder sprites),
PowerShell test scripts in `F:\Projects\BlobMerge\Tools\`.

**Spec:** `BlobMergeUnity/Docs/FISH_PROGRESSION.md` (read it first).

## Global Constraints

- Fish scene only: Game and Hexa must not change behaviour. New logic runs only when `GameController.ProgressionOn`.
- Never batch-open `BlobMergeUnity/`; build/test only through `Tools\test.ps1` (mirror build). Don't touch
  `F:\Projects\UnityProjects\BlobMerge`.
- Never edit `Assets/Resources/UI/HUD*.prefab`, `Assets/UI/Sprites/*.png`, `Assets/Resources/Fonts/Barlow-Bold Outline.mat`.
- New tunables get code defaults; changed semantics → new field name (old values persist in the user's scenes).
- All text: TMP Barlow Bold, white, thin dark-blue outline → always style through `TextStyle.Apply`.
- Never use `Time.unscaled*` in visuals — use `GameTime.UnscaledDelta`.
- C# files are LF. PowerShell `Set-Content -Encoding utf8` adds a BOM — write files with the Write/Edit tools.
- The AdSim director sets `lane.ValueOverride`; when it is set, queue rules and level completion are off (ads unchanged).
- Verify with the bot (log numbers + screenshots) before calling a task done. Not a git repo: no commit steps.

**Commands used throughout** (PowerShell):
- Build + run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs <s> -scene Fish -extra '<flag>','<value>'`
- Replay last build: add `-nobuild`
- Read results: `Select-String F:\Projects\BlobMerge\_testout\player.log -Pattern '<text>' | % Line`
- Compile errors show in `F:\Projects\BlobMerge\_testout\wbuild.log` (test.ps1 prints `error CS` lines).
- **Before Task 1 — baseline:** `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 60 -scene Game`, then copy
  `_testout\player.log` to `_testout\baseline_game.log`; same for `-scene Hexa` → `baseline_hexa.log` (Task 7 compares).

## File map

| File | Responsibility |
|---|---|
| Create `Assets/Scripts/ProgressionMath.cs` | Pure bar/level maths |
| Create `Assets/Scripts/QueueRules.cs` | Pure queue fairness maths |
| Create `Assets/Scripts/SelfTests.cs` | `-blobselftest` checks, logs PASS/FAIL |
| Create `Assets/Scripts/Progression.cs` | `FishLevel` table, level state, save/load |
| Create `Assets/Scripts/UI/ProgressBarView.cs` | Lower-HUD bar |
| Create `Assets/Resources/Fish/fish_10..12.png` (+ `.meta`) | Placeholder creatures |
| Create `Tools/make_placeholder_fish.py` | Generates the placeholders |
| Modify `Assets/Scripts/GameController.cs` | Fields, Start, hooks, win flow, radius curve, camera space |
| Modify `Assets/Scripts/LaneSpawner.cs` | Spawn scaling, streak, pity, `Spawned` event |
| Modify `Assets/Scripts/UI/HudView.cs` | `ShowLevelComplete` |
| Modify `Assets/Scripts/DebugAutopilot.cs` | `-blobselftest`, `-level`, `-blobcontinue`, `-blobfilltest`, logs |
| Modify `Assets/Themes/BoardTheme_Fish.asset` | Append 3 piece colours |
| Modify `Docs/FISH_PROGRESSION.md`, `Docs/AGENT_GUIDE.md` | Measured numbers, how-to |

---

### Task 1: Pure maths + self-test harness

**Files:**
- Create: `Assets/Scripts/ProgressionMath.cs`, `Assets/Scripts/QueueRules.cs`, `Assets/Scripts/SelfTests.cs`
- Modify: `Assets/Scripts/DebugAutopilot.cs` (`Awake`, first lines)

**Interfaces:**
- Produces: `ProgressionMath.Upcoming(int current, int goal) → int`, `ProgressionMath.Fill(int points, int upcoming, float factor, float cap) → float`,
  `ProgressionMath.LevelIndex(int level, int count) → int`; `QueueRules.Scale(int rolled, int spawnMin, int spawnMax) → int`,
  `QueueRules.Streak(int v, IReadOnlyList<int> recent, int maxSameInRow) → bool`, `QueueRules.Alternative(int v, int min, int max) → int`,
  `QueueRules.HasMatch(int v, IReadOnlyList<int> present) → bool`, `QueueRules.PityValue(IReadOnlyList<int> present, int min, int max) → int`;
  `SelfTests.RunAll()`.

- [ ] **Step 1: Write the failing self-tests** — create `Assets/Scripts/SelfTests.cs`:

```csharp
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
            Debug.Log($"[BlobMerge] selftest summary: pass={pass} fail={fail}");
        }

        static void MathTests()
        {
            Check("upcoming of nothing is 1", ProgressionMath.Upcoming(0, 32) == 1);
            Check("upcoming doubles", ProgressionMath.Upcoming(4, 32) == 8);
            Check("upcoming stops at goal", ProgressionMath.Upcoming(32, 32) == 32 && ProgressionMath.Upcoming(16, 32) == 32);
            Check("fill ratio", Mathf.Approximately(ProgressionMath.Fill(8, 8, 2f, 0.95f), 0.5f));
            Check("fill capped before made", Mathf.Approximately(ProgressionMath.Fill(100, 8, 2f, 0.95f), 0.95f));
            Check("level index clamps", ProgressionMath.LevelIndex(0, 8) == 0 && ProgressionMath.LevelIndex(3, 8) == 2 &&
                                        ProgressionMath.LevelIndex(12, 8) == 7);

            Check("scale by spawn min", QueueRules.Scale(4, 4, 64) == 16);
            Check("scale capped at max", QueueRules.Scale(16, 2, 16) == 16);
            Check("scale min 1 is identity", QueueRules.Scale(8, 1, 8) == 8);
            var recent = new List<int> { 4, 2, 2 };
            Check("streak blocks a third 2", QueueRules.Streak(2, recent, 2));
            Check("streak allows a 4", !QueueRules.Streak(4, recent, 2));
            Check("streak off at 0", !QueueRules.Streak(2, recent, 0));
            Check("alternative doubles", QueueRules.Alternative(2, 1, 16) == 4);
            Check("alternative halves at max", QueueRules.Alternative(16, 1, 16) == 8);
            var present = new List<int> { 32, 8, 4, 128 };
            Check("match found", QueueRules.HasMatch(8, present) && !QueueRules.HasMatch(2, present));
            Check("pity picks smallest in range", QueueRules.PityValue(present, 1, 64) == 4);
            Check("pity respects min", QueueRules.PityValue(present, 8, 64) == 8);
            Check("pity none in range", QueueRules.PityValue(new List<int> { 128 }, 1, 64) == 0);
        }
    }
}
```

In `DebugAutopilot.Awake()`, directly after `var a = System.Environment.GetCommandLineArgs();` add:

```csharp
            if (System.Array.IndexOf(a, "-blobselftest") >= 0)
            {
                SelfTests.RunAll();
                enabled = false;
                Application.Quit();
                return;
            }
```

- [ ] **Step 2: Build to verify it fails**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 5 -scene Fish -extra '-blobselftest'`
Expected: `error CS0103: The name 'ProgressionMath' does not exist` (and `QueueRules`) in the printed build lines.

- [ ] **Step 3: Implement** — create `Assets/Scripts/ProgressionMath.cs`:

```csharp
using UnityEngine;

namespace BlobMerge
{
    /// <summary>Pure maths for the Fish progression bar and level table (covered by <see cref="SelfTests"/>).</summary>
    public static class ProgressionMath
    {
        /// <summary>The next fish above <paramref name="current"/> (values are powers of two), never past the goal.</summary>
        public static int Upcoming(int current, int goal)
        {
            if (current <= 0) return 1;
            return current >= goal ? goal : Mathf.Min(goal, current * 2);
        }

        /// <summary>Bar fill: merge points / (next value × factor), held below <paramref name="cap"/> until the fish exists.</summary>
        public static float Fill(int points, int upcoming, float factor, float cap)
        {
            if (upcoming <= 0 || factor <= 0f) return 0f;
            return Mathf.Min(cap, points / (upcoming * factor));
        }

        /// <summary>1-based level → table index; levels past the end replay the last entry.</summary>
        public static int LevelIndex(int level, int count) => Mathf.Clamp(level - 1, 0, Mathf.Max(0, count - 1));
    }
}
```

Create `Assets/Scripts/QueueRules.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace BlobMerge
{
    /// <summary>Pure lane-queue fairness rules for the Fish progression (covered by <see cref="SelfTests"/>).</summary>
    public static class QueueRules
    {
        /// <summary>Shifts a value rolled from the 1..16 odds table up by the level's smallest spawn, capped at its largest.</summary>
        public static int Scale(int rolled, int spawnMin, int spawnMax) => Mathf.Min(rolled * Mathf.Max(1, spawnMin), spawnMax);

        /// <summary>True when the newest <paramref name="maxSameInRow"/> spawns already equal <paramref name="v"/> (0 = rule off).</summary>
        public static bool Streak(int v, IReadOnlyList<int> recent, int maxSameInRow)
        {
            if (maxSameInRow <= 0) return false;
            int n = 0;
            for (int i = recent.Count - 1; i >= 0 && recent[i] == v; i--) n++;
            return n >= maxSameInRow;
        }

        /// <summary>A neighbouring value to break a streak: double it, or halve it when doubling leaves the range.</summary>
        public static int Alternative(int v, int min, int max) => v * 2 <= max ? v * 2 : Mathf.Max(min, v / 2);

        public static bool HasMatch(int v, IReadOnlyList<int> present)
        {
            for (int i = 0; i < present.Count; i++) if (present[i] == v) return true;
            return false;
        }

        /// <summary>Smallest value present (lane + tank) within [min, max]; 0 if none.</summary>
        public static int PityValue(IReadOnlyList<int> present, int min, int max)
        {
            int best = 0;
            for (int i = 0; i < present.Count; i++)
            {
                int p = present[i];
                if (p >= min && p <= max && (best == 0 || p < best)) best = p;
            }
            return best;
        }
    }
}
```

- [ ] **Step 4: Run the self-tests**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 5 -scene Fish -extra '-blobselftest'` then
`Select-String F:\Projects\BlobMerge\_testout\player.log -Pattern 'selftest' | % Line`
Expected: every line `PASS`, last line `selftest summary: pass=18 fail=0`.

---

### Task 2: Progression state + level start on `GameController`

**Files:**
- Create: `Assets/Scripts/Progression.cs`
- Modify: `Assets/Scripts/GameController.cs` (fields after `public LaneSpawner lane`, new `Start()`, `ProgressionOn`)
- Modify: `Assets/Scripts/SelfTests.cs`, `Assets/Scripts/DebugAutopilot.cs`

**Interfaces:**
- Consumes: `ProgressionMath.*` (Task 1).
- Produces: `struct FishLevel { int goalValue, spawnMin, spawnMax }`; `class Progression` with `enabled`, `levels`,
  `barTargetFactor`, `barCapBeforeMade`, `Level`, `Current`, `Points`, `Complete`, `Def`, `Goal`, `Upcoming`, `Fill`,
  `RawFill`, `Begin(int level)`, `OnValue(int value, bool fromMerge) → bool record`, `static SavedLevel`, `static Save(int)`;
  `GameController.progression`, `GameController.ProgressionOn`, `GameController.StartLevelOverride`.

- [ ] **Step 1: Write the failing tests** — in `SelfTests.RunAll()` add `ProgressionTests();` after `MathTests();` and add:

```csharp
        static void ProgressionTests()
        {
            var p = new Progression();
            p.Begin(1);
            Check("level 1 goal is 32", p.Goal == 32);
            Check("first spawn is a record", p.OnValue(1, false) && p.Current == 1 && p.Upcoming == 2);
            p.OnValue(1, false);
            Check("spawns give no points", p.Points == 0);
            p.OnValue(2, true);
            Check("merge record resets points", p.Current == 2 && p.Points == 0);
            p.OnValue(2, true);
            Check("merge adds its value", p.Points == 2);
            Check("fill = points / (next x factor)", Mathf.Approximately(p.Fill, 2f / (4f * 2f)));
            p.OnValue(32, true);
            Check("goal completes the level", p.Complete && Mathf.Approximately(p.Fill, 1f));
            Check("nothing counts after complete", !p.OnValue(64, true) && p.Current == 32);
            p.Begin(20);
            Check("levels past the end replay the last", p.Goal == 4096 && p.Def.spawnMin == 4);
        }
```

- [ ] **Step 2: Build to verify it fails**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 5 -scene Fish -extra '-blobselftest'`
Expected: `error CS0246: The type or namespace name 'Progression' could not be found`.

- [ ] **Step 3: Implement** — create `Assets/Scripts/Progression.cs`:

```csharp
using System;
using UnityEngine;

namespace BlobMerge
{
    [Serializable]
    public struct FishLevel
    {
        [Tooltip("Create this value to win the level (32 = octopus … 512 = whale … 4096).")]
        public int goalValue;
        [Tooltip("Smallest lane value; the odds table (1..16) is multiplied by it. 1, 2, 4 …")]
        public int spawnMin;
        [Tooltip("Largest lane value (bigger rolls are capped to this).")]
        public int spawnMax;
        public FishLevel(int goal, int min, int max) { goalValue = goal; spawnMin = min; spawnMax = max; }
    }

    /// <summary>
    /// Fish scene levels: a goal fish per level, the biggest fish made so far, and merge points toward the next one
    /// (drives the lower-HUD bar). Tunables live on the Game object; runtime state resets on every scene load.
    /// </summary>
    [Serializable]
    public class Progression
    {
        [Tooltip("Levels with a goal fish (Fish scene only). Off = endless game like the other scenes.")]
        public bool enabled = true;
        [Tooltip("Goal + lane value range per level. Levels past the end replay the last one.")]
        public FishLevel[] levels =
        {
            new FishLevel(32, 1, 8), new FishLevel(64, 1, 8), new FishLevel(128, 1, 16), new FishLevel(256, 1, 16),
            new FishLevel(512, 2, 16), new FishLevel(1024, 2, 32), new FishLevel(2048, 4, 32), new FishLevel(4096, 4, 64),
        };
        [Tooltip("Merge points needed for the next fish = its value × this (bar fill).")]
        [Range(0.5f, 4f)] public float barTargetFactor = 2f;
        [Tooltip("The bar stops here until the next fish really exists, then snaps full.")]
        [Range(0.5f, 1f)] public float barCapBeforeMade = 0.95f;

        public const string LevelKey = "BlobMerge.Fish.Level";

        [NonSerialized] public int Level = 1;
        [NonSerialized] public int Current;   // biggest value seen this level (spawns + merges)
        [NonSerialized] public int Points;    // merge points since Current was reached
        [NonSerialized] public bool Complete;

        public FishLevel Def => levels[ProgressionMath.LevelIndex(Level, levels.Length)];
        public int Goal => Def.goalValue;
        public int Upcoming => ProgressionMath.Upcoming(Current, Goal);
        public float Fill => Complete ? 1f : ProgressionMath.Fill(Points, Upcoming, barTargetFactor, barCapBeforeMade);
        /// <summary>Uncapped fill, for calibrating <see cref="barTargetFactor"/>.</summary>
        public float RawFill => ProgressionMath.Fill(Points, Upcoming, barTargetFactor, float.MaxValue);

        public void Begin(int level)
        {
            Level = Mathf.Max(1, level);
            Current = 0;
            Points = 0;
            Complete = false;
        }

        /// <summary>A value appeared (lane spawn or merge result). Returns true when it is a new biggest fish.</summary>
        public bool OnValue(int value, bool fromMerge)
        {
            if (Complete) return false;
            if (value > Current)
            {
                Current = value;
                Points = 0;
                if (Current >= Goal) Complete = true;
                return true;
            }
            if (fromMerge) Points += value;
            return false;
        }

        public static int SavedLevel => Mathf.Max(1, PlayerPrefs.GetInt(LevelKey, 1));
        public static void Save(int level) { PlayerPrefs.SetInt(LevelKey, level); PlayerPrefs.Save(); }
    }
}
```

In `GameController.cs`, after `public LaneSpawner lane = new LaneSpawner();` add:

```csharp

        [Header("Progression (Fish scene)")]
        public Progression progression = new Progression();
```

After `public bool FishMode => …;` add:

```csharp
        /// <summary>Levels + progress bar: Fish scene with progression enabled. Other scenes never run any of it.</summary>
        public bool ProgressionOn => FishMode && progression.enabled && progression.levels != null && progression.levels.Length > 0;
        /// <summary>Queue rules and level wins: off while the ad director scripts the lane.</summary>
        public bool ProgressionRules => ProgressionOn && lane.ValueOverride == null;
        /// <summary>Bot "-level n": play this level instead of the saved one (set before Start).</summary>
        [System.NonSerialized] public int StartLevelOverride;
        /// <summary>(level) — fired from Start when a Fish level begins.</summary>
        public event System.Action<int> LevelStarted;
```

After `Awake()` add:

```csharp
        void Start()
        {
            if (!ProgressionOn) return;
            int level = StartLevelOverride > 0 ? StartLevelOverride : Progression.SavedLevel;
            progression.Begin(level);
            LevelStarted?.Invoke(level);
        }
```

In `DebugAutopilot`: add field `static bool levelArgUsed;` and in `Awake()` (after the `-hextex` loop) add:

```csharp
            for (int i = 0; i < a.Length - 1; i++)
                if (a[i] == "-level" && !levelArgUsed && int.TryParse(a[i + 1], out int lv))
                {
                    GetComponent<GameController>().StartLevelOverride = lv; // first scene load only; later levels follow the save
                    levelArgUsed = true;
                }
```

`GameController.Start` may run before `DebugAutopilot.Start` (so subscribing to `LevelStarted` there can miss it);
log from `DebugAutopilot.Update` instead: add field `bool levelLogged;` and at the top of `Update()` (after `float t = …`):

```csharp
            if (!levelLogged && game.ProgressionOn)
            {
                levelLogged = true;
                Debug.Log($"[BlobMerge] level {game.progression.Level} start goal={game.progression.Goal} " +
                          $"range={game.progression.Def.spawnMin}-{game.progression.Def.spawnMax}");
            }
```
(`LevelStarted` stays as a public event for other listeners.)

- [ ] **Step 4: Run the self-tests and a level start**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 5 -scene Fish -extra '-blobselftest'` → `pass=27 fail=0`.
Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -nobuild -secs 10 -scene Fish -extra '-level','3'` then
`Select-String F:\Projects\BlobMerge\_testout\player.log -Pattern 'level ' | % Line`
Expected: `[BlobMerge] level 3 start goal=128 range=1-16`.
Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -nobuild -secs 10 -scene Game` → no `level` line, no exceptions.

---

### Task 3: Lane queue — spawn range, no streaks, pity

**Files:**
- Modify: `Assets/Scripts/LaneSpawner.cs` (fields, `Tick`, `NextValue`)
- Modify: `Assets/Scripts/DebugAutopilot.cs` (queue stats in the summary)

**Interfaces:**
- Consumes: `QueueRules.*` (Task 1), `GameController.ProgressionRules`, `progression.Def` (Task 2).
- Produces: `LaneSpawner.maxSameInRow`, `pityAfter`, `pityMaxValue`, `[NonSerialized] Action<int> Spawned`,
  `[NonSerialized] int PitySpawns, StreakRerolls`.

- [ ] **Step 1: Add the queue check to the bot (fails until the rules exist)** — in `DebugAutopilot` add fields
`readonly List<int> spawnSeq = new List<int>();` (add `using System.Collections.Generic;` if missing) and in `Start()`:

```csharp
            game.lane.Spawned += v => spawnSeq.Add(v);
```

Add a helper and call `LogQueue();` right after the existing `summary:` `Debug.Log` in `Update()`:

```csharp
        void LogQueue()
        {
            int maxRun = 0, run = 0;
            for (int i = 0; i < spawnSeq.Count; i++)
            {
                run = i > 0 && spawnSeq[i] == spawnSeq[i - 1] ? run + 1 : 1;
                maxRun = Mathf.Max(maxRun, run);
            }
            Debug.Log($"[BlobMerge] queue: spawns={spawnSeq.Count} maxRun={maxRun} pity={game.lane.PitySpawns} " +
                      $"rerolls={game.lane.StreakRerolls} seq={string.Join(",", spawnSeq)}");
        }
```

- [ ] **Step 2: Build to verify it fails**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 5 -scene Fish`
Expected: `error CS1061: 'LaneSpawner' does not contain a definition for 'Spawned'`.

- [ ] **Step 3: Implement** — in `LaneSpawner.cs`, after the `weight16…` line add:

```csharp

        [Header("Fairness (Fish progression only)")]
        [Tooltip("Never spawn the same value more than this many times in a row (0 = off).")]
        public int maxSameInRow = 2;
        [Tooltip("After this many spawns in a row that match nothing in the lane or tank, the next one is a guaranteed match (0 = off).")]
        public int pityAfter = 3;
        [Tooltip("Largest value a guaranteed match may be (also capped by the level's spawn range).")]
        public int pityMaxValue = 64;
```

After `[NonSerialized] public Func<bool> SpawnGate;` add:

```csharp
        /// <summary>Every lane spawn's value (bot statistics).</summary>
        [NonSerialized] public Action<int> Spawned;
        [NonSerialized] public int PitySpawns, StreakRerolls;
        readonly List<int> recent = new List<int>();
        readonly List<int> present = new List<int>();
        int dry; // spawns in a row that matched nothing on the board
```

In `Init` add `PitySpawns = StreakRerolls = dry = 0; recent.Clear();`.

In `Tick`, replace

```csharp
            int value = startIndex < startValues.Length ? startValues[startIndex++] : NextValue();
```
with
```csharp
            int value;
            if (startIndex < startValues.Length)
            {
                value = startValues[startIndex++];
                if (game.ProgressionRules) value = QueueRules.Scale(value, game.progression.Def.spawnMin, game.progression.Def.spawnMax);
            }
            else value = NextValue(blobs);
            Spawned?.Invoke(value);
```

Replace the whole `NextValue()` method with:

```csharp
        int NextValue(List<Blob> blobs)
        {
            if (ValueOverride != null)
            {
                int ov = ValueOverride();
                if (ov > 0) return ov;
            }
            int v = Roll();
            if (!game.ProgressionRules) return v;

            var def = game.progression.Def;
            v = QueueRules.Scale(v, def.spawnMin, def.spawnMax);
            present.Clear();
            foreach (var b in blobs)
                if (b.State == BlobState.Lane || b.State == BlobState.Aiming || b.State == BlobState.Free) present.Add(b.Value);

            if (QueueRules.Streak(v, recent, maxSameInRow)) { v = QueueRules.Alternative(v, def.spawnMin, def.spawnMax); StreakRerolls++; }
            if (pityAfter > 0 && dry >= pityAfter)
            {
                int p = QueueRules.PityValue(present, def.spawnMin, Mathf.Min(pityMaxValue, def.spawnMax));
                if (p > 0) { v = p; PitySpawns++; } // a guaranteed match wins over the streak rule
            }
            dry = QueueRules.HasMatch(v, present) ? 0 : dry + 1;
            recent.Add(v);
            if (recent.Count > 8) recent.RemoveAt(0);
            return v;
        }

        /// <summary>The original shot-based odds (1..16).</summary>
        int Roll()
        {
            int s = Shots;
            float w1 = Mathf.Max(weight1Min, weight1Start - weight1Drop * s);
            float w8 = s >= eightAfterShots ? Mathf.Min(weight8Max, weight8PerShot * (s - eightAfterShots + 1)) : 0f;
            float w16 = s >= sixteenAfterShots ? Mathf.Min(weight16Max, weight16PerShot * (s - sixteenAfterShots + 1)) : 0f;
            float total = w1 + weight2 + weight4 + w8 + w16;
            float r = UnityEngine.Random.value * total;
            if ((r -= w1) < 0) return 1;
            if ((r -= weight2) < 0) return 2;
            if ((r -= weight4) < 0) return 4;
            if ((r -= w8) < 0) return 8;
            return 16;
        }
```

- [ ] **Step 4: Verify with the bot**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 90 -scene Fish -extra '-level','8'` then
`Select-String F:\Projects\BlobMerge\_testout\player.log -Pattern 'queue:|level ' | % Line`
Expected: `level 8 start goal=4096 range=4-64`; `queue:` with every value in 4..64, first three `4,8,16`, `maxRun<=2`
(a 3 is allowed only right after a pity spawn), `pity>=1` over 90 s.
Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -nobuild -secs 60 -scene Fish -extra '-level','1'` → values 1..8 only.
Run the Game scene 30 s → `queue: … pity=0 rerolls=0`, values 1..16 as before.

---

### Task 4: Creature ladder to 4096 + big-fish size curve

**Files:**
- Create: `F:\Projects\BlobMerge\Tools\make_placeholder_fish.py`, `Assets/Resources/Fish/fish_10.png`, `fish_11.png`, `fish_12.png` (+ `.meta`)
- Modify: `Assets/Scripts/GameController.cs` (`FishSprite`, `RadiusForTier`, new fields in "Fish bubble")
- Modify: `Assets/Themes/BoardTheme_Fish.asset` (`pieceColors`, append 3 lines)
- Modify: `Assets/Scripts/DebugAutopilot.cs` (`-blobfilltest`)

**Interfaces:**
- Produces: `GameController.fishRadiusPerTier`, `GameController.fishBigRadiusPerTier`; `FishSprite(tier)` valid for tiers 0..12.

- [ ] **Step 1: Add the fill test (the failing check)** — in `DebugAutopilot` add fields `bool fillTest, fillLogged; int fillSpawned;`.
In `Awake()` next to `sagTest = …` add `fillTest = System.Array.IndexOf(a, "-blobfilltest") >= 0;` and next to
`if (sagTest) duration = 4f;` add `if (fillTest) duration = 10f;`. In `Update()` after the `sagTest` line add
`if (fillTest) { FillTest(t); return; }` and add:

```csharp
        /// <summary>"-blobfilltest": drops the whole 2048…1 chain (everything a 4096 attempt must hold) and measures it.</summary>
        void FillTest(float t)
        {
            var bd = game.Board;
            while (fillSpawned < 12 && t > 0.5f + fillSpawned * 0.35f)
            {
                int v = 2048 >> fillSpawned;
                float x = ((fillSpawned % 3) - 1) * bd.HalfW * 0.5f;
                game.SpawnFree(v, new Vector2(x, bd.DangerY + 0.3f));
                fillSpawned++;
            }
            if (!fillLogged && t > 8.5f)
            {
                fillLogged = true;
                float covered = 0f, top = float.MinValue;
                foreach (var b in game.Blobs)
                {
                    if (b.State != BlobState.Free) continue;
                    covered += b.Body.Area();
                    top = Mathf.Max(top, b.Body.Max.y);
                }
                float field = 2f * bd.HalfW * (bd.DangerY - bd.FloorY) - game.chamfer * game.chamfer;
                Debug.Log($"[BlobMerge] fill test: covered={covered / field:P0} top={top:F2} danger={bd.DangerY:F2} fits={top < bd.DangerY}");
                if (!string.IsNullOrEmpty(shotPrefix)) ScreenCapture.CaptureScreenshot(shotPrefix + "fill.png");
            }
            if (t > duration) Application.Quit();
        }
```

- [ ] **Step 2: Run it to see today's failure**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 10 -scene Fish -extra '-blobfilltest'` then
`Select-String F:\Projects\BlobMerge\_testout\player.log -Pattern 'fill test' | % Line`
Expected (current radii): `covered` well above 75 % and/or `fits=False`. Note the numbers for the report.

- [ ] **Step 3: Placeholder creatures** — create `F:\Projects\BlobMerge\Tools\make_placeholder_fish.py`:

```python
"""Placeholder creatures for tiers 10-12 (1024, 2048, 4096): hue-shifted copies of the whale (fish_09) so FishLook's
whale swim/eye data still fits. Replace with real art later (then add FishLook.Eyes entries).
Usage: python -I make_placeholder_fish.py <Assets/Resources/Fish dir>"""
import sys, uuid, pathlib
from PIL import Image

src_dir = pathlib.Path(sys.argv[1])
whale = Image.open(src_dir / "fish_09.png").convert("RGBA")
meta = (src_dir / "fish_09.png.meta").read_text(encoding="utf-8")
for tier, shift in ((10, 60), (11, 150), (12, 220)):  # hue shift in 0..255 units
    r, g, b, a = whale.split()
    h, s, v = Image.merge("RGB", (r, g, b)).convert("HSV").split()
    h = h.point(lambda x: (x + shift) % 256)
    out = Image.merge("HSV", (h, s, v)).convert("RGB")
    out.putalpha(a)
    name = f"fish_{tier:02d}.png"
    out.save(src_dir / name)
    lines = [("guid: " + uuid.uuid4().hex) if l.startswith("guid: ") else l for l in meta.split("\n")]
    (src_dir / (name + ".meta")).write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print("wrote", name)
```

Run: `python -I F:\Projects\BlobMerge\Tools\make_placeholder_fish.py F:\Projects\BlobMerge\BlobMergeUnity\Assets\Resources\Fish`
Expected: `wrote fish_10.png`, `fish_11.png`, `fish_12.png`. Open one with the Read tool to eyeball it.

- [ ] **Step 4: Code** — in `GameController.FishSprite`, change the doc comment to
`/// <summary>Fish for a value: Resources/Fish/fish_00 (1) … fish_12 (4096+).</summary>` and
`fishSprites ??= new Sprite[10];` → `fishSprites ??= new Sprite[13];`.

In the `[Header("Fish bubble (Fish scene)")]` block, after `fishSize`, add:

```csharp
        [Tooltip("Fish scene: bubble growth per value step up to the whale (512). Other scenes use Radius Per Tier.")]
        public float fishRadiusPerTier = 0.085f;
        [Tooltip("Fish scene: growth per step above the whale (1024, 2048, 4096). Small, so a 4096 attempt fits the tank.")]
        public float fishBigRadiusPerTier = 0.03f;
```

Replace `RadiusForTier` with:

```csharp
        public float RadiusForTier(int tier) => FishMode
            ? Mathf.Min(maxRadius, baseRadius + fishRadiusPerTier * Mathf.Min(tier, 9) + fishBigRadiusPerTier * Mathf.Max(0, tier - 9))
            : Mathf.Min(maxRadius, baseRadius + radiusPerTier * tier);
```

In `Assets/Themes/BoardTheme_Fish.asset`, after the line `  - {r: 0.2471, g: 0.549, b: 0.9412, a: 1}` (the 10th piece colour) add:

```yaml
  - {r: 0.302, g: 0.8, b: 0.8, a: 1}
  - {r: 0.2, g: 0.3, b: 0.55, a: 1}
  - {r: 0.85, g: 0.3, b: 0.55, a: 1}
```

- [ ] **Step 5: Verify fit + look**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 10 -scene Fish -extra '-blobfilltest'`
Expected: `fill test: covered<=75% … fits=True`. If not, lower `fishRadiusPerTier` in 0.005 steps (code default) and
rerun; report the final value and coverage. Open `_testout\shotfill.png` with Read: whale still clearly the biggest
regular creature, 1024–4096 placeholders visible and bigger than the whale.

---

### Task 5: Level flow — record hooks, win, retry, panel

**Files:**
- Modify: `Assets/Scripts/GameController.cs` (`SpawnLaneBlob`, `TickMerge`, `Update`, new `OnProgressValue`, `BeginLevelWin`, `Restart`)
- Modify: `Assets/Scripts/UI/HudView.cs` (`levelCompleteFormat`, `ShowLevelComplete`)
- Modify: `Assets/Scripts/DebugAutopilot.cs` (logs, `-blobcontinue`)

**Interfaces:**
- Consumes: `Progression.OnValue/Fill/RawFill/Save`, `ProgressionRules` (Task 2).
- Produces: `GameController.RecordFish` `event Action<int,float>` (value, raw fill before), `GameController.LevelWon`
  `event Action<int,float,int>` (level, seconds, throws), `GameController.Restart()`, `HudView.ShowLevelComplete(int, Sprite)`.

- [ ] **Step 1: Bot expectations first** — in `DebugAutopilot.Start()` add:

```csharp
            game.RecordFish += (v, before) => Debug.Log($"[BlobMerge] record fish {v} at t={Time.time - start:F1} throws={game.lane.Shots} barBefore={before:P0}");
            game.LevelWon += (lv, secs, shots) => Debug.Log($"[BlobMerge] level {lv} complete t={secs:F1}s throws={shots}");
```

- [ ] **Step 2: Build to verify it fails**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 5 -scene Fish`
Expected: `error CS1061: 'GameController' does not contain a definition for 'RecordFish'`.

- [ ] **Step 3: Implement GameController** — next to `LevelStarted` add:

```csharp
        /// <summary>(value, raw bar fill just before) — a new biggest fish this level.</summary>
        public event System.Action<int, float> RecordFish;
        /// <summary>(level, seconds, throws) — the goal fish was made.</summary>
        public event System.Action<int, float, int> LevelWon;
        bool levelWon;
        float wonAt;
```

Add these methods in the `// ===== merge` section:

```csharp
        /// <summary>A value appeared (lane spawn or merge): feeds the progress bar, wins the level on the goal fish.</summary>
        void OnProgressValue(int value, bool fromMerge, Vector2 pos)
        {
            if (!ProgressionOn || levelWon) return;
            float before = progression.RawFill;
            if (!progression.OnValue(value, fromMerge)) return;
            RecordFish?.Invoke(value, before);
            if (progression.Complete && ProgressionRules) BeginLevelWin(pos);
        }

        void BeginLevelWin(Vector2 pos)
        {
            levelWon = true;
            wonAt = Time.time; // physics keeps running ~0.6 s so the pop plays, then the panel freezes the game
            aim.Cancel();
            aim.InputEnabled = false;
            fx.Shake(0.8f);
            fx.Sparkles(pos, 60, 10f, 2.5f);
            fx.Ring(pos, Color.white, 1f, 7f, 0.6f, 0.25f);
            sfx.Merge(12, 6);
            Progression.Save(progression.Level + 1);
            LevelWon?.Invoke(progression.Level, Time.timeSinceLevelLoad, lane.Shots);
        }

        /// <summary>Reloads the scene: the saved level (retry after a loss, the next one after a win).</summary>
        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
```

In `SpawnLaneBlob`, after `sfx.Spawn();` add `OnProgressValue(value, false, pos);`.
In `TickMerge`, after `Merged?.Invoke(value, n.State == BlobState.Lane);` add `OnProgressValue(value, true, mid);`.

In `Update()`, replace the inline reload

```csharp
                if (Input.GetMouseButtonDown(0) && Time.unscaledTime > gameOverAt + 0.8f)
                {
                    Time.timeScale = 1f;
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
```
with
```csharp
                if (Input.GetMouseButtonDown(0) && Time.unscaledTime > gameOverAt + 0.8f) Restart();
```

and in the `else` branch replace

```csharp
                lane.Tick(blobs);
                CheckDanger();
```
with
```csharp
                if (levelWon)
                {
                    if (Time.time - wonAt > 0.6f) ShowLevelComplete();
                }
                else
                {
                    lane.Tick(blobs);
                    CheckDanger();
                }
```

and add, under `EndGame()`:

```csharp
        void ShowLevelComplete()
        {
            gameOver = true; // reuses the frozen game-over state: tap reloads the scene (= next level)
            gameOverAt = Time.unscaledTime;
            Time.timeScale = 0f;
            hud.ShowLevelComplete(progression.Level, FishSprite(Tier(progression.Goal)));
        }
```

- [ ] **Step 4: Implement HudView** — after `gameOverFormat` add:

```csharp
        [Tooltip("Fish levels. {0} = the level just completed.")]
        [TextArea] public string levelCompleteFormat = "LEVEL {0}\nCOMPLETE!\n<size=55%>tap for the next level</size>";
        Image levelFish;
```

After `ShowGameOver` add:

```csharp
        /// <summary>Win panel (reuses the game-over panel) with the goal fish above the card.</summary>
        public void ShowLevelComplete(int level, Sprite fish)
        {
            if (gameOverPanel != null) gameOverPanel.SetActive(true);
            if (gameOverText == null) return;
            gameOverText.text = string.Format(levelCompleteFormat, level);
            if (fish == null) return;
            if (levelFish == null)
            {
                var rt = new GameObject("LevelFish", typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(gameOverText.transform.parent, false);
                rt.anchoredPosition = gameOverText.rectTransform.anchoredPosition + new Vector2(0f, 330f);
                rt.sizeDelta = new Vector2(300f, 300f);
                levelFish = rt.gameObject.AddComponent<Image>();
                levelFish.preserveAspect = true;
                levelFish.raycastTarget = false;
            }
            levelFish.sprite = fish;
        }
```

- [ ] **Step 5: Bot continue mode** — in `DebugAutopilot` add `bool continueMode; static float sessionStart = -1f;`.
In `Awake()`: `continueMode = System.Array.IndexOf(a, "-blobcontinue") >= 0;` and after `start = Time.time;`:
`if (sessionStart < 0f) sessionStart = Time.realtimeSinceStartup;`.
In `Update()`, in the `if (game.IsGameOver)` block replace `else if (Time.unscaledTime - overAt > 1f) Application.Quit();` with:

```csharp
                else if (Time.unscaledTime - overAt > 1f)
                {
                    if (continueMode) game.Restart(); // win → next level, loss → retry (the bot keeps playing)
                    else Application.Quit();
                }
```

and change `if (t > duration + 1.5f)` to
`if (continueMode ? Time.realtimeSinceStartup - sessionStart > duration + 1.5f : t > duration + 1.5f)`.
In the over-logging block (`if (overAt < 0f)`) add when `game.ProgressionOn && !game.progression.Complete`:
`Debug.Log($"[BlobMerge] level {game.progression.Level} failed t={t:F1} throws={game.lane.Shots} best={game.progression.Current}");`

- [ ] **Step 6: Verify**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 240 -scene Fish -extra '-level','1','-blobcontinue'` then
`Select-String F:\Projects\BlobMerge\_testout\player.log -Pattern 'level |record fish' | % Line`
Expected: `level 1 start goal=32`, a chain of `record fish 1 … 32` with rising values, `level 1 complete t=…`, then
`level 2 start goal=64` (save advanced). After a `failed` line the next start is the same level. Open the last
`_testout\shotover.png`: LEVEL n COMPLETE panel with the goal fish above the card, white Barlow text with outline.

---

### Task 6: Lower-HUD progress bar + container space

**Files:**
- Create: `Assets/Scripts/UI/ProgressBarView.cs`
- Modify: `Assets/Scripts/GameController.cs` (`progressBarSpace` field, `FitCamera`, `BuildUI`)

**Interfaces:**
- Consumes: `progression.Current/Upcoming/Goal/Fill` (Task 2), `FishSprite` (Task 4), `UiSprites.Circle/RoundedRect`,
  `TextStyle.Apply`, `GameController.SteadyToScreen`, `Board.FloorY`, `frameThickness`.
- Produces: `ProgressBarView.Create(GameController, HudView) → ProgressBarView`.

- [ ] **Step 1: Container space** — in `GameController`, under `[Header("Progression (Fish scene)")]` after the
`progression` field add:

```csharp
        [Tooltip("World units kept free below the container for the progress bar (Fish levels only).")]
        public float progressBarSpace = 2.2f;
```

In `FitCamera()` replace `float hudTop = hudSpace, bottom = frameThickness + 0.25f;` with
`float hudTop = hudSpace, bottom = frameThickness + 0.25f + (ProgressionOn ? progressBarSpace : 0f);`

- [ ] **Step 2: The view** — create `Assets/Scripts/UI/ProgressBarView.cs`:

```csharp
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BlobMerge
{
    /// <summary>
    /// Fish levels, lower HUD (mock "A3"): [current fish] [green bar] [next fish silhouette] » [goal fish, gold ring].
    /// Built at runtime under the HUD canvas (HUD_Fish.prefab is not edited); centred between the container's bottom
    /// edge and the screen bottom. On a new biggest fish the bar holds full, then the fish slides into the Current slot.
    /// </summary>
    public class ProgressBarView : MonoBehaviour
    {
        // 1080x1920 reference units (the phone mock ×3)
        const float Width = 924f, Icon = 120f, GoalIcon = 162f, Gap = 12f, BarH = 54f, ChevronW = 40f;

        GameController game;
        Canvas canvas;
        RectTransform root, fill, curFishRt, curDiscRt;
        Image fillImg, curFish, nextFish, goalFish;
        int shownCurrent = -1, sw, sh;
        float shown, hold, pop, slide, slideDx;

        public static ProgressBarView Create(GameController g, HudView hud)
        {
            var canvas = hud.GetComponent<Canvas>();
            if (canvas == null) canvas = hud.GetComponentInChildren<Canvas>();
            var go = new GameObject("ProgressBar", typeof(RectTransform));
            var v = go.AddComponent<ProgressBarView>();
            v.game = g;
            v.canvas = canvas;
            v.root = (RectTransform)go.transform;
            v.root.SetParent(canvas.transform, false);
            v.root.anchorMin = v.root.anchorMax = new Vector2(0.5f, 0f);
            v.root.pivot = new Vector2(0.5f, 0.5f);
            v.root.sizeDelta = new Vector2(Width, GoalIcon);

            float curX = -Width * 0.5f + Icon * 0.5f;
            float goalX = Width * 0.5f - GoalIcon * 0.5f;
            float chevX = goalX - GoalIcon * 0.5f - Gap - ChevronW * 0.5f;
            float nextX = chevX - ChevronW * 0.5f - Gap - Icon * 0.5f;
            v.slideDx = nextX - curX;

            v.curDiscRt = Disc(v.root, "Current", curX, Icon, new Color(0.78f, 0.94f, 1f, 0.75f), out v.curFish).rectTransform;
            v.curFishRt = v.curFish.rectTransform;
            Disc(v.root, "Next", nextX, Icon, new Color(0.78f, 0.94f, 1f, 0.75f), out v.nextFish);
            v.nextFish.color = new Color(0f, 0f, 0f, 0.55f); // silhouette until it's made
            Disc(v.root, "Goal", goalX, GoalIcon, MeshUtil.Hex("FFD34D"), out v.goalFish);

            float barL = curX + Icon * 0.5f + Gap, barR = nextX - Icon * 0.5f - Gap;
            var bar = Img(v.root, "Bar", UiSprites.RoundedRect, MeshUtil.Hex("0A1836"), new Vector2((barL + barR) * 0.5f, 0f),
                          new Vector2(barR - barL, BarH), true);
            var ol = bar.gameObject.AddComponent<Outline>();
            ol.effectColor = MeshUtil.Hex("1F5A96");
            ol.effectDistance = new Vector2(4f, -4f);
            v.fillImg = Img(bar.rectTransform, "Fill", UiSprites.RoundedRect, MeshUtil.Hex("3CC23A"), Vector2.zero, Vector2.zero, true);
            v.fill = v.fillImg.rectTransform;
            v.fill.anchorMin = Vector2.zero;
            v.fill.anchorMax = new Vector2(0f, 1f);
            v.fill.offsetMin = new Vector2(5f, 5f);
            v.fill.offsetMax = new Vector2(0f, -5f);

            var chev = new GameObject("Chevron", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            chev.rectTransform.SetParent(v.root, false);
            chev.rectTransform.anchoredPosition = new Vector2(chevX, 0f);
            chev.rectTransform.sizeDelta = new Vector2(ChevronW + 30f, 80f);
            TextStyle.Apply(chev, MeshUtil.Hex("9FE3FF"));
            chev.text = ">>";
            chev.fontSize = 46;
            chev.alignment = TextAlignmentOptions.Center;
            chev.raycastTarget = false;
            return v;
        }

        static Image Disc(RectTransform parent, string name, float x, float size, Color rim, out Image fish)
        {
            var ring = Img(parent, name, UiSprites.Circle, rim, new Vector2(x, 0f), new Vector2(size, size), false);
            Img(ring.rectTransform, "Glass", UiSprites.Circle, new Color(0.10f, 0.32f, 0.58f, 0.92f), Vector2.zero,
                new Vector2(size - 8f, size - 8f), false);
            fish = Img(ring.rectTransform, "Fish", null, Color.white, Vector2.zero, new Vector2(size * 0.78f, size * 0.78f), false);
            fish.preserveAspect = true;
            return ring;
        }

        static Image Img(RectTransform parent, string name, Sprite sprite, Color color, Vector2 pos, Vector2 size, bool sliced)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            if (sliced) img.type = Image.Type.Sliced;
            return img;
        }

        void LateUpdate()
        {
            var p = game.progression;
            float dt = GameTime.UnscaledDelta;

            if (p.Current != shownCurrent)
            {
                bool record = shownCurrent >= 0 && p.Current > shownCurrent;
                shownCurrent = p.Current;
                curFish.enabled = p.Current > 0;
                if (p.Current > 0) curFish.sprite = game.FishSprite(GameController.Tier(p.Current));
                nextFish.sprite = game.FishSprite(GameController.Tier(p.Upcoming));
                goalFish.sprite = game.FishSprite(GameController.Tier(p.Goal));
                if (record) { hold = 0.3f; pop = 1f; slide = 1f; }
            }

            if (hold > 0f) { hold -= dt; shown = hold > 0f ? 1f : 0f; }
            else shown = Mathf.Lerp(shown, p.Fill, 1f - Mathf.Exp(-10f * dt));
            fill.anchorMax = new Vector2(shown, 1f);
            fillImg.enabled = shown > 0.03f;

            pop = Mathf.Max(0f, pop - dt * 4f);
            curDiscRt.localScale = Vector3.one * (1f + 0.3f * pop);
            if (hold <= 0f) slide = Mathf.Max(0f, slide - dt / 0.25f);
            float e = slide * slide * (3f - 2f * slide);
            curFishRt.anchoredPosition = new Vector2(slideDx * e, 0f);

            // centred between the container's bottom edge and the screen bottom (re-measured when the screen changes)
            if (Screen.width != sw || Screen.height != sh)
            {
                sw = Screen.width;
                sh = Screen.height;
                Vector2 s = game.SteadyToScreen(new Vector2(0f, game.Board.FloorY - game.frameThickness));
                root.anchoredPosition = new Vector2(0f, s.y * 0.5f / canvas.scaleFactor);
            }
        }
    }
}
```

- [ ] **Step 3: Hook it up** — in `GameController` add field `ProgressBarView bar;` (UI section) and at the end of
`BuildUI()` add:

```csharp
            if (ProgressionOn) bar = ProgressBarView.Create(this, hud);
```

- [ ] **Step 4: Verify the bar**

Run: `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 60 -scene Fish -extra '-level','3'` and open
`_testout\shot3.png`, `shot4.png`, `shot5.png` with the Read tool.
Expected: bar below the container, not overlapping the frame or screen edge; current fish in colour, next as a dark
silhouette, gold-ringed dolphin goal; green fill grows between records (compare shots); container ~90 px (at 540 px
width) higher than in `Branding/themes/fish/ingame_final.png`. Also run the Game scene for 20 s and open its last shot:
no bar, container position unchanged.

---

### Task 7: Calibration, regression, docs

**Files:**
- Modify: `Assets/Scripts/Progression.cs` (table / `barTargetFactor` defaults), `Assets/Scripts/LaneSpawner.cs` (only if needed)
- Modify: `Docs/FISH_PROGRESSION.md`, `Docs/AGENT_GUIDE.md` (§5.11 Fish bullet, §7 bot flags)

- [ ] **Step 1: Measure per level.** The bot throws faster than people (normal persona ≈ 16 throws/min, AGENT_GUIDE §12),
so judge levels by **throws**: target throws = target minutes × 16 → L1 16, L2 32, L3 48, L4 64, L5 80, L6 112, L7 160, L8 240.
For each level n = 1..8 run 3 times:
`& F:\Projects\BlobMerge\Tools\test.ps1 -nobuild -secs 900 -scene Fish -extra '-level','<n>'` (shorter `-secs` for low levels:
120 for 1–2, 300 for 3–5) and collect `level n complete … throws=` / `level n failed … best=` lines into a table.
If the bot can't finish the top levels, extrapolate from the record-fish times (`record fish v at t= throws=`) and say so.

- [ ] **Step 2: Tune.** Levels that finish in < 0.7× target throws: lower `spawnMin`/`spawnMax` one step; > 1.4×: raise.
Bar: take the median `barBefore` over all `record fish` lines; set `barTargetFactor` = current × median / 0.9
(so a typical record lands at ~90 %). Edit the code defaults in `Progression.cs` (and tell the user to copy them in the
Fish scene's `Game` object, or run Setup Project only if they agree — Fish.unity is their scene). Rebuild, rerun the
levels that moved.

- [ ] **Step 3: Regression.** `test.ps1 -secs 60 -scene Game` and `-scene Hexa`: no `Exception`, `queue:` shows
`pity=0 rerolls=0`, no `level` lines, summary throws/merges in the same range as `baseline_game.log` / `baseline_hexa.log` (runs are random, so
compare ranges, not exact numbers). `-blobselftest` still all PASS.
Also start an AdSim Fish take if quick (`ad.ps1 -scene Fish -persona normal`) and confirm no level-complete panel.

- [ ] **Step 4: Docs.** In `FISH_PROGRESSION.md`: set status "implemented", replace the table's target times with the
measured throws/minutes, the final `barTargetFactor`, `fishRadiusPerTier` + fill-test coverage. In `AGENT_GUIDE.md` §5.11 Fish
bullet add one paragraph: progression (`progression` on Game, levels, PlayerPrefs `BlobMerge.Fish.Level`), the bar,
queue rules, radius curve; in §7 list the new bot flags `-blobselftest`, `-level n`, `-blobcontinue`, `-blobfilltest` and
the new log lines. Report the measured numbers and screenshots to the user.
