# BlobMerge — Agent Guide

Everything an agent needs to keep working on this project: what the game is, every decision the user made (and why),
how the code is built, how to test without touching the user's open Editor, and the traps already hit.
Keep this file current when you change behaviour. Last updated: 2026-10-07.

---

## 1. What this is

A portrait **mobile (Android) soft-body number-merge puzzle** in **Unity 6000.0.67f1 + URP**.
Blobs carry values 1, 2, 4, 8 … (doubling). Blobs float in a **top lane**; the player **presses a lane blob, drags
toward a target and releases** (catapult/crossbow, longer drag = more power). Thrown blobs fall into a **container**,
squish like soft balloons, and **equal values merge into the doubled value** (also in the lane). Game over when a
resting blob stays above the **danger line**. Score = sum of merge results; best score saved in PlayerPrefs.

Origin: `GPT/reference_game_spec_for_claude.md` (a GPT-written brief) + reference clip `GPT/ref/01.mp4`
(2194×2134, 50 s). **The user corrected many of the brief's guesses — section 3 overrides the brief.**
Visual target for the environment: a purple "aquarium" mockup the user supplied (deep purple BG, thick rounded purple
frame, wavy water line under the lane, bubbles, score panel with "SCORE" tab, crown + best, gear button).

## 2. Locations & environment

| What | Where |
|---|---|
| Workspace root | `F:\Projects\BlobMerge` (not a git repo) |
| Unity project | `F:\Projects\BlobMerge\BlobMergeUnity` |
| Unity editor | `C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe` (Android module + SDK/NDK/JDK installed) |
| Scene | `Assets/Scenes/Game.unity` (generated: camera + `Game` object) |
| Agent tools | `F:\Projects\BlobMerge\Tools\test.ps1`, `sag.ps1`, `build-android.ps1` |
| Build mirror / outputs | `F:\Projects\BlobMerge\_testclone` (private copy for batch builds), `_testout` (screens/logs) |
| APK | `BlobMergeUnity/Builds/BlobMerge.apk` (debug-signed) |
| Reference spec / clip | `GPT/reference_game_spec_for_claude.md`, `GPT/ref/01.mp4` (ffmpeg is on PATH) |
| Unrelated older project | `F:\Projects\UnityProjects\BlobMerge` — **never touch** |
| AdSim ad recorder | `Assets/AdSim/`, `Assets/Scripts/Ads/`, `Tools/ad.ps1` → `_adout/` — see §12 |

The user (Muzammil, Mindstorm Studios) usually has **the project open in the Unity Editor**. Batch mode cannot open a
project that is open → always build via the mirror (`Tools/test.ps1`). Scripts you edit get hot-reloaded in their
Editor; scene-serialized values in *their* `Game.unity` keep old values until they run **Tools → BlobMerge → Setup
Project** (which regenerates the scene) — new fields simply get code defaults.

## 3. Design decisions from the user (do not revert)

In order of the conversation; later ones win.

1. **Start state:** board empty; lane starts with **1, 2, 4**; over time **1 gets rarer** and **8** (later 16) appears.
2. **White dot in the reference means nothing.** No cue ball, no `LaunchMode`. Direct catapult throw of a blob.
3. **No combos.** Score = merge values; progression 2, 4, 8, 16, 32, 64, 128 …
4. **Blob visuals:** "2D sprite on a 3D blob mesh" → implemented as procedural meshes with a glossy bubble look.
5. **Android only.** URP created by our setup.
6. **Water-bubble physics:** blobs must be soft, alive the whole game (not rigid, not "dead").
7. **Aiming:** press a blob and **drag TOWARD** where it should go (not a pull-back slingshot); longer drag = more power.
   Aiming is its own component (`AimController`). Trajectory must follow the finger.
8. **Lane is not a fixed row:** blobs enter from **both left and right walls**; the player may throw **any** of them.
9. **Production juice** wanted everywhere (particles, merge/creation animations, physics feel, shake, haptics).
10. Bounce must be adjustable (wall / bumper / blob-blob).
11. **Softness = balloon pressure** ("a 16 resting on the floor should flatten at the bottom") — not jiggle.
12. **Irregular, lumpy shapes** at spawn (potato/pill-like), kept while still squashable.
13. **Merging in the lane** too: same values drifting together auto-merge (also two arriving in sequence).
    **Fish: no automatic lane merges** (user 2026-10-12): lane blobs never merge on their own and the magnet is off; a thrown blob merges with a same-value lane blob (also during `launchPassThroughTime`) — `GameController.laneAutoMerge` (false; Blobs/Hexa always auto-merge).
14. **Controllable gap** between blobs.
15. **Lane blobs smaller** (start scale) and **tween to full size during the throw** — never "land then grow".
    User sets both scales himself (`laneScale`, `landedScale`).
16. Removed: grey bars on the container top, side bumpers (kept behind `sideBumpers=false`).
17. **Camera shake** on max aim stretch and on merges in the container.
18. **Purple aquarium environment** from the user's mockup.
19. **All text = TextMeshPro, Barlow Bold, white face, thin dark-blue outline.**
20. HUD moved up 30 px; **HUD must be editable by the user** → `HUD.prefab`.
21. The user prefers concise answers, measured verification (numbers/screenshots) and tunables exposed in the
    inspector with clear tooltips.

## 4. Architecture

Everything is built at runtime by one component (`GameController` on the `Game` object). No Unity Physics/Physics2D —
a **custom soft-body solver**. Every mesh uses **one vertex-colour, alpha-blended unlit shader**.

