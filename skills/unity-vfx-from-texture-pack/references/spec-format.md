# Spec format (VfxGraphGenerator)

A `Spec` is one graph: `name` (→ `VFX_<name>.vfx`, must match an `EffectType` for bursts), `worldSpace` (trails),
and `layers`. Every burst layer is its own spawner → initialize → update → output system started by the shared
`OnPlay` event, so one `EffectsManager.Play` call fires all layers.

## Layer fields

| Field | Meaning | Typical |
|---|---|---|
| `texture` | file in `TexDir` (null = default round particle) | `"Tx_Burst_radial_05.png"` |
| `cols`, `rows` | flipbook grid (confirmed with `flipbook_grids.py`) | 2×2, 3×4, 4×4 |
| `frames` | `None`, `Animate` (texIndex over life), `Random` (one frame per particle) | Animate for smoke/fire, Random for bursts/sparks |
| `alphaBlend` | alpha blending instead of additive | true for smoke, scorch, liquid |
| `facing` | `Camera`, `Velocity` (stretched streaks), `Ground` (flat, normal up), `GroundAlongDirection` (flat, quad Y = event direction) | slashes, lightning, impact streaks use GroundAlongDirection |
| `motion` | `None`, `Sphere`, `Up`, `Flat`, `Direction` (event direction + `spread`) | sparks: Sphere/Up/Flat |
| `count` / `rate` | burst count / continuous rate (rate > 0 → trail, no event) | |
| `delay` | burst delay (s) | smoke after fireball: 0.1 |
| `lifetime`, `speed`, `sizeMul` | random ranges (sizeMul multiplies the event `size`) | |
| `sizeCurve` + `growFrom` | `Shrink`, `Grow` (growFrom → 1), `Pop` (fast grow then hold) | rings: Grow from 0.15–0.3 |
| `colorBoost` | colour multiplier (HDR glow with bloom) | 2–3 additive, ~1 alpha |
| `fixedColor` | ignore the event colour (neutral smoke, dark scorch) | |
| `tint` | trail colour (trails have no event colour) | |
| `scale` | non-uniform quad scale (x, y) | slash (1, 0.37), lightning (0.35, 1), sparks (0.3, 2.2) |
| `offset` | added to spawn position | ground layers: y = 0.03–0.08; offset down if `position` is chest height |
| `fadeIn` | alpha 0→1→0 instead of 1→0 | smoke, telegraphs |
| `randomRotation` | random angleZ | off for upright flames / directional layers |
| `constantDirection` | trails: fixed local direction | |
| `capacity` | particle capacity per system | raise for trails/heavy effects |

## Event contract

`EffectsManager.Play(type, position, color, size, direction)` sets spawn attributes `position`, `color` (rgb),
`size`, `direction`. Conventions that kept call sites simple:
- **size** ≈ the world radius / scale of the effect (explosion radius, nova range, slash range, segment length).
- **direction** = hit direction / swing direction / segment direction (normalised, horizontal).
- Hit-type effects are spawned at chest height; ground layers use a negative y `offset` to reach the floor.
