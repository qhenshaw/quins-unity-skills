---
name: unity-editor-cli-recovery
description: Keep a Unity Editor that is driven from the command line (the Pipeline package's `unity command`) responsive and tidy. Use when `unity command` calls time out while `unity status` still says ready, fail with "400 Bad Request" or "No Pipeline instance" after a recompile or Play mode change, when a newly written script is missing after a recompile, after a branch switch, pull or scene rebuild, when a game-view capture is needed as a file, when Play mode is driven with the Editor unfocused, or before calling an Editor command or C# API you haven't used yet. Complements the unity-cli skill, which covers the commands themselves.
---

# Unity Editor CLI recovery

The `unity-cli` skill teaches the commands. This skill covers what goes wrong when an agent drives an Editor it
can't see: modal dialogs that block every command, captures that leave files in the project, a Play mode that
freezes because the Editor is unfocused, and guessed command names.

Scripts live next to this file: `scripts/unity-dialog.ps1` (Windows), and `scripts/capture.sh`,
`scripts/unity-retry.sh`, `scripts/unity-recompile.sh`, `scripts/unity-eval.sh` and `scripts/play-capture.sh` (Git
Bash or any POSIX shell). Run them as `bash <this skill's base directory>/scripts/<name>` - the base directory is
printed when the skill loads. Don't `find` them in the plugin cache: older plugin versions stay there and the first
match is often stale. Pass `--project-path <project>` to every `unity command`: another project's Editor is often open
too, and without it the call can go to the wrong one.

## 1. Commands time out but `unity status` says ready

A modal dialog almost always causes this. The Editor's main thread waits on the dialog, so the Pipeline server
accepts connections but never answers. Don't wait for the user and don't restart Unity first:

```bash
powershell -File scripts/unity-dialog.ps1 -Project <project path or name>            # windows and their buttons
powershell -File scripts/unity-dialog.ps1 -Project <project path or name> -Click Reload
```

`-Project` matches the Editor whose main window title starts with that project's path (or folder name). Without
it the script lists every Editor; `-Click` refuses to act without `-Project` when several Editors are open.

Which button to press:

| Dialog | Press | Why |
|---|---|---|
| "The open scene(s) have been modified externally" (after a checkout, pull, or a script that rewrote the scene) | `Reload` | The file on disk is what git has; `Ignore` keeps a stale scene that overwrites it on the next save. |
| "Scene(s) Have Been Modified" / save prompt before a build, Play mode or scene switch | `Don't Save` if the changes are scratch work, `Save` only if this session made them on purpose | Saving keeps edits nobody reviewed. |
| API Updater, Safe Mode, package or Editor version prompts, licence dialogs | Ask the user | These change the project or its setup. |

Run the script again after clicking: dialogs can chain, such as a reload followed by a save prompt. Restart
Unity only if no dialog shows and commands still time out.

## 2. "400 Bad Request" / "No Pipeline instance" right after a change

Different from section 1: the call fails *fast*. The Editor drops its Pipeline server during every domain reload
(recompile, entering Play mode, asset import), so a call made in that window gets one of these, or empty output.
It is transient - retry, don't diagnose:

```bash
scripts/unity-retry.sh -- command script eval --file "$(cygpath -w my.cs)" --project-path <project>
scripts/unity-retry.sh --tries 3 -- recompile --project-path <project>
```

It retries only those transient failures (12 × 5 s by default) and returns anything else at once, including a
compile error in your eval snippet. A timeout isn't retried: that's section 1.

**Files created outside the Editor need an `AssetDatabase.Refresh()` before `unity recompile`.** Recompile only
compiles scripts the AssetDatabase already knows. A new `.cs` or `.asmdef` written by an agent is left out, the
recompile reports no errors, and the new type then "doesn't exist". Use `scripts/unity-recompile.sh <project>`, which
refreshes, recompiles with retries, prints the compiler messages and exits 1 on errors. It also stops Play mode
first (`--stay-in-play` to skip): the Editor defers compilation while playing, so a recompile in Play mode compiles
nothing. The same goes for new assets you'll load from code in the next eval (a texture, a prefab copied in by a
script): refresh first.

