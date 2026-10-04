#!/usr/bin/env bash
# One command from source to a running app on every connected phone (Master Plan P1-A13, Appendix C.8).
#
#   tools/build_ios.sh                 build the working tree as it is
#   tools/build_ios.sh <branch>        fetch, switch to <branch>, fast-forward, then build
#
# Options (environment):
#   ATLAS_TEAM_ID=XXXXXXXXXX   Apple team id of the signing owner (the Personal Team). Needed for the first
#                              build; after that Xcode keeps it in the exported project.
#   ATLAS_DEV=1                development build: Metal HUD on, debugger allowed. Published numbers always
#                              come from a release build (the default).
#   ATLAS_NO_INSTALL=1         stop after the Xcode build.
#   ATLAS_LAUNCH=1             launch the app on each phone after installing it.
#   ATLAS_UNITY=/path/Unity    Unity binary, if it is not in the Hub's default folder.
#
# Runs on the build seat only (macOS, Xcode 26, Unity with iOS Build Support). Close the project in the Unity
# Editor first: a batch-mode build cannot open a project that is already open.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
project="$repo/ATLAS"
out="$project/Build/iOS"
derived="$project/Build/DerivedData"
log="$project/Build/unity-build.log"
bundle_id="$(sed -n 's/^ *iPhone: \(com\..*\)$/\1/p' "$project/ProjectSettings/ProjectSettings.asset" | head -1)"

step() { printf '\n== %s\n' "$*"; }
die() { printf 'build_ios: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "the iOS build runs on the Mac build seat only"
command -v xcodebuild >/dev/null || die "xcodebuild not found: install Xcode 26 and run xcode-select"

if [ $# -ge 1 ]; then
  branch="$1"
  step "Switching to $branch"
  [ -z "$(git -C "$repo" status --porcelain)" ] || die "the working tree has changes; commit or stash them before building a branch"
  git -C "$repo" fetch origin
  git -C "$repo" checkout "$branch"
  # A branch that exists only on this machine has nothing to pull; one that is on the remote must fast-forward.
  if git -C "$repo" rev-parse --verify --quiet "refs/remotes/origin/$branch" >/dev/null; then
    git -C "$repo" pull --ff-only origin "$branch" || die "$branch does not fast-forward to origin/$branch; sort that out first"
  fi
  git -C "$repo" lfs pull
fi
commit="$(git -C "$repo" rev-parse --short HEAD)"
dirty="$([ -z "$(git -C "$repo" status --porcelain)" ] || echo '+local changes')"

version="$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt")"
unity="${ATLAS_UNITY:-/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity}"
[ -x "$unity" ] || die "Unity $version not found at $unity (install it through the Hub, or set ATLAS_UNITY)"
[ ! -e "$project/Temp/UnityLockfile" ] || die "the project is open in the Unity Editor; close it and run again"

mkdir -p "$project/Build"
unity_args=(-batchmode -quit -projectPath "$project" -buildTarget iOS
            -executeMethod Atlas.EditorTools.AtlasBuild.IOS -buildOutput "$out" -logFile "$log")
configuration=Release
if [ "${ATLAS_DEV:-0}" = "1" ]; then
  unity_args+=(-atlasDevelopment)
  configuration=Debug
fi

step "Unity $version: exporting the Xcode project ($commit$dirty)"
if ! "$unity" "${unity_args[@]}"; then
  grep -E "error CS|Error building|BuildFailedException|Shader error|^Atlas:" "$log" | tail -40 >&2 || true
  die "the Unity build failed; full log: $log"
fi
[ -d "$out/Unity-iPhone.xcodeproj" ] || die "Unity finished but $out/Unity-iPhone.xcodeproj is missing; see $log"

step "Xcode: building $configuration"
xcode_args=(-project "$out/Unity-iPhone.xcodeproj" -scheme Unity-iPhone -configuration "$configuration"
            -destination 'generic/platform=iOS' -derivedDataPath "$derived" -allowProvisioningUpdates
            CODE_SIGN_STYLE=Automatic)
if [ -n "${ATLAS_TEAM_ID:-}" ]; then
  xcode_args+=("DEVELOPMENT_TEAM=$ATLAS_TEAM_ID")
fi
if ! xcodebuild "${xcode_args[@]}" build > "$project/Build/xcodebuild.log" 2>&1; then
  grep -E "error:|requires a development team|No profiles|Signing for" "$project/Build/xcodebuild.log" | tail -30 >&2 || true
  die "xcodebuild failed; full log: $project/Build/xcodebuild.log
If the error is about a development team: set ATLAS_TEAM_ID to the Personal Team id of the signing owner
(Xcode > Settings > Accounts > the team > the id shown under 'Team ID' in the developer portal membership page)."
fi

app="$(find "$derived/Build/Products/$configuration-iphoneos" -maxdepth 1 -name '*.app' | head -1)"
[ -n "$app" ] || die "no .app found under $derived/Build/Products/$configuration-iphoneos"
echo "built: $app"

if [ "${ATLAS_NO_INSTALL:-0}" = "1" ]; then
  exit 0
fi

step "Installing on connected phones"
devices_json="$(mktemp)"
xcrun devicectl list devices --json-output "$devices_json" >/dev/null
ids="$(/usr/bin/python3 - "$devices_json" <<'PY'
import json, sys
data = json.load(open(sys.argv[1]))
for d in data.get("result", {}).get("devices", []):
    hw = d.get("hardwareProperties", {})
    conn = d.get("connectionProperties", {})
    if hw.get("platform") == "iOS" and conn.get("tunnelState") in ("connected", "disconnected") \
            and conn.get("pairingState") == "paired" and hw.get("reality") == "physical":
        print(d["identifier"] + "\t" + d.get("deviceProperties", {}).get("name", "iPhone"))
PY
)"
rm -f "$devices_json"
[ -n "$ids" ] || die "no paired iPhone is connected (cable in, unlocked, Trust This Computer, Developer Mode on)"

failed=0
while IFS=$'\t' read -r id name; do
  echo "-> $name ($id)"
  if xcrun devicectl device install app --device "$id" "$app"; then
    if [ "${ATLAS_LAUNCH:-0}" = "1" ] && [ -n "$bundle_id" ]; then
      xcrun devicectl device process launch --device "$id" "$bundle_id" || true
    fi
  else
    echo "   install failed on $name" >&2
    failed=1
  fi
done <<< "$ids"

[ "$failed" = "0" ] || die "at least one install failed. First launch after a new signing identity:
Settings > General > VPN & Device Management > the owner account > Trust."
step "Done: $commit$dirty is on the phone(s). Add a line to the queue in docs/build-seat.md."
