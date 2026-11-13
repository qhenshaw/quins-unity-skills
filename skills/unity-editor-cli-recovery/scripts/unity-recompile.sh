#!/usr/bin/env bash
# Import files changed on disk, then recompile and print the compiler messages. Exit 1 on compile errors.
#
# usage: unity-recompile.sh <project path> [--no-refresh] [--stay-in-play]
#
# Why the refresh: `unity recompile` compiles the scripts the AssetDatabase already knows. A .cs file (or asmdef)
# that was CREATED outside the Editor is not imported yet, so it is silently left out - the recompile reports
# success and the new class "doesn't exist" later. AssetDatabase.Refresh() imports it first.
# Why leave Play mode: the Editor defers script compilation while playing, so a recompile in Play mode compiles
# nothing (and the refresh often fails mid-reload). The script stops Play mode first unless --stay-in-play.
# Every step retries through unity-retry.sh, because the Editor drops connections while it reloads.
set -uo pipefail
PROJECT="${1:?project path required}"; shift
REFRESH=1; LEAVE_PLAY=1
for a in "$@"; do
  case "$a" in
    --no-refresh) REFRESH=0 ;;
    --stay-in-play) LEAVE_PLAY=0 ;;
  esac
done
HERE="$(cd "$(dirname "$0")" && pwd)"

eval_code() { # $1 = C# snippet; prints the eval output
  local tmp file
  tmp="$(mktemp -t recompile-XXXXXX).cs"
  printf '%s\n' "$1" > "$tmp"
  file="$tmp"; command -v cygpath >/dev/null && file="$(cygpath -w "$tmp")"
  bash "$HERE/unity-retry.sh" -- command script eval --file "$file" --project-path "$PROJECT"; local code=$?
  rm -f "$tmp"; return $code
}

if [ $LEAVE_PLAY = 1 ]; then
  state=$(eval_code 'if (UnityEditor.EditorApplication.isPlaying) { UnityEditor.EditorApplication.ExitPlaymode(); return "exiting"; } return "edit";' 2>&1 | grep -v claude-code-hint | tail -1)
  if [ "$state" = "exiting" ]; then
    echo "(Play mode stopped: scripts don't compile while playing)" >&2
    for i in $(seq 1 30); do
      s=$(eval_code 'return UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode ? "playing" : "edit";' 2>&1 | grep -v claude-code-hint | tail -1)
      [ "$s" = "edit" ] && break
      sleep 1
    done
  fi
fi

if [ $REFRESH = 1 ]; then
  out=$(eval_code 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";' 2>&1)
  if ! printf '%s' "$out" | grep -q refreshed; then
    echo "refresh failed:" >&2; printf '%s\n' "$out" | head -5 >&2; exit 1
  fi
fi

out=$(bash "$HERE/unity-retry.sh" --tries 4 -- recompile --project-path "$PROJECT" --timeout 300 --format tsv); code=$?
printf '%s\n' "$out"
if printf '%s\n' "$out" | grep -qiE '^error'; then exit 1; fi
exit $code
