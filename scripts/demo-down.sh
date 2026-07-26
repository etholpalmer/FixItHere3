#!/usr/bin/env bash
# demo-down.sh — stop what demo-up.sh started.
#
#   scripts/demo-down.sh            # stop the backend (leaves simulators booted)
#   scripts/demo-down.sh --sims     # also shut down the simulators
#
# Simulators are left running by default: booting them is the slow part, so a
# quick `demo-up.sh` again is nicer than a cold boot. Pass --sims for a full stop.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

PORT=5162
RUN_DIR=".demo-run"
SHUT_SIMS=false
for arg in "$@"; do
  case "$arg" in
    --sims) SHUT_SIMS=true ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

# Prefer the pid we recorded; fall back to whoever holds the port.
stopped=false
if [ -f "$RUN_DIR/backend.pid" ] && kill "$(cat "$RUN_DIR/backend.pid")" 2>/dev/null; then
  stopped=true
fi
for pid in $(lsof -nP -iTCP:$PORT -sTCP:LISTEN -t 2>/dev/null); do
  kill "$pid" 2>/dev/null && stopped=true
done
rm -f "$RUN_DIR/backend.pid"
$stopped && echo "backend stopped (:$PORT)" || echo "no backend was running on :$PORT"

if $SHUT_SIMS; then
  xcrun simctl shutdown all 2>/dev/null || true
  echo "simulators shut down"
fi
