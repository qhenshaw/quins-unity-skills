---
name: unity-asset-pack-extraction
description: Import only the assets a Unity project actually uses from large .unitypackage asset packs (Synty POLYGON/Sidekick, Asset Store art packs), with GUIDs preserved and dependencies followed, then convert the pack's materials to the project's own shader (URP Lit or a custom uber shader). Use when a project wants a few models/props/characters from multi-GB packs, wants to avoid importing a vendor's shaders and scripts, has pink/grey materials after importing, or asks to "replace placeholder art with <pack>".
---

# Selective asset pack extraction + material conversion

Big art packs are 1–10 GB each and bring their own shaders, scripts, demo scenes and render-pipeline assumptions.
Instead of importing everything, read the `.unitypackage` directly, write just the chosen assets (and what they
reference) into `Assets/`, then point their materials at the project's shader.

A `.unitypackage` is a gzipped tar of `<guid>/pathname`, `<guid>/asset`, `<guid>/asset.meta`. Writing `asset` to
`pathname` with its `.meta` alongside reproduces the import exactly - same GUIDs, so prefab → mesh → material →
texture references resolve, and re-importing the full pack later would match rather than duplicate.

## Workflow

1. **Locate the packages.** Asset Store downloads live in
   `%APPDATA%\Unity\Asset Store-5.x\<Publisher>\<Category>\*.unitypackage` (Windows) or
   `~/Library/Unity/Asset Store-5.x/` (macOS). Ask the user if they're elsewhere.
2. **Index them** (no extraction): `python scripts/index_unitypackage.py <out_dir> <pkg>...` writes
   `guid  pathname  sizeKB` per entry. Grep the index for the props/characters/FX the game needs; check the
   pack's own `Prefabs/` naming (e.g. Synty `SM_Prop_*`, `SM_Wep_*`, `SK_*` / `Character_*`).
   **Index every pack the user owns once and keep the indexes** (one `.txt` per package, in a git-ignored folder
   such as `Library/PackIndexes/` or a notes folder outside the project). Indexing a multi-GB pack takes a while;
   grepping saved indexes takes milliseconds, so later requests ("find something that reads as chest armor")
   become one search across every pack - often turning up a better source in a pack you weren't looking at
   (e.g. static modular-character parts instead of props). Re-index only when a pack updates.
3. **Dry-run the extraction** to see what the patterns pull in with dependencies and the size:
   `python scripts/extract_unitypackage.py <project_root> <pkg> "Prefabs/Props/SM_Prop_Barrel_0[1-3]" "Prefabs/Weapons/SM_Wep_Staff" --dry-run`
   Tighten patterns until the list and MB are what you expect.
4. **Extract** (same command without `--dry-run`). It is re-run safe: existing files are skipped, so add more
   patterns later as the game needs them. Put the extracted tree somewhere predictable (packs normally write under
   `Assets/<Vendor>/...` - optionally move afterwards *in the Editor* so GUIDs and references stay intact).
5. **Refresh the Editor** (`AssetDatabase.Refresh()` / focus the Editor) and check the import log.
6. **Convert materials.** Copy `templates/Editor/PackMaterialConverter.cs` into an editor folder, set `PackRoot`,
   the target shader names, and the source slot names for this vendor (see below), then run
   **Tools > Asset Packs > Convert Pack Materials**. Re-run after every new extraction.
7. **Look at it.** Drop a few prefabs in a scene (or render thumbnails) and check for grey/black/pink materials,
   missing alpha cut-outs on foliage, and over-bright emission before wiring them into gameplay.
8. **Commit** with the project's LFS rules covering `.fbx`, `.png`, `.tga`, `.psd` etc. Never change a GUID.

## Choosing what to extract

- Extract **prefabs**, not raw FBX, when the pack has them: prefabs pull their meshes, materials and textures.
- Characters: also extract the avatar/animation assets they reference; animation packs are often separate packages.
- Skip by default (the extractor does): `.cs .asmdef .dll .shader .shadergraph .hlsl .cginc .unity .lighting`.
  Vendor shaders usually target a specific pipeline version and drag in subgraphs/scripts; converting is cheaper.
  Pass `--keep-code` only if the user explicitly wants a vendor script (e.g. a character creator runtime).
- Shared textures (atlases like Synty's `PolygonX_Texture_01`) come along automatically via the materials.
- `--no-deps` extracts just the matches - useful for textures or audio.

## Mapping a vendor's shader properties

Open one extracted `.mat` in a text editor. Under `m_SavedProperties`:
- `m_TexEnvs` lists texture slots (`_Albedo_Map`, `_MainTex`, `_BaseColorMap`, …) → add the base / normal / emission
  names to `BaseSlots`, `NormalSlots`, `EmissionSlots`.
- `m_Floats` holds toggles and values (`_AlphaClip`, `_Alpha_Clip_Threshold`, `_Smoothness`) → `AlphaClipFlags`, `CutoffFloats`.
These saved properties remain after the shader is missing or swapped, which is why conversion works without the
vendor shader and can be re-run. The template already includes Synty's names and Unity's standard ones.

Name-based rules are fine and cheap: foliage (`Leaf`, `Fern`) → alpha clip + double sided; crystals → emission/rim.

## Gotchas

- **Palette-textured characters turn grey.** Synty Sidekick (and many low-poly packs) colour meshes from tiny
  (32×32) palette textures. Mipmaps + bilinear filtering blend neighbouring cells at gameplay distance. Import those
  point-sampled, uncompressed, no mipmaps (`PaletteTextureFilter` in the template).
- **Embedded materials** inside FBX files are read-only; skip them (the template does) or extract the model's
  external `.mat` files, which the importer `.meta` references by GUID - the extractor follows those.
- **Git Bash on Windows rewrites arguments starting with `/`** (paths *and* regexes). Run with
  `MSYS_NO_PATHCONV=1` and pass Windows-style script paths, or run from PowerShell/cmd.
- **Python on Windows:** write helper scripts with a file editor rather than heredocs (escaping breaks), and use
  `encoding='utf8'` when writing indexes.
- **Licensing:** extracted assets stay under the vendor's licence. Don't commit them to public repos or ship them in
  plugins/templates; keep them in the (private) game repo only.
- If materials are pink after conversion, the target shader name is wrong or not in the build's pipeline.

## Files

- `scripts/index_unitypackage.py` - list every entry (GUID, path, size) of one or more packages.
- `scripts/extract_unitypackage.py` - selective, dependency-following, GUID-preserving extraction (`--dry-run`, `--no-deps`, `--keep-code`).
- `templates/Editor/PackMaterialConverter.cs` - re-runnable material conversion + palette texture fix.
