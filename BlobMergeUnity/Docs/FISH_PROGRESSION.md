# Fish scene — progression & queue balancing (design spec)

Status (2026-10-12, latest): **v2 built, reviewed, APK `Builds/BlobMerge_Fish.apk` 14:40** — one continuous game,
11-creature bar with per-game shift, spawn table + difficulty director (§0b), 13 creatures in the user's order (below).
§3 levels, §5 bar and §6 level flow describe v1 (removed; history). §2, §4 and §7 still apply where not superseded.
Scope: `Fish.unity` only — Game and Hexa scenes must stay bit-identical (user rule: a theme never touches other scenes).

**Current state at a glance**
- Game: endless, game over = fresh board. Lifetime `BlobMerge.Fish.Highest` decides first-ever new-fish celebrations
  (fly-in to the bar) and the one-time full-screen celebration for the top creature (Whale). Bar window shifts within
  each game when that game makes the bar's 11th creature (smallest leaves bar + game, leftovers pop).
- Queue (§0b): pool driven by the biggest creature in the current game (start Fry/Tetra/Clown 54/30/16 %), rising
  sawtooth difficulty, director refuses helpful picks and locks the most needed creature; no pity; streak rule kept.
- Lane (user 2026-10-12): no automatic lane merges and no magnet in Fish (`laneAutoMerge` false); a thrown blob merges
  with a same-value lane blob only when flying into it (closing > 0.5 u/s within ~53°). `helpLane` 0.4.
- Tank: `fishRadiusPerTier` 0.115 (original look) + `fishBigRadiusPerTier` 0.05 — deliberately tight (the full chain to
  the top fits only 1/3 fill tests).
- Sound: throw sound off (`throwSoundVolume` 0; light 4-variant puff available at ~0.25), max-stretch sound off
  (haptic kept), tap sound kept. Celebration = `LevelCompleteView` unlock burst (rays/core/glow particles/confetti/
  bubbles; tunables in `GameController.celebration`).
- Bot calibration (before the reorder/lane change): Whale-position tier ~7–9 human-min, top reached ~1/11 games at
  ~18–23 human-min, every game fails, none before tier 9. **Needs a re-run** after the lane change (fewer free merges).

**Open / next**
- User playtest on device (difficulty feel, early-game tension, bar near the gesture bar, burst performance).
- Re-calibrate with the bot (lane change made the game harder); early game still has no fails (levers: pool size,
  grace, lock length).
- The user's saved Fish scene keeps old serialized values for some tunables (e.g. `helpLane` 0.8) — reset them on the
  Game object or re-open the scene from disk.
