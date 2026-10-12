# AdSim — persona-driven gameplay ad recorder

Records gameplay videos of a game being played by a scripted **human-like** player with a visible hand: thinking
pauses, probes, wrong moves, misses, hesitation, overshooting travel, shaky drags, follow-through. The whole thing is
game-agnostic except one adapter class per game. `RESEARCH.md` has the reference-ad analysis and the numbers the
default persona is calibrated on.

## Layout
| Path | What |
|---|---|
| `Runtime/` (`AdSim.Runtime`, no game deps) | `Persona`, `Rng`, `VirtualPointer`, `HumanMotor`, `HandOverlay`, `FrameRecorder`, `AdDirector`, `AdRequest`/`HandProfile`/`TakeLog` |
| `Editor/` (`AdSim.Editor`) | `AdSimLauncher`: runs a request (scene → play mode → director → record → exit) |
| `Personas/*.json` | `reference` (calibrated on the ad), `noob`, `pro` |
| `Hands/*.json` | hand profiles: idle/click textures + fingertip pixels |
| game adapter (e.g. `Assets/RotateRings/Ads/`) | `AdDirector` subclass: camera, scene prep, the decision loop |

## How a take works
1. `AdSimLauncher` opens the scene, enters play mode (also in batchmode), adds the request's director.
2. `Time.captureFramerate = fps`: game time advances exactly one frame per captured frame, so slow work
   (e.g. a blocking solver call) never shows in the video.
3. The director renders the game camera into a W×H texture; `FrameRecorder` grabs it at the start of every frame
   (state fully updated, particles included) and pipes raw RGB into ffmpeg → MP4.
4. The director's `Perform()` iterator decides what to do; `HumanMotor` turns it into a pointer path; the game
   reads the `VirtualPointer` through its normal input path (so taps, blocks, shakes and effects are genuine);
   `HandOverlay` draws the hand with its fingertip on the pointer; pressing shrinks it around the fingertip
  (`pressScale`), and after a release it stays small for `releaseHold` before easing back (`releaseOut`). A separate
  click sprite is opt-in (`useClickSprite`) because swapping poses makes the hand jump.
5. Writes `<output>.mp4` and `<output>.json` (decision log + pointer path), leaves play mode, quits in batchmode.

## Run
```bash
# batchmode (Unity closed; GPU needed: no -nographics; no -quit)
"C:/Program Files/Unity/Hub/Editor/6000.0.60f1/Editor/Unity.exe" -batchmode -projectPath . -executeMethod AdSim.EditorTools.AdSimLauncher.RunFromCommandLine -adRequest Logs/adsim/request.json -logFile Logs/adsim.log
```
Request JSON:
```json
{ "scene": "Assets/RotateRings/Scenes/Game.unity",
  "director": "RotateRings.Ads.RotateRingsDirector, RotateRings.Ads",
  "persona": "Assets/AdSim/Personas/reference.json",
  "hand": "Assets/AdSim/Hands/cartoon_hand.json",
  "output": "Logs/adsim/take.mp4", "width": 720, "height": 1280, "fps": 30, "seed": 1, "maxSeconds": 59,
  "args": "level=10053" }
```
`maxSeconds` (default 59) hard-caps the video; the persona's play loop wraps up before it (`TimeLimit`).
`args` is game-specific (`key=value;...`). Rotate Rings: `level=<id>` or `index=<1-based play order>`, `verbose=1`
logs every stroke (asked vs. actual rotation, grab angle, which ring the game picked).

## Workflow with Claude
Ask in plain words ("a noob who keeps doing the wrong thing, slowly"). Claude writes a persona JSON (start from the
closest preset and change the knobs below), records one or more seeds, reviews contact sheets and the take log,
adjusts, and hands over the MP4. Presets are starting points, not limits.

## Persona knobs (all optional; see `Persona.cs` for defaults)
* **Thinking**: `thinkTime`, `afterSuccessPause`, `lookAtTargetRate` (point at the piece a move frees),
  `hesitationRate` (drift towards another piece and come back).
* **Mistakes**: `probeRate` (touch a wrong piece: blocked shake or small wiggle), `wrongDirectionRate`
  (start the right piece the wrong way, then reverse), `missRate` (tap empty space), `wanderRate` (a real wrong move;
  re-planned, undone if it makes the board unsolvable), `probeDrag`, `wrongDirectionDrag`, `realizeTime`.
* **Travel**: `travelSpeed` (W/s peak), `minTravelTime`, `overshoot`, `aimError`, `curvature`.
* **Drag**: `dragSpeed` (deg/s peak), `maxStroke` (regrip after), `stallRate`/`stallTime`, `pressDwell`,
  `holdBeforeRelease`, `followThrough`.
* **Noise**: `jitter` (OU sigma, W), `jitterTau`, `idleDrift`.
* **Presentation**: `handScale` (visible hand width / W, default 0.17), `entrance`, `maxSeconds`, `tailSeconds`.
* **Hand profile** (not persona): `pressScale`, `pressIn`, `releaseHold`, `releaseOut`, `useClickSprite`.

## Porting to another game
1. Copy `Assets/AdSim` (and a hand: art + profile JSON with the fingertip pixels of each pose).
2. Give the game an input override (one static `Func` the input layer checks first) and flags to hide dev UI and
   stop auto-advancing.
3. Write an `AdDirector` subclass: `AcquireCamera()`, `PrepareScene()` (load the level after the camera has its
   capture size), `Perform()` (decide with the game's own rules/solver, act with `Motor` primitives and per-frame
   `Motor.Base` for drags; call `Log(kind, detail)` for the take log; set `Completed`).
4. Write a request with `director` = `"Namespace.Type, AssemblyName"`.

## Known limits
* The hand never rotates (like the reference ad); very long drags are split into regrip strokes.
* The solver's move order is used as-is; it can look less "obvious" than a human's order.
* `live_solve.py` races strategies, so the plan (not the persona randomness) can differ between runs with one seed.
