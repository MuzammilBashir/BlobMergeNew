# Game hooks (the only changes the game itself needs)

The simulator plays through the game's **real input path**, so taps, drags, blocked feedback and effects are genuine.
Add these to the game (Rotate Rings' version: `Example-RotateRings/game_hooks.patch`).

## 1. Input override (required)
One choke point where the game reads the pointer, with a static override checked first:

```csharp
public static class PointerInput
{
    public struct State { public bool Pressed, Held, Released; public Vector2 Screen; }

    /// When set, replaces device input (scripted players, e.g. the ad recorder's virtual pointer).
    public static System.Func<State> Override;

    public static State Read()
    {
        if (Override != null) return Override();
        // ... normal Input System / legacy input ...
    }
}
```
Screen positions are in the **capture camera's pixel space** (origin bottom-left). That's what
`Camera.ScreenToWorldPoint` expects once the camera renders into the capture texture, so the game needs no changes there.

If the game has no choke point (input read in many places, or UGUI buttons through the EventSystem), there are two
options:
* add one (preferred; usually a small refactor), or
* feed a virtual Input System device instead: `InputSystem.AddDevice<Touchscreen>()` + `InputState.Change(...)` from
  the director each frame. That needs no game changes, but UI raycasts and multi-touch quirks then become your problem.

## 2. Runtime switches (strongly recommended)
* **Hide dev/debug UI** during takes (`GameController.DevOverlay = false` in Rotate Rings).
* **No auto-advance** after a win (`AutoAdvance = false`), so the take ends on the cleared board.
* **Expose the gameplay camera** (`GameCamera`) and a **level loader** by id/index.

## 3. Truth source for the director (game-specific)
The persona decides *how* to play, but something must decide *what* is correct:
* a solver (Rotate Rings: `tools/sim/live_solve.py` via `LiveSolver`, called blocking; that's free because time
  advances per captured frame), or
* the game's own hint system, or a scripted move list for a fixed level.
"Wrong" moves should be ones the game's rules make look plausible: a blocked piece, a no-op, the wrong direction.

## 4. Things that break recordings
* `Time.realtimeSinceStartup` / unscaled time in gameplay animations: they ignore `Time.captureFramerate` and will
  run at wall-clock speed. Use `Time.deltaTime`.
* `WaitForEndOfFrame` coroutines: they don't run in batchmode.
* Screen-space UI on an overlay canvas: it isn't rendered into the capture texture. Use "Screen Space - Camera" on the
  gameplay camera if UI should appear in the ad.
