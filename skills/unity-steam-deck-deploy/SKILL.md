---
name: unity-steam-deck-deploy
description: Build a Unity game for the Steam Deck (Windows x64 under Proton, D3D11 first, fullscreen window), smoke-test the build locally, and push it to a dev-kit-paired Deck over SSH - streamed to a staging folder, verified by file count and bytes, swapped in without killing a running session, and registered as a Proton shortcut once. Use when the user wants a Deck build, wants to upload/deploy/sideload a build to their Steam Deck, or tests each feature on a Deck.
---

# Unity → Steam Deck (Proton) build and deploy

The Deck runs Windows builds through Proton, so a native Linux build isn't needed. Ship a Windows x64 player built
with **Direct3D 11 first** (Proton translates D3D11 through DXVK, its most mature path) in **fullscreen-window**
mode (takes the Deck's native 1280×800), then copy it over the dev kit SSH connection. The deploy runs after every
feature, so make it one command that can't half-copy a build.

## One-time setup (the user does this)

1. On the Deck: Settings > System > enable Developer Mode, then Developer > enable **Dev Kit**.
2. On the PC: install the **SteamOS Devkit Client** and pair with the Deck. Pairing creates the SSH key
   (Windows: `%LOCALAPPDATA%\steamos-devkit\steamos-devkit\devkit_rsa`; Linux: `~/.local/share/steamos-devkit/devkit_rsa`)
   and installs `~/devkit-utils/` on the Deck. Note the Deck's IP (the client shows it).
3. Test it: `ssh -i <key> deck@<ip> echo ok`.

Ask for the IP if it isn't known; never guess one.

## Workflow

1. **Build.** Copy `templates/Editor/SteamDeckBuild.cs` into an editor folder, rename the namespace, and run
   **Build > Steam Deck (Proton)** or, from the `unity` CLI, `script eval` with
   `return MyGame.Editor.SteamDeckBuild.Build();`. It returns one line: `Succeeded <folder> size=… errors=…`.
2. **Smoke-test locally** if the game has a headless/autoplay mode (strongly recommended - a Deck round trip is
   slow): `scripts/local-smoke.sh <build>/<Game>.exe <game args…>` runs it with a timeout and fails on exceptions in
   the player log. No autoplay mode? Say so and list what to check by hand on the Deck.
3. **Deploy.** `scripts/deck-deploy.sh --host <ip> --build <folder> --game <Name> [--exe <Name>.exe] [--register]`:
   - streams a `tar` of the build over SSH into `~/devkit-game/<Name>.new` (skips `*_BackUpThisFolder_ButDontShipItWithYourGame`,
     the IL2CPP debug folder - it's often bigger than the game);
   - compares **file count and total bytes** on both sides and stops if they differ;
   - swaps `.new` into `~/devkit-game/<Name>`. If the game is **running**, the old build is kept as `<Name>.old` (the
     running process keeps its open files) - it never kills the user's session. Tell the user to quit and relaunch;
   - `--register` (first deploy only) adds the Steam shortcut with Proton Experimental via the dev kit's
     `steam-client-create-shortcut`. It then shows up under Non-Steam in Gaming Mode.
4. **Report** what was verified (local smoke result, byte-identical copy) and what wasn't (anything only visible on
   the Deck: controls, performance, text size at 1280×800).

## Gotchas

- **`~/devkit-utils/*` scripts have CRLF line endings** - running them directly fails with `bad interpreter`/`\r`.
  Call them through `python3 ~/devkit-utils/<script>`.
- **Don't rsync from Git Bash on Windows** - there's usually no rsync on the PC side; a `tar` stream over `ssh` works
  everywhere and is one connection.
- **Don't overwrite a running game's folder in place.** Wine/Proton maps the assemblies; partial overwrites crash it
  or mix versions. Stage and swap.
- **Restore the project's graphics API settings** after the build (the template does it in `finally`) and save
  assets, or ProjectSettings keeps D3D11-only. Also reset `Time.timeScale = 1` before building: a Play session
  stopped while paused leaves it at 0 and the settings save writes it into the project - the build starts frozen.
- **Gamepad style:** the Deck reports itself as an Xbox controller. If the game shows platform-specific button art,
  key a Steam Deck style off an env var (`SteamDeck=1` is set by Steam on the Deck) rather than the device name.
- **Player.log** lives inside the Proton prefix:
  `~/.local/share/Steam/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/LocalLow/<Company>/<Product>/Player.log`.
  For a quick look, `scripts/deck-deploy.sh --host <ip> --game <Name> --log` finds the newest one.
- **Black screen at launch:** try launch options `-force-d3d12` or `-force-vulkan`.

## Files

Run the scripts as `bash <this skill's base directory>/scripts/<name>` (the base directory is printed when the
skill loads); don't `find` them in the plugin cache, where older plugin versions linger.

- `templates/Editor/SteamDeckBuild.cs` - menu + `Build()` (D3D11/D3D12, fullscreen window, settings restored, README
  written next to the exe for manual installs).
- `scripts/deck-deploy.sh` - staged upload, verification, swap, optional shortcut registration, log fetch.
- `scripts/local-smoke.sh` - runs the built exe with a timeout and checks its log for exceptions.
