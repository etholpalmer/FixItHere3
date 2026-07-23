#!/usr/bin/env bash
# Load the CC0 sample photos into a booted simulator's photo library so the demo
# operator can attach a trade-relevant photo in chat. Pass the target UDID.
#   ./load.sh <simulator-udid>
set -euo pipefail
UDID="${1:?usage: load.sh <simulator-udid>}"
DIR="$(cd "$(dirname "$0")" && pwd)"
xcrun simctl addmedia "$UDID" "$DIR"/*.jpg
echo "Loaded $(ls "$DIR"/*.jpg | wc -l | tr -d ' ') sample photos into $UDID"
