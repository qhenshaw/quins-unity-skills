#!/usr/bin/env bash
# Reads or drives the in-game debug menu through `unity command debugmenu`, printing only the answer.
#
#   unity-readout.sh [-p <project>] <Section>                  every control with a value: "Label: value"
#   unity-readout.sh [-p <project>] <Section> <Label>          that control's value alone
#   unity-readout.sh [-p <project>] --do <Section> <Label> [value]
#                                                              press a button, or set a toggle (true/false) or
#                                                              slider (number); prints "did: <Label>" or the error
#
# Without -p the CLI finds the project from the current folder (or UNITY_PROJECT_PATH). The game must be in
# Play mode. Needs the `unity` CLI and Python 3 (set PYTHON if it isn't `python`).
# Exit code: 0 on success, 1 if the command failed, returned an error or matched nothing.
set -uo pipefail

project=()
if [ "${1:-}" = "-p" ]; then
  project=(--project-path "$2")
  shift 2
fi

mode=read
if [ "${1:-}" = "--do" ]; then
  mode=do
  shift
fi

if [ $# -lt 1 ] || { [ "$mode" = do ] && [ $# -lt 2 ]; }; then
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  exit 2
fi

section=$1
label=${2:-}

if [ "$mode" = do ]; then
  args=(debugmenu do --section "$section" --label "$label")
  [ $# -ge 3 ] && args+=(--value "$3")
else
  args=(debugmenu list --section "$section")
fi

# The CLI prints notices (updates, banners) on stderr; keep them out of the JSON.
unity command ${project[@]+"${project[@]}"} "${args[@]}" --result-only 2>/dev/null | "${PYTHON:-python}" -c '
import json, sys

mode, label = sys.argv[1], sys.argv[2]
lines = [l for l in sys.stdin.read().splitlines() if l.strip()]
try:
    data = json.loads(lines[-1])
    if isinstance(data, str):  # some CLI versions wrap the result in a JSON string
        data = json.loads(data)
except (IndexError, ValueError):
    sys.exit("failed: no JSON result (is the Editor open and in Play mode?)")
if isinstance(data, dict) and "error" in data:
    sys.exit("failed: " + str(data["error"]))
if mode == "do":
    print("did: " + label)
    sys.exit(0)

def show(value):
    return json.dumps(value) if isinstance(value, bool) else str(value)

found = False
for sec in data:
    for control in sec["controls"]:
        if "value" not in control or (label and control["label"] != label):
            continue
        found = True
        print(show(control["value"]) if label else control["label"] + ": " + show(control["value"]))
if not found:
    sys.exit("failed: nothing to read in that section" + (" with that label" if label else ""))
' "$mode" "$label"
