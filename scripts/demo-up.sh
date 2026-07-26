#!/usr/bin/env bash
# demo-up.sh — spin up the whole FixItHere demo in one command:
#   the control console (backend) + the Customer app + the Provider app,
#   each app on its own iOS Simulator so the two sit side by side.
#
# Usage:
#   scripts/demo-up.sh              # backend + both apps on two simulators
#   scripts/demo-up.sh --backend    # backend + /dev console only (no apps)
#   scripts/demo-up.sh --no-build   # skip the app rebuild (reinstall last build)
#
# Override the devices (any name from `xcrun simctl list devices available`):
#   CUSTOMER_SIM="iPhone 17 Pro"  PROVIDER_SIM="iPhone 17 Pro Max"  scripts/demo-up.sh
#
# Stop everything with:  scripts/demo-down.sh
#
# macOS + Xcode + the .NET 10 SDK with the `maui` workload are assumed (the same
# toolchain the apps build with). Backend-only mode needs none of the Apple bits.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

CUSTOMER_SIM="${CUSTOMER_SIM:-iPhone 17 Pro}"
PROVIDER_SIM="${PROVIDER_SIM:-iPhone 17 Pro Max}"
PORT=5162
RUN_DIR=".demo-run"          # pid + log scratch, git-ignored
BACKEND_ONLY=false
BUILD=true
for arg in "$@"; do
  case "$arg" in
    --backend)  BACKEND_ONLY=true ;;
    --no-build) BUILD=false ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done
mkdir -p "$RUN_DIR"

say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }

# ---- backend (the control console) ---------------------------------------
say "Backend + /dev console"
if lsof -nP -iTCP:$PORT -sTCP:LISTEN >/dev/null 2>&1; then
  echo "already listening on :$PORT — reusing it"
else
  # Reseeds a fresh SQLite database on every boot, so state is identical each run.
  nohup dotnet run --project src/Backend.Api > "$RUN_DIR/backend.log" 2>&1 &
  echo $! > "$RUN_DIR/backend.pid"
  printf "starting"
  for _ in $(seq 1 40); do
    curl -s -o /dev/null "http://localhost:$PORT/services" && break
    printf "."; sleep 1
  done
  echo
fi
if ! curl -s -o /dev/null "http://localhost:$PORT/services"; then
  echo "ERROR: backend did not become healthy on :$PORT — see $RUN_DIR/backend.log" >&2
  exit 1
fi
echo "healthy on http://localhost:$PORT   (console: http://localhost:$PORT/dev)"

# ---- one app onto one simulator ------------------------------------------
# udid_for <device name> — boot it if needed, echo its UDID.
udid_for() {
  local name="$1" udid
  udid=$(xcrun simctl list devices available \
         | grep -F "$name (" | head -1 | grep -oE '[0-9A-F-]{36}')
  [ -z "$udid" ] && { echo "ERROR: no available simulator named '$name'" >&2; return 1; }
  xcrun simctl boot "$udid" >/dev/null 2>&1 || true   # ok if already booted
  xcrun simctl bootstatus "$udid" -b >/dev/null 2>&1 || true
  echo "$udid"
}

launch_app() {
  local proj="$1" bundle="$2" simname="$3" udid appdir
  udid=$(udid_for "$simname") || return 1
  if $BUILD; then
    echo "building $proj (net10.0-ios, Debug)…"
    dotnet build "$proj" -f net10.0-ios -c Debug --nologo >"$RUN_DIR/$(basename "$proj").build.log" 2>&1 \
      || { echo "ERROR: build failed — see $RUN_DIR/$(basename "$proj").build.log" >&2; return 1; }
  fi
  appdir=$(ls -d "$proj"/bin/Debug/net10.0-ios/*/"$(basename "$proj")".app 2>/dev/null | head -1)
  [ -z "$appdir" ] && { echo "ERROR: no built .app under $proj/bin/Debug — run without --no-build" >&2; return 1; }
  xcrun simctl install "$udid" "$appdir"
  xcrun simctl terminate "$udid" "$bundle" >/dev/null 2>&1 || true
  xcrun simctl launch "$udid" "$bundle" >/dev/null
  echo "  $(basename "$proj") -> $simname"
}

if ! $BACKEND_ONLY; then
  say "Customer app"
  launch_app src/Customer.Mobile com.companyname.Customer.Mobile "$CUSTOMER_SIM"
  say "Provider app"
  launch_app src/Provider.Mobile com.companyname.Provider.Mobile "$PROVIDER_SIM"
  open -a Simulator 2>/dev/null || true
fi

open "http://localhost:$PORT/dev" 2>/dev/null || true

# ---- what the operator has now -------------------------------------------
cat <<EOF

$( $BACKEND_ONLY && echo "Control console is up." || echo "Everything is up." )

  Control console : http://localhost:$PORT/dev   (Start Demo, clock, reset, route)
$( $BACKEND_ONLY || cat <<APPS
  Customer app    : $CUSTOMER_SIM   — prefilled john.reyes@gmail.com / Customer1!
  Provider app    : $PROVIDER_SIM   — prefilled contact@mikesplumbing.ca / Provider1!
APPS
)
  Both prefilled logins share the soonest job (John Reyes <-> Mike's Plumbing),
  so a two-sided flow is one tap away on each app.

  Tip: the clock starts at 1x, so a provider's drive advances only a step every
  couple of real minutes. Click 60x (or 120x) in the console during the travel
  beat to watch the marker move and the map zoom in.

  Stop everything:  scripts/demo-down.sh
EOF
