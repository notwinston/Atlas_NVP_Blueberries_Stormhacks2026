#!/usr/bin/env bash
# Run once per clone: enables Git LFS and registers UnityYAMLMerge as the merge driver
# named in .gitattributes. Works on macOS and in Git Bash on Windows.
set -euo pipefail
cd "$(dirname "$0")/.."

version=$(sed -n 's/^m_EditorVersion: //p' ATLAS/ProjectSettings/ProjectVersion.txt)
case "$(uname -s)" in
  Darwin) merge="/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/Helpers/UnityYAMLMerge" ;;
  *)      merge="C:/Program Files/Unity/Hub/Editor/$version/Editor/Data/Tools/UnityYAMLMerge.exe" ;;
esac
[ -e "$merge" ] || { echo "UnityYAMLMerge not found at $merge: install Unity $version through the Hub" >&2; exit 1; }

git lfs install --local
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "\"$merge\" merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
echo "Git LFS and UnityYAMLMerge ($version) are set up for this clone."
