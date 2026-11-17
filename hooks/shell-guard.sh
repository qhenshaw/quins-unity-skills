#!/usr/bin/env bash
# PreToolUse hook (Bash), shipped with the quins-unity-skills plugin. Exit 2 blocks the call and shows stderr to Claude.
#
# Git Bash on Windows only (these work fine on macOS / Linux) - QUINS_SHELL_GUARD=off disables, =on forces any OS:
#  1. Heredocs (cat > file <<'EOF', python - <<EOF, ...): bodies containing apostrophes or quotes fail with
#     "unexpected EOF while looking for matching `''", leaving half-written files.
#  2. A bare `python -` (script from stdin) with no heredoc: it waits on stdin until the tool times out.
#  3. Multi-line `python -c "..."` programs: nested quoting and escapes get mangled (a "\n" inside a string becomes
#     a real line break), breaking the files being edited.
#  Instead: write files with the Write tool and change them with Edit; put a longer script in a file and run it by
#  path. One-line `python -c` calls are fine.
#
# Every OS - QUINS_WAIT_GUARD=off disables:
#  4. Unbounded polling loops (`until <cond>; do sleep 10; done`). A background command has no time limit, so a
#     condition that never comes true waits forever (one waited 5 hours on a log a 44 s rebuild never wrote).
#     A loop that sleeps must be bounded: `timeout 300 bash -c '...'`, a `for i in $(seq 1 30)` counter, a
#     `[ $n -lt 30 ]` / `(( ))` counter, or a `$SECONDS` deadline.
#  5. `unity command script eval` (or unity-eval.sh) without `--timeout`: the CLI gives up after 30 s by default
#     while the Editor keeps working, so a long eval looks finished or failed when it is neither. State the expected
#     duration: `--timeout 30` for quick snippets, more for builds / rebuilds.
#
# Plain bash + grep (grep -P for check 3, skipped if missing).
input="$(cat)"

# The command string alone (so the description or other fields can't trigger a check); the whole input if grep -P is missing.
cmd="$(printf '%s' "$input" | grep -oP '"command"\s*:\s*"(\\.|[^"\\])*"' 2>/dev/null | head -n 1)"
[ -n "$cmd" ] || cmd="$input"

# ------------------------------------------------------------------ every OS
if [ "${QUINS_WAIT_GUARD:-on}" != "off" ]; then
  # 4. A loop that sleeps, with nothing bounding it.
  if printf '%s' "$cmd" | grep -qE '(^|[^A-Za-z0-9_])(until|while)[[:space:]]' \
     && printf '%s' "$cmd" | grep -qE '(^|[^A-Za-z0-9_])sleep[[:space:]]+[0-9]' \
     && ! printf '%s' "$cmd" | grep -qE '(^|[^A-Za-z0-9_-])timeout[[:space:]]+(-[a-z]+[[:space:]]+)*[0-9]|SECONDS|(^|[^A-Za-z0-9_])seq[[:space:]]|-(lt|le|gt|ge)[[:space:]]|\(\('; then
    echo "Blocked: this polling loop has no deadline, so if its condition never comes true it waits forever. Bound it by the task's expected duration (about 2-3x): wrap it in 'timeout <seconds> bash -c ...', count with 'for i in \$(seq 1 N)' or '[ \$n -lt N ]', or check \$SECONDS. Print progress each poll, and confirm the work is actually running (file timestamps, editor status, console errors) instead of only waiting for its result." >&2
    exit 2
  fi
  # 5. Editor eval with the CLI's silent 30 s default.
  if printf '%s' "$cmd" | grep -qE 'script[[:space:]]+eval|unity-eval\.sh' && ! printf '%s' "$cmd" | grep -qE -- '--timeout'; then
    echo "Blocked: 'script eval' / unity-eval.sh without --timeout gives up after 30 s while the Editor keeps working, so a long eval looks finished (or failed) when it isn't. Pass '--timeout <seconds>' sized to the work (30 for a quick snippet, 600+ for builds or content rebuilds)." >&2
    exit 2
  fi
fi

# ------------------------------------------------------------------ Git Bash on Windows only
case "${QUINS_SHELL_GUARD:-auto}" in
  off) exit 0 ;;
  on) ;;
  *) case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*) ;; *) exit 0 ;; esac ;;
esac
fix="Write file contents with the Write tool, change files with Edit, and for a longer script Write it to a file (e.g. in the scratchpad) and run it by path."

# 1. A heredoc operator: << (not <<<), optional -, optional quote (JSON-escaped \" or '), then a delimiter word.
if printf '%s' "$input" | grep -qE '(^|[^<])<<-?[[:space:]]*(\\"|'"'"')?[A-Za-z_][A-Za-z0-9_]*'; then
  echo "Blocked: heredocs fail in Git Bash on Windows when the body has quotes or apostrophes. $fix" >&2
  exit 2
fi

# 2. python / python3 / py given "-" as the script (reads stdin) - nothing will ever arrive.
if printf '%s' "$input" | grep -qE '(^|[^A-Za-z0-9_./-])(python3?|py)(\.exe)?[[:space:]]+-([[:space:];&|)]|\\"|"|$)'; then
  echo "Blocked: 'python -' reads its script from stdin, which never comes here (the call hangs until it times out). $fix" >&2
  exit 2
fi

# 3. python -c whose program spans lines: in the hook's JSON input the command is one line, so a newline inside the
#    -c argument shows up as \n between the opening quote and the closing one.
if printf '%s' "$input" | grep -qP '(python3?|py)(\.exe)?\s+-c\s+(\\"((?!\\").)*?\\n|'"'"'[^'"'"']*?\\n)' 2>/dev/null; then
  echo "Blocked: multi-line 'python -c' programs get their quotes and escapes mangled in this shell (\\n in strings turns into real line breaks). $fix" >&2
  exit 2
fi
exit 0
