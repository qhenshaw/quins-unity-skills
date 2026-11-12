# quins-unity-skills

A Claude Code plugin with Unity skills distilled from working prototypes.

## Skills

| Skill | What it does |
|---|---|
| `quins-unity-skills:unity-vfx-from-texture-pack` | Builds a full set of gameplay VFX (hits, crits, deaths, explosions, slashes, lightning, status effects, pickups, trails…) as VFX Graph assets generated from code, using a texture / flipbook pack. Includes the generator, runtime `EffectsManager`, a PlayMode test, and scripts to inventory a pack (contact sheets, flipbook grid detection). Proven on Unity 6000.6 / URP 17.6 / VFX Graph 17.6. |
| `quins-unity-skills:unity-playmode-input-tests` | PlayMode tests that drive the game through real Input System devices (keyboard, mouse, gamepad state events) and UI Toolkit mouse clicks/drags, so broken bindings and disabled action maps fail a test instead of shipping. Base class, examples, asmdef, the flakiness gotchas, and a CLI runner that can't report a stale or empty run as green. |
| `quins-unity-skills:unity-asset-pack-extraction` | Pulls only the assets you use out of large `.unitypackage` packs (Synty, Asset Store), GUIDs preserved and dependencies followed, with saved package indexes for instant searches across every pack, skipping vendor shaders/scripts; then converts the materials to your own shader (URP Lit or custom) and fixes palette-textured characters that render grey. Index + extract scripts and a re-runnable converter. |
| `quins-unity-skills:unity-model-icon-renderer` | Renders prefabs into transparent, outlined sprite icons sized to an item's cell footprint, auto-posed (long weapons diagonal, compact props pick the best-filling view, small props tiled in long slots). URP preview-scene renderer, re-runnable, plus a contact-sheet script and a review loop for fixing poses and swapping models. |
| `quins-unity-skills:unity-editor-cli-recovery` | Keeps a Unity Editor driven through the Pipeline package's `unity command` responsive: finds and clicks the modal dialog that blocks commands (per project, safe with several Editors open), retries the transient "400 Bad Request" / "No Pipeline instance" failures during domain reloads, refreshes before recompiling so new scripts aren't silently skipped, captures the game view to any path without leaving files in the project, wraps Play mode with Run In Background, and how to look up commands and C# APIs for the installed version. Windows dialog script + POSIX capture, retry and recompile scripts. |
| `quins-unity-skills:unity-debug-menu-cli` | An in-game UI Toolkit debug menu where every feature registers its own section of buttons, toggles, sliders and live readouts, compiled out of release builds, plus two generic `unity command debugmenu` commands that make every control a CLI command, so tests and agents set up and read game state without extra code. Runtime menu, Editor commands, an example feature section and a quiet readout script. |
| `quins-unity-skills:unity-steam-deck-deploy` | Builds a Windows x64 player for the Steam Deck under Proton (D3D11 first, fullscreen window, project settings restored), smoke-tests it locally, then pushes it to a dev-kit-paired Deck over SSH: staged upload, file count + byte verification, swap without killing a running session, one-time Proton shortcut registration, and Player.log fetch. Editor template + two bash scripts. |

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

## Evals

`evals/` checks that each skill is picked up from a realistic request that doesn't name it (one `trigger-*` case per
skill, two for the CLI recovery skill) and that none load for an unrelated task (`no-trigger-unrelated`):

```
claude plugin eval . --ablation none --no-publish -j 4
```

About $2 for the full suite (9 cases x 2 runs). Run it after changing a skill's `description`, or after adding a skill
(copy a `trigger-*` folder and change the prompt and the skill name in its grader). Results go to `evals/results/`
(git-ignored).

## Notes

- No licensed art is included. Skills reference texture packs / models by path in your own project.
- The VFX skill's scripts need Python 3 with Pillow and numpy (`pip install pillow numpy`); the asset pack scripts need only Python 3, as does the debug menu's readout script (plus the `unity` CLI). The icon sheet script needs Pillow. The Steam Deck scripts need bash, ssh and tar (Git Bash on Windows) and a Deck paired with the SteamOS Devkit Client.
- Bump `version` in both `.claude-plugin/plugin.json` and `marketplace.json` when publishing changes, or installed copies won't update.
