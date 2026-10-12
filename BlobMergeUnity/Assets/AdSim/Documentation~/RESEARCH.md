# Ad simulator — research notes

Goal: an **agentic ad simulator** that plays our game on camera like a human with a given persona
("dumb noob, slow, many wrong moves" / "pro, fast, decisive"), with a composited hand + stylus, and records an MP4.
This file records what the reference ad does and the numbers to calibrate against. v1 is implemented — see `README.md`.

Reference studied: `Rotate Rings (Video, AppLovin, 2026-09-20, 16)` — 720×1280, 30 fps, 32.0 s.
Analysis scripts and per-frame data live in scratch (derived from a reference asset — keep them out of the repo).
Method: masked template matching of the hand sprite on every frame (OpenCV `TM_SQDIFF` + mask, 3 scales),
board-change detection outside the hand mask, and manual reading of dense frame strips.

## 1. Structure of the ad
| Time | Content |
|---|---|
| 0.0–11.03 s | Live-action AI-generated intro (muscle guy, crowd, "IQ 168"), hard cut at **11.03 s** |
| 11.03–32.0 s | ~21 s of gameplay: screen recording + `IQ = ?` black banner (y≈60–180 px) + composited hand/stylus |

The video ends mid-gameplay with no end card; the network appends the CTA.

## 2. The hand is a 2D sprite, not real footage
* One PNG (dark-skinned hand + white stylus) whose **angle never changes**: the stylus always points to the upper left,
  about 30° above horizontal, with the hand to the lower right. It is only **translated**, plus a scale animation:
  * **Entrance:** for 11.0–11.8 s the hand is drawn at ~1.5× scale (as if close to the camera), then settles at 1.0×.
  * Steady state: scale ≈ 1.0 (±6%, within matcher noise).
* The tip is the hotspot. There is **no touch indicator**: touch-down/up can only be inferred from the board reacting.
* The hand **never lifts away** between moves. It hovers over the board the whole time and leaves the frame only when a
  drag follows its ring's arc off-screen right before a shatter (18.0–19.0 s top-right, 26.5–27.4 s bottom-right,
  29.8 s bottom).
* There is a slight global **camera drift/zoom** (ring outlines show up in frame differences while the board is still),
  i.e. a faked handheld camera.
* **Frame cadence:** every 6th frame is a duplicate (`MMMMM.`), so the source was 25 fps padded to 30.
  Don't replicate this; record at a true 30/60 fps.

## 3. Move timeline (what the "player" did)
Board: 11 rings (TL green, top red, TR orange, L yellow, R green, centre green, blue, magenta, BL red,
bottom orange, BR yellow). At 13.8 s, after the first ring clears, the camera **re-frames** (zooms out).

| t (s) | Action | Verdict |
|---|---|---|
| 11.0–11.8 | Big hand swoops in from the bottom right, tip **points at the target** (TL green), then slides to the top red | intro / "reading the board" |
| 11.8–13.5 | Slow drag along top-red's upper-left arc (~1.7 s, with a ~0.5 s stall at 13.0) | ✅ frees TL green (shatter 13.67) |
| 14.0–15.5 | Drags top red again (gap rotates from right to top-left) | setup / neutral |
| 15.6–16.7 | Pokes yellow near its clasp, then the centre green; little or no board change | ❌ probe / wrong |
| 16.8–18.6 | Moves to TR orange, drags it, follows the arc off the top-right edge | ✅ frees TR orange (shatter 18.9) |
| 19.3–20.1 | Drags top red around its top arc | neutral |
| 20.2–22.3 | Drifts down to the yellow/red junction, hovers about 1 s, then pokes R green | ❌ hesitation / probe |
| 22.4–23.7 | Scrubs back and forth over centre green / blue / bottom orange (jittery, ~1.3 screen widths of path in ~1 s) | ❌ indecision |
| 23.7–25.4 | Works magenta (drag), touches near BR yellow's clasp | setup |
| 25.6–27.4 | Drags BR yellow (gap from bottom round to left), follows through off-screen | ✅ frees BR yellow (shatter ~27.3) |
| 27.6–29.6 | Enters from below, drags BL red, then **holds still ~0.9 s** before release | ✅ frees BL red (shatter 29.67) |
| 30.0–32.0 | Probes bottom orange / magenta; video cuts | ❌ probe (cut) |

