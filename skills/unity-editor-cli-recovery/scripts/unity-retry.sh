#!/usr/bin/env bash
# Run a `unity` CLI call and retry it while the Editor is briefly unreachable (domain reload, recompile, asset import).
#
# usage: unity-retry.sh [--tries 12] [--delay 5] -- <unity args...>
#   unity-retry.sh -- command script eval --file "$(cygpath -w build.cs)" --project-path 'E:\Proj'
#   unity-retry.sh --tries 3 -- recompile --project-path 'E:\Proj'
#
# Retries only the transient failures - "400 Bad Request", "No Pipeline instance", refused/reset connections and
# empty output - which all mean "the Editor is reloading, try again in a moment". Anything else (a compile error in
# an eval snippet, an unknown command, a real result) is returned at once with its exit code.
# A TIMEOUT is not retried: that is almost always a modal dialog - see unity-dialog.ps1.
set -uo pipefail
TRIES=12; DELAY=5
while [ $# -gt 0 ]; do
  case "$1" in
    --tries) TRIES="$2"; shift 2 ;;
    --delay) DELAY="$2"; shift 2 ;;
    --) shift; break ;;
    *) break ;;
  esac
done
[ $# -gt 0 ] || { sed -n 2,12p "$0"; exit 2; }
export UNITY_NO_PAGER=1

TRANSIENT='400 Bad Request|No Pipeline instance|ECONNREFUSED|ECONNRESET|socket hang up|connection refused'
for ((i = 1; i <= TRIES; i++)); do
  out=$("${UNITY:-unity}" "$@" 2>&1); code=$?
  if [ -n "$(printf '%s' "$out" | tr -d '[:space:]')" ] && ! printf '%s' "$out" | grep -qiE "$TRANSIENT"; then
    printf '%s\n' "$out"
    if printf '%s' "$out" | grep -qiE 'timed? ?out'; then
      echo "(timed out: probably a modal dialog in the Editor - run unity-dialog.ps1 -Project <project>)" >&2
    fi
    exit $code
  fi
  [ $i -lt $TRIES ] && sleep "$DELAY"
done
echo "unity still unreachable after $TRIES tries: $(printf '%s' "$out" | head -3)" >&2
exit 1
