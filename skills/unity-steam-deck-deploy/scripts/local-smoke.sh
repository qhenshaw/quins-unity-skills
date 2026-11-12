#!/usr/bin/env bash
# Run a built player headlessly before shipping it to the Deck, and fail on exceptions in its log.
#
# usage: local-smoke.sh <path/to/Game.exe> [game args...]
#   e.g. local-smoke.sh Builds/SteamDeck/MyGame.exe -autoplay runs=1 maxwave=2 speed=5
# env:   SMOKE_TIMEOUT (seconds, default 300), SMOKE_LOG (log path, default a temp file)
#
# The game must quit by itself (an autoplay / smoke mode). Exit codes: 0 ok, 1 exceptions or non-zero exit, 124 timeout.
set -uo pipefail
EXE="$1"; shift
[ -f "$EXE" ] || { echo "not found: $EXE" >&2; exit 2; }
LOG="${SMOKE_LOG:-$(mktemp -t smoke-XXXXXX.log)}"
WINLOG="$LOG"; command -v cygpath >/dev/null && WINLOG=$(cygpath -w "$LOG")

echo "== running $(basename "$EXE") $* (timeout ${SMOKE_TIMEOUT:-300}s)"
timeout "${SMOKE_TIMEOUT:-300}" "$EXE" -logFile "$WINLOG" "$@" >/dev/null 2>&1
code=$?
[ $code = 124 ] && { echo "TIMEOUT - the game did not quit on its own"; tail -n 20 "$LOG"; exit 124; }

exc=$(grep -cE "Exception|NullReference|MissingReference" "$LOG" || true)
echo "   exit code: $code   exceptions in log: $exc   log: $LOG"
if [ "$exc" != 0 ]; then grep -nE -A3 "Exception|NullReference|MissingReference" "$LOG" | head -40; exit 1; fi
[ $code = 0 ] || exit 1
echo "== smoke test passed"
