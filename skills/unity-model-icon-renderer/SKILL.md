---
name: unity-model-icon-renderer
description: Render 3D models/prefabs in a Unity (URP) project into transparent, outlined sprite icons for inventories, shops, item cards and grid backpacks - sized to the item's cell footprint, auto-posed so long weapons lie diagonally and compact props fill the slot. Use when a project needs item/inventory icons from its 3D art, has placeholder or missing icons, wants icons regenerated after art changes, or wants Backpack Battles-style grid item art.
---

# Item icons rendered from 3D models

Turn the game's own prefabs into consistent icon sprites from code: an isolated preview scene, an orthographic
camera with three directional lights, a pose chosen per model, a 2× supersampled render, premultiplied
downsample, and a dark outline so icons read on any background. Re-runnable - regenerate whenever art or item
shapes change.

## Workflow

1. **Check the pipeline.** The template targets URP (`UniversalAdditionalCameraData`). For Built-in, delete that
   block; for HDRP, use `HDAdditionalCameraData` and disable post-processing the same way.
2. **Copy `templates/Editor/ModelIconRenderer.cs`** into an editor-only folder/asmdef (references URP runtime if
   using an asmdef) and rename the namespace.
3. **Decide the mapping** from game data to jobs: which prefab represents each item, its cell footprint (1×1 for
   plain icons; the item's grid shape for grid inventories), and the output path. Keep that mapping in the
   project's content code, not in the renderer.
4. **Call `ModelIconRenderer.RenderBatch(jobs)`** from a menu item or the content build step, then assign the
   resulting sprites to the item data (`AssetDatabase.LoadAssetAtPath<Sprite>(path)`), `SetDirty`, `SaveAssets`.
   PNGs are imported as sprites (alpha is transparency, no mipmaps).
5. **Look at every icon** with `scripts/icon_sheet.py` and fix outliers per item (see *Review loop*) rather than
   tweaking the global heuristics.
6. **Commit the PNGs via Git LFS.** Don't commit icons rendered from licensed art to public repos.

## How the pose is chosen

- **Elongated models** (longest axis > 1.6× the middle one: swords, staffs, arrows, wands) are laid along the slot's
  long axis - vertical in tall slots, horizontal in wide ones, **diagonal in square slots** - then spun 25° around
  that axis so the side faces the camera.
- **Compact models** (gems, potions, books, chests) start from an upright 3/4 view, then a sweep of yaw (−90…90),
  pitch (−50, −25, 0) and roll (0…135) renders a tiny probe and keeps the view with the **most silhouette pixels**.
  Pixel coverage rather than bounds avoids thin diagonals winning; dead-on views are penalised 7% because they look
  flat; the automatic pose gets a 10% bonus so it's only replaced by a clearly better view.
- **Compact model in a long slot** (coverage < 30%, slot ≥ 3:1): copies are tiled along the slot with slight
  rotation/scale variation (three shards in a 1×3 slot) instead of one tiny model.
- **Facing:** most packs (Synty included) author props facing +Z; the camera looks along +Z, so models get a 180°
  yaw first (`Options.facingCorrection`). Change it if a pack faces another way.

## Review loop

Never trust a batch you haven't looked at. One sheet catches what a hundred log lines won't.

1. **Sheet:** `python scripts/icon_sheet.py <icon_folder> sheet.png --match ^Armor_ Shield --strip-prefix`, then view
   the image. Icons keep their aspect ratio, so a 3×1 item shows as a strip, as it will in the grid.
2. **Classify each bad icon** - the fix differs:
   - *Side-on / lying down / back facing camera* → pose it with `rotationOverride`. It is applied as
     `Euler(override) * facingCorrection`, so `(0,0,0)`-ish values mean "as authored, facing the camera": small
     tilts like `(-5, 15, 0)` give an upright 3/4 view. `(0, 180, 0)` shows the back. Zero means "auto", so use a
     tiny non-zero value if you want the authored pose exactly.
   - *Edge-on flat thing (shield, plate, coin)* → the prefab usually has a **rotated child** (weapon display prefabs
     often rotate the mesh 90° on X so it lies flat in-game). Log the child's `localEulerAngles` before guessing;
     cancel it in the override and add a roll to lay the long side along the slot, e.g. `(0, 90, 80)` for a flat
     oval shield in a 3×1 slot.
   - *Wrong silhouette for the slot* (a tall thin model in a square slot: training dummies, armor stands, banners on
     poles) → no rotation fixes that; **swap the model**.
3. **Audition replacement models in one render:** temporarily point several existing items at candidate prefabs
   (`item.iconModel = candidate`, zero override), render, sheet, pick. Then put the winners in the content code -
   if the content build re-assigns models on every run, the temporary assignments are overwritten anyway.
4. **Record poses in the content code**, not only on the assets, so a rebuild with "overwrite data" keeps them.
   Apply a recorded pose only when the item has none of its own, so hand-tuned poses in the Inspector survive.
5. Re-render and re-sheet until every icon reads at a glance.

**Good model sources for wearables:** static modular-character parts (e.g. Synty Modular Fantasy Hero
`Characters_ModularParts_Static/Chr_Torso_*_Static`) make far better chest-armor icons than props such as armor
stands or training dummies - they fill a square slot and read as "armor" immediately. Character attachment prefabs
(helmets, shoulder pads, capes) work well for the smaller slots. Static parts use the pack's ordinary texture
material, so they convert to your shader like any prop.

## Gotchas

- **Preview scenes have no environment lighting or skybox** - without the fill and rim lights, the side away from
  the key light renders black.
- **Post-processing and MSAA break alpha.** Disable post on the icon camera (tonemapping/bloom write alpha = 1) and
  supersample instead of MSAA.
- **Set `cam.scene = previewScene`** or the camera renders the open scene too.
- **Strip colliders, disable Animators, particle/trail/line renderers** on the instance - VFX children and bind-pose
  animators otherwise inflate bounds or show up as smears.
- **Premultiply when downsampling,** or transparent edges pick up dark fringes from the clear colour.
- Skinned characters render in bind pose (T-pose); give them a `rotationOverride` or a posed prefab variant.
- Palette-textured low-poly models look grey if their palette textures have mipmaps (see the
  `unity-asset-pack-extraction` skill).

## Files

- `templates/Editor/ModelIconRenderer.cs` - `RenderBatch(IconJob[], Options)`, `IconJob(model, cells, pngPath, rotationOverride)`,
  menu **Tools > Icons > Render Selected Prefabs** (1×1 icons into an `Icons/` folder next to each prefab).
- `scripts/icon_sheet.py` - labelled contact sheet of rendered icons, filterable by name (needs Pillow).
