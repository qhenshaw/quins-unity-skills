#!/usr/bin/env bash
# Run a C# eval file in the Editor with {{placeholders}} filled in, retrying while the Editor reloads.
#
# usage: unity-eval.sh <project path> <file.cs> [key=value ...]
#   unity-eval.sh 'E:\Proj' show.cs item=FrostCandle mode=fusion     # {{item}} -> FrostCandle, {{mode}} -> fusion
#
# Why: `unity command script eval` runs inside the Editor, so environment variables and shell arguments never reach
# the snippet. Instead of one copy of the script per value, write {{key}} where a value goes and pass key=value here.
# Values are inserted verbatim (quote them in the C#: "{{item}}"). Unfilled {{keys}} are an error, so a typo can't
# silently run the snippet with a literal "{{item}}". Prints the eval output; exit code is the eval's.
set -uo pipefail
PROJECT="${1:?project path required}"; SRC="${2:?eval file required}"; shift 2
HERE="$(cd "$(dirname "$0")" && pwd)"
[ -f "$SRC" ] || { echo "no such file: $SRC" >&2; exit 2; }

PY=python3; "$PY" -c 1 2>/dev/null || PY=python
tmp="$(mktemp -t eval-XXXXXX).cs"
"$PY" - "$SRC" "$tmp" "$@" <<'PYEOF'
import re, sys
src, dst, pairs = sys.argv[1], sys.argv[2], sys.argv[3:]
text = open(src, encoding='utf-8').read()
for p in pairs:
    if '=' not in p:
        sys.exit(f"bad argument '{p}' (want key=value)")
    k, v = p.split('=', 1)
    text = text.replace('{{' + k + '}}', v)
left = sorted(set(re.findall(r'\{\{(\w+)\}\}', text)))
if left:
    sys.exit('unfilled placeholders: ' + ', '.join(left))
open(dst, 'w', encoding='utf-8').write(text)
PYEOF
[ $? = 0 ] || { rm -f "$tmp"; exit 2; }
file="$tmp"; command -v cygpath >/dev/null && file="$(cygpath -w "$tmp")"
bash "$HERE/unity-retry.sh" -- command script eval --file "$file" --project-path "$PROJECT"; code=$?
rm -f "$tmp"
exit $code
