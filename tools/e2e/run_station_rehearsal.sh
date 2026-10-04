#!/usr/bin/env bash
# The Station rehearsal: starts the real Hub (dotnet) and the real Forge (uvicorn, procedural generator) on
# free ports, plays walks into the Hub through tools/ReplayPhone, sets a parameter and sends each operator
# command through the Hub's HTTP API, posts fixture rooms and their object jobs to the Forge, downloads and
# validates the meshes, then stops everything and reads the logs. Exit code 0 only when every check passed.
#
#   bash tools/e2e/run_station_rehearsal.sh            # the whole rehearsal
#   KEEP=1 bash tools/e2e/run_station_rehearsal.sh     # keep the work folder (logs, GLBs) and print where it is
#
# Needs: the .NET 8 SDK (on PATH or in ~/.dotnet) and the Forge's Python environment (station/forge/.venv, or
# FORGE_PYTHON=<interpreter with the packages of station/forge/requirements.lock>). No Unity, no phone, no GPU.
set -u

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
HERE="$REPO/tools/e2e"
ARTIFACTS="${ATLAS_STATION_ARTIFACTS:-$HOME/.cache/atlas-station/e2e}"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/atlas-e2e.XXXXXX")"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

DOTNET="$(command -v dotnet || true)"
[ -x "$HOME/.dotnet/dotnet" ] && DOTNET="$HOME/.dotnet/dotnet"
PY="${FORGE_PYTHON:-$REPO/station/forge/.venv/bin/python}"

HUB_PID="" FORGE_PID="" PHONE_PID="" FAILED=0

say()  { printf '\n== %s\n' "$*"; }
fail() { printf '  FAIL  %s\n' "$*"; FAILED=1; }

stop() {   # stop <pid> <name>: asks (terminate: a background job never sees an interrupt), waits up to 10 s, then kills
    local pid="$1" n=0
    [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null || { fail "$2 was not running any more when it was to be stopped"; return 0; }
    kill -TERM "$pid" 2>/dev/null
    while kill -0 "$pid" 2>/dev/null && [ $n -lt 100 ]; do sleep 0.1; n=$((n + 1)); done
    if kill -0 "$pid" 2>/dev/null; then kill -KILL "$pid" 2>/dev/null; fail "$2 did not stop when asked and was killed"; fi
    wait "$pid" 2>/dev/null
    return 0
}

cleanup() {
    [ -n "$PHONE_PID" ] && kill -KILL "$PHONE_PID" 2>/dev/null
    [ -n "$HUB_PID" ] && kill -KILL "$HUB_PID" 2>/dev/null
    [ -n "$FORGE_PID" ] && kill -KILL "$FORGE_PID" 2>/dev/null
    if [ "${KEEP:-0}" = "1" ] || [ "$FAILED" != "0" ]; then echo "work folder kept: $WORK"; else rm -rf "$WORK"; fi
}
trap cleanup EXIT

wait_http() {   # wait_http <url> <seconds> <pid>: until the URL answers 200, or the process is gone
    local n=0 limit=$(( $2 * 5 ))
    while [ $n -lt $limit ]; do
        [ "$(curl -s -o /dev/null -w '%{http_code}' "$1" 2>/dev/null)" = "200" ] && return 0
        kill -0 "$3" 2>/dev/null || return 1
        sleep 0.2; n=$((n + 1))
    done
    return 1
}

# ---- 0  what is needed ------------------------------------------------------------------------------------
say "Station rehearsal ($REPO)"
[ -n "$DOTNET" ] && [ -x "$DOTNET" ] || { fail "no .NET SDK: install .NET 8 or put it in ~/.dotnet"; exit 1; }
"$PY" -c "import uvicorn, fastapi, PIL, numpy" 2>/dev/null || {
    fail "no Forge environment at $PY: cd station/forge && python -m venv .venv && .venv/bin/pip install -r requirements.lock"; exit 1; }
command -v curl >/dev/null || { fail "curl is needed"; exit 1; }

read -r UDP HTTP FORGE <<EOF
$("$PY" - <<'PORTS'
import socket
udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); udp.bind(("127.0.0.1", 0))
a = socket.socket(); a.bind(("127.0.0.1", 0))
b = socket.socket(); b.bind(("127.0.0.1", 0))
print(udp.getsockname()[1], a.getsockname()[1], b.getsockname()[1])
PORTS
)
EOF
HUB_URL="http://127.0.0.1:$HTTP"
FORGE_URL="http://127.0.0.1:$FORGE"
echo "  Hub UDP $UDP, Hub HTTP $HTTP, Forge $FORGE; work folder $WORK"

# ---- 1  build ---------------------------------------------------------------------------------------------
say "Build the Hub and ReplayPhone"
for project in station/hub tools/ReplayPhone; do
    if "$DOTNET" build "$REPO/$project" --artifacts-path "$ARTIFACTS" -v q >"$WORK/build.out" 2>&1; then
        echo "  ok    $project builds"
    else
        tail -30 "$WORK/build.out"; fail "$project does not build"; exit 1
    fi
done
# The built programs are started directly, so each is one process that hears its own signals.
HUB_DLL="$ARTIFACTS/bin/Hub/debug/atlas-hub.dll"
PHONE_DLL="$ARTIFACTS/bin/ReplayPhone/debug/ReplayPhone.dll"
[ -f "$HUB_DLL" ] && [ -f "$PHONE_DLL" ] || { fail "the build left no $HUB_DLL or $PHONE_DLL"; exit 1; }

