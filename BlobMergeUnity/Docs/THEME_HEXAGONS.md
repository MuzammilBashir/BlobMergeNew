# Theme variation 1 — Hexagons

Same game (lane, toward-finger aim, merges, pile, danger line, juice, HUD, tank) — only the **pieces** change.
Mock (placeholder, user to re-pick): `Branding/themes/hexagons/selected/mock.png` (r2 "C flat"); all rounds in
`Branding/themes/hexagons/`. Theme 2 (fish in a transparent bubble, evolving by merge) comes next and reuses the
same switch.

## User rules for this theme
- A piece is **always a regular hexagon** (6 equal sides, 120°, slightly rounded corners). Never lumpy, never a
  pentagon/heptagon — the AI mocks are only for colour/finish.
- **Only squash & stretch** (launch stretch, impact squash, merge pop) — and it always **springs back to the exact
  hexagon**. No balloon sag at rest.
- **Less glossy:** matte / soft-shaded, no specular sheen.

## Design
One switch on `GameController`: `pieceShape` = `Blob` (default, current game untouched) | `Hexagon`.

### Physics (`SoftBody`)
- New rest outline: regular hexagon, `hexCornerRound` (fraction of side) rounded corners, points spread evenly by
  arc length (`hexPointsPerSide` per side, ≥5 so flat faces collide flat), area normalised like blobs
  (same `RadiusForTier` → same sizes, merges and pile height).
- **Rigid restore:** `SolveShape` gets `Rigidity` (0..1): target = lerp(affine fit, rotation-only fit scaled by
  √AreaScale). Blob = 0 (unchanged). Hexagon ≈ 1 → after any squash the body returns to the true hexagon.
  `hexShapeStiffness` sets how fast (snappy vs jelly). `ClampAspect` still limits extreme squash.
- Pressure for hexagons is low (`hexPressure`) — the rigid target carries the volume; pressure only fills dents.
- Launch `Stretch()` and merge pop already exist → they become the hex squash & stretch for free.
- `PredictPath` is point-based → unaffected.

### Rendering (`Blob.Render`)
- `Hexagon` uses the raw points (no Catmull-Rom rounding of corners beyond the rest rounding).
- Flat soft-shaded look: base colour, gentle top-light → bottom-dark gradient, inner band slightly lighter
  (soft bevel), thin darker same-hue rim, soft drop shadow under the piece, **no sheen discs**.
- Label, flash, selection outline unchanged.

### Tunables (new fields, inspector, tooltips)
`pieceShape`, `hexCornerRound`, `hexPointsPerSide`, `hexShapeStiffness`, `hexRigidity`, `hexPressure`,
`hexMaxSquash`. Blob fields keep their meaning (no scene migration needed).

## Verification (bot)
- `test.ps1 -extra '-blobhex'` → screenshots of lane, flight, pile.
- New log line `[BlobMerge] hex shape: n=… meanErr=… maxErr=…` — deviation of resting pieces' points from a perfect
  hexagon (fraction of radius); target mean < 2 %, max < 5 %.
- Squash check: peak aspect during impacts > 1.15 (visible squash) and recovery to < 2 % error within ~0.3 s.
- Regression: blob mode screenshots/log unchanged (`-blobsag` widths same as before).

## Later (not in this step)
- Hex-specific juice (edge sparkles, hex-shaped particles/rings), hex danger/merge SFX variants.
- Ad recording with the hex theme (`ad.ps1` passes the flag through).
- Theme 2: `pieceShape = FishBubble` — round transparent bubble body + fish sprite per tier inside.

## Status (2026-10-08) — step 1 implemented & verified
- `GameController.pieceShape = Hexagon` (inspector, live for new pieces) or bot flag `-blobhex`; `-hexstiff <k>` overrides
  `hexShapeStiffness` for tuning.
- Code: `SoftBody.HexOutline` + `SoftBody.Rigidity` (rotation-only target blend) + `ShapeError()/Aspect()`;
  `Blob.RenderHex` (flat soft-shaded); `GameController` hex fields + `Hex`, `PointsPerPiece`, `MaxSquashNow`, `RigidityNow`.
- Drop test (`-blobhex -blobsag`): resting error mean 0.5–1.1 %, max ≤ 3.4 %, area 1.00, width/height ≈ 1.12 (flat side down).
- 60 s bot, stiffness sweep: 0.05 → rest 2.4 % / max 8.2 % (dented bottom pieces) · **0.08 (default)** → 1.1 % / 5.3 %,
  137 squash events, peak aspect 1.89 · 0.12 → ≤1.9 % but half the squash. Aim prediction error 0.00–0.01,
  physics 1.2–2.1 ms with 9–12 pieces (30 points each), 60 fps.
- Blob mode regression: identical code path (Rigidity 0); sag widths vary per run with the random lumps (256: 1.22–1.72),
  areas 0.97–1.02.

## Step 2 (2026-10-08) — own scene, physics, HUD, background
- `Assets/Scenes/Hexa.unity` with its own `Game` object (all tunables), `HUD_Hexa.prefab`, `BoardTheme_Hexa.asset`
  (purple copy to restyle). Duplicate `hexShapeStiffness/hexPressure/hexMaxSquash` removed — Hexa uses the normal fields
  (`ApplyHexaPreset`). Piece colours moved into the theme (`pieceColors`).
- Verified: Hexa 60 s bot — 49 throws, 14 lane + 41 pile merges, 60 fps, physics ≤ 1.4 ms; Game scene 30 s — blobs and
  purple look unchanged, build clean.