```
Assets/Scripts/
  GameController.cs      orchestrator: settings, fixed 60 Hz physics loop, spawns, merges, scale tween, juice,
                         danger/game over, camera fit + shake, HUD hookup. ~700 lines, all tunables here.
  Blob.cs                one numbered blob: owns SoftBody, renders it (fill + sheen + outline), TMP label,
                         state machine, size spring, flash, chain counter, grow tween fields.
  Physics/SoftBody.cs    pressure balloon: Verlet ring + gas pressure + slack skin + affine shape matching
                         (with squash clamp) + min-volume safety + deformation damping.
  Physics/SoftBodyWorld.cs  sub-stepped solver: half-planes (walls/floor/chamfers/ceiling), circles (bumpers),
                         body-vs-body point-in-polygon push-out with Gap, restitution, contacts, PredictPath().
  AimController.cs       input (mouse/touch or simulated), pick, drag→direction/power, arrow + trajectory dots.
  LaneSpawner.cs         [Serializable] settings + logic: side spawning, value odds, float springs, magnet.
  Board.cs               container visuals (frame, field vignette, water band/wave, bubbles, danger bar,
                         doors) + collision planes.
  MeshUtil.cs            vertex-coloured mesh builders (polygon, fan, filled, rounded, disc, ring, arrow), Lin().
  Fx/Fx.cs               pooled particles in one mesh (droplets, sparkles, rings), floating "+N" TMP texts,
                         trauma camera shake (offset + roll, Sustain()).
  Fx/Sfx.cs              procedurally generated sounds + Android haptics (VIBRATE permission via Handheld ref).
  UI/HudView.cs          HUD prefab component (refs, safe area, score punch, game over) + default Build().
  UI/TextStyle.cs        shared TMP font + outline material for all text.
  UI/UiSprites.cs        SDF-rasterised sprites (rounded rect 9-slice, crown, gear, circle).
  DebugAutopilot.cs      command-line bot + diagnostics (does nothing without args).
  Editor/ProjectSetup.cs Tools menu: Setup Project, Build Android APK, BuildWindowsTest, SetupAndBuildAndroid.
  Editor/FontSetup.cs    [InitializeOnLoad] imports TMP essentials, creates Barlow SDF font asset (with retry).
  Editor/HudPrefabBuilder.cs  creates HUD.prefab, outline material, sprite PNGs (never overwrites; Rebuild menu).
Assets/Shaders/VertexColor.shader   "BlobMerge/VertexColor": unlit, SrcAlpha/OneMinusSrcAlpha, ZWrite Off, Cull Off
Assets/Materials/VertexColor.mat
Assets/Fonts/Barlow-Bold.ttf (+ Barlow-OFL.txt)
Assets/Resources/Fonts/Barlow-Bold SDF.asset, Barlow-Bold Outline.mat   (TextStyle loads these by name)
Assets/Resources/UI/HUD.prefab                                          (GameController loads if hudPrefab empty)
Assets/UI/Sprites/RoundedRect.png, Crown.png, Gear.png
Assets/Settings/URP_Asset.asset, URP_Renderer.asset
```

### Frame flow
`GameController.Update`: apply inspector physics settings to the world (live) → accumulate time → up to 3 ×
`PhysicsTick(1/60)` → `lane.Tick` (spawning) → `CheckDanger` → `Blob.TickVisual` (mesh rebuild) → `board.Tick` →
camera shake. Game over sets `Time.timeScale = 0`; tap restarts (scene reload).

`PhysicsTick(h)`: per blob `TickPhysics` (size spring) → grow tween (`TickGrow`) → clear lane pass-through →
lane forces (`LaneSpawner.ApplyForces`) → merge jobs (`TickMerge`) → `world.Step(h)` → contacts (impact FX,
`TryMerge`) → wall/floor impact FX → flight trails.

### Blob states (`BlobState`)
`Lane` (floating, no gravity, lane springs, heavier `MassScale`) → `Aiming` (held; never merges) → `Free`
(gravity, in play) → `Merging` (pulled to partner, then released and replaced).

### Collision groups (`SoftBody.Group` / `IgnoreGroup`)
`LaneGroup = 1`. A thrown blob ignores lane blobs for `launchPassThroughTime` (0.25 s) so neighbours can't knock it
off its aim. A merging pair gets a unique group (`MergeGroupBase + id`) and ignores each other so they fuse.

### Sorting orders (all transparent, vertex colour)
Backdrop −20 · Frame/field 0 · water+bubbles+danger 1 · doors 3 · selection outline 9 · blob 10 · labels 20 ·
FX 30 · aim guide 40 · floating texts 50 · HUD canvas (overlay) 10.

### Units
World units: board 8 × 14 (x ∈ [−4, 4], floor y = −7). Small blob radius ≈ 0.5, `radius = base + perTier·tier`
(tier = log2(value)), capped 1.75. Camera is orthographic, fit to board + `hudSpace` above.

## 5. Systems in detail

### 5.1 Soft body (the "balloon")
Per blob: `pointsPerBlob` (20) Verlet points in a CCW ring. Each substep (4/tick, 2 constraint iterations):
- **Integrate:** gravity (if `UseGravity`) + `ExtraAccel` + **gas pressure**: outward push on each point
  ∝ `Pressure · g · (A0/A)` (ideal gas, ratio clamped ≤ 4). `pressure` is in units of gravity: ~1 = each skin point's
  outward push equals its weight. Big blobs get `pressure · largeBlobPressureScale`.
