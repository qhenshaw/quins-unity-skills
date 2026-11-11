# quins-unity-skills

A Claude Code plugin with Unity skills distilled from working prototypes.

## Skills

| Skill | What it does |
|---|---|
| `quins-unity-skills:unity-vfx-from-texture-pack` | Builds a full set of gameplay VFX (hits, crits, deaths, explosions, slashes, lightning, status effects, pickups, trails…) as VFX Graph assets generated from code, using a texture / flipbook pack. Includes the generator, runtime `EffectsManager`, a PlayMode test, and scripts to inventory a pack (contact sheets, flipbook grid detection). Proven on Unity 6000.6 / URP 17.6 / VFX Graph 17.6. |
| `quins-unity-skills:unity-playmode-input-tests` | PlayMode tests that drive the game through real Input System devices (keyboard, mouse, gamepad state events) and UI Toolkit mouse clicks/drags, so broken bindings and disabled action maps fail a test instead of shipping. Base class, examples, asmdef, CLI run commands and the flakiness gotchas. |
| `quins-unity-skills:unity-asset-pack-extraction` | Pulls only the assets you use out of large `.unitypackage` packs (Synty, Asset Store), GUIDs preserved and dependencies followed, skipping vendor shaders/scripts; then converts the materials to your own shader (URP Lit or custom) and fixes palette-textured characters that render grey. Index + extract scripts and a re-runnable converter. |
| `quins-unity-skills:unity-model-icon-renderer` | Renders prefabs into transparent, outlined sprite icons sized to an item's cell footprint, auto-posed (long weapons diagonal, compact props pick the best-filling view, small props tiled in long slots). URP preview-scene renderer, re-runnable. |
| `quins-unity-skills:unity-editor-cli-recovery` | Keeps a Unity Editor driven through the Pipeline package's `unity command` responsive: finds and clicks the modal dialog that blocks commands (per project, safe with several Editors open), captures the game view to any path without leaving files in the project, wraps Play mode with Run In Background, and how to look up commands and C# APIs for the installed version. Windows dialog script + POSIX capture script. |

## Install

From GitHub:

```
/plugin marketplace add qhenshaw/quins-unity-skills
/plugin install quins-unity-skills@quins-unity-skills
```

To try it locally before publishing:

```
/plugin marketplace add E:/Projects/quins-unity-skills
/plugin install quins-unity-skills@quins-unity-skills
```

## Layout

```
.claude-plugin/plugin.json        plugin manifest (bump "version" when publishing changes)
.claude-plugin/marketplace.json   lets this repo act as its own marketplace
skills/<skill-name>/SKILL.md      one folder per skill (+ templates/, scripts/, references/)
```

Add a new skill by creating `skills/<new-skill>/SKILL.md` with `name` and `description` frontmatter.

## Notes

- No licensed art is included. Skills reference texture packs / models by path in your own project.
- The VFX skill's scripts need Python 3 with Pillow and numpy (`pip install pillow numpy`); the asset pack scripts need only Python 3.
- Bump `version` in both `.claude-plugin/plugin.json` and `marketplace.json` when publishing changes, or installed copies won't update.
