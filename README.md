# quins-unity-skills

A Claude Code plugin with Unity skills distilled from working prototypes.

## Skills

| Skill | What it does |
|---|---|
| `quins-unity-skills:unity-vfx-from-texture-pack` | Builds a full set of gameplay VFX (hits, crits, deaths, explosions, slashes, lightning, status effects, pickups, trails…) as VFX Graph assets generated from code, using a texture / flipbook pack. Includes the generator, runtime `EffectsManager`, a PlayMode test, and scripts to inventory a pack (contact sheets, flipbook grid detection). Proven on Unity 6000.6 / URP 17.6 / VFX Graph 17.6. |

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
- The VFX skill's scripts need Python 3 with Pillow and numpy (`pip install pillow numpy`).