- **Slack skin (`SolveEdges`):** edges resist stretching (`edgeStiffness`) but only weakly resist compression (12%) →
  a balloon can sag.
- **Affine shape matching (`SolveShape`):** pulls toward the best-fit *affine* copy of the lumpy rest outline →
  keeps lumps, allows squash/stretch/rotation. Then **`ClampAspect`** (polar decomposition) limits the stretch ratio
  to `maxSquash` so lumpy blobs can't become slivers.
- **`SolveMinArea(minVolume)`:** crush limit (default 55% of rest area).
- **`DampDeformation`:** damps only motion relative to the body's mean velocity → soft/doughy, not jiggly, flight unaffected.
- Rest shape: random harmonic lumps (`silhouetteNoise`) + random elongation along a random axis (`elongation`),
  normalised to the circle area of `radius`.
- Size changes use `AreaScale` (area multiplier; linear scale = √AreaScale).

Measured sag (256 at rest, width/height): pressure 1.5 → melted (~2.2, loses ~½ volume) · 2.5 → 1.55 · 3–4 → ~1.4
(balloon look; default 3.5) · 6 → 1.1 · 10 → 1.0. **Rigid shape matching even at 0.005 prevents sag** (that's why
matching is affine).

### 5.2 World / collisions
- Static geometry = half-planes (walls, floor, ceiling, two 45° bottom chamfers) and optional circles (bumpers).
  Wall response: restitution `wallBounce`, tangential `wallFriction` per substep, records `Impact`/`ImpactPoint`.
- Body vs body: AABB reject → each point of A within `Gap` of / inside polygon B is pushed to distance `Gap` outside
  B's nearest edge (mass-weighted between the point and the two edge points). Approaching faster than
  `bounceMinSpeed` → restitution `blobBounce`; otherwise `contactDamping`.
- **Contacts** are reported when within `Gap + ContactSlop (0.03)` → merges work with a gap.
- `PredictPath()` replays the exact integration (substeps, damping, speed cap, planes, circles) for a point of radius r —
  used by the aim preview. Verified: predicted vs real position after 11 ticks, median error ≈ 0.00–0.03 units.

### 5.3 Lane (`LaneSpawner`)
- Keeps `laneCount` (3) throwable blobs; a merging pair up top counts as one.
- Start values `startValues` {1,2,4} staggered; then weighted odds: w1 drops per shot to a floor, 8 after
  `eightAfterShots` (6), 16 after `sixteenAfterShots` (24).
- Spawns at the wall with more free room, entering at `entrySpeed`, starting tiny (`laneSpawnStartArea`) and
  springing to lane size; door flash + splash + sound.
- Float forces: vertical spring to `LaneY`, weak centering, damping; `aimPullAccel` leans the held blob back;
  **`sameValueMagnet`** pulls a lane blob toward the nearest equal value within `magnetRange` (without it half-size
  lane blobs never touched → lane merges dropped from ~14 to 1 per minute; with it ~20).
- **Fish: no automatic lane merges** (user 2026-10-12): lane blobs never merge on their own and the magnet is off; a thrown blob merges with a same-value lane blob (also during `launchPassThroughTime`).

### 5.4 Aim (`AimController`)
- `PickBlob` = nearest `Lane` blob within `CurrentRadius + pickPadding`.
- `AimMode.TowardFinger` (default): origin = blob centre at grab; dead zone `minDrag + 0.6·r`; power =
  ((dist − dead)/`maxDrag`)^`powerCurve`; speed = lerp(`minSpeed`, `maxSpeed`, power). `DragDelta` = legacy gesture mode.
