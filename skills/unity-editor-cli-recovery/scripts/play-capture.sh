#!/usr/bin/env bash
# Put the game in a state and screenshot it, in one command: recompile, enter Play, wait until a check says ready,
# then for each shot run its setup snippet and capture the game view. Optionally crop and tile the shots into one
# contact sheet. Leaves Play mode afterwards (unless --stay). Nothing is written into the project.
#
# usage: play-capture.sh -p <project> --shot NAME=setup.cs[:k=v,k=v] [--shot ...]
#                        [--scene Assets/Scenes/Game.unity] [--until ready.cs[:k=v]] [--timeout 90]
#                        [--out <dir>] [--crop x,y,w,h] [--sheet sheet.png] [--width 1920 --height 1080]
#                        [--settle 0.5] [--no-recompile] [--stay]
#
#   --until   An eval snippet run once a second until its last output line is "ready". It may also push the game
#             forward each time it runs (e.g. start the run, end the wave, pick level-ups) and return the state
#             it is in otherwise - that keeps "how to get there" in one file.
#   --shot    NAME=FILE[:k=v,...]: FILE is run through unity-eval.sh with those placeholders ({{k}}), then the
#             game view is saved as <out>/NAME.png. Repeat for several shots of the same session.
#   --crop    x,y,w,h in capture pixels, applied to every shot before tiling (e.g. one UI panel).
#   --sheet   tile the (cropped) shots left to right into one PNG with their names underneath (needs Pillow).
#   --settle  seconds to wait after a setup snippet before capturing (layout / animations), default 0.5.
#
# Example (two item tooltips, cropped to the inspector, side by side):
#   play-capture.sh -p 'E:\Proj' --scene Assets/_Project/Scenes/Game.unity --until to-shop.cs \
#     --shot wand=show-item.cs:id=Pistol --shot totem=show-item.cs:id=BloodiedTotem \
#     --crop 455,30,450,1030 --sheet /tmp/tooltips.png
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PROJECT=""; SCENE=""; UNTIL=""; TIMEOUT=90; OUT=""; CROP=""; SHEET=""; WIDTH=1920; HEIGHT=1080; SETTLE=0.5
RECOMPILE=1; STAY=0; SHOTS=()
while [ $# -gt 0 ]; do
  case "$1" in
    -p|--project) PROJECT="$2"; shift 2 ;;
    --scene) SCENE="$2"; shift 2 ;;
    --until) UNTIL="$2"; shift 2 ;;
    --timeout) TIMEOUT="$2"; shift 2 ;;
    --shot) SHOTS+=("$2"); shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    --crop) CROP="$2"; shift 2 ;;
    --sheet) SHEET="$2"; shift 2 ;;
    --width) WIDTH="$2"; shift 2 ;;
    --height) HEIGHT="$2"; shift 2 ;;
    --settle) SETTLE="$2"; shift 2 ;;
    --no-recompile) RECOMPILE=0; shift ;;
    --stay) STAY=1; shift ;;
    *) echo "unknown argument: $1" >&2; sed -n 5,9p "$0" >&2; exit 2 ;;
  esac
