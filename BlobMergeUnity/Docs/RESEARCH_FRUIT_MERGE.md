# Research: how "Fruit Merge" (com.melon.drop.fruit.merge.master 2.80) does progression

Source: the APK the user provided (2026-10-09), studied statically: Unity 6000.0.66 IL2CPP; text configs read with UnityPy,
class/field/constant names recovered with Il2CppDumper (`dump.cs`). Design research only: no assets or code copied.
Gaps: scene-component values (exact spawn weights in endless mode) could not be decoded (stripped type trees; the rebuilt
layouts misalign for this Unity version), and some tuning comes from Firebase Remote Config at runtime.

## What the game is
One endless Suika-style box (`GameMode` Normal / Tournament) + a separate **Level mode** (20 puzzle levels) + meta
systems (build/chapters with gold, daily challenge, profile avatars/frames, **themes** incl. "Marine Life").

## Progression in the endless game (the screen the user shared)
1. **Fruit bar = a static picture of all 11 fruits** (`BarFruitController`: one image + three hammer-reward labels;
   `FruitBarThemeController` swaps the artwork per theme, `_isCycleFruitsA` picks bar art variant A/B). Nothing on it
   unlocks, moves or disappears; it is a permanent "evolution chart" + the reward hints (coins/hammers on dragon fruit,
   pineapple, watermelon). The bar never changes during a game.
2. **"Best fruit" popup** (`PopupBestFruitController`, `BestFruitConfigs.MIN_FRUIT_ID = 6`, `MAX_FRUIT_ID = 10`): when you
   create a fruit of id 6–10 (the 5 biggest) for the first time ever (saved in `BestFruitData`), a popup shows its
   name + image (and "can you get TWO?" text for the top). Small fruit never celebrate. Lifetime, not per game.
3. **Account level from merges** (remote key `endless_level_system`: `Enabled, MinLevel, MaxLevel, MergesPerLevel,
   ShowLevelUpPopup, PopupFrequencyInterval`): a player level that rises every N merges across games, with a level-up
   popup only every few levels. This is the "progress" layer of the endless game; the board is never reset by it.
4. **Merge milestones** (`IsMergeMilestone(totalMerges)`, lifetime merge counter, "AreAllMilestonesClaimed",
   "Milestone already claimed!"): rewards at lifetime merge counts.
5. **Spawning:** `PlayerController.GenNextFruit` / `RandomBallIdWithWeighted` (weighted random over the small fruit),
   with an A/B test (`Test1v7Type.RandomFruitRule`) and a "fruit 1 mode" (`_isFruit1Mode`, `_noFruit1Count`) — a rule
   that tracks how long since the smallest fruit came (likely a pity/guarantee or a phase where it stops). Exact numbers
   not recoverable statically.
6. **Removing the smallest fruit is a paid power-up, not progression:** `PowerType.CullTheWeak` = "Remove the smallest
   Fruits"; others: `FruitNinja` ("Tap to remove a specific Fruit"), `UpgradeFruit` ("Tap to upgrade a specific Fruit";
   "Watermelon can't be upgraded"), `ShakeBox`. The 4 buttons in the top corners of the screenshot.
7. Two watermelons (top fruit) merge and vanish (`TotalWatermelonCollected`, "Create {0} watermelon").

## Level mode (separate from endless) — `LevelConfigs` + `Level0..19`
20 levels. Each: `FruitNeed` goals ("5-3,6-1" = make three of fruit 5 and one of fruit 6), `FruitGen` spawn weights per
fruit id (e.g. L0 "0,0,0,2" → only fruit 3; L19 "4,6,12,24,12,5,3,1" → spawns up to fruit 7, peak at fruit 3),
`TotalMove` limit (5 → 24 drops), and a hand-placed starting board (`FruitMap`). Win = goals met within the moves.

## Takeaways for BlobMerge (for the user to decide)
- Their **bar is permanent and static**; the "celebration" for new big fruit is a **popup on first-ever creation of the
  top 5**, not a per-level unlock screen and not a board reset.
- Progress over time comes from an **account level driven by merge count** (+ occasional level-up popup) and lifetime
  milestones, while the board itself is one continuous endless game.
- "Smallest fruit disappears" exists only as a **power-up** there. The user's sliding 11-creature window (drop #1 when
  #12 arrives) is our own idea, not theirs — it fits us because we have 13 creatures vs their 11.
- Spawn odds: weighted toward the small fruit; their level mode shows typical weight shapes (peak 1–2 tiers above the
  smallest).