- Arrow tip sits under the finger; dotted path from `PredictPath`.
- Pointer is converted through the **un-shaken camera** (`GameController.UnshakePoint`, uses the camera's actual roll).
- `SimEnabled/SimDown/SimUp/SimPos` let the bot drive the real input path.

### 5.5 Merge
- Rule: same value, both mergeable (`Free`, or `Lane` if `mergeInLane`; never `Aiming`), on contact.
- **Fish: no automatic lane merges** (user 2026-10-12): lane blobs never merge on their own and the magnet is off; a thrown blob merges with a same-value lane blob (also during `launchPassThroughTime`): Lane+Lane is refused unless `LaneAutoMerge`; a `Free` blob still in its pass-through window merges on overlap (log `lane merge by throw`).
- `MergeJob` (`mergeDuration` 0.12 s): both shrink to 70% of *current* size, attract (`mergeAttraction`), ignore each
  other; then both released and one blob (value×2) spawned at the mass-weighted midpoint: in lane (stays `Lane`,
  lane size) if either source was in the lane and the midpoint is above the danger line, else `Free` at `landedScale`.
  Starts at `mergeStartArea` and pops via the size spring (pushes neighbours).
- Juice: white flash, two droplet bursts (old/new colour), sparkles, two rings, "+N" text, pitched pop (rises per tier
  and per chain), haptic, shake (`pileMergeShake + perTier·tier` in the container, `laneMergeShake` in the lane).

### 5.6 Scale (lane → pile)
`laneScale` (start) and `landedScale` (end) are **linear**; area = scale². On launch `Growing=true`; `TickGrow` sets
`AreaScale` directly (bypassing the spring) with smoothstep of `max(time/scaleTweenDuration, fall progress)`, where
fall progress is measured to the **surface below** (floor or top of the pile under the blob). Verified: blobs reach full
size before contact (21/21 tracked throws).

### 5.7 Danger / game over
A `Free` blob older than 1.2 s, slow (<~1.2 u/s) with centre + 0.5 r above `DangerY` for `dangerHoldSeconds` → game
over (danger bar pulses pink while counting). Time freezes; tap after 0.8 s reloads the scene.

### 5.8 Juice inventory
Launch: stretch along direction (`launchStretch`), puff, ring, whoosh (pitch by power), `launchShake·power`.
Max stretch (power ≥ `aimMaxThreshold`): kick `aimMaxKick` + click/thump + haptic + blob flash, then `aimMaxRumble`
while held. Impacts (> `impactFxSpeed`): splash + thud/plop + small shake. Trails above `trailSpeed`. Spawn: door
flash, splash. Shake model: trauma (0..1), strength = trauma², offset `shakeMaxOffset`, roll `shakeMaxAngle`.
Measured peaks: pile merge 0.06–0.18 units, max-stretch kick 0.03–0.07, lane merge 0.01–0.04.

### 5.9 Board visuals (`Board`)
Rounded/bevelled purple frame (dark base, main, highlight bands, inner lip) with soft drop shadow, vignetted field
(fan gradient), backdrop vignette, darker lane band above an animated **wave line** at `WaterY = LaneY −
waterLineBelowLane`, `bubbleCount` drifting bubbles (fade near the water), rounded lavender **danger bar**, lavender
door bars. Palette constants at the top of `Board.cs`.

### 5.10 HUD & text
- `HUD.prefab` (`Assets/Resources/UI/`) with `HudView` on the root: `safeArea`, `topBar`, `scoreText`, `bestText`,
  `settingsButton` (no action yet), `gameOverPanel`, `gameOverText`, `scorePunchScale`, `gameOverFormat`.
  The user edits it in Prefab Mode. `HudPrefabBuilder` never overwrites it (only the explicit Rebuild menu).
- If the prefab is missing, `HudView.Build()` makes the same layout at runtime with generated sprites.
- Reference resolution 1080×1920, match 0.5. Top elements: panel y −175, crown/gear y −170, best y −140
  (top-anchored). `hudOffsetY` adds to `topBar`.
- **All text** (HUD, blob labels, floating texts) = TMP with Barlow Bold SDF + **one shared material**
  `Barlow-Bold Outline.mat` (outline width 0.18, face dilate = width/2; the user darkened the outline colour). Edit the
  material to restyle all.
- **Blob numbers and floating "+N" texts copy the HUD prefab's style** at startup: `HudView.worldTextStyle` (empty →
  the Score text) → `TextStyle.CopyFrom` (font, shared material, face colour, `fontStyle`). The user styles the HUD
  texts with TMP **Bold** (`fontStyle = Bold`); keep it that way so in-game numbers match.
  Blob labels: `TextMeshPro` 3D with a **constant digit height** `blobLabelHeight` (0.36 world units) on every blob,
  whatever its value, digit count, state or size (user requirement: no auto-size, no scaling with lane/tween/pop).
  Font size is derived once by measuring "8" (`Blob.fontSizePerUnit`). Only while a lane blob pops in from almost
  nothing does the label shrink with it. `blobLabelFollowsSize` re-enables scaling. Verified via the bot's
  `label heights:` log line (all 0.360).

