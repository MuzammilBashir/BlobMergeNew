# Simulator: persona-driven gameplay ad recorder (portable kit)

Records gameplay ads of a game being played by a scripted **human-like** player with a visible hand: thinking
pauses, wrong moves, probes, misses, hesitation, overshooting travel, natural drags, follow-through. You describe the
player ("a noob who keeps doing the wrong thing", "a pro, fast and decisive"), a persona JSON captures it, and the
recorder produces an MP4 (capped at 59 s by default) plus a decision log.

Source of truth: SortRings repo (`Assets/AdSim`, adapter `Assets/RotateRings/Ads`, commit `4a5e52b`).

## What's in this folder
| Path | What it is | Copy into the new game? |
|---|---|---|
| `Assets/AdSim/` | The engine: runtime, editor launcher, personas, hand profile + art, docs, stats tool | **Yes, as-is**, to `Assets/AdSim/` |
| `Assets/AdSim/Documentation~/README.md` | How it works, request format, every persona knob | (comes with it) |
| `Assets/AdSim/Documentation~/RESEARCH.md` | Reference-ad analysis and calibration numbers | (comes with it) |
| `Assets/AdSim/Tools~/take_stats.py` | Take stats vs the reference ad (`python ... Logs/adsim/*.json`) | (comes with it) |
| `Template/MyGameDirector.cs` + `MyGame.Ads.asmdef` | Skeleton adapter for a new game; compiles as-is | **Yes**, to e.g. `Assets/MyGame/Ads/`, then rename |
| `Template/request.example.json` | A recording request | Yes, to `Logs/adsim/` (or anywhere) |
| `Template/GameHooks.md` | The small changes the game needs (input override, switches) | Read |
| `Example-RotateRings/` | The complete Rotate Rings adapter, its hook diff and a real request | Reference only |

Folders ending in `~` are ignored by Unity (docs/tools travel with the module without being imported).

## Requirements
* Unity 6 (developed on 6000.0.60f1, URP; nothing URP-specific in the module).
* **ffmpeg** on PATH (or set `"ffmpeg"` in the request to its full path).
* A GPU for batchmode recording (don't pass `-nographics`), and Unity must not have the project open.
* Whatever your adapter uses as its truth source (Rotate Rings uses Python for its solver; the module doesn't need Python,
  only `take_stats.py` does: numpy).

## Port it to a new game: steps
1. Copy `Assets/AdSim/` into the project.
2. Add the game hooks from `Template/GameHooks.md`: an input override at the game's pointer choke point (required),
   plus switches to hide dev UI, stop auto-advance, expose the gameplay camera and load a level by id.
3. Copy `Template/MyGameDirector.cs` + `MyGame.Ads.asmdef` into e.g. `Assets/MyGame/Ads/`, rename, add your game's
   runtime asmdef to `references`. If the game has no asmdefs (everything in Assembly-CSharp), delete the asmdef and
   use `"director": "MyGame.Ads.MyGameDirector, Assembly-CSharp"`.
4. First run with the template as-is: you should get an MP4 of the hand tapping/dragging over your scene. That proves
   capture, hand and input routing.
5. Fill `Perform()` with real decisions: get the correct next move from the game (solver / hints / script), add persona
   detours (probe a wrong piece, wrong direction, miss, hesitate) and act with `Motor` primitives. The Rotate Rings
   adapter shows the full pattern (plan, merge into gestures, regrip strokes, nudges, re-plan after detours).
6. Record:
```bash
"C:/Program Files/Unity/Hub/Editor/6000.0.60f1/Editor/Unity.exe" -batchmode -projectPath . -executeMethod AdSim.EditorTools.AdSimLauncher.RunFromCommandLine -adRequest Logs/adsim/request.json -logFile Logs/adsim.log
```
   Output: `<output>.mp4` + `<output>.json`. Check `Logs/adsim.log` for `[AdSim]` lines.

## Hand art
`Assets/AdSim/Hands/cartoon_hand.json` points at `Hands/Art/handidle.png` (and an optional `handclick.png`). For new
art: set `idleTip` to the fingertip pixel (from the image's top-left), `imageSize`, and `visibleWidthPx` (the drawn
hand's width inside the image). Pressing shrinks the hand around the fingertip (`pressScale`), holds a moment after
release (`releaseHold`) and eases back (`releaseOut`). `useClickSprite` swaps to the click pose instead (it can jump).

## Personas
`reference` (calibrated on a real Rotate Rings ad), `noob`, `pro`. They're starting points: copy one and change the
knobs (all listed in `Documentation~/README.md`). Distances are fractions of the capture width, so personas carry
across resolutions and games. Keep `jitter` small and `jitterTau` >= 0.2 s or the hand looks like it has a tremor.

## Working with Claude
In a Claude Code session in the game's repo: "make an ad where the player is <description>, level <n>". Claude writes
the persona JSON, records a few seeds, reviews contact sheets + `take_stats.py`, tunes, and hands over the MP4.