# ---- 2  the Forge and the Hub -----------------------------------------------------------------------------
say "Start the Forge (procedural generator) and the Hub"
( cd "$REPO/station/forge" && exec env FORGE_GENERATOR=procedural FORGE_DATA_DIR="$WORK/forge-data" \
    FORGE_HUB_URL="$HUB_URL/internal/job" FORGE_PUBLIC_URL="$FORGE_URL" FORGE_DEBUG_KEEP= FORGE_HOSTED_ENABLED= \
    "$PY" -m uvicorn forge.app:app --host 127.0.0.1 --port "$FORGE" ) >"$WORK/forge.out" 2>&1 &
FORGE_PID=$!

"$DOTNET" "$HUB_DLL" --wwwroot "$REPO/station/hub/wwwroot" \
    --udp "$UDP" --http "$HTTP" --bind 127.0.0.1 --advertise 127.0.0.1 --forge "$FORGE_URL" \
    --logs "$WORK/hub-logs" --presets "$WORK/presets" --quiet >"$WORK/hub.out" 2>&1 &
HUB_PID=$!

if wait_http "$FORGE_URL/health" 60 "$FORGE_PID"; then echo "  ok    the Forge answers on $FORGE_URL"
else tail -20 "$WORK/forge.out"; fail "the Forge did not start"; exit 1; fi
if wait_http "$HUB_URL/api/session" 60 "$HUB_PID"; then echo "  ok    the Hub answers on $HUB_URL"
else tail -20 "$WORK/hub.out"; fail "the Hub did not start"; exit 1; fi
for route in / /pair /api/pair /api/params /api/presets; do
    code="$(curl -s -o /dev/null -w '%{http_code}' "$HUB_URL$route")"
    [ "$code" = "200" ] && echo "  ok    GET $route -> 200" || fail "GET $route -> $code"
done

# ---- 3  the live walk: parameters, commands, the Forge round trip -----------------------------------------
say "Play the synthetic walk through ReplayPhone and drive the Station"
"$DOTNET" "$PHONE_DLL" --hub "127.0.0.1:$UDP" >"$WORK/phone-live.out" 2>&1 &
PHONE_PID=$!
"$PY" "$HERE/station_rehearsal.py" live --hub "$HUB_URL" --forge "$FORGE_URL" --work "$WORK" || FAILED=1
sleep 2.2        # two more Telemetry rows, so perf.csv has something to check
stop "$PHONE_PID" "ReplayPhone"; PHONE_PID=""
received="$(grep -c '^command: \|command: ' "$WORK/phone-live.out" || true)"
[ "${received:-0}" -ge 8 ] && echo "  ok    ReplayPhone walked until it was stopped and answered $received commands" \
    || { tail -5 "$WORK/phone-live.out"; fail "ReplayPhone answered ${received:-0} commands, 8 were sent"; }
LIVE_SESSION="$(cat "$WORK/live.session" 2>/dev/null || true)"
SESSIONS=()
[ -n "$LIVE_SESSION" ] && SESSIONS+=(--session "$LIVE_SESSION")

# ---- 4  the recorded walks, replayed ----------------------------------------------------------------------
say "Replay the recorded walks of /recordings through ReplayPhone"
for spec in "walk_lap_sample_3x3 4" "walk_wall_approach 1" "walk_jump_and_loss 1"; do
    set -- $spec
    walk="$REPO/recordings/$1.poselog"
    "$PY" "$HERE/station_rehearsal.py" record --walk "$walk" --out "$WORK/$1.state.bin" | sed 's/^/  /' || { fail "$1 could not be recorded"; continue; }
    sleep 0.6    # the Hub lets the seat of the last phone go
    "$DOTNET" "$PHONE_DLL" "$WORK/$1.state.bin" --hub "127.0.0.1:$UDP" --speed "$2" >"$WORK/phone-$1.out" 2>&1
    code=$?
    session="$(sed -n 's/.*connected, session \([^ ,]*\).*/\1/p' "$WORK/phone-$1.out" | head -1)"
    if [ $code -ne 0 ] || [ -z "$session" ]; then tail -5 "$WORK/phone-$1.out"; fail "ReplayPhone $1: exit code $code"; continue; fi
    echo "  ok    ReplayPhone $1 at speed $2: exit code 0, session $session"
    "$PY" "$HERE/station_rehearsal.py" replay --hub "$HUB_URL" --walk "$walk" --session "$session" || FAILED=1
    SESSIONS+=(--session "$session")
done

# ---- 5  stop, then the logs -------------------------------------------------------------------------------
say "Stop the Hub and the Forge, read the logs"
stop "$HUB_PID" "the Hub"; HUB_PID=""
stop "$FORGE_PID" "the Forge"; FORGE_PID=""
"$PY" "$HERE/station_rehearsal.py" logs --work "$WORK" --hub-logs "$WORK/hub-logs" "${SESSIONS[@]}" || FAILED=1

say "$( [ "$FAILED" = "0" ] && echo 'Station rehearsal PASSED' || echo 'Station rehearsal FAILED' )"
exit "$FAILED"
