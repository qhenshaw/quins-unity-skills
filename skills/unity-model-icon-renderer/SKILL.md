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
5. **Look at every icon** (contact sheet or the shop UI) and fix outliers with a per-item `rotationOverride`
   (Euler degrees, applied before the facing correction) rather than tweaking the global heuristics.
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