**Passing values to an eval.** `script eval` runs inside the Editor, so shell variables never reach the snippet.
Write `{{key}}` where a value goes and run `scripts/unity-eval.sh <project> show.cs id=Pistol mode=fusion`: it fills
the placeholders (quote them in the C#: `"{{id}}"`), refuses to run with any left unfilled, and retries like
`unity-retry.sh`. One snippet then serves every item or mode instead of a copy per value.

## 3. Game-view captures as files

```bash
bash scripts/capture.sh <project-path> <out.png> [--width 1920 --height 1080 --source camera ...]
```

It prints the PNG's path. Newer Pipeline packages take `--output-path` with an absolute path, which writes
nowhere else. Older ones only take `--save_path`, which lands inside the project (some versions resolve it
against `Assets/`, so Unity also writes a `.meta`). The script tries the first and falls back to a throwaway
folder that it moves out and deletes with its `.meta`. Either way, nothing reaches `git status`. Afterwards,
check with `git status --short` that the project is still clean.

Without `--save_path`/`--output-path` the command returns the image inline as base64, which is fine for a quick
look but bloats the transcript: prefer a file and read it.

`--source screen` (the default) includes Screen Space - Overlay UI in Play mode. `--source camera` doesn't need a
Game window but, on older packages, never shows overlay canvases.

**A game state, captured in one command.** To check UI or a scene in a specific state (an item's tooltip, a boss
fight, a menu), use `scripts/play-capture.sh` instead of chaining the steps by hand:

```bash
bash scripts/play-capture.sh -p <project> --scene Assets/Scenes/Game.unity --until to-shop.cs \
  --shot wand=show-item.cs:id=Pistol --shot totem=show-item.cs:id=BloodiedTotem \
  --crop 455,30,450,1030 --sheet out/tooltips.png
```

It recompiles (which also leaves Play mode), enters Play with Run In Background on, runs the `--until` snippet once
a second until it returns `ready`, then for each `--shot` runs its setup snippet (placeholders as in
`unity-eval.sh`), waits `--settle` seconds and captures. `--crop` + `--sheet` tile the shots into one labelled
image to read in a single look. It leaves Play mode and turns Run In Background off afterwards (`--stay` to keep
playing), and writes nothing into the project. Let the `--until` snippet also push the game forward (start the run,
end the wave, pick level-ups) and return the state it is in otherwise, so "how to get there" lives in one file.
Use it for the mockup → build → screenshot → compare loop: capture the same items the mockup shows, compare,
fix, and capture again.

## 4. Play mode with the Editor unfocused

An Editor driven from a terminal is usually unfocused. Without Run In Background its player loop stops after the
first frame, while `editor status` still reports playing. Wrap Play-mode work:

```bash
unity command --project-path <p> editor.playmode set_run_in_background --enable true
unity command --project-path <p> editor.playmode start
# ... drive and check the game ...
unity command --project-path <p> editor.playmode stop
unity command --project-path <p> editor.playmode set_run_in_background --enable false
```

Always turn it back off. The override is recorded in
`ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json`, which otherwise shows up as a change.

## 5. Look commands and APIs up instead of guessing

Parameter names differ between Pipeline versions (`save_path` vs `--output-path` above), so check the installed
package before using a command for the first time:

- `unity command --project-path <p> --no-pager --format tsv` lists the command groups;
  add `--tag <group>` for a group's commands and parameters.
- `<project>/Library/PackageCache/com.unity.pipeline@*/Documentation~/commands/*.md` is the full reference for
  that project's version.
- A wrong parameter prints the command's usage text instead of an error. If the output is a list of `--options`,
  the call didn't run.

For C# through `eval`:

- Print a type's surface before writing code against it:
  `eval 'return string.Join("\n", typeof(T).GetMethods().Select(m => m.ToString()));'`.
- `eval` returns its `return` value. For async work (deploys, long imports), store the outcome with
  `AppDomain.CurrentDomain.SetData("key", value)` in one eval and poll `AppDomain.CurrentDomain.GetData("key")`
  with a second, rather than blocking the main thread.

## Checklist when a session ends

- No `_cli_capture_*` folders, stray PNGs or `.meta` files in the project (`git status --short`).
- Run In Background override turned back off; Play mode stopped.
- Any dialog you dismissed is mentioned to the user, together with the button you pressed.