Summary: **4 frees in ~21 s** (one every ~5 s). About 40% of the time is productive dragging, and the rest is
setup, probing and hesitation. The "wrong" moves are mostly **no-op touches and hovering**, not blocked-rotation shakes.
For an "IQ = ?" hook, the player looks competent but not optimal.

## 4. Motion numbers (tip, after the entrance; W = screen width)
| Metric | Value |
|---|---|
| Speed percentiles 10/25/50/75/90 | 0 / 0.06 / 0.29 / 0.93 / 2.35 W/s (inflated by the 25→30 fps duplicates) |
| Time near-still (< 0.15 W/s) | 36% |
| Pauses > 0.1 s | 16 of them; median 0.17 s, p75 0.27 s, max 0.57 s (plus ~0.9 s holds before releases) |
| Movement bursts | median peak ~1.1 W/s, median length ~0.1 s (many small corrective sub-movements) |
| High-frequency jitter | measured ~1.4% W, but that is almost all tracker noise (template matching on a low-contrast stylus). Using it made the hand look like it had a tremor; v1 uses ~0.1% W slow (0.3 s) wobble instead |
| Drag style | Tip traces the ring's **arc** (angular drag around the centre), uneven speed, mid-drag stalls, follow-through past release |
| Travel between rings | Fast swoop (0.3–0.5 s) with overshoot and a small correction, never a straight constant-speed line |

## 5. Feasibility: can we mimic it?
* **Hand movement:** yes, easily. Composite a hand+stylus sprite with a fixed angle, driven by a tip trajectory;
  entrance scale-in, follow-through off-screen on frees, idle hover drift. We control it fully, so it can look *better*
  than the reference (true 60 fps, no duplicate frames).
* **Order of moves, right/wrong:** yes. We already have the pieces: `BoardSolver` (true rules), `live_solve.py`
  (a solution from any state) and `SolutionPlayer` (plays moves through the real solver). A persona agent picks each
  step from {next solution move, plausible wrong move, probe/no-op touch, hover/hesitate, undo-ish reverse drag}
  with persona-weighted probabilities, and re-solves from the new state after any detour, so it always finishes.
* **Erratic human motion:** yes, as a parametric motor model: minimum-jerk point-to-point travel with noisy
  endpoints, overshoot and correction, Ornstein–Uhlenbeck/Perlin jitter, Poisson pauses, uneven angular drag speed
  with stalls, and follow-through. All the parameters above come from this ad.

## 6. Proposed pipeline (for later)
1. **Prompt → persona** (LLM): free text → JSON persona, e.g.
   `{thinkTime, errorRate, probeRate, hesitationRate, dragSpeed, travelSpeed, jitter, overshoot, followThrough,
   pauseBeforeRelease, lookahead, wrongMoveStyle: [noop|blocked|reverse], targetDuration}`.
   "Noob": errorRate 0.4–0.6, thinkTime 1–2.5 s, slow drags, high jitter, blocked-shake moves.
   "Pro": errorRate ~0, thinkTime 0.1–0.3 s, fast arcs, little jitter, no hovering.
   The reference ad sits between them (call it "IQ-test competent").
2. **Planner/improviser** (deterministic, seeded; an LLM optionally only for high-level beats): reads the board and
   produces intents (`look at ring`, `probe`, `drag ring k by Δ`, `hesitate`, `celebrate pause`) using the solver for
   truth. Optional "beats" to hit, e.g. "fail twice, then a streak of 3 fast frees".
3. **Motor model**: intents → tip trajectory (screen space) + touch down/up events, fed into the real game input path
   (or a `SolutionPlayer`-like driver) so game feedback (shake, shatter, camera re-frame) is genuine.
4. **Presentation**: hand sprite overlay, optional banner text, fake handheld camera drift, cut/trim.
5. **Capture**: Unity Recorder (or batch frame dump + ffmpeg) at 720×1280 / 1080×1920, 30–60 fps.

**Decisions (2026-10-07, user):** the user provides the hand/stylus art; the simulator must be **level-agnostic**
(learn behaviour patterns, not level-specific move lists); the game is driven by a script; the "LLM" is Claude
in this repo's Claude Code session (user asks → Claude prepares persona/plan → runs the capture).

Earlier open questions (now answered above): which hand/stylus art we may use (we can't reuse the ad's), whether the level should
be a parity copy of the ad's 11-ring board (look for it in `Assets/RotateRings/Levels`), real input injection vs a
scripted driver, and how the LLM is hosted (offline tool vs in-editor).
