#!/usr/bin/env bash
# Local CI — the same gates as .github/workflows/ci.yml, run on this machine.
#
# The GitHub workflow is paused to workflow_dispatch-only; this script (wired
# into .githooks/pre-push) is what gates pushes instead. Keep the two in sync:
# if a gate is added here, mirror it in ci.yml so a manual cloud run still
# means the same thing.
#
# Gates (matching the workflow's jobs):
#   tests         the four net10.0 test suites (Shared, Backend.Api,
#                 Customer.Mobile, Provider.Mobile). Compiles Domain/Api/Update,
#                 never Views/*.fs or MauiProgram.fs.
#   compile-gate  `dotnet build -f net10.0-ios -t:Compile` on both apps — the
#                 F# compiler over the view code, stopping before the asset
#                 pipeline. Catches a view referencing a Msg case, model field
#                 or widget that no longer exists, which the test projects
#                 structurally cannot see.
#   build-apps    (--full only) the FULL package build, needing a working
#                 Xcode. Unlike the hosted image, a dev machine's Xcode works,
#                 so there is no classify-and-skip here: a failure fails.
#   linux         (--linux only) the test suites inside the dotnet/sdk:10.0
#                 Docker image — recovers the ubuntu leg the cloud run had.
#
# Usage:
#   scripts/ci-local.sh              # tests + compile-gate (what gates on push)
#   scripts/ci-local.sh --tests-only
#   scripts/ci-local.sh --full       # adds the full iOS package build
#   scripts/ci-local.sh --linux      # adds the Linux-container test leg
#
# Exit code: 0 iff every selected gate passed.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

export DOTNET_NOLOGO=true
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true
export DOTNET_CLI_TELEMETRY_OPTOUT=true

run_tests=true run_compile=true run_full=false run_linux=false
for arg in "$@"; do
  case "$arg" in
    --tests-only) run_compile=false ;;
    --full)       run_full=true ;;
    --linux)      run_linux=true ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

TEST_PROJECTS=(tests/Shared.Tests tests/Backend.Api.Tests
               tests/Customer.Mobile.Tests tests/Provider.Mobile.Tests)
APP_PROJECTS=(src/Customer.Mobile src/Provider.Mobile)

declare -a summary
failed=false

# gate <name> <fn> — run one gate, record "name PASS/FAIL <seconds>s".
gate() {
  local name="$1" fn="$2" start elapsed
  echo ""
  echo "════ $name ════"
  start=$SECONDS
  if "$fn"; then
    elapsed=$(( SECONDS - start ))
    summary+=("PASS  $name  (${elapsed}s)")
  else
    elapsed=$(( SECONDS - start ))
    summary+=("FAIL  $name  (${elapsed}s)")
    failed=true
  fi
}

tests_gate() {
  local ok=true
  for p in "${TEST_PROJECTS[@]}"; do
    echo "--- $p ---"
    dotnet test "$p" -c Release --nologo \
      --logger "trx;LogFileName=$(basename "$p").trx" \
      --results-directory artifacts/tests || ok=false
  done
  $ok
}

compile_gate() {
  local ok=true
  for proj in "${APP_PROJECTS[@]}"; do
    echo "--- $proj ---"
    dotnet build "$proj" -f net10.0-ios -t:Compile -c Release --nologo || ok=false
  done
  $ok
}

full_build_gate() {
  local ok=true
  for proj in "${APP_PROJECTS[@]}"; do
    echo "--- $proj ---"
    dotnet build "$proj" -f net10.0-ios -c Debug --nologo || ok=false
  done
  $ok
}

linux_gate() {
  # --user so the container cannot leave root-owned bin/obj in the host tree;
  # a writable HOME because the SDK needs one once it is no longer root.
  # Container-local restore dirs keep macOS bin/obj/nuget out of the Linux run.
  docker run --rm -v "$PWD":/src -w /src \
    --user "$(id -u):$(id -g)" -e DOTNET_CLI_HOME=/tmp -e HOME=/tmp \
    -e DOTNET_NOLOGO=true -e DOTNET_CLI_TELEMETRY_OPTOUT=true \
    mcr.microsoft.com/dotnet/sdk:10.0 \
    bash -c 'set -e
      for p in tests/Shared.Tests tests/Backend.Api.Tests \
               tests/Customer.Mobile.Tests tests/Provider.Mobile.Tests; do
        echo "--- $p (linux) ---"
        out="/tmp/out/$(basename "$p")"
        dotnet test "$p" -c Release --nologo \
          --artifacts-path "$out" --packages /tmp/nuget
      done'
}

$run_tests   && gate "Tests (4 suites)"                 tests_gate
$run_compile && gate "View-code compile gate (iOS)"     compile_gate
$run_full    && gate "Build apps (iOS, full package)"   full_build_gate
$run_linux   && gate "Tests (Linux container)"          linux_gate

echo ""
echo "════ Local CI summary ════"
printf '%s\n' "${summary[@]}"
if $failed; then
  echo "RESULT: FAIL"
  exit 1
fi
echo "RESULT: PASS"
