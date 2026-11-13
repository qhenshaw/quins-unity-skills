---
name: unity-playmode-input-tests
description: Write Unity PlayMode tests that drive the game through REAL Input System devices (keyboard, mouse, gamepad state events) and UI Toolkit mouse clicks, so input, menus and UI flows are verified the way a player uses them. Use when a Unity project needs automated gameplay/input/UI tests, when adding a test for a movement, menu, pause, shop or drag-and-drop change, or when tests currently bypass input through autopilots or direct method calls.
---

# PlayMode tests through real input devices

Goal: tests that press keys, move sticks and click UI Toolkit elements through the Input System, so a broken
binding, a disabled action map or a UI element that never receives the click fails a test instead of shipping.

Why this matters: a project once tested movement by calling `PlayerController.ExternalMove()` from an autopilot.
Every test passed while movement and dash were disabled in builds (an `EnableUI()` call turned the gameplay map
off). Driving the device instead of the code path catches that class of bug.

## Workflow

1. **Check the setup.** Input System package (1.x), Test Framework package, a PlayMode test asmdef
   (`templates/Tests.PlayMode.asmdef`: references the game asmdef, `Unity.InputSystem`, `UnityEngine.TestRunner`,
   `UnityEditor.TestRunner`, `nunit.framework.dll`, define constraint `UNITY_INCLUDE_TESTS`).
2. **Copy `templates/InputTestBase.cs`** into the test folder, rename the namespace, and fill in the
   "project hooks" region (load the game scene, start a run, find the UIDocument root). Keep it the only base class.
3. **Write tests as `[UnityTest] IEnumerator`** that inherit from it: act through `Press` / `Release` / `Set` /
   `Tap` / `MoveMouse` / `Click`, then assert on game state. See `templates/ExampleInputTests.cs`.
4. **Use code shortcuts only for setup, never for the thing under test.** Starting a run, granting gold or ending a
   wave through code is fine; the step being tested (moving, pausing, buying, dragging) must go through a device.
5. **Run them** in the live Editor or batchmode and keep them green. If a behaviour can't be driven through a
   device, say so and list it as a manual check rather than faking the input.

## Key techniques

- **Queue state events, don't call actions.** `StateEvent.From(device)` + `control.WriteValueIntoEvent(value, ptr)`
  + `InputSystem.QueueEvent(ptr)` goes through actions, interactions and the UI input module like hardware.
- **Don't use `InputTestFixture`** when the game enables its actions at startup (bootstrap, `RuntimeInitializeOnLoadMethod`):
  the fixture resets the Input System after that, leaving the game's actions with stale state. Use the existing
  `Keyboard.current` / `Mouse.current` and add a `Gamepad` per test, removed in teardown.
- **Ignore Game view focus.** Set `InputSystem.settings.editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView`
  and `backgroundBehavior = IgnoreFocus` in SetUp (restore in TearDown), or results depend on which window was clicked last.
- **Release everything in TearDown** (neutral `KeyboardState`, mouse button up, neutral `GamepadState`), wait a frame,
  then remove added devices. Reset `Time.timeScale` and any pause state.
- **UI Toolkit mouse coordinates:** panel space has origin top-left and may be scaled; the mouse device uses screen
  pixels with origin bottom-left. Convert with `PanelToScreen` (uses `panel.visualTree.layout` for scale). For
  world → panel (e.g. a health bar over a head) use `RuntimePanelUtils.CameraTransformWorldToPanel` and compare in
  panel space — mixing conversions makes tests flaky.
- **Give input a couple of frames.** Press → `yield return null` ×2 → release. UI clicks need the pointer move
  processed before the press, so `Click` moves first, waits, then presses.

## Gotchas (each one cost a debugging session)

- **No move event if the mouse is already there.** A test that hovers the same position the previous test left the
  mouse at gets no `PointerMove`, so hover/tooltip code never runs. `MoveMouse` nudges away first.
- **`VisualEffect.aliveParticleCount` and similar GPU readbacks lag.** Poll for up to ~30 frames with `WaitFor`
  instead of asserting on the next frame.
- **`WaitForSeconds` stops while paused** (`timeScale = 0`). Use `WaitForSecondsRealtime` around pause menus;
  `WaitFor` uses unscaled time.
- **Random content makes setup flaky.** Reroll / reseed until the precondition holds and `Assert.Inconclusive` if it
  can't, rather than failing for the wrong reason.
- **Builds start at the main menu.** Have at least one test that enters gameplay from the real first scene; scene-
  direct tests miss input maps that the menu disables.
- **Script recompiles are deferred while in Play mode** — exit Play mode before re-running after a code edit.

## Running from the CLI

With the Unity Pipeline package (`unity` CLI) and an open Editor, use `scripts/run-tests.sh`. Run the scripts as `bash <this skill's base directory>/scripts/<name>` (the base directory is printed when the skill loads); don't `find` them in the plugin cache, where older plugin versions linger. It waits out the few seconds after a recompile or Play-mode change when the Editor's Pipeline server is down ("isn't reachable" / "No Pipeline instance") and resubmits, so you don't need to poll the Editor first:

```
scripts/run-tests.sh <TestAssemblyName> Assembly     # whole suite
scripts/run-tests.sh ArmorTests                       # one class (TestName is a partial match)
scripts/run-tests.sh "ArmorTests.Block_"              # one test
```

It exits non-zero on failures, inconclusive tests, zero matches or a rejected run, and prints each failure's message.
The raw commands are `unity command test run --mode playmode --filter <f> --filter-type <t> --async-tests true`
then polling `unity command test status --detail summary` / `--detail failures`. Watch for:

- **`test status` shows the previous run until a new one starts.** A run that never started looks like a fresh
  green result. Check that `total` / `duration` changed (the script waits for that).
- **`--filter-type` is only `TestName`, `Assembly` or `Category`.** `Class` and `Regex` don't exist; the command
  prints its usage and runs nothing - which, with the point above, reads as "all passed".
- **`TestName` is a case-insensitive partial match on the full name** (`Namespace.Class.Method`), so a class name
  or a `Class.MethodPrefix` selects a group - there's no need for a class filter.
- **`"total":0` is not a pass.** A typo'd filter matches nothing and reports zero failures.
- **Count-based assertions** (e.g. "27 recipes") break when content grows - update them with the content, and give
  the message the reason for the number.

Batchmode alternative: `Unity.exe -batchmode -projectPath <p> -runTests -testPlatform PlayMode -testResults results.xml`
(the project must not be open in another Editor).

## Files

- `templates/InputTestBase.cs` — device setup/teardown, `Press`/`Release`/`Set`/`Tap`, `WaitFor`, UI Toolkit mouse helpers.
- `templates/ExampleInputTests.cs` — keyboard movement, gamepad stick + button, pause with real time, drag-and-drop in UI Toolkit.
- `templates/Tests.PlayMode.asmdef` — test assembly definition.
- `scripts/run-tests.sh` — runs tests in the open Editor via the `unity` CLI and reports a result that can't be stale.
