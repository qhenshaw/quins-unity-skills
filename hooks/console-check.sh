#!/usr/bin/env bash
# PostToolUse hook (Bash), shipped with the quins-unity-skills plugin: after a Bash call that touches Unity (a `unity`
# command, a unity-*.sh helper, a test run, an autoplay / report poll), ask the open Editor for its console error count
# and tell Claude when it went up since the last check.
#
# Why: waiting on a result file or a test status says nothing about the game itself. An exception thrown every frame
# (weapons stopped working, the player stood still at 0 HP) went unnoticed through a whole autoplay batch because
# nothing looked at the console. This puts new errors in front of Claude without it having to remember to look.
#
# Silent when: the command doesn't mention Unity, the folder isn't a Unity project, the `unity` CLI or the Editor's
# Pipeline server isn't there (8 s limit), or no new errors appeared. QUINS_CONSOLE_CHECK=off disables it.
[ "${QUINS_CONSOLE_CHECK:-on}" = "off" ] && exit 0
input="$(cat)"
cmd="$(printf '%s' "$input" | grep -oP '"command"\s*:\s*"(\\.|[^"\\])*"' 2>/dev/null | head -n 1)"
[ -n "$cmd" ] || cmd="$input"
printf '%s' "$cmd" | grep -qiE 'unity|run-tests|BalanceReports|autoplay' || exit 0
# Already reading the console: nothing to add.
printf '%s' "$cmd" | grep -qE 'console[[:space:]]+(get|status|clear)' && exit 0
command -v unity >/dev/null 2>&1 || exit 0

# Project folder: the hook's cwd (JSON-escaped backslashes undone).
cwd="$(printf '%s' "$input" | grep -oE '"cwd"[[:space:]]*:[[:space:]]*"([^"\\]|\\.)*"' | head -n 1 | sed -E 's/^"cwd"[[:space:]]*:[[:space:]]*"//; s/"$//; s/\\\\/\\/g')"
[ -n "$cwd" ] || cwd="$(pwd)"
proj="$cwd"; command -v cygpath >/dev/null 2>&1 && proj="$(cygpath -u "$cwd" 2>/dev/null || printf '%s' "$cwd")"
[ -d "$proj/ProjectSettings" ] || exit 0

status="$(timeout 8 unity command console status --project-path "$cwd" --format json 2>/dev/null)" || exit 0
errors="$(printf '%s' "$status" | grep -oE '"consoleErrors":[0-9]+' | grep -oE '[0-9]+')"
[ -n "$errors" ] || exit 0
compileFailed="$(printf '%s' "$status" | grep -oE '"compilationFailed":(true|false)' | grep -oE 'true|false')"

key="$(printf '%s' "$cwd" | cksum | cut -d' ' -f1)"
state="${TMPDIR:-/tmp}/quins-console-check-$key"
last=0; [ -f "$state" ] && last="$(cat "$state" 2>/dev/null)"; case "$last" in ''|*[!0-9]*) last=0 ;; esac
printf '%s' "$errors" > "$state" 2>/dev/null
[ "$errors" -gt "$last" ] || [ "$compileFailed" = "true" ] || exit 0

new=$((errors - last)); [ "$new" -lt 0 ] && new=0
msg="Unity console: $errors error(s) now"
[ "$new" -gt 0 ] && msg="$msg ($new new since the last check)"
[ "$compileFailed" = "true" ] && msg="$msg; compilation FAILED"
msg="$msg. Read them before trusting any result or waiting longer: unity command console get --level error --tail 20 --project-path <project>. A run can look fine (report pending, tests 'running') while the game is broken."
printf '{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"%s"}}\n' "$msg"
exit 0