### 5.11 Variants: one scene each (`Game`, `Hexa`)
Each variant is its own scene with its own `Game` object = its own copy of **every** tunable, its own HUD prefab and
its own `BoardTheme` asset (background, frame, field, water, bubbles, danger bar, **piece colours**):
- `Assets/Scenes/Game.unity` — blobs, `HUD.prefab`, `Themes/BoardTheme_Purple.asset`.
- `Assets/Scenes/Hexa.unity` — `pieceShape = Hexagon` + `GameController.ApplyHexaPreset()` start values (pressure 1,
  shape stiffness 0.08 = spring-back speed, maxSquash 1.6), `Resources/UI/HUD_Hexa.prefab` (one-time copy of HUD),
  `Themes/BoardTheme_Hexa.asset` = **dark galaxy** (user feedback: no vignette → linear multi-stop `gradient`;
  stars subtle — small, low `starOpacity`, gentle twinkle, no sparkle crosses; subtle animated props: `nebulaClouds`,
  `distantGalaxy`, `shootingStarEvery`). Space props draw in the `Space` mesh (order −15) behind the frame; the frame is
  built from bands around the opening (`MeshUtil.AddBand`) so a field colour with alpha < 1 = tinted glass showing the
  galaxy through. Opaque field (purple theme) looks identical to before.
  **CURRENT (user pick 2026-10-08): `BoardTheme_Hexa` = the blue-white look (same values as `BoardTheme_HexaLight`),
  original candy piece colours, no texture. Galaxy options below were tried and set aside; their code options stay.**
  v3 (discarded, from the user's flat-vector "Space Survival" reference): diagonal violet→magenta→crimson gradient,
  dark plum frame with coral rim, blinking stars + diamond sparkles (`sparkleShare`, `blinkSharpness`), coloured
  full-screen shooting stars (`shootingStarColors`, `shootingStarCrossTime`), floating flat rocks (`debris*`), piece
  palette re-tuned (mint/lavender/coral/pink…). Previous galaxy kept as `BoardTheme_HexaNebula`.
  Hex textures: `GameController.hexTexture` = None | Camo | Stripes | Craters | MixByValue (`Blob.BuildTexture`:
  pattern polygons clipped to the hexagon once, drawn through `SoftBody.FitAffine` so they turn/squash with the piece);
  bot `-hextex <name>`. Concept sheet: `Branding/themes/hexagons/hex_texture_concepts.png`. The earlier blue-white look is kept as
  `Themes/BoardTheme_HexaLight.asset` (assign it to Hexa's `boardTheme` to switch).
Hexagons: `SoftBody.HexOutline` + `Rigidity` (shape target blended toward a rotation-only fit → always springs back);
only `hexCornerRound`, `hexPointsPerSide`, `hexRigidity` are hex-only, everything else uses the normal fields.
Look = `Blob.RenderHex`. Setup Project regenerates **both** scenes (code defaults + preset) but never overwrites theme
assets or HUD prefabs. Scripts: `test.ps1 -scene Hexa`, `build-android.ps1 -scene Hexa` (→ `BlobMerge_Hexa.apk`,
app id `….hexa`, name "BlobMerge Hexa"), `ad.ps1 -scene Hexa`. Bot flag `-blobhex` applies the preset in any scene.
Plan, numbers, mocks: `Docs/THEME_HEXAGONS.md`.
- `Assets/Scenes/Fish.unity` (2026-10-08) — `pieceShape = FishBubble` + `ApplyFishPreset()` (round balloons: noise 0.02,
  pressure 7, maxSquash 1.5, minVolume 0.75), `HUD_Fish.prefab` (HUD copy recoloured navy/ocean blue),
  `Themes/BoardTheme_Fish.asset` (cyan→navy gradient behind a glass field, rising bubbles, `lightRays` 6, teal frame
  with cyan rim, bubble tints per creature). Creatures: `Resources/Fish/fish_00..09.png` (1 fry, 2 neon tetra, 4 clownfish,
  8 pufferfish, 16 angelfish, 32 turtle, 64 octopus, 128 dolphin, 256 shark, 512 sting ray, 1024 hammerhead, 2048 orca, 4096 whale (reordered by the user 2026-10-12; files fish_09..12); Higgsfield, 256 px) drawn by a
  SpriteRenderer with `Resources/Materials/FishSprite.mat` (shader `BlobMerge/SpriteUnlit`) between the bubble body
  (order 10) and its gloss (12); the number sits under the creature (`fishLabelDrop`). Creature turns only on real
  travel (|vx| > 2.5, Free), shrinks to fit a squashed bubble. Tunables `fish*`, `bubbleTint`, `bubbleRim`.
  **Alive (`FishLook.cs`):** each creature is a 12×12 textured grid bent by a travelling wave per type (tail + fin
  flutter / octopus tentacles / turtle flippers; amplitude & speed per tier) and blinks: skin-coloured eyelids (+ lash
  line) close over eyes measured on the sprites (`FishLook.Eyes`, sprite-local units) every `fishBlinkMin..Max` s,
  0.16 s, 15 % double blinks. Order: bubble 10, creature 11, lids 12, gloss 13. New sprites need new eye entries.
  **Audio:** `Sfx.Bubbly` (set from `FishMode` before the Sfx component wakes) swaps in `Sfx.RenderBubbly()`:
  `b_pop` merge (click + rising plip + blub, still pitched up per tier/chain), `b_blub` landing, `b_whoosh` throw,
  `b_spawn` trickle, `b_plink` grab, and a 24 s seamless `ambience` loop (`ambienceVolume` 0.35). Own RNG seed, so the
  default sounds stay bit-identical (verified). Ads: director mixes `RenderAll()` + the ambience loop under the take.
  Exports: `Branding/themes/fish/sfx/`, preview `fish_sfx_preview.wav`.
  **Progression v2 (2026-10-09, Fish only; spec `Docs/FISH_PROGRESSION.md` §0):** one continuous game, no levels.
  `GameController.progression` (`Progression.cs`, `ProgressionMath.cs`, `ProgressionOn`) keeps lifetime progress in
  PlayerPrefs `BlobMerge.Fish.Highest` (highest tier ever made, start 2: first-ever celebrations + Orca panel); not saved
  in ad takes. **`WindowStart` is per game (user 2026-10-12):** every game starts at 0 (bar fry..sting ray, lane 1/2/4),
  this game making the bar's last creature shifts it (`GameHighest`), game over = fresh board + window 0; the old saved
  `BlobMerge.Fish.WindowStart` key is deleted on load. Lower HUD =
  `UI/CreatureBarView.cs` (11 tiers `[WindowStart … +10]`, `windowSize` 11, stepped path 6 lower + 5 upper drawn by
  `UI/PathStroke.cs`, placed every frame in the canvas's own space between container bottom and screen bottom, shrunk
  to fit narrow canvases; container moved up by `progressBarSpace`). A new creature (lane spawn or merge) flies from
  its spot into its slot (0.7 s), the slot pops (ring, additive `UIAdditive` flash, 8 sparkles), "NAME!" floats over
  the tank; play never stops. Making the bar's last creature shifts the bar (first icon fades, the rest slide, the next
  pops in; a known creature shifts via `CreatureBarView.QueueShift` without the fly-in), `WindowStart++`, then every blob of the dropped tier pops (no score); lane values are scaled to
  ≥ `SpawnFloor` = 2^WindowStart (`QueueRules.Scale`, not when the director sets `ValueOverride`). Orca first ever =
  `LevelCompleteView` once ("ALL CREATURES / FOUND!", "ORCA UNLOCKED!", slow-mo, tunables `GameController.celebration`),
  tap resumes the same game. **Queue v2 (2026-10-12, spec §0b):** `SpawnTable.cs` (pure: pool `[WindowStart … top]`,
  weights decay/small-fade, sawtooth difficulty, lock length) + director in `LaneSpawner.DirectorTier` (only when
  `ProgressionRules`): R = biggest tier made this game (`lane.GameBest`, spawns + merges via `lane.NoteValue`), refuses
  helpful candidates with chance D × help (exposed tank match `helpExposed` / lane match `helpLane`), locks the most needed
  creature; `maxSameInRow 2` stays; pity removed. Tunables `lane.pool*`, `difficulty*`, `relax*`, `climbSeconds`,
  `grace*`, `help*`, `refusalTries`, `lock*`. Game/Hexa keep the old shot-based `Roll()` untouched. Ad takes: the director only sets
  `AdTake` (bar shows the saved window, celebrations play, nothing saved, no orca panel). 13 creatures (tiers 10–12
  sting ray/hammerhead/orca, `fish_10..12`). Radius `fishRadiusPerTier 0.115` (original look, restored by §0b;
  +`fishBigRadiusPerTier 0.05` above tier 9).
  **Setup Project generates Fish.unity only if missing** (it is the user's scene). Scripts: `-scene Fish`
  (APK `BlobMerge_Fish.apk`, id `….fish`). Mocks/sprites: `Branding/themes/fish/`.

## 6. Tuning cheat-sheet (GameController inspector)

| Want | Change |
|---|---|
| Softer / flatter balloons | `pressure` ↓ (2.5–3.5), `largeBlobPressureScale` ↓ |
| Firmer / rounder | `pressure` ↑ (6–10) |
| Less jiggle / more doughy | `deformationDamping` ↑ (0.12–0.2) |
| Keep lumps more | `shapeStiffnessSmall/Large` ↑ (affine, doesn't stop sag) |
| Lumpier spawn shapes | `silhouetteNoise`, `elongation` |
| Stop slivers / allow more squash | `maxSquash` (1.5 firm … 3 floppy) |
| Bounce | `wallBounce`, `blobBounce`, `bumperBounce`, `bounceMinSpeed` |
| Space between blobs | `blobGap` (0.04–0.12 looks good; 0.2 squeezes small blobs) |
| Lane/landed size | `laneScale`, `landedScale`, `scaleTweenDuration` |
| Throw power feel | AimController `minSpeed`, `maxSpeed`, `maxDrag`, `powerCurve` |
| More lane merges | Lane `sameValueMagnet`, `magnetRange`, `centeringSpring` |
| Shake | Camera shake section (values are trauma; strength = value²) |
| Text outline | `Assets/Resources/Fonts/Barlow-Bold Outline.mat` |
| HUD layout | `Assets/Resources/UI/HUD.prefab` |

Physics values apply **live** in Play mode (`ApplyPhysicsSettings` each frame). Shape noise/elongation/radius only
affect newly spawned blobs. Board geometry/visual values apply on Play start.

## 7. Testing workflow (agents)

```powershell
& F:\Projects\BlobMerge\Tools\test.ps1 -secs 60            # mirror → batch build (Windows) → bot plays → screenshots
& F:\Projects\BlobMerge\Tools\test.ps1 -secs 60 -nobuild   # replay last build
& F:\Projects\BlobMerge\Tools\test.ps1 -secs 30 -width 540 -height 1170   # phone aspect 19.5:9
& F:\Projects\BlobMerge\Tools\sag.ps1 -pressures 2.5,3.5,6 # softness measurement
& F:\Projects\BlobMerge\Tools\build-android.ps1            # APK (needs sandbox disabled)
```
Then `Read` the PNGs in `_testout` and grep `player.log` for `[BlobMerge]`.

`BuildWindowsTest` **regenerates the scene in the mirror** (so code defaults are tested) and sets `runInBackground`.

### DebugAutopilot command line
| Arg | Effect |
|---|---|
| `-blobauto <s>` | bot plays for s seconds (presses lane blobs, drags toward matching blobs or random targets; first throw and ~25% go to max power), then quits |
| `-blobshot <prefix>` | screenshots at 1.8, 2.08, 2.3 s (mid-aim), 35%, 70%, 100% (+ `over.png` on game over) |
| `-blobsag` | sag test: drops 256/64/16/4, photographs at 3.5 s |
| `-blobpressure <p>` `-blobshape <k>` `-blobgap <g>` | override those settings for the run |
| `-blobselftest` | run the progression/queue/spawn-table checks (44, `selftest PASS/FAIL`), use `-secs 14` |
| `-fishreset` | Fish: clear `BlobMerge.Fish.Highest` (first load only). Bot runs save progress in the Windows player's PlayerPrefs, so pass `-fishreset` or `-fishstart` for reproducible Fish runs |
| `-fishstart <highest>[,<window>]` | Fish: save lifetime highest (tier); the optional window is the first game's bar start (game highest = min(highest, window+9) so the bar's last creature still shifts), e.g. `9,0`, `11,2` |
| `-fishspawn <v>,<v>` | Fish: drop these side by side at 1 s (they merge): `512,512` with `-fishstart 9,0` = sting ray + shift; `2048,2048` with `11,2` = orca panel |
| `-blobfishshots` | with `-blobshot`: `fish<k>_0..3.png` at +0.1/0.4/0.8/1.4 s after each new fish / window shift |
| `-blobwinshots` | with `-blobshot`: orca panel `win0..5.png`; the bot then "taps" (`ContinueAfterCelebration`) |
| `-blobcontinue` | after a game over, reload and keep playing (Fish progress kept) |
| `-blobspeed <k>` | game clock × k (`Time.timeScale`; physics still 1/60 s ticks, max 3 per frame: k = 2 keeps 60 ticks per game second, 3 drops to ~53). Calibration runs |
| `-fishresetgames` | Fish: clear the saved lifetime highest on **every** load (each continue game is a first-time player) |
| `-blobnodirector` | Fish: `lane.difficultyOn = false` (plain spawn table) |
| `-blobfilltest` | drop the 2048…1 chain, log `fill test: covered= top= danger= fits=` |

Log lines: `t=… blobs physicsMs fps`, `aim check … error=` (prediction vs reality), `grow done … above floor`,
`LANDED WHILE GROWING`, `shake after PILE/lane merge/MAX STRETCH: peak`, `merged -> v IN LANE/in play`,
`summary: laneMerges pileMerges maxStretchKicks throws minGap`, `over: …` (state at game over).
Fish adds `new fish v t= throws= highest= window=`, `window shift -> n t= floor=`, `culled n of v`, `orca celebration`,
`game n: new R <tier> at t= throws=`, `game n: game over t= R= throws= highest= window= D= locks= refusals=`,
`game n: end (timeout) …`, every 30 game-s `game n: difficulty D= R= locks= refusals= lockedSpawns= t= throws= tank= ticks/s=`
(+ `shotg<n>_late.png`; continue mode saves `shotover<n>.png`),
`queue: spawns= maxRun= rerolls= refusals= locks= lockedSpawns= seq=` (Game/Hexa must show `rerolls=0 refusals=0 locks=0`).

Performance reference (desktop): physics 0.2–1.2 ms per 60 Hz tick for 3–12 blobs.

## 8. Gotchas already hit (don't repeat)

1. **PowerShell 5.1 `Set-Content -Encoding utf8` writes a BOM** → broke `Packages/manifest.json`. Write files with
   `[IO.File]::WriteAllText(path, text, (New-Object Text.UTF8Encoding $false))` or the Write tool.
2. **Android Gradle "Unable to establish loopback connection":** set `TEMP/TMP` to a short folder and run outside the
   sandbox (`build-android.ps1` does the TEMP part).
3. **Batch mode refuses while the Editor has the project open** → use the `_testclone` mirror.
4. **Mirror on the long 8.3 temp path** (`C:\Users\MUZAMM~1\...`) produced a player crashing on URP asset load
   ("serialization layout") → keep the mirror at `F:\Projects\BlobMerge\_testclone`.
5. **Unfocused player pauses** (runInBackground off) → the bot stalls; the test build now enables it.
6. **Scene-serialized values override new code defaults** in the user's scene. Prefer *new field names* for changed
   semantics (e.g. `mergeInLane` replaced `allowLaneMerges`), or tell the user to run Setup Project.
7. **Triangle winding:** procedural meshes were culled once; the shader is now `Cull Off`.
8. **Vertex colours in linear colour space:** always convert with `MeshUtil.Lin()` (all builders do).
9. **TextMesh/renderer bounds aren't valid the frame text changes** (old label bug); TMP auto-size now.
10. **Rigid shape matching kills balloon sag**; lumpy rest outlines give the skin slack → slivers. Solved with affine
    matching + `ClampAspect` + `minVolume`.
11. **Pressure + shape tests:** `SolveArea` (hard volume) fights sag — keep `areaStiffness` 0.
12. **Aim mismatch causes found:** origin = touch point instead of blob, arrow length decoupled from finger, preview
    ignoring damping/bumpers/ceiling, shake moving the pointer, lane neighbours nudging the throw. All fixed —
    keep `PredictPath` in sync with `SoftBody.Integrate` if you change integration.
13. **Measuring timing:** compare by physics ticks (`PhysicsTicks`), not real time.
14. **`Handheld` only exists on mobile targets** — wrap in `#if UNITY_ANDROID || UNITY_IOS`.
15. **TMP essentials import is asynchronous;** `TMP_FontAsset.CreateFontAsset` NREs on `TMP_Settings` until it
    finishes → `FontSetup` retries via `delayCall`.
16. **Generated textures must stay readable** (`Apply(false, false)`) to be saved as PNG.
17. **Line endings:** C# files use LF; PowerShell `.Replace("...`r`n")` edits silently miss — use the Edit tool.
18. Small trauma values are invisible (strength = trauma²): old merge shake 0.18 → 0.01 units.
19. **Anti-aliasing for recordings:** the URP asset has MSAA 4× (`URP_Asset.asset`, set by Setup Project), but when a
    camera renders into a RenderTexture, URP uses the **texture's** `antiAliasing`, not the asset's. The AdSim capture
    texture therefore takes `AdRequest.msaa` (default 4; `Tools/ad.ps1 -msaa`). Verified: 1× shows stair-stepped blob
    and frame edges, 4× is smooth. `ReadPixels` on the MSAA texture resolves it automatically.

## 9. Conventions

- Namespace `BlobMerge` (editor: `BlobMerge.EditorTools`). Comment density: short XML summaries on types/non-obvious
  methods, inline `//` for intent.
- Every tunable is a public inspector field with a `[Tooltip]` (and `[Range]` where sensible), grouped by `[Header]`.
- No Unity physics; no textures for gameplay; one material (`VertexColor.mat`) for all meshes, colour via vertex
  colours authored in sRGB hex (`MeshUtil.Hex`) and converted with `MeshUtil.Lin`.
- Pool blobs (`GameController.GetBlob/Release`), reuse mesh lists (no per-frame allocations in hot paths).
- Verify changes with the bot + screenshots + numbers before claiming done; report measured values to the user.

## 10. Open items / ideas

- Settings button has no action (user to decide: sound/vibration toggles, restart, etc.).
- `frameThickness` in the user's scene is 0.6; the mockup suggests ~0.35.
- No device test yet (haptics, touch feel, performance on low-end Android).
- Audio is procedural; real SFX assets could replace `Sfx` clips.
- Possible: wall gap option, better game-over flow, BlobMerge-specific ad personas (§12).

## 11. History (short)

1. Read GPT brief, extracted frames, asked questions; user answered (§3 items 1–6).
2. v1: rigid Physics2D circles, pull-back slingshot, fixed queue → user rejected (dead blobs, wrong controls, fixed row).
3. v2: custom soft-body solver, toward-finger aim, side-spawning lane, juice systems.
4. Tuning rounds: bounce sliders, jiggle vs softness, pressure balloon model, lumpy shapes + affine matching,
   lane merges + gap, 50% lane scale + flight tween, shake on max stretch / pile merges.
5. Restyle to purple aquarium mockup; TMP + Barlow; HUD prefab for user editing.

## 12. AdSim ad recorder (`Assets/AdSim/` + `Assets/Scripts/Ads/`)

Full process knowledge lives in the user-level skills **`ad-director`** (`~/.claude/skills/ad-director/`: pipeline,
porting, recording, personas, audio, endcards, learnings, patched kit, this project as the reference example) and
**`game-branding`** (brand art, layouts, endcard builder, VO roster). Status: `ADSIM_PROGRESS.md`; lessons:
`ADSIM_LEARNINGS.md`; branding history: `F:/Projects/BlobMerge/Branding/log.md`; music: `F:/Projects/BlobMerge/Sounds/MUSIC.md`.

```powershell
& F:\Projects\BlobMerge\Tools\ad.ps1 -persona normal -gameArgs "prefill=16,8,4,2"            # default persona/endcard/music
& F:\Projects\BlobMerge\Tools\ad.ps1 -persona pro -seed 2 -music hype -width 1080 -height 1920
& F:\Projects\BlobMerge\Tools\ad.ps1 -persona noob -endcard none -music none -verbose
```
Final ads are **1080×1920** (720×1280 is only the authoring size of generated endcard art).
Records in the `_testclone` mirror (safe with the Editor open) → `_adout/<name>.mp4` (+ `.json` take log,
`_sfx.wav`, `_music.wav`, `_sheet.png`). Options: `-persona noob|normal|pro|<json>`, `-endcard
blasttheblob_playnow|none|<json>`, `-music auto|chill|upbeat|hype|<file>|none`, `-musicVol`, `-variants 59,45,30` (one separate continuous take per length — never cut/stitched;
skill `ad-director/knowledge/08_variants.md`), `-mirror _testclone2` (parallel runs), `-width/-height` (**1080×1920 default = final size**), `-msaa`, `-secs`, `-seed`, `-nocopy`, `-verbose`, `-gameArgs "k=v;…"` (`prefill=…`, `still=1`,
`laneBias=0.85`, `sfx=1`).

Pieces in this project:
- `Assets/AdSim/` = patched kit (Editor-only asmdef). Original kit hidden in `Assets/AdSimulator_Skeleton~/`.
- `Scripts/Ads/BlobMergeDirector.cs` (`#if UNITY_EDITOR`): plans throws with `World.PredictPath`; personas noob
  (~80 % wrong, confused, misses avoid every match), normal (~35 % wrong in runs of ~3), pro (exact wall-rebound
  merges, 1.25× speed); BlobMerge rule: every non-merge is wrong; ad-only match supply (lane bias, settle gate,
  wait for incoming match, pair after forced placement); gameplay SFX track rebuilt from `Sfx.Played`; music cue;
  HUD canvas switch; best score restored; endcard input guard.
- Game hooks: `AimController.Override`; `GameController.Board/Aim/SteadyToScreen/BestKey/MergesEnabled`, camera fit
  by `cam.aspect`; `GameTime` (capture-safe visual clock — never `Time.unscaled*` in visuals); `Sfx.Render()/Played`;
  `WavUtil`; Editor `SfxExport` (Tools → BlobMerge → Export Sfx); `LaneSpawner.ValueOverride/SpawnGate` (ad-only).
- Endcard `Assets/AdSim/Endcards/blasttheblob_playnow.json` (+ `BlastTheBlob/` art, game SFX, Una VO), built from
  `F:/Projects/BlobMerge/Branding/endcard_layouts.json` by the game-branding skill.

Measured (2026-10-08, prefill 16,8,4,2): noob 10.8 throws/min 80 % wrong · normal 15.5–17.3/min 38–50 % wrong ·
pro 21.7–22.7/min, 15–33 % forced setups, 90–95 % of merges via wall rebounds; hand 0.5 W/s average (real ads 0.51).
