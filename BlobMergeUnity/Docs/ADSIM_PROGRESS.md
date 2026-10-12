# AdSim for BlobMerge — progress log

Session 2026-10-07. Technical reference: `AGENT_GUIDE.md` §12. Lessons and gotchas: `ADSIM_LEARNINGS.md`.

## Where things stand
- **Ad recorder ported and working.** `Tools/ad.ps1` records a 59 s MP4 (+ take log, contact sheet) of a persona-driven
  hand playing the real game, from the `_testclone` mirror (Editor can stay open).
- **Endcards merged** (kit update from `Downloads/AdSimulator_Endcards_Update`) with a BlobMerge-branded endcard.
- **Latest deliverable:** `_adout/noob-endcard.mp4` — noob persona, 1080×1920, seed 1, `prefill=16,8,4,2`,
  BlobMerge endcard at 55–59 s, audio = endcard sfx.

```powershell
& F:\Projects\BlobMerge\Tools\ad.ps1 -persona noob -seed 1 -width 1080 -height 1920 -name noob-endcard -gameArgs "prefill=16,8,4,2"
& F:\Projects\BlobMerge\Tools\ad.ps1 -persona pro  -seed 1 -width 1080 -height 1920 -gameArgs "prefill=16,8,4,2"
& F:\Projects\BlobMerge\Tools\ad.ps1 -endcard none        # no endcard;  -msaa 1|2|4|8;  -verbose logs every plan
```

## Timeline
1. **Read the kit** (`AdSimulator_Skeleton`): README, GameHooks, RESEARCH, template director.
2. **Port** (user: "use your defaults"):
   - Engine copied to `Assets/AdSim/` (without `.meta`, fresh GUIDs); runtime asmdef constrained to `UNITY_EDITOR`.
   - Kit folder renamed `Assets/AdSimulator_Skeleton~` (hidden; otherwise duplicate `AdSim.*` assemblies).
   - Game hooks: `AimController.Override` (screen pixels, same un-shake path as touch), `GameController.Board/Aim/
     SteadyToScreen`, public `BestKey`, camera fit by `cam.aspect`.
   - `Scripts/Ads/BlobMergeDirector.cs`: plans throws by simulating lane blob × 41 angles × 14 powers with
     `World.PredictPath`; first touched blob decides merge/no merge. HUD canvas → Screen Space Camera during takes;
     best score restored after.
   - `Tools/ad.ps1` (mirror → batchmode → MP4 → contact sheet).
3. **First take** worked; found Unity's unscaled clock ignores `captureFramerate` → added `GameTime` (wave, bubbles,
   shake noise, score punch). Fixed slow pacing (look-at trips, re-plan drift), varied throw power, `prefill` arg.
