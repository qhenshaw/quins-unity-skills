---
name: unity-vfx-from-texture-pack
description: Build a full set of gameplay VFX in Unity from a texture / flipbook pack by generating VFX Graph assets from code — hits, crits, deaths, explosions, slashes, lightning, status effects, pickups, level-ups, dashes and projectile trails. Use when a Unity (URP + VFX Graph) project wants textured particle effects, wants to "add VFX everywhere", has a VFX texture pack to wire up, or needs VFX Graphs authored programmatically.
---

# Unity VFX from a texture pack

Generates every effect as a VFX Graph asset from a C# spec list, so effects are reproducible, reviewable in
diffs and quick to iterate. Proven on Unity 6000.6 / URP 17.6 / VFX Graph 17.6 with a 280-texture pack.

The pieces (all in `templates/`):

| File | Role |
|---|---|
| `Editor/VfxGraphGenerator.cs` | Builds `VFX_<Name>.vfx` graphs from `Spec`s (layers of textures/flipbooks). Menu: Tools > VFX > Generate VFX Graphs |
| `Editor/Unity.VisualEffectGraph.EditorTests.asmdef` | Editor assembly **named on purpose** to get VFX/ShaderGraph/URP `InternalsVisibleTo` access — it is not a test assembly |
| `Runtime/EffectType.cs` | One enum value per effect; the generator's spec names must match |
| `Runtime/EffectsManager.cs` | One pooled `VisualEffect` per type; `EffectsManager.Play(type, position, color, size, direction)` |
| `Tests/EffectsPlayModeTests.cs` | Every type has a graph and emits |

Read `references/vfx-graph-internals.md` before changing the generator, and `references/spec-format.md` when writing specs.

## Workflow

1. **Check versions.** The generator uses internal VFX Graph editor APIs. Confirm the project's VFX Graph version;
   if it isn't 17.x, compile the template first and fix API drift (see internals reference) before anything else.

2. **Install the templates.** Copy `Editor/` into an editor folder and `Runtime/` into the game's runtime assembly.
   Rename the `VfxToolkit` namespaces to the project's. Keep the asmdef **name** exactly
   `Unity.VisualEffectGraph.EditorTests`; add the project's runtime assembly to its references only if specs need game types.
   Set `TexDir` / `OutDir` in the generator. Add an `EffectsManager` to the gameplay scene (or the scene builder).

3. **Inventory the texture pack** (don't guess from file names):
   - `python scripts/contact_sheets.py <pack> <out>` and look at every sheet. Note what each texture is:
     radial burst, directional burst, ring, slash/crescent, smoke puff, fire, electricity strip, flash/star, trail, noise.
   - `python scripts/flipbook_grids.py <pack>` proposes column×row grids from empty gutters between frames.
     Confirm each flipbook you plan to use with `--check "file.png=4x4" --overlay <out>` and look at the overlay.
     Several grids can pass (a 4×4 also passes 2×2) — pick the one whose cells are whole sprites. Textures where
     nothing passes are single images or not clean grids: never use them as flipbooks.
   - Write the confirmed grids into the specs; nothing detects them at runtime.

4. **List the effects the game needs**, then map each to 2–4 layers (see the recipe table below). Add each name to
   `EffectType` and a `Spec` in `VfxGraphGenerator.Specs()`. Trails are specs with `rate > 0` and `worldSpace = true`.

5. **Generate**, then wire `EffectsManager` entries (one per `EffectType`, asset `VFX_<Type>.vfx`) — ideally from code
   so new types are picked up automatically — and add trail graphs as child `VisualEffect`s on projectile prefabs.

6. **Hook gameplay**: call `EffectsManager.Play` at hits (per weapon impact type), crits, deaths, spawns, explosions,
   melee swings (direction = swing direction), chain lightning (position = segment midpoint, size = length, direction =
   segment direction), dash, status apply/tick, pickups, heals, level-up, player hurt, enemy shots.

7. **Verify visually.** In Play mode set `Time.timeScale = 0.03`, fire every effect type in a grid around the player,
   capture at two moments (just after spawn and ~5 s real time later). Fix layers that read badly, regenerate, repeat.
   Then capture real combat. Then add/extend the PlayMode test (template) — poll `AliveParticles`, never assert on the
   spawn frame.

## Effect recipes (layers that worked)

| Effect | Layers |
|---|---|
| Hit | small radial burst (random frame, Pop) + directional impact streak along hit dir + velocity-stretched sparks |
| Crit | large star burst + soft flash + more sparks |
| Death | animated smoke puff (alpha blend, tinted) + animated radial burst + rising wisps |
| Explosion | animated fireball flipbook + delayed smoke (alpha, **fixed dark grey**) + ground shockwave ring + ground scorch decal (alpha, fixed near-black) + upward sparks |
| Slash | **crescent swoosh flipbook** on GroundAlongDirection, quad scale (1, ~0.37), + flat sparks |
| Nova | ground ring growing from ~15% + flat radial burst + flat streak sparks |
| Lightning | animated electricity strip, GroundAlongDirection, size = segment length, scale (0.35, 1) |
| Status (burn/frost/poison) | small looping-looking flipbook (fire ground / spikes / bubbles), re-played every ~0.45 s while active |
| Weapon impacts | one spec per element (arcane, fire, frost, poison, slash, shock, holy, blunt) chosen from weapon tags |
| Trails | 1–2 continuous layers (rate 10–30/s) in world space, tinted per projectile element |

## Gotchas (each cost real time)

- **Slash textures made for curved meshes look like hard wedges on a flat quad** — use crescent swoosh flipbooks instead.
- **Alpha-blended smoke takes the event colour and turns muddy** — give smoke/scorch layers a fixed colour.
- **Big source textures (6–8k)**: the generator caps import at 2048 and sets Clamp + alpha-is-transparency; flipbooks bleed without Clamp.
- **`aliveParticleCount` lags several frames** (GPU readback) — poll up to ~30 frames in tests.
- **Graph "was out of date and has been upgraded" warnings** after generation are harmless.
- **Projectile trails on pooled objects** restart when re-enabled; world-space trails need a Local-space position slot to spawn at the object.
- **Huge ground decals with large radii** (explosion size × radius) dominate the screen — keep decal size multipliers ≤ ~1.
- **Additive layers on grey textures + event colour** is the cheapest way to make one graph serve many element colours.
- Keep the generator idempotent: it deletes and recreates each `.vfx`. Hand-tuned graphs should be removed from the spec list.

## Licensing

Never copy texture packs or other licensed art into this skill or another project's repo; reference them by path and
map them per project.
