#!/usr/bin/env bash
# Deploy a Windows/Proton build to a dev-kit-paired Steam Deck over SSH.
#
# usage:
#   deck-deploy.sh --host <ip> --build <folder> --game <Name> [--exe <Name>.exe] [--register] [--user deck]
#   deck-deploy.sh --host <ip> --game <Name> --log            # print the tail of the newest Player.log on the Deck
#
# Upload: tar stream -> ~/devkit-game/<Name>.new, verify file count + bytes, swap into ~/devkit-game/<Name>.
# If the game is running, the previous build is kept as <Name>.old (never killed); otherwise it is deleted.
# --register adds a Steam shortcut with Proton Experimental (needed once per game).
# Key: $DECK_KEY, else the SteamOS Devkit Client's devkit_rsa.
set -euo pipefail

HOST=""; BUILD=""; GAME=""; EXE=""; USER_NAME="deck"; REGISTER=0; LOG=0
while [ $# -gt 0 ]; do
  case "$1" in
    --host) HOST="$2"; shift 2 ;;
    --build) BUILD="$2"; shift 2 ;;
    --game) GAME="$2"; shift 2 ;;
    --exe) EXE="$2"; shift 2 ;;
    --user) USER_NAME="$2"; shift 2 ;;
    --register) REGISTER=1; shift ;;
    --log) LOG=1; shift ;;
    -h|--help) sed -n 2,12p "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -n "$HOST" ] && [ -n "$GAME" ] || { echo "need --host and --game" >&2; exit 2; }
EXE="${EXE:-$GAME.exe}"

KEY="${DECK_KEY:-}"
if [ -z "$KEY" ]; then
  for k in "${LOCALAPPDATA:-}/steamos-devkit/steamos-devkit/devkit_rsa" "$HOME/.local/share/steamos-devkit/devkit_rsa" \
           "$HOME/Library/Application Support/steamos-devkit/devkit_rsa"; do
    [ -f "$k" ] && { KEY="$k"; break; }
  done
fi
[ -f "$KEY" ] || { echo "no dev kit SSH key found; pair with the SteamOS Devkit Client or set DECK_KEY" >&2; exit 1; }

SSH=(ssh -i "$KEY" -o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 "$USER_NAME@$HOST")
DIR="devkit-game/$GAME"

if [ "$LOG" = 1 ]; then
  "${SSH[@]}" "f=\$(ls -t ~/.local/share/Steam/steamapps/compatdata/*/pfx/drive_c/users/steamuser/AppData/LocalLow/*/'$GAME'/Player.log 2>/dev/null | head -1);
               [ -n \"\$f\" ] && { echo \"== \$f\"; tail -n 80 \"\$f\"; } || echo 'no Player.log found (has the game run yet?)'"
  exit 0
fi

[ -d "$BUILD" ] || { echo "build folder not found: $BUILD" >&2; exit 1; }
[ -f "$BUILD/$EXE" ] || { echo "$EXE not in $BUILD (use --exe)" >&2; exit 1; }

echo "== checking $HOST"
"${SSH[@]}" "echo reachable" >/dev/null
RUNNING=$("${SSH[@]}" "pgrep -f '$EXE' >/dev/null && echo yes || echo no")
[ "$RUNNING" = yes ] && echo "   $EXE is running on the Deck - it will be left alone; relaunch to get the new build"

EXCL='*_BackUpThisFolder_ButDontShipItWithYourGame'
echo "== uploading $BUILD -> ~/$DIR.new"
( cd "$BUILD" && tar --exclude="./$EXCL" --exclude="$EXCL" -cf - . ) |
  "${SSH[@]}" "rm -rf ~/'$DIR.new' && mkdir -p ~/'$DIR.new' && tar -xf - -C ~/'$DIR.new'"

LOCAL=$(cd "$BUILD" && find . -path "./$EXCL" -prune -o -type f -print0 | xargs -0 stat -c %s | awk '{n++; s+=$1} END {print n, s}')
REMOTE=$("${SSH[@]}" "cd ~/'$DIR.new' && find . -type f -print0 | xargs -0 stat -c %s | awk '{n++; s+=\$1} END {print n, s}'")
echo "   local  files/bytes: $LOCAL"
echo "   remote files/bytes: $REMOTE"
[ "$LOCAL" = "$REMOTE" ] || { echo "MISMATCH - left in ~/$DIR.new, live build untouched" >&2; exit 1; }

echo "== swapping in"
# While the game runs, the build it was started from may be the live folder OR an earlier .old copy, so nothing old is
# deleted; each swap parks the previous build as <Name>.old.<time>. Once the game isn't running, all of them go.
"${SSH[@]}" "cd ~/devkit-game && if pgrep -f '$EXE' >/dev/null; then
               { [ -d '$GAME' ] && mv '$GAME' '$GAME.old.'\$(date +%s) || true; } && mv '$GAME.new' '$GAME' &&
               echo '   previous build parked in ~/devkit-game/$GAME.old.* (game still running; rerun later to clean up)'
             else
               rm -rf '$GAME'.old* && { [ -d '$GAME' ] && mv '$GAME' '$GAME.old' || true; } && mv '$GAME.new' '$GAME' && rm -rf '$GAME.old' &&
               echo '   old build removed'
             fi"

if [ "$REGISTER" = 1 ]; then
  echo "== registering Steam shortcut (Proton Experimental)"
  PARMS="{\"gameid\": \"$GAME\", \"directory\": \"/home/$USER_NAME/$DIR\", \"force_appid\": \"\", \"argv\": [\"$EXE\"], \"env\": {}, \"lepton_args\": \"\", \"settings\": {\"steam_play\": \"1\", \"steam_play_debug\": \"0\", \"steam_play_debug_version\": \"2019\", \"compat_tool\": \"proton-experimental\"}}"
  # devkit-utils scripts ship with CRLF line endings: run them through python3, not directly.
  "${SSH[@]}" "python3 ~/devkit-utils/steam-client-create-shortcut --parms '$PARMS'"
fi
echo "== done: ~/$DIR"
