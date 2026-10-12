# BlobMerge

> For AI agents and developers: the full, up-to-date knowledge base is in [Docs/AGENT_GUIDE.md](Docs/AGENT_GUIDE.md)
> (design decisions, architecture, tuning, testing tools in `../Tools/`, known pitfalls). This README is the quick
> user overview and may lag behind it.

A portrait mobile soft-body number-merge puzzle, made in Unity 6 (6000.0.67f1) with URP. It targets Android.

## Open and play

1. Open `BlobMergeUnity/` in Unity Hub with **6000.0.67f1**.
2. Run **Tools → BlobMerge → Setup Project (URP, Android, Scene)**. This rebuilds `Assets/Scenes/Game.unity`, so run it again after pulling changes that alter the setup.
3. Press **Play** with a portrait Game view, such as 1080×2340.

**Controls:** press on **any** floating blob at the top, drag **toward** where you want it to go, and release. A longer drag throws harder. The arrow shows direction and power, and the dotted line previews the flight path. A drag that is too short cancels the throw.

## Rules

- The board starts empty. Blobs **1, 2, 4** squeeze in through the white doors on the left and right walls and float together at the top. The lane always refills to 3 blobs, and new ones come in from whichever side has more room.
- As you throw more, **1** gets rarer, **8** starts appearing after 6 throws, and **16** after 24.
- Two thrown blobs with the same value merge into double the value: 1+1→2 … 64+64→128 → 256 and so on. Chain merges raise the pitch of the pop sound.
- Score: each merge adds the value it creates. The best score is saved.
- Game over: a resting blob stays above the dashed red line for `dangerHoldSeconds`. Tap to restart.

## How it works

| File | Role |
|---|---|
| `Physics/SoftBody.cs` | One water-bubble body: a ring of Verlet points with edge springs, an **area (volume) constraint** and weak **shape matching**. |
| `Physics/SoftBodyWorld.cs` | Sub-stepped solver for bodies vs walls, floor and chamfered corners (half-planes), bumpers (circles) and other bodies (point-in-polygon push-out, mass-weighted). It reports contacts and impacts. |
| `Blob.cs` | A numbered blob: renders the soft body as a smooth glossy bubble (Catmull-Rom outline, gradient, sheen), with an upright label, white selection outline, flash and size spring. |
| `AimController.cs` | Standalone input and aiming: picks a blob, tracks the drag, maps it to power, and draws the arrow and trajectory. It hooks into the game through callbacks. |
| `LaneSpawner.cs` | Top-lane spawning (which side, start values, odds) and the float springs that hold lane blobs in place. |
| `GameController.cs` | Wires everything together: fixed 60 Hz physics, merges, effects, score, danger and game over, HUD. |
| `Board.cs` | Frame, chamfered field, bumpers, entry doors, pulsing danger line, and the matching collision geometry. |
| `Fx/Fx.cs` | Pooled droplets, sparkles, shockwave rings, floating "+N" text and camera shake, all in one mesh and one draw call. |
| `Fx/Sfx.cs` | Procedurally generated sound effects (pop, plop, whoosh, thud, tick, spawn, game over) and short Android vibrations. |
| `DebugAutopilot.cs` | A bot for automated checks. It does nothing without command-line arguments. |
| `Editor/ProjectSetup.cs` | Setup Project, Build Android APK, Windows test build. |

All meshes use one vertex-color shader (`Shaders/VertexColor.shader`), so no textures are needed.

### Feel and juice

- **Merge:** the two blobs pull together and shrink. The new blob appears small and springs up past its full size, physically pushing its neighbors aside. You also get a white flash, two droplet bursts (old and new color), sparkles, two shockwave rings, floating "+value" text, a screen shake that grows with tier, a pitched pop and a haptic pulse.
- **Throw:** the blob stretches along the throw direction and jiggles back. There's a puff of droplets, a ring and a whoosh whose pitch follows power. Fast blobs leave a droplet trail.
- **Impacts:** hard hits on walls or blobs splash droplets, play a thud or plop, and shake the screen slightly when strong.
- **Aiming:** the held blob gets a white outline, swells slightly, and is pulled back against the aim like a slingshot.
- **Spawning:** the entry door flashes, droplets spray, and the blob squeezes in.

### Tuning (inspector on the `Game` object)

- **Soft-body physics:** gravity, substeps, iterations, damping, edge/area stiffness, shape stiffness for small and large blobs (lower is wobblier), wall bounce and friction, contact damping.
- **Size spring:** pop stiffness and damping, and the starting size for spawned and merged blobs.
- **Merge:** duration, attraction, optional lane merges.
- **Juice:** launch stretch, shake amounts, speed thresholds for impact effects and trails.
- **Lane:** blob count, start values, spawn timing, float springs, aim pull-back, value odds.
- **AimController** component: min/max drag, min/max throw speed, power curve, pick padding, arrow look, trajectory dots.

### Editing the HUD

- **Layout:** open `Assets/Resources/UI/HUD.prefab` in Prefab Mode and move, resize, recolor or add anything. The game only uses the references on the `HudView` component at the prefab root (score text, best text, settings button, game-over panel and text). If you replace an element, drag the new one into its slot.
- **Moving the top HUD:** move the `TopBar` object, which holds the score panel, best score and settings button.
- **Art:** replace the PNGs in `Assets/UI/Sprites/` (RoundedRect is 9-sliced).
- **Text look (all text in the game):** edit `Assets/Resources/Fonts/Barlow-Bold Outline.mat` (Outline color and thickness, Face dilate).
- **Settings button:** wire its `Button → On Click` in the prefab.
- **Reset:** **Tools → BlobMerge → Rebuild HUD Prefab** restores the default layout and overwrites your edits.

### Performance

About 0.5–0.7 ms of physics per 60 Hz tick on desktop with 10–12 blobs (20 points each, 4 substeps × 2 iterations). If a low-end phone struggles, first lower `substeps` to 3 or `pointsPerBlob` to 16.

### Automated check (desktop)

`ProjectSetup.BuildWindowsTest` builds `Builds/WinTest/BlobMerge.exe`. Run it with:

```
BlobMerge.exe -screen-fullscreen 0 -screen-width 540 -screen-height 1100 -blobauto 60 -blobshot C:/temp/shot
```

The bot plays through the real input path, logs physics time, saves screenshots and quits.
