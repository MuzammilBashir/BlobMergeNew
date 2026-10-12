# AdSim for BlobMerge — learnings

What we found while porting and tuning the ad recorder. Read before changing the director, personas or recording.

## What the user wants from ads (feedback so far)
- **Noob must look human, not scripted.** Long thinking pauses, frequent "grab and let go" probes and slow hand travel
  read as artificial. A believable noob plays at a normal pace and simply **misses**.
- **A miss should look like an attempt:** aim at a real matching blob and land beside it — not random throws.
- **~80% wrong** for noob, the rest clean merges.
- **Wall rebounds look skilled.** A noob that banks off walls every move "feels pro" → bank shots must be rare for noob.
- Prefill (`16,8,4,2`) gives a busier, better opening shot. 1080×1920 is the delivery size.

## Recording pipeline
- Batchmode can't open a project the Editor has open → always record from the `_testclone` mirror (`ad.ps1`).
- `-nocopy` reuses the mirror as is: **stale files survive** (a folder the user removed broke compilation in a later
  `-nocopy` run). Sync (omit `-nocopy`) on the first take of a batch.
- "HandleProjectAlreadyOpenInAnotherInstance" crash = another process (or a parallel session) has the mirror open; retry.
- Anything dropped into `Assets/` that doesn't compile (e.g. the kit's Rotate Rings example) breaks **both** the user's
  Editor and recordings. Kit reference material must live in a `~` folder or outside `Assets/`.
- `Time.captureFramerate` drives `Time.deltaTime` but **not** `Time.unscaledDeltaTime` (measured 0.0167 vs 0.0333).
  Visuals use `GameTime.Unscaled/UnscaledDelta`; never use `Time.unscaled*` for visuals.
- Game over sets `timeScale = 0` → captured time stops → the take would hang. The director sets `timeScale = 1`
  after game over and during the endcard.
- Overlay canvases aren't in the capture texture: switch to Screen Space – Camera during takes.
- MSAA must be set on the capture RenderTexture (URP ignores the pipeline asset when rendering to a texture).
- Takes are only roughly reproducible per seed (game `Random` + physics), good enough for picking seeds.

## Director / decision making
- Truth source = the game's own `PredictPath` (same solver as the trajectory preview); first touched `Free` blob
  decides the outcome. Thrown radius grows lane→landed scale during flight; include that in contact tests.
- Keep the final "settle" re-plan close to the chosen shot (same preferred power, angle penalty) — re-sampling caused
  big visible re-aims and slow throws.
- Near-miss selection: same blob, ≤25° / ≤0.3 power off, no merge, landing **≥ target radius + thrown radius + 0.7**
  from the target (closer misses roll in and merge anyway — 6 of 11 did before this rule).
- Independent per-throw rolls drift a lot over ~15 throws (0.8 gave 92%); correct toward the target share.
- Bank detection: horizontal travel flips sign along the predicted path.
- The tank is tall (≈1.3 W from lane to floor): full "look at target" trips cost ~2 s each way at kit speeds — point
  partway, and travel faster on long returns.
- Persona `dragSpeed` mapping for BlobMerge: 280 = 0.9 W/s; `wrongDirectionDrag` = degrees off the aim.

## Endcards
- Kit endcards `ec1–ec3` carry **Point Out** placeholder branding — never ship them in a BlobMerge ad; use
  `blobmerge_playnow` (or a copy with real art).
- The director must return no input while `EndcardActive`, or the endcard tap throws a blob.
- The endcard always ends at the 59 s cap; gameplay is cut if needed (`cap cut to endcard`).
- Each endcard sprite needs its own material (shared material → white boxes); don't "optimise" that.

## Working style that helped
- Verify every take with the log (`[AdSim]` throw/merge/miss/bank lines), the contact sheet and a full-res frame
  before reporting; report measured numbers.
- When applying a kit update, diff our copy against the original kit first and re-apply local changes (MSAA) on top.

## 2026-10-08 additions
- Everything learned is consolidated in the **ad-director** skill (`knowledge/07_learnings.md` = full list) and the
  **game-branding** skill. Highlights:
- Personas = noob / normal / pro (user rules); normal from 11 real Point Out ads (~35 % wrong in bursts of ~3,
  hand 0.5 W/s); BlobMerge: every non-merge is wrong; pro = exact wall-rebound merges at 1.25× speed.
- ~1 in 3 throws can't merge for anyone with normal spawns → ad-only match supply; remaining forced setups accepted.
- Unity audio isn't in the video → rebuild the SFX track from sound events; music one track at 50 % (−16 LUFS).
- Higgsfield: GPT Image for art (720×1280 frames, small sizes), seed_audio for VO (approved 8-voice roster, random
  pick); its SFX/music models are pipeline-only — never for ad audio.
- Endcards are frame-exact in the endcard player (user's frame spec), not AI video.
- PowerShell 5.1: keep .ps1 ASCII; stray `.claude-flow/` folders from a plugin appear wherever commands run.
- **Every ad is ONE continuous take** (59 / 45 / 30 s each recorded separately, ending in the endcard). Cutting the
  shorter versions from a 59 s master was rejected: visible jumps in board/score. Never cut, trim or stitch gameplay.
- **Final ads are 1080×1920** (720×1280 only for generated art). Parallel recording = one mirror copy per run.
- ffmpeg: one input read by several filter branches at different times stalls (GB of buffered frames) — use each
  input once; if a job writes nothing for ~1 min it's stalled, not slow.

