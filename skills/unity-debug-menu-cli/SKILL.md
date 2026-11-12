---
name: unity-debug-menu-cli
description: Add an in-game debug menu to a Unity project where every feature registers its own section of buttons, toggles, sliders and live readouts, and two generic CLI commands expose every control to tests and agents. Use when a Unity project wants an in-game debug menu, wants cheats or tuning controls per feature (god mode, spawn, kill, time scale), or wants tests and agents to set up and read game state from the command line instead of through one-off scripts.
---

# Debug menu with a CLI mirror

Goal: one place to set up and inspect game state. Each feature adds a section of controls to an in-game menu,
and the same controls are reachable from the command line, so a person, a test and an agent all steer the game
the same way. A control added to the menu is a command at once; nothing extra is written for the CLI.

How it sits next to its neighbours: `unity-editor-cli-recovery` keeps the Editor answering commands,
`unity-playmode-input-tests` verifies input the way a player uses it, and this skill sets and reads game state.
**Debug controls are for setting up and inspecting state, not for testing input.** Spawn the boss with a debug
button, then fight it through real devices.

## The pieces

- **Menu** (`templates/Runtime/`, `templates/UI/`): UI Toolkit, opened and closed by an Input System action, one
  collapsible foldout per section, and the game keeps running while it is open. `DebugMenu.Register` and
  `Unregister` are `[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]`, so calls and their lambdas
  vanish from release builds; `DebugMenuView` wraps its body in `#if UNITY_EDITOR || DEVELOPMENT_BUILD` and
  destroys itself otherwise.
- **Registration, one-way.** The menu lives in its own assembly that references no feature. Features reference
  it and register their own section, so adding or deleting a feature never touches the menu:

  ```csharp
  private void OnEnable() => DebugMenu.Register(this, "Spells", section => section
      .Button("Refill mana", () => mana.Fill())
      .Toggle("Infinite mana", () => infiniteMana, v => infiniteMana = v)
      .Slider("Cast speed", 0.1f, 5f, () => castSpeed, v => castSpeed = v)
      .Readout("Mana", () => $"{mana.Current:0}/{mana.Max:0}"));

  private void OnDisable() => DebugMenu.Unregister(this);
  ```

  Sections that share a title merge into one foldout.
- **CLI mirror** (`templates/Editor/DebugMenuCommands.cs`, needs the Pipeline package's `unity` CLI): two
  `[CliCommand]` methods walk the same registry the menu draws from.

  ```bash
  unity command debugmenu list [--section Enemies]      # sections, controls, current values
  unity command debugmenu do --section Enemies --label "Freeze AI" --value true
  ```

  `do` presses a button, sets a toggle (`true`/`false`) or a slider (number), and returns the section's values
  afterwards. Both work in Play mode whether the menu is open or closed.

## Workflow

1. **Copy the templates** into a folder of their own (e.g. `Assets/Features/DebugTools/`): `Runtime/` and `UI/`
   together, `Editor/` in an `Editor` subfolder. Name it `DebugTools`, not `Debug`, which clashes with
   `UnityEngine.Debug`. Rename the `MyGame` placeholder in namespaces and both asmdefs to the project's name. If
   the project has no asmdefs, delete both and keep the `Editor` folder.
2. **Build the menu object** (a prefab is best): a GameObject with a `UIDocument` (source asset
   `DebugMenu.uxml`; a Panel Settings asset using `DebugTheme.tss`, sort order 1000 so it draws over game UI,
   Scale With Screen Size at 1920x1080), `DebugMenuView` and `GameDebugControls`. Put it in every scene that
   should have the menu, or spawn it at load.
3. **Bind the toggle action.** Out of the box `DebugMenuView` uses its own action: backquote on keyboard,
   Select + Start on gamepad. Edit that action's bindings in the Inspector, or add a `Debug/ToggleDebugMenu`
   action to the project's input actions asset and assign it to `Toggle Action Reference`. Keep it in a map that
   is never disabled, so the menu opens during menus and cutscenes too.
4. **Give every feature a section** as part of building the feature (`templates/Example/EnemiesDebugControls.cs`):
   - buttons for its main actions (spawn, attack, kill, give item, skip to phase),
   - toggles for its cheats and states (god mode, infinite resources, freeze AI),
   - sliders for its tuning values,
   - readouts of its live state, one line each, written so a single read answers "what is it doing now".

   Put the registration in the feature's `Scripts/Debug/` folder. A feature with no object of its own in the
   scene puts its controls component on the menu prefab. List the controls in the feature's docs and update them
   when the feature changes.
5. **Drive it from the CLI.** `scripts/unity-readout.sh` prints only the answer, which suits loops and checks:

   ```bash
   bash scripts/unity-readout.sh -p <project> Enemies              # every value in the section
   bash scripts/unity-readout.sh -p <project> Enemies Alive        # one value
   bash scripts/unity-readout.sh -p <project> --do Enemies "Kill all"
   ```

   Write a dedicated, named `[CliCommand]` only for something used very often, or that needs arguments a
   button, toggle or slider can't express.

## Pitfalls

- **Reset debug statics when Play starts.** With domain reload off, static cheats and the section list survive
  into the next Play session. `DebugMenu` clears itself in a
  `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`; do the same for every static flag a toggle sets.
- **A pause or level-up screen freezes what the menu drives.** With `Time.timeScale` at 0 a button still runs,
  but nothing it started moves and readouts stop changing. Before deciding a control is broken, read the
  `Game` section's `Time scale`, and give the screens that pause a readout or a button to dismiss them.
- **Labels are the CLI's keys.** `--label` matches the text exactly and takes the first match in the section,
  so keep labels stable and unique per section. Renaming one breaks every script that uses it. Labels built
  from asset names (`$"Spawn {prefab.name}"`) change when the asset is renamed.
- **The `unity` CLI prints notices on stderr** (update prompts, banners) that break JSON parsing. Read results
  with `--result-only` and discard stderr (`2>/dev/null`), as the script does. Some versions return the result
  as a JSON string that has to be parsed a second time.
- **Sections exist only while their owner is enabled.** Outside Play mode `list` returns `[]`, and a section
  owned by an object that isn't in the scene yet is missing. An empty list is not a failed command.
- **Keep serialized fields outside the `#if`**, and only the code inside, or the component's data differs
  between Editor and release builds.

## Files

- `templates/Runtime/` — `DebugMenu.cs` (registry, section builder), `DebugMenuView.cs` (the UI and toggle
  action), `GameDebugControls.cs` (FPS, time scale, reload scene), `MyGame.DebugTools.asmdef`.
- `templates/UI/` — `DebugMenu.uxml`, `DebugMenu.uss`, `DebugTheme.tss`.
- `templates/Editor/` — `DebugMenuCommands.cs`, `MyGame.DebugTools.Editor.asmdef` (references
  `Unity.Pipeline.Attributes`).
- `templates/Example/EnemiesDebugControls.cs` — one feature's section with all four control kinds.
- `scripts/unity-readout.sh` — quiet read and `--do`; needs the `unity` CLI and Python 3.
