#!/usr/bin/env bash
# Capture the Unity game view to a PNG anywhere on disk, leaving nothing behind in the project.
#
#   capture.sh <project-path> <out.png> [extra capture args, e.g. --width 1920 --height 1080 --source camera]
#
# Newer Pipeline packages take `--output-path` (absolute paths allowed), which is tried first. Older ones only take
# `--save_path`, which lands inside the project (some versions resolve it against Assets/, so a .meta appears too).
# For those the shot goes into a throwaway folder that is then moved out and deleted, .meta files included.
set -euo pipefail

if [[ $# -lt 2 ]]; then
    echo "usage: capture.sh <project-path> <out.png> [extra capture args]" >&2
    exit 2
fi
project="$1"; out="$2"; shift 2
command -v cygpath >/dev/null && { project="$(cygpath -m "$project")"; out="$(cygpath -m "$out")"; }
mkdir -p "$(dirname "$out")"
out="$(cd "$(dirname "$out")" && pwd -W 2>/dev/null || pwd)/$(basename "$out")"
rm -f "$out"

capture() {
    unity command --project-path "$project" --caller plugin --skill unity-editor-cli-recovery --no-banner \
        capture game_view "$@" 2>&1
}

# 1. Newer packages: write straight to the destination.
result="$(capture --output-path "$out" "$@" || true)"
if [[ -s "$out" ]]; then
    echo "$out"
    exit 0
fi

# 2. Older packages: capture into a throwaway folder inside the project, then move the PNG out.
tmp="_cli_capture_$$_$RANDOM"
cleanup() {
    for base in "$project" "$project/Assets"; do
        rm -rf "${base:?}/$tmp" "${base:?}/$tmp.meta"
    done
}
trap cleanup EXIT
result="$(capture --save_path "$tmp/shot.png" "$@" || true)"
for base in "$project" "$project/Assets"; do
    if [[ -s "$base/$tmp/shot.png" ]]; then
        mv "$base/$tmp/shot.png" "$out"
        cleanup
        sleep 1   # the Editor may write the folder's .meta just after the capture; sweep again
        cleanup
        echo "$out"
        exit 0
    fi
done

echo "Capture failed. Last output:" >&2
echo "$result" | tail -5 >&2
exit 1