4. **Pro + prefill**: 29 throws / 40 merges / up to 128.
5. **1080×1920 all personas** (MSAA 4 — the `-msaa` option was added by the user's other session).
6. **Noob rework** (user: "very slow, looks artificial; most moves should miss"):
   - New `noob.json`: normal pace, few probes, `wanderRate` = wrong-move share.
   - Wrong move = **near-miss**: aim at the real matching target, a few degrees / some power off, landing clearly
     beside it (≥ one blob width so it doesn't roll in and merge anyway).
7. **Noob 80% wrong + rare wall rebounds** (user: rebounds every move "feels pro"):
   - `wanderRate` 0.8 with drift correction (take stays within ±8% of the target share).
   - New persona knob `bankShotRate` (in kit `Persona.cs`): noob 0.1, others 1. Throw log tags `(miss)` / `(bank)`.
   - Result seeds 1–4: 49/63 misses (78%), 4 banks.
8. **Endcards update applied** (kept our MSAA lines). Kit endcards ec1–ec3 are Point Out placeholder branding, so
   made `Endcards/blobmerge_playnow.json` + generated `Endcards/BlobMerge/icon.png`. Director ignores the hand during
   the endcard; un-freezes time if a game over happens under it.

## Current personas
| Persona | Character | Notes |
|---|---|---|
| `reference` | kit default, calibrated on Rotate Rings ad | detours make it ~4.5 s/throw |
| `pro` | fast, accurate, banks freely | ~2 s/throw, chains to 128 |
| `noob` | BlobMerge-tuned casual | ~80% near-misses, banks 10% of moves, ~3.5 s/throw |

## 2026-10-08 — branding + Blast The Blob endcard
- Game name **Blast The Blob**. Higgsfield (GPT Image 2.5, xhigh) icon/title round → picked icon_r1_0 (pink 64) +
  title_r1_4; layout **B · hero icon**; plate bg0 + green PLAY NOW button. 30 credits.
- Endcard `blasttheblob_playnow` (default in `Tools/ad.ps1`), frame-timed per the user's spec (30 fps, 105 frames),
  built from `Branding/endcard_layouts.json`. Kit change: `EndcardHandStep.duration` (exact-time hand travel).
- Whole flow packaged as the **`game-branding` skill** (`~/.claude/skills/game-branding/`: SKILL.md, scripts,
  templates). Branding history: `F:/Projects/BlobMerge/Branding/log.md`.
- Director: `still=1` arg (no hand, no play, merges off via `GameController.MergesEnabled`), big prefills drop in one
  by one (spawning overlapped blobs exploded the solver).

## 2026-10-08 — ad audio
- Gameplay SFX now in the MP4: `Sfx.Played` event → director logs every sound → `BeforeMix` renders
  `<take>_sfx.wav` from `Sfx.Render()` (exact pitch/volume/time) → mixed at 0 s. Kit additions: `AddAudioCue(at:)`,
  `BeforeMix()`, `OutputPath`, final-mix `alimiter` (peak ≈ −1.9 dB). Args `sfx=<level>`, `music=<wav>;musicVol=`.
- Music library `F:/Projects/BlobMerge/Sounds` (17 tracks) categorised chill/upbeat/hype in `MUSIC.md` +
  `music_catalog.json`; `Tools/ad.ps1 -music auto` picks a random track from the persona's category, −16 LUFS, 50 %.
- Endcard VO: Una; 8-voice random roster in the game-branding skill (`sounds.md`, `voices.json`, `pick_voice.py`).

## 2026-10-08 — personas rebuilt (noob / normal / pro)
From the Point Out ad analysis + user rules (skill `ad_behaviour.md`): noob ~80 % wrong + confused, normal ~35 % wrong
in bursts of ~3, pro exact wall-rebound merges at 1.25× speed. New persona knobs `wrongBurst`, `bankPreference`;
non-merge = wrong; misses avoid every matching blob; ad-only match supply (lane bias, settle gate, wait for incoming
match, pair after forced placement). Default persona `normal`. Metrics: skill `scripts/take_metrics.py`.

## 2026-10-08 — length versions + final size
- Every persona: 59 / 45 / 30 s, **each its own single continuous take** ending in the endcard (`Tools/ad.ps1
  -variants 59,45,30`), same seed/prefill/music track. Cutting versions from one master was tried and rejected by the
  user (visible jumps) — never cut, trim or stitch gameplay.
- Parallel recording: one mirror copy per run (`-mirror _testclone2/_testclone3`, ~6 GB each).
- **Final ads 1080×1920** (`ad.ps1` default); 720×1280 only for generated art / endcard authoring.
- Skills: `ad-director` (umbrella) + `game-branding`; export `_exports/AdDirector_Skills_<date>.zip`.

## Delivered 2026-10-08 — Blast The Blob ads (all single takes, 1080×1920, seed 1, prefill 16,8,4,2)
`_exports/BlastTheBlob_Ads_1080x1920_2026-10-08/` (+ .zip): noob / normal / pro × 59 / 45 / 30 s.
| Persona | Music | 59 s | 45 s | 30 s |
|---|---|---|---|---|
| noob | chill (Genric_06) | 11 throws, max 64 | 7 throws | 5 throws |
| normal | upbeat (Hype_09) | 15 throws, max 64 | 11 throws | 7 throws |
| pro | hype (Hype_07) | 19 throws, max 128 | 15 throws | 9 throws |
All: exact frame counts (1770/1350/900), peaks −1.2…−1.8 dB, 0 hard cuts before the endcard.
Rebuild: three runs in parallel — `Tools/ad.ps1 -persona <p> -name btb-<p> -variants 59,45,30 -mirror _testclone[2|3]
-gameArgs "prefill=16,8,4,2"` (sync code into each mirror first; `-nocopy` skips the sync).

## Open items for next session
- [ ] User review of `noob-endcard.mp4` (pacing, miss look, endcard).
- [x] Real icon/logo for the endcard → Blast The Blob art (2026-10-08).
- [x] HUD showing through the endcard → covered by the opaque plate.
- [ ] Gameplay is cut mid-throw when the endcard starts — optionally wrap up the last move before it.
- [x] Gameplay audio in the MP4 (SFX track + music + endcard sounds/VO).
- [ ] Persona tuning per user feedback; possibly a "reference" retune for BlobMerge.
- [ ] Optional: end the take on the biggest merge; hand art for BlobMerge.
- [x] Endcard uses the game's own SFX + Una VO.
- [ ] Rename game in-app (HUD/title) if "Blast The Blob" is final.
- [ ] User listening pass: music categories (measured, not heard), VO takes, mix balance.
- [ ] Open questions to user: visible wrong-move feedback (red flash/✗)? multicut + intro hook? glove/real hand? share SortRings `ad_analyze.py`.

## 2026-10-08 — Hexa variant ads: started
- Hexa scene (`Assets/Scenes/Hexa.unity`, blue-white theme, hexagon pieces, numbers +20 % = `blobLabelHeight 0.432`);
  Hexa preset now equals the user's tuned scene (damping 0.2, deformationDamping 0.06, edgeStiffness 0.9,
  areaStiffness 0.005, bounces 0.2/0.4/0.15, laneScale 0.85, landedScale 1.3).
- `Tools/ad.ps1 -scene Hexa` works unchanged with the existing director/personas. Proof take `_adout/hexa-normal-test.mp4`
  (normal, 30 s, prefill 16,8,4,2): 1080×1920, 900 frames, 7 throws / 9 merges / max 64, audio peak −1.0 dB.
- Open: Hexa brand/endcard (still Blast The Blob blob art), HUD best-score text contrast on the light background.

## 2026-10-08 — Hexa Blast brand + first 59 s ads
- Brand (game-branding, `Branding/HexaBlast/`): icon r1_1 (bursting pink 64 hexagon), title r1_4, plate (blue-white +
  floating hexagons), button reused from BTB, VO **Skye** ("Hexa Blast!", "Play now!"). Endcard `hexablast_playnow`.
  28.2 credits. HUD_Hexa best-score text → navy #171A54 (was white, invisible on the light background).
- Ads (seed 1, prefill 16,8,4,2, single 59 s takes, 1080×1920, `_exports/HexaBlast_Ads_1080x1920_2026-10-08/`):
  | Persona | Music | throws | merges | max | wrong % | bank % | peak |
  |---|---|---|---|---|---|---|---|
  | noob | chill Genric_03 | 10 | 12 | 32 | 80 | 0 | −1.7 dB |
  | normal | upbeat Hype_06 | 15 | 14 | 64 | 47 | 13 | −2.9 dB |
  | pro | hype Hype_07 | 19 | 13 | 64 | **47** | 68 | −3.7 dB |
- **Issue:** pro (and normal) miss far more on hexagons than on blobs — the director predicts throws with a circle
  (`PredictPath`), but hexagon corners/flat faces rebound differently, so bank shots fail. Next: hexagon-aware
  prediction or pro straight shots on Hexa, then re-record pro/normal.

## 2026-10-08 — real-physics shot check (Hexa) + pro/normal re-recorded
- Diagnosis of the first pro take: 9/19 throws were *forced* (no reachable match), 2/10 planned merges failed in the
  real physics. Fixes (director, hexagons only — `verify` arg, default on when `game.Hex`):
  `GameController.PreviewThrow` (save every blob → real launch physics → real solver until first contact/rest →
  restore); the planner's top-20 circle shots are confirmed with it (best one that truly merges wins); misses are
  re-rolled if the real physics would merge them; lane bias counts only *exposed* pile blobs. Pro recorded with
  `laneBias=1`.
- Re-recorded (59 s, seed 1, prefill 16,8,4,2): **pro** 19 throws, 23 merges, max **128**, wrong 21 % (was 47 %), bank
  74 %, 14/15 planned merges landed; **normal** 16 throws, 16 merges, max 64, wrong 44 %, 9/9 planned merges landed.
  Both 1770 frames, peaks −2.7/−2.8 dB. Delivered to `_exports/HexaBlast_Ads_1080x1920_2026-10-08/` (noob unchanged).
- Remaining: pro still has 4 forced placements (lane value had no reachable target).

## 2026-10-08 — final Hexa Blast set (HUD fix)
- Best score back to **white + standard dark-blue outline** (user picked mock A over a thicker-outline material, which
  was removed). All three re-recorded with the real-physics planner, 59 s, 1080×1920, 1770 frames:
  | Persona | Music | throws | merges | max | wrong % | forced | planned→landed | peak |
  |---|---|---|---|---|---|---|---|---|
  | noob | chill Genric_04 | 9 | 14 | 64 | 78 | 1 | 2→2 | −2.5 dB |
  | normal | upbeat Hype_06 | 15 | 18 | 128 | 40 | 2 | 9→9 | −2.2 dB |
  | pro | hype Hype_02 | 20 | 21 | 128 | 25 | 5 | 15→14 | −2.7 dB |
  `_exports/HexaBlast_Ads_1080x1920_2026-10-08/`.

## 2026-10-08 — Hexa physics: rigid when landed, less bounce, user HUD → final takes
- Hexagons now go **rigid once landed** (floor/pile contact): shape stiffness → `hexLandedStiffness` 0.7 over
  `hexSettleTime` 0.12 s and drawn from the exact hexagon pose → straight sides (rest error 0.1 % mean / 0.4 % max).
  Squash & stretch only in flight + merge pop.
- Bounce: wall 0.2 → 0.06, blob 0.15 → 0.04, bumper 0.4 → 0.15 (Hexa.unity + preset). Restitution alone didn't
  fix the "rubber" feel (rebound median 40 %): the squash springing back launches the piece → new
  `hexLandingBounce` 0.2 caps upward speed for 0.4 s after a landing → measured rebound median 20 % (bot metric
  `[BlobMerge] rebound:`).
- User restyled `HUD_Hexa.prefab`. Final takes (59 s, 1080×1920, 1770 frames): noob 8 throws / max 32 / wrong 75 %,
  normal 15 / 64 / 47 %, pro 20 / 64 / 25 %; peaks −1.7…−2.1 dB. Log "exceptions" = Unity's Android USB scan at
  batch startup (harmless).

## 2026-10-08 — Fish theme: first ad (normal, 59 s)
- No ambience (user); music from the library by mood: **Generic_09** (airy/bright, 96 BPM, chill) — pass the file name
  with extension (`-music Generic_09.wav`; a bare name records without music). No endcard yet (`-endcard none`).
- Fish FX: merges = water splash (clear droplets) + rising air bubbles (`Fx.WaterSplash`, `Fx.Bubbles`, new particle
  kind `Bubble`); flight trail = small white bubbles (`Fx.BubbleTrail`); launch/spawn puffs = bubbles; impacts = water.
  Softer, lower `b_pop` (phase-exact `SoftBubble`, no click), merge pitch base ×0.5.
- Take: 17 throws, 23 merges, max 128, 1770 frames, peak −2.1 dB → `_exports/Fish_Ads_1080x1920_2026-10-08/`.
- Gotcha: a batch Unity can hang at startup ("Loading mode Default") — if a take's log stops for minutes, kill that
  mirror's Unity and record in another mirror (`-mirror _testclone2`).

## 2026-10-08 — Fish Fantasy ads (noob / normal / pro, 59 s)
- Endcard `fishfantasy_playnow` (whale icon r1_2, title r1_3, calm plate 53289856, Kayla VO). Fish fixes: selection 20 %,
  fading white arrow (AimController.arrowFade 0.8 → 0.3), numbers hidden (`fishShowNumbers` off; "+N" stays).
- Music by persona + underwater mood: noob Generic_01 (mellow), normal Generic_09 (airy, user-approved), pro Hype_08 (bright).
  | Persona | throws | merges | max | wrong % | bank % | peak |
  |---|---|---|---|---|---|---|
  | noob | 10 | 11 | 32 | 80 | 0 | −1.0 dB |
  | normal | 16 | 17 | 64 | 44 | 12 | −1.0 dB |
  | pro | 22 | 27 | 128 | 14 | 100 | −1.0 dB |
  All 1770 frames, 1080×1920, no game over → `_exports/FishFantasy_Ads_1080x1920_2026-10-08/`.
- Android: `Tools/build-android.ps1 -scene Fish` → `BlobMergeUnity/Builds/BlobMerge_Fish.apk` (29.7 MB, id
  `com.mindstormstudios.blobmerge.fish`, label "BlobMerge Fish", arm64, minSdk 24 / target 36). Script now maps
  Fish → `BlobMerge_Fish.apk` (it copied the old BlobMerge.apk before the fix).
- Fish APK now ships as **"Fish Fantasy"** with the whale icon (`Assets/Icons/FishFantasy_AppIcon.png`, full-bleed
  icon_r1_2): `ProjectSetup.BuildAndroid` sets product name + default icon for the Fish build only and restores them.
  Verified with aapt: label 'Fish Fantasy', id …blobmerge.fish, launcher icon = whale. 30.0 MB.
