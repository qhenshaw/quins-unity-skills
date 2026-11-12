#!/usr/bin/env bash
# Import files changed on disk, then recompile and print the compiler messages. Exit 1 on compile errors.
#
# usage: unity-recompile.sh <project path> [--no-refresh]
#
# Why the refresh: `unity recompile` compiles the scripts the AssetDatabase already knows. A .cs file (or asmdef)
# that was CREATED outside the Editor is not imported yet, so it is silently left out - the recompile reports
# success and the new class "doesn't exist" later. AssetDatabase.Refresh() imports it first.
# Both steps retry through unity-retry.sh, because the Editor drops connections while it reloads.
set -uo pipefail
PROJECT="${1:?project path required}"; REFRESH=1
[ "${2:-}" = "--no-refresh" ] && REFRESH=0
HERE="$(cd "$(dirname "$0")" && pwd)"

if [ $REFRESH = 1 ]; then
  tmp="$(mktemp -t refresh-XXXXXX).cs"
  printf 'UnityEditor.AssetDatabase.Refresh(); return "refreshed";\n' > "$tmp"
  file="$tmp"; command -v cygpath >/dev/null && file="$(cygpath -w "$tmp")"
  "$HERE/unity-retry.sh" -- command script eval --file "$file" --project-path "$PROJECT" >/dev/null || {
    rm -f "$tmp"; echo "refresh failed" >&2; exit 1; }
  rm -f "$tmp"
fi

out=$("$HERE/unity-retry.sh" --tries 4 -- recompile --project-path "$PROJECT" --timeout 300 --format tsv); code=$?
printf '%s\n' "$out"
if printf '%s\n' "$out" | grep -qiE '^error'; then exit 1; fi
exit $code
