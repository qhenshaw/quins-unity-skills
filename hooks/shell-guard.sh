#!/usr/bin/env bash
# PreToolUse hook (Bash), shipped with the quins-unity-skills plugin: refuse shell patterns that keep failing when
# Claude Code's Bash tool runs Git Bash on Windows. On macOS / Linux it allows everything (these work fine there).
#
#  1. Heredocs (cat > file <<'EOF', python - <<EOF, ...): bodies containing apostrophes or quotes fail with
#     "unexpected EOF while looking for matching `''", leaving half-written files.
#  2. A bare `python -` (script from stdin) with no heredoc: it waits on stdin until the tool times out.
#  3. Multi-line `python -c "..."` programs: nested quoting and escapes get mangled (a "\n" inside a string becomes
#     a real line break), breaking the files being edited.
#
# Instead: write files with the Write tool and change them with Edit; put a longer script in a file and run it by
# path. One-line `python -c` calls are fine. Set QUINS_SHELL_GUARD=off to disable, =on to force it on any OS.
#
# Exit 2 blocks the call and shows stderr to Claude. Plain bash + grep (grep -P for check 3, skipped if missing).
case "${QUINS_SHELL_GUARD:-auto}" in
  off) exit 0 ;;
  on) ;;
  *) case "$(uname -s 2>/dev/null)" in MINGW*|MSYS*|CYGWIN*) ;; *) exit 0 ;; esac ;;
esac
input="$(cat)"
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