done
[ -n "$PROJECT" ] && [ ${#SHOTS[@]} -gt 0 ] || { sed -n 2,24p "$0"; exit 2; }
OUT="${OUT:-$(mktemp -d -t play-capture-XXXXXX)}"; mkdir -p "$OUT"
export UNITY_NO_PAGER=1

code_eval() { # $1 = C# snippet
  local tmp file; tmp="$(mktemp -t pc-XXXXXX).cs"; printf '%s\n' "$1" > "$tmp"
  file="$tmp"; command -v cygpath >/dev/null && file="$(cygpath -w "$tmp")"
  bash "$HERE/unity-retry.sh" -- command script eval --file "$file" --project-path "$PROJECT" 2>&1; local c=$?
  rm -f "$tmp"; return $c
}
file_eval() { # $1 = FILE[:k=v,k=v]
  # Split on the last ':' that starts "key=" (so Windows paths like C:/x.cs keep their drive colon).
  local spec="$1" f="$1" vars=()
  if [[ "$spec" =~ ^(.+):([A-Za-z_][A-Za-z0-9_]*=.*)$ ]]; then
    f="${BASH_REMATCH[1]}"; IFS=',' read -r -a vars <<< "${BASH_REMATCH[2]}"
  fi
  bash "$HERE/unity-eval.sh" "$PROJECT" "$f" "${vars[@]}" 2>&1
}
last_line() { tr -d '\r' | grep -v '^\s*$' | grep -v 'claude-code-hint' | tail -1; }

# 1. Fresh scripts (this also stops Play mode: nothing compiles while playing).
if [ $RECOMPILE = 1 ]; then
  bash "$HERE/unity-recompile.sh" "$PROJECT" > "$OUT/recompile.txt" || { echo "compile errors:" >&2; grep -i '^error' "$OUT/recompile.txt" >&2; exit 1; }
fi

# 2. Enter Play (optionally in a given scene), with Run In Background so an unfocused Editor keeps ticking.
unity command --project-path "$PROJECT" editor.playmode set_run_in_background --enable true >/dev/null 2>&1
open=""
[ -n "$SCENE" ] && open="UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\"$SCENE\");"
code_eval "if (!UnityEditor.EditorApplication.isPlaying) { $open UnityEditor.EditorApplication.EnterPlaymode(); } return \"entering\";" >/dev/null
for i in $(seq 1 60); do
  s=$(code_eval 'return UnityEditor.EditorApplication.isPlaying && !UnityEditor.EditorApplication.isCompiling ? "playing" : "wait";' | last_line)
  [ "$s" = "playing" ] && break; sleep 1
done
[ "$s" = "playing" ] || { echo "Play mode did not start" >&2; exit 1; }

# 3. Wait for the state the shots need.
if [ -n "$UNTIL" ]; then
  state=""
  for ((i = 0; i < TIMEOUT; i++)); do
    state=$(file_eval "$UNTIL" | last_line)
    [ "$(printf '%s' "$state" | tr '[:upper:]' '[:lower:]')" = "ready" ] && break
    sleep 1
  done
  [ "$(printf '%s' "$state" | tr '[:upper:]' '[:lower:]')" = "ready" ] || { echo "not ready after ${TIMEOUT}s (last: $state)" >&2; exit 1; }
fi

# 4. Shots.
names=()
for spec in "${SHOTS[@]}"; do
  name="${spec%%=*}"; setup="${spec#*=}"
  printf '%s: %s\n' "$name" "$(file_eval "$setup" | last_line)"
  sleep "$SETTLE"
  png="$OUT/$name.png"; win="$png"; command -v cygpath >/dev/null && win="$(cygpath -w "$png")"
  bash "$HERE/capture.sh" "$PROJECT" "$win" --width "$WIDTH" --height "$HEIGHT" >/dev/null 2>&1
  [ -f "$png" ] && names+=("$name") || echo "capture failed for $name" >&2
done

# 5. Leave Play mode, restore Run In Background.
if [ $STAY = 0 ]; then
  code_eval 'UnityEditor.EditorApplication.ExitPlaymode(); return "stopped";' >/dev/null
fi
unity command --project-path "$PROJECT" editor.playmode set_run_in_background --enable false >/dev/null 2>&1

# 6. Contact sheet.
if [ -n "$SHEET" ] && [ ${#names[@]} -gt 0 ]; then
  PY=python3; "$PY" -c 1 2>/dev/null || PY=python
  "$PY" - "$OUT" "$SHEET" "$CROP" "${names[@]}" <<'PYEOF' || echo "(no sheet: needs Python with Pillow)" >&2
import sys
from PIL import Image, ImageDraw
out, sheet, crop, names = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:]
ims = []
for n in names:
    im = Image.open(f'{out}/{n}.png').convert('RGB')
    if crop:
        x, y, w, h = (int(v) for v in crop.split(','))
        im = im.crop((x, y, x + w, y + h))
    ims.append((n, im))
gap, label = 10, 22
W = sum(im.width for _, im in ims) + gap * (len(ims) - 1)
H = max(im.height for _, im in ims) + label
canvas = Image.new('RGB', (W, H), (20, 20, 24))
d = ImageDraw.Draw(canvas)
x = 0
for n, im in ims:
    canvas.paste(im, (x, 0))
    d.text((x + 4, im.height + 4), n, fill=(200, 200, 210))
    x += im.width + gap
canvas.save(sheet)
print(sheet)
PYEOF
fi
for n in "${names[@]}"; do echo "$OUT/$n.png"; done
