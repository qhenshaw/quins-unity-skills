#!/usr/bin/env bash
# Run Unity tests in the open Editor through the Pipeline `unity` CLI and print a result you can trust.
#
# usage: run-tests.sh <filter> [TestName|Assembly|Category] [playmode|editmode]
#   run-tests.sh MyGame.Tests.PlayMode Assembly       # a whole test assembly
#   run-tests.sh ArmorTests                            # TestName = case-insensitive PARTIAL match on the full name
#   run-tests.sh "ArmorTests.Block_"                   # one test (or a prefix)
# env: UNITY (default: unity), PROJECT (passed as --project-path), POLL_SECONDS (default 600)
#
# Why a script: `test status` keeps reporting the PREVIOUS run until a new one starts, and `test run` rejects an
# unknown --filter-type by printing usage - so a typo'd run looks exactly like a green one. This script fails on a
# rejected run and waits until the status really belongs to the new run.
set -uo pipefail
FILTER="${1:?filter required}"; TYPE="${2:-TestName}"; MODE="${3:-playmode}"
U=("${UNITY:-unity}" command); [ -n "${PROJECT:-}" ] && U+=(--project-path "$PROJECT")
export UNITY_NO_PAGER=1

case "$TYPE" in TestName|Assembly|Category) ;; *)
  echo "invalid filter type '$TYPE' - only TestName (partial match), Assembly, Category exist (no Class/Regex)" >&2; exit 2 ;; esac

before=$("${U[@]}" test status --detail summary 2>&1 | grep -E "^(duration|summary):" | tr '\n' ' ')

out=$("${U[@]}" test run --mode "$MODE" --filter "$FILTER" --filter-type "$TYPE" --async-tests true 2>&1)
if echo "$out" | grep -qiE "usage:|unknown option|invalid|error"; then
  echo "test run was rejected:" >&2; echo "$out" | head -20 >&2; exit 2
fi

seen_running=0
for ((i = 0; i < ${POLL_SECONDS:-600} / 3; i++)); do
  st=$("${U[@]}" test status --detail summary 2>&1)
  echo "$st" | grep -qiE "status: *(running|pending|started)" && seen_running=1
  if echo "$st" | grep -qiE "status: *(completed|failed|cancel)"; then
    now=$(echo "$st" | grep -E "^(duration|summary):" | tr '\n' ' ')
    # Done only if we saw it run, or the finished result differs from the one before we started.
    if [ $seen_running = 1 ] || [ "$now" != "$before" ]; then break; fi
  fi
  sleep 3
done

echo "$st" | grep -E "^(status|duration|summary):"
summary=$(echo "$st" | grep -E "^summary:")
[ -n "$summary" ] || { echo "no summary - did the run start?" >&2; exit 1; }
if echo "$summary" | grep -q '"total":0'; then echo "0 tests matched '$FILTER' ($TYPE)" >&2; exit 1; fi

# Windows' "python3" is often the Microsoft Store stub: use whichever interpreter actually runs.
PY=python3; "$PY" -c 1 2>/dev/null || PY=python
"${U[@]}" test status --detail failures 2>/dev/null | "$PY" -c "
import sys, re, json
t = sys.stdin.read(); m = re.search(r'results: (\[.*\])', t, re.S)
for r in (json.loads(m.group(1)) if m else []):
    print('FAIL' if r.get('result', r.get('status', '')).lower().startswith('fail') else r.get('result', 'NOT PASSED'),
          r['fullName'].split('.')[-1], '::', (r.get('message') or '').strip().replace('\r\n', ' ')[:300])
" 2>/dev/null || true
echo "$summary" | grep -qE '"failed":0' && echo "$summary" | grep -qE '"inconclusive":0' || exit 1
