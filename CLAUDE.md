# BlobMerge — instructions for agents

Portrait Android soft-body number-merge puzzle, **Unity 6000.0.67f1 + URP**, project at `BlobMergeUnity/`.
**Ad work: use the `ad-director` skill (+ `game-branding` for art/endcards/VO); project status in
`BlobMergeUnity/Docs/ADSIM_PROGRESS.md`, lessons in `ADSIM_LEARNINGS.md`.**
**Full knowledge base: `BlobMergeUnity/Docs/AGENT_GUIDE.md` — read it before changing gameplay, physics, HUD or builds.**

## Must-knows
- The user (Muzammil) usually has the project **open in the Unity Editor**. Never batch-open `BlobMergeUnity/`
  directly; build/test through the mirror with the scripts in `Tools/`:
  - `& F:\Projects\BlobMerge\Tools\test.ps1 -secs 60` → Windows build + autoplay bot → `_testout\shot*.png`, `player.log`
  - `& F:\Projects\BlobMerge\Tools\sag.ps1 -pressures 2.5,3.5,6` → balloon softness measurement
  - `& F:\Projects\BlobMerge\Tools\ad.ps1 -persona normal|noob|pro` → gameplay ad MP4 (hand, SFX, music, endcard) → `_adout\` (AGENT_GUIDE §12)
    Final ads: **1080×1920**, every length (`-variants 59,45,30`) its own **single continuous take** — never cut/stitch.
  - `& F:\Projects\BlobMerge\Tools\build-android.ps1` → APK (run outside the sandbox; sets TEMP for Gradle)
  - Variants = scenes: `Game` (blobs), `Hexa` (hexagons) and `Fish` (creatures in bubbles) — pass `-scene Hexa|Fish`
    to test/build/ad scripts; each scene has its own tunables, HUD prefab and `BoardTheme` (AGENT_GUIDE §5.11).
  - **Fish progression/queue/difficulty: `BlobMergeUnity/Docs/FISH_PROGRESSION.md` (status block at the top, §0, §0b)
    — read it before changing the Fish scene.** Fish-only rules there override the general ones below.
- Verify every change with the bot (screenshots + `[BlobMerge]` log numbers) before saying it's done.
- All tunables live on the `Game` object (`GameController`, `AimController`, `GameController.lane`), live in Play mode.
  New fields get code defaults; changed semantics → use a **new field name**. Old values persist in the user's scene
  until **Tools → BlobMerge → Setup Project** (which regenerates the scene).
- Editable assets the user owns — never overwrite: `Assets/Resources/UI/HUD.prefab`, `Assets/UI/Sprites/*.png`,
  `Assets/Resources/Fonts/Barlow-Bold Outline.mat`.
- Never touch `F:\Projects\UnityProjects\BlobMerge` (different, older project).
- Ad recorder: `Assets/AdSim/` (kit engine, Editor-only) + `Assets/Scripts/Ads/BlobMergeDirector.cs`. The original kit
  sits hidden in `Assets/AdSimulator_Skeleton~/` — keep the `~` (un-hiding duplicates the AdSim assemblies).
  Never use `Time.unscaled*` in visuals — use `GameTime` (Unity's unscaled clock breaks recorded videos).

## Design rules the user set (don't revert)
Soft **pressure-balloon** blobs (custom solver, not Physics2D) with lumpy shapes · **press a lane blob and drag TOWARD
the target**, longer = stronger, arrow + exact trajectory preview · lane blobs **enter from both walls**, any can be
thrown, start 1/2/4, then 1 rarer and 8/16 appear · **same values merge**, also in the lane (magnet pulls them
together; **Fish: no auto lane merge/magnet — only a throw into a lane twin merges**) · controllable `blobGap` · lane blobs at `laneScale`, tween to `landedScale` **during flight** · no combos,
no cue ball · lots of juice (particles, pops, shake on max stretch and container merges, haptics) · purple aquarium
look · all text TMP **Barlow Bold, white, thin dark-blue outline** · Android only.

## Gotchas
PowerShell 5.1 `Set-Content -Encoding utf8` adds a BOM (broke manifest.json) · keep `PredictPath` in sync with
`SoftBody.Integrate` · C# files are LF · vertex colours need `MeshUtil.Lin()` · rigid shape matching kills sag ·
compare physics timing by `PhysicsTicks` · see AGENT_GUIDE §8 for the full list.

## Backups (moved out of the repo 2026-10-12)
`F:\Projects\BlobMerge_Backups\2026-10-12\`: `Branding.zip` (icons, endcards, themes, art sources), `Sounds.zip` (ad music
library — **unzip to `Sounds\` before `ad.ps1`**, it reads `Sounds\music_catalog.json`), `_adout.zip` (recorded ads),
`superpowers.zip` (mocks + Fish build ledger `sdd/fish-progression/progress.md`), `_testout.zip`, `GPT.zip` (original
brief/reference), `claude-code-game-studios.zip`, plus the older `_backups\` and `_exports\` zips.
Repo = `BlobMergeUnity/` (3 scenes: Game, Hexa, Fish) + `Tools/` + docs; `.gitignore` excludes Library/Builds/_testclone etc.