- Ad takes: they read the Editor's saved progress; offered a fixed ad start state (not built).
- `Resources/Fish/Cute Neon Tetra Game Icon.png` (user's reference, 1.7 MB) ships in the build unless moved.
- Player level from merges (reference game's `endless_level_system`) — later.

Work records (mocks + ledger now in `F:\Projects\BlobMerge_Backups\2026-10-12\superpowers.zip`): research `Docs/RESEARCH_FRUIT_MERGE.md`; mocks `.superpowers/brainstorm/1844-1791529241/content/`
(bar, celebration, ladder) and `.../1878-1791779753/content/spawn-table.html`; build ledger with every decision and
ruling `.superpowers/sdd/fish-progression/progress.md` (+ task briefs/reports); bot flags in AGENT_GUIDE §7.
Mocks: `F:\Projects\BlobMerge\.superpowers\brainstorm\1844-1791529241\content\progress-bar-a3.html` (picked layout).

> **Creature order (user, 2026-10-12):** 0 Fry, 1 Neon Tetra, 2 Clownfish, 3 Pufferfish, 4 Angelfish, 5 Turtle, 6 Octopus,
> 7 Dolphin, 8 Shark, **9 Sting Ray (512), 10 Hammerhead (1024), 11 Orca (2048), 12 Whale (4096, the final creature)**.
> Sprites fish_05/06 and fish_09..12, FishLook rows and bubble tints were rotated to match; older mentions below of "Orca 4096 / Whale 512" are history.

## 0. v2 — continuous game + creature bar (user decision 2026-10-09; SUPERSEDES §3 levels, §5 bar, §6 level flow)

Research basis: `Docs/RESEARCH_FRUIT_MERGE.md`. Mock: `.superpowers/brainstorm/…/content/ladder-v2.html`.

- **One continuous game.** No levels, no board reset on unlock, no Level Complete panel per unlock. Game over still
  ends the run (fresh board, same progress). The queue rules (§7) stay.
- **Lifetime progress (PlayerPrefs):** `BlobMerge.Fish.Highest` = highest tier ever made (start: 2 = clownfish, since the
  lane spawns 1/2/4); it decides the first-ever new-fish celebrations and the first-ever Orca panel. The old
  `BlobMerge.Fish.Level` key is ignored.
- **Window per game (user decision 2026-10-12, supersedes the saved `WindowStart`):** every game starts with WindowStart
  0 (bar = tiers 0..10, queue Fry/Tetra/Clown, start values {1,2,4} never scaled). Making the bar's last creature for the
  first time **in this game** shifts the bar for this game only (slide + cull as below); the scene reload (game over)
  resets it. The old saved `BlobMerge.Fish.WindowStart` key is ignored and deleted on load.
- **Creature bar** (replaces the 3-bubble bar): the 11 tiers `[WindowStart … WindowStart+10]`, all in full colour, on a
  stepped path — 6 icons on the lower row, 5 on the upper row (like the reference); icons ≈ 26 px at 360-px phone width
  (≈ 78 ref units), ≈ 7 px gaps (≈ 21 ref units). No coins/rewards. Always visible.
- **New fish (a tier > Highest is created, lane or tank):** small celebration, play never stops: the creature's image
  lifts from the merge point, glowing, and flies in an arc into its bar slot (0.7 s); the slot punch-scales with a white
  ring, an additive glow flash and a few sparkles; a small "<NAME>!" label floats up over the tank. Highest is saved.
- **Shift:** when the last creature on the bar (WindowStart+10) is created for the first time this game and a next
  creature exists (≤ tier 12): after its celebration, the first icon shrinks/fades out, every icon slides one slot along the path,
  the next creature pops in at the end; WindowStart++ (this game only). A known creature (not first-ever) shifts the bar
  without the fly-in. From then on the dropped tier never spawns
  (lane values are scaled to ≥ 2^WindowStart via the existing `QueueRules.Scale`). **Ruling:** any blobs of the dropped
  tier still in the tank/lane pop with a small bubble burst (no score).
- **Orca (tier 12) first ever:** the full-screen celebration (`LevelCompleteView`, "ORCA UNLOCKED!") plays once; tap
  continues the same game (no reset, no level number). **Ruling.**
- **Ads (`AdTake`):** the bar and new-fish celebrations show, nothing is saved.
- **Implementation (2026-10-09):** `Progression.cs` (Highest/WindowStart, `windowSize` 11, `OnValue` → new fish /
  shift, Load/Save/DeleteSaved), `ProgressionMath.cs` (IsNewFish, ShiftDue incl. the tier-12 end, SpawnFloor,
  MaxWindowStart), `UI/CreatureBarView.cs` + `UI/PathStroke.cs` (bar, fly-in, pop, label, shift; `SlotCanvasPos(tier)`),
  `GameController` (`SpawnFloor`, `OnProgressValue`, cull on `ShiftDone`, orca panel `BeginOrcaCelebration` /
  `ContinueAfterCelebration`; events `NewFish`, `WindowShifted`, `Culled`, `OrcaCelebration`), `LaneSpawner` (start
  values and rolls scaled to the floor; pity cap = `pityMaxValue` × floor), `LevelCompleteView` (title/word/ribbon
  texts, no BEST line). New fish also count lane spawns (the creature flies from the lane door). Removed: level table,
  bar fill, best times, `ProgressBarView`, `HudView.ShowLevelComplete`, director's fixed level 3.
  Bot: `-fishreset`, `-fishstart h,w`, `-fishspawn v,v`, `-blobfishshots`, `-blobwinshots` (orca); logs `new fish`,
  `window shift ->`, `culled n of v`. Verified: selftest 28/28; fresh 90 s run new fish 8/16/32/64/128/256; `-fishstart
  9,0 -fishspawn 512,512` → sting ray, `window shift -> 1`, `culled 1 of 1`, no 1s spawned after; `-fishstart 11,2
  -fishspawn 2048,2048` → orca panel, bot tap resumed the same game; Game 30 s unchanged (`pity=0 rerolls=0`).
- **Later (not now):** player level from merge count with an occasional level-up popup (the reference's
  `endless_level_system`).

## 0b. v2 queue + difficulty (user decision 2026-10-12; SUPERSEDES the v1 spawn range/pity in §3/§7)

Goal: hard moments and real fails; the Orca (12th unlock) takes **≥ 25 min** in one game for a good player. Kept (user
rules): 3 throwable lane blobs, lane merges + magnet. Table mock: `.superpowers/brainstorm/…/content/spawn-table.html`.

- **Fish: no automatic lane merges** (user 2026-10-12): lane blobs never merge on their own and the magnet is off; a thrown blob merges with a same-value lane blob (also during `launchPassThroughTime`).

- **Base spawn table** — driven by `R` = biggest tier made **in the current game** (resets on game over; the bar's
  lifetime progress does not drive it). Pool = tiers `[WindowStart … top]` (this game's window), top =
  clamp(max(WindowStart+start-1, R-lag), ≤ WindowStart+cap-1). Weight(t) = decay^(t-WindowStart) × fade^max(0, (R-fadeFrom)-t), normalised.
  Defaults: start 3, lag 3, cap 6, decay 0.55, fadeFrom 6, fade 0.6 (→ new game 54/30/16 % Fry/Tetra/Clown; at Whale
  24/22/20/18/10/6 %). No values above the pool ever spawn (no more 8/16 early, no ×-scaling).
- **Difficulty D (0..1), rising sawtooth:** base(R) = lerp(`difficultyMin` 0.3, `difficultyMax` 1.0, (R-2)/10). On each
  new biggest tier this game: D drops by `relaxDepth` 0.15 for `relaxSeconds` 20, then climbs back to base over
  `climbSeconds` 40. First `graceSeconds` 60 of a game: D ≤ `graceDifficulty` 0.1.
- **Director (per spawn, sees lane + tank):** helpfulness of a candidate value = `helpExposed` 1.0 if it would merge at
  once with an exposed tank creature (top of the pile, reachable by a throw), `helpLane` 0.4 if it matches a lane blob
  (a deliberate throw merges them; was 0.8 while the magnet merged them for free), else 0. Refuse a candidate with probability D × help and re-sample (≤ `refusalTries` 4,
  then take the least helpful). **Lock:** the most needed value (biggest exposed matchable creature within the pool) is
  locked with probability D for round(lerp(4, 12, D)) spawns (weight 0), released early when the player builds that
  value; `lockCooldown` 3 spawns.
- *History:* the spec's original numbers were difficulty 0.15→0.85, relax 0.3, grace 90 s, lane help 0.6, locks 3–10,
  cooldown 4 (changed by the first calibration, below); `relaxDepth` 0.25 → 0.15 in the fix-round calibration.
- Pity rule removed; the no-three-in-a-row rule stays. Start values {1,2,4} stay (never scaled: every game starts at
  window 0).
- **Space:** `fishRadiusPerTier` back to 0.115 (original look), `fishBigRadiusPerTier` 0.05.
- All numbers are tunables on the `Game` object (lane/difficulty section); bot logs D, refusals, locks, per-game unlock
  times, fails.
- **Implementation (2026-10-12):** `SpawnTable.cs` (pure, = the mock's `draw()`: `PoolTop`, `Weights`, `Sample` with
  exclusions, `Base`, sawtooth `Difficulty`, `LockLength`; selftests check the 54/30/16, Turtle 50/27/15/8, Whale
  24/22/20/18/10/6 and Orca-with-floor-2 22/20/18/17/15/8 rows). `LaneSpawner.DirectorTier` runs only when
  `ProgressionRules` (Fish, no `ValueOverride`, no `AdTake`); Game/Hexa keep the original shot-based `Roll()` unchanged.
  R = `lane.GameBest` (spawns + merges via `lane.NoteValue`, starts at the bar's first tier, reset by the scene reload);
  game clock = scaled `Time.time` since `lane.Init`. **Exposed** (fix round): match targets = Free blobs below the danger
  line that rest (< 2 u/s) or were thrown/made < 1 s ago (a just-thrown blob or a fresh merge result counts at once);
  exposed = no other Free blob (resting, falling or just thrown) on top of it (higher centre, |dx| < 0.75 × summed radii,
  centres closer than 1.25 × summed radii). `poolStart`/`poolCap` are clamped ≥ 1; an empty sample falls back to the
  window's first tier. Help = `helpExposed` for an exposed match, else `helpLane` for a Lane-blob match. The streak rule
  zeroes the streak tier's weight; a lock zeroes the locked tier; a merge that builds the locked tier releases it early;
  every release starts the cooldown. Pity code/fields/`-blobpity` removed. Tunables (lane): `poolStart/Lag/Cap/Decay/
  FadeFrom/Fade`, `difficultyOn`, `difficultyMin/Max`, `relaxDepth/Seconds`, `climbSeconds`, `graceSeconds/Difficulty`,
  `helpExposed/Lane`, `refusalTries`, `lockMinSpawns/MaxSpawns`, `lockCooldown`. Bot: `-blobcontinue` (+ `-fishresetgames`
  = each game fresh, `-blobspeed 2`, `-blobnodirector`), logs `game n: new R`, `game over … R=`, `difficulty D= …`.
- **Calibration (bot ≈ 48 throws/min; human 16–20/min).** Run 1 = spec defaults (0.15/0.85, relax 0.3, grace 90 s, lane
  help 0.6, locks 3–10, cooldown 4), progress persisting: fresh game 1 Whale 169 throws, Sting ray 199, Hammerhead 274,
  game over 331 (R 11); after the bar shifted to window 2 the floor ×4 makes games much faster (Orca at **162** throws in
  one game, two others died at R 10 after ~155). Tuning (one step, now the code defaults): `difficultyMin 0.3`,
  `difficultyMax 1.0`, `relaxDepth 0.25`, `graceSeconds 60`, `helpLane 0.8`, `lockMin/MaxSpawns 4/12`, `lockCooldown 3`.
  Run 2 (tuned, every game fresh, 8 finished games): Whale median ~141 throws (120–178 ≈ 7–9 human-min), Sting ray in 6/8
  (198–303), Hammerhead 3/8 (262–314), **Orca 0/8**; every game failed, lengths 249–365 throws (≈ 12–23 human-min); fails
  before the Whale 0/8. Refusals ≈ 0.6 per throw (run 1: 0.3).
- **Fix round (2026-10-12): per-game window + director fixes** (exposed sees all Free blobs, just-thrown blobs and fresh
  merge results count as targets). Run 3 (relax 0.25, 10 fresh games): Whale 122–194 throws (median ~160), Sting ray
  7/10 (198–355), Hammerhead 4/10 (276–348), **Orca 1/10 at 376 throws** (≈ 19–23.5 human-min < 25) → one step:
  `relaxDepth 0.15`. Run 4 (relax 0.15, 11 fresh games): Whale 121–166 (median ~142 ≈ 7–9 human-min), Sting ray 7/11
  (202–253), Hammerhead 3/11 (313–326), **Orca 1/11 at 363 throws** (≈ 18–23 human-min); ends R9 ×4, R10 ×4, R11 ×2,
  R12 ×1; game lengths 210–429 throws (median ~252 ≈ 13–16 human-min); fails before the Whale 0/11. Open: the early
  game is untouched by the director (grace + low D); a lucky good run still reaches the Orca in < 25 human-min.

## 1. Goals

- Give the Fish variant a level structure with a visible goal, so a run has a clear "win".
- Short early levels (1–5 min), a long tail: the final level (4096) is a ~15 min run.
- Fairer queue: no frustrating streaks, guaranteed relief when nothing in the lane/container matches.

## 2. Creature ladder (13 tiers)

| Tier | Value | Creature | Sprite |
|---|---|---|---|
| 0–9 | 1…512 | fry, neon tetra (was guppy until 2026-10-12), clownfish, pufferfish, angelfish, octopus, turtle, dolphin, shark, **whale** | existing `fish_00..09` |
| 10 | 1024 | sting ray (manta) | `fish_10` (Higgsfield, `Branding/themes/fish/sprites/r2`) |
| 11 | 2048 | hammerhead shark (two eyes) | `fish_11` |
| 12 | 4096 | orca, end boss | `fish_12` |

Tiers 10–12 were generated 2026-10-09 with GPT Image 2.5 using fish_07–09 as style references; their eyes/swim are
in `FishLook` rows 10–12. Bubble tints for tiers 10–12 go into `BoardTheme_Fish.asset` piece colours (append only).

## 3. Levels

Each level: fresh empty board, lane restarts with `startValues` {1,2,4}, `Shots` resets. Win = the goal value is
created anywhere (container or lane). Lose = existing danger-line game over → retry the **same** level.

| Lvl | Goal | Spawn min–max | Target (throws / min) | Measured bot throws (runs) | ≈ human min @ 16/min |
|---|---|---|---|---|---|
| 1 | Octopus 32 | 1–4 | 16 / ~1 | 15, 15 | 0.9 |
| 2 | Turtle 64 | 1–4 | 32 / ~2 | 26, 31 | 1.8 |
| 3 | Dolphin 128 | 1–16 | 48 / ~3 | 39, 28 | 2.1 |
| 4 | Shark 256 | 1–16 | 64 / ~4 | 39, 59 | 3.1 |
| 5 | Whale 512 | 2–16 | 80 / ~5 | 80, 54 | 4.2 |
| 6 | 1024 | 2–32 | 112 / ~7 | 90 | 5.6 |
| 7 | 2048 | 4–32 | 160 / ~10 | 114 | 7.1 |
| 8 | 4096 | 4–32 | 240 / ~15 | 179, 224 | 12.6 |
| 9+ | 4096 (endless replay of level 8) | 4–32 | best time tracked | | |

Bot = `DebugAutopilot` (~45 throws/min, plays faster and merges better than people), so judge by **throws**, not
seconds. First pass (L1/L2 at 1–8, L8 at 4–64) finished in 0.56–0.65× target, so one tuning step: L1/L2 max 8→4,
L8 max 64→32 (rows above are the tuned values; L3–7 measured untuned: 0.70–0.84× target (L3 0.698×, borderline,
left alone)). A real player is slower/clumsier than the bot, so expect real times ≥ the "human min" column.
Re-measure after any table change:
`test.ps1 -nobuild -secs 300 -scene Fish -extra '-level','<n>'` and read `level n complete t= throws=`.

Spawn range per level **scales** the existing shot-based odds: the 1..16 roll is multiplied by `spawnMin` and capped
at `spawnMax` (level 8, 4–32: 1,2,4,8,16 → 4,8,16,32,32; the shot ramps for "8 after 6 shots, 16 after 24" keep their
shape). Start values {1,2,4} scale the same way. No new weight fields needed.
The range is also what makes 4096 reachable in ~11–14 min: a 4096 needs 4096 "fry" of material, which at ~16
throws/min needs an average spawn value of ~17 (original L8 4–64 plan; tuned 4–32: ~180–225 bot throws).

Numbers above are bot-calibrated (2026-10-09, 1–2 runs per level; the bot finished every level, nothing extrapolated).

### Data

`FishLevel { int goalValue; int spawnMin; int spawnMax; }` list on the Fish scene's `Game` object
(`GameController.progression`), so the user tunes it live in Play mode like every other tunable. Saved level:
`PlayerPrefs "BlobMerge.Fish.Level"`; best time per level `"BlobMerge.Fish.BestTime.<n>"` (float seconds,
`Progression.SaveBestTime` keeps the minimum; saved on every win, not in ad takes). The Level Complete panel shows it
as a last line `BEST m:ss` (`HudView.levelBestFormat`; empty string hides it).

## 4. Container fit (big fish)

Radius was `min(maxRadius 1.75, 0.5 + 0.115·tier)`, ×`landedScale` 1.1. Holding the chain needed for a 4096
(2048, 1024, 512 … ) covers ~75–80 % of the area below the danger line → near-certain game over.
Fix (Fish scene only): new field `radiusTopTier` / flatter curve — `radiusPerTier` ≈ 0.085 and tiers ≥ 10 grow by a
small fixed step, keeping the whale visibly huge vs. a fry. Acceptance: bot fill test — the full 2048→1 chain fits
below the danger line with ≥ 25 % free area.
**2026-10-12 (§0b restored the original look):** `fishRadiusPerTier 0.115` + `fishBigRadiusPerTier 0.05`;
`-blobfilltest` × 3: covered 80 / 80 / 79 %, top 2.94 / 2.86 / 1.75 vs danger 2.60 → **fits 1 of 3** (the full 2048→1
chain usually pokes ~0.3 above the danger line). Sizes left as the user wants a tight tank: a 4096 attempt needs a near
perfect pile.
*Previous:* Fish-only `fishRadiusPerTier = 0.10` (tiers 0–9) + `fishBigRadiusPerTier = 0.05` (each tier above 9);
`-blobfilltest` coverage 67–69 % of the field (fits below the danger line); measured 2026-10-09 before calibration
(68 % after the art swap), not re-run after the calibration (radii unchanged, so still valid).

## 5. Lower-HUD progress bar (layout "A3")

`[last unlocked fish] [green bar] [fish this level unlocks, silhouette] » [final fish (Orca), gold ring]` — small
icons (40 px / 54 px final at 360-px phone scale; ×3 in 1080 reference units), 4 px gaps, **no panel behind it, no
level number, no number on the bar**. User rule (2026-10-09): **the fish do not change during a level** — each level
unlocks one fish.

- **1st bubble** = `Progression.Unlocked`: the previous level's goal (level 1: the fish below its goal, Angelfish 16).
- **2nd bubble** = `Goal`: the fish this level unlocks, a dark silhouette until made; on the unlock it punch-scales
  into colour with a pop on its bubble.
- **3rd bubble** = `EndGoal`: the last fish of the table (Orca 4096) on every level.
- **Fill** = merge points earned over the whole level ÷ `ProgressionMath.LevelTarget` = goal × log2(goal / spawnMin)
  × `levelBarFactor` (default **0.75**; new field — the old per-fish `barTargetFactor` is gone). Capped at 95 % until
  the goal exists, then full. Calibration (bot, one run per level, raw fill at the unlock at factor 1.0):
  L1 60 %, L2 63 %, L3 78 %, L4 62 %, L5 110 %, L6 104 %, L7 69 %, L8 68 % → median ≈ 68 % → 0.75 puts it near 90 %;
  L5–6 reach the 95 % cap a little early (per-level factor if that bothers).
- A new level punch-scales both fish in their bubbles (ease-out-back from 25 % over 0.35 s, ~10 % overshoot; the
  2nd 0.12 s later). Tweened fill.
- Built at runtime by `ProgressBarView` under the HUD canvas — **`HUD_Fish.prefab` is not edited**; optional prefab
  references let the user move it into the prefab later. Icons use the creature sprites inside a bubble disc.
- Position is re-measured every frame in the canvas's own space
  (`RectTransformUtility.ScreenPointToLocalPointInRectangle` with the canvas's camera), so it follows live `progressBarSpace` edits and the AdSim capture (canvases switched to
  ScreenSpaceCamera into a 1080×1920 texture). It uses the steady (unshaken) container edge.
- The Fish container moves up ~90 px (1080-ref ≈ 270 units) via a new Fish-only bottom-margin field so the bar sits
  below it on the backdrop.

## 6. Level complete / fail

- **Complete (celebration, user-approved 2026-10-09):** `GameController.celebration` (`LevelCelebration`) +
  `UI/LevelCompleteView.cs` (built hidden at level start, played at the win), `UI/RayFan.cs`,
  `Resources/Shaders/UIAdditive.shader`. Timeline on `WinClock` (GameTime): goal merge = shake + haptic + fanfare
  (`Sfx.LevelWin`) + slow-mo (`slowMoScale` 0.3 for `slowMoSeconds` 0.4), no particles at the merge or the bar;
  0.70 s dim (`dimAlpha` 0.82); 0.80 s the goal fish punch-scales in place at the screen centre over the **unlock
  burst**: slowly turning additive ray fan (`rayTurnSeconds` 49, `rayCount` 29, white/cyan `rayColor`, `rayBlur`
  0.4), breathing white core glow, additive glow particles (`glowParticlesPerSecond` 22), confetti burst
  (`burstConfetti` 72) and rising bubbles (`bubblesPerSecond` 4); 1.05 s "LEVEL n", 1.2 s "COMPLETE!" letter by letter,
  1.55 s green "<FISH> UNLOCKED!" ribbon (`FishLook.Name`), 1.8 s BEST time, `tapAfter` 2.3 s "tap to continue"
  (kept above the bar; taps ignored before it); confetti rain while up (`celebrationConfetti`, `confettiPieces` 30).
  `panelAt` 0.7 = when gameplay freezes. Mocks: `.superpowers/brainstorm/…/level-complete-juice.html`, `burst-v1/v2.html`.
  Bot: `-blobwinshots` (needs `-blobshot`) → `win0..5.png` at +0.1…2.6 s. Tap → next level, fresh board.
- **Fail:** existing game-over panel; tap → reload the same level.
- Ads: the AdSim director keeps working. It sets `GameController.AdTake` on every take (and `ValueOverride` unless
  `laneBias=0`); either one switches queue rules and level wins off, and `AdTake` also skips every save (level, best
  time). The take restarts the bar at a fixed level 3 (goal Dolphin 128) so every take shows the same goal; the
  director's queue and take length are unchanged, and the bar shows and fills, which also works as ad content.

## 7. Queue balancing — anti-streak rules (`LaneSpawner.NextValue`)

> **Superseded by §0b (2026-10-12):** the pity rule (item 2, `pityAfter`, `pityMaxValue`) is removed; the streak rule
> stays (it now zeroes the streak tier's weight in the spawn table). Kept below as history.

Applied after the weighted roll, only when `ValueOverride == null`:
1. **No streaks:** if the last `maxSameInRow` (2) spawns equal the roll, reroll (excluding that value).
2. **Pity:** count consecutive spawns that had **no equal value** in lane or container at spawn time. When the count
   reaches `pityAfter` (3), the next spawn = the smallest value present in lane/container within the level's spawn
   range (≤ `pityMaxValue`); reset the counter. A pity spawn **may override the streak rule**: it is the one allowed
   3-in-a-row exception (relief beats variety).
3. Tunables on `GameController.lane`: `maxSameInRow`, `pityAfter`, `pityMaxValue`; `0` disables a rule.
   Game/Hexa get the rules off by default (no behaviour change there).

## 8. Code plan (files)

- `Assets/Scripts/Progression.cs` — level table, current/upcoming/goal, merge points, save/load, win detection.
- `Assets/Scripts/UI/ProgressBarView.cs` — the bar (runtime-built UI, tweened).
- `GameController.cs` — hooks: on merge → `Progression.OnMerge(value)`; level start/reset; level-complete flow;
  Fish-only container margin + radius curve fields (new names, code defaults = today's behaviour).
- `LaneSpawner.cs` — spawn range, 32/64 ramps, anti-streak + pity.
- `HudView.cs` — level-complete panel (built at runtime if the prefab lacks it).
- `DebugAutopilot.cs` — `-level <n>` flag, log lines below.

## 9. Verification (bot, `test.ps1 -scene Fish`)

> **Pity parts superseded by §0b** (pity removed, `-blobpity` gone; the queue line is now
> `queue: spawns= maxRun= rerolls= refusals= locks= lockedSpawns= seq=`). History below.

Log lines: `[BlobMerge] level n start goal=… range=…`, `level n complete t=…s throws=…`,
`best time level n: …s new=…`, `level n failed t= throws= best=`,
`record fish v at t=… throws=… barBefore=…%` (uncapped points ÷ (next × factor) just before the record),
`queue: spawns= maxRun= pity= rerolls= seq=`.
Bot flags: `-blobselftest` (29 checks: pure maths + best-time save, run with `-secs 14`), `-level n`,
`-blobcontinue` (keep playing after a win/loss), `-blobpity n`, `-blobfilltest`. Screenshots of the bar mid-level,
at a record fish, the level-complete panel. Bot wins save `BlobMerge.Fish.Level` in the Windows player's
PlayerPrefs, so always pass `-level n` for reproducible Fish runs.
Checks: per-level times vs. the table (normal persona); bar at 85–95 % on record fish; zero 3-in-a-row spawns except
pity overrides; pity fires; 4096 chain fits (§4); Game and Hexa runs unchanged (no rules fire, summary numbers in the
same range as a baseline run taken before the change — runs are random, so not identical).
Implementation plan: `Docs/FISH_PROGRESSION_PLAN.md`.

**Result (2026-10-09):** selftest 27/27 PASS; Fish levels 1–8 all completed by the bot (table in §3); queue in Fish
never shows a run > 2 and pity fires (`pity=1 rerolls=1–3` in 23–40 spawns); regression Game/Hexa 60 s: throws 49/49,
pileMerges 41/40, `pity=0 rerolls=0`, no `level` lines (baselines 49/41, 49/39; laneMerges is random). The AdSim ad
run was **not** repeated (ads verified by code review: `ValueOverride` switches queue rules and level wins off).
**Final fixes (2026-10-09):** selftest 29/29 PASS; Fish `-level 1` win logs `best time level 1: 15.8s` and the panel
shows `BEST 0:16`; Fish ad take `-variants 30` ran the full 30 s (900 frames, `fish adTake=1 level=3 goal=128`, no
`level n complete`), bar below the container and fully on screen in the 1080×1920 video; Game 30 s: no level lines,
`pity=0 rerolls=0`, no exceptions.

**User next steps:** Fish.unity on disk holds none of the new fields, so a fresh load gets the right code defaults.
If the Fish scene was open in the Editor during the change, re-open Fish.unity from disk WITHOUT saving (or reset on
the Game object: fishRadiusPerTier 0.10, fishBigRadiusPerTier 0.05, progressBarSpace 2.2, progression.levels table,
levelBarFactor 0.75, lane maxSameInRow 2 / pityAfter 3 / pityMaxValue 64). Setup Project was not run.

## 10. Out of scope (later)

Rewarded-ad continue · level select / map screen · meta rewards.
