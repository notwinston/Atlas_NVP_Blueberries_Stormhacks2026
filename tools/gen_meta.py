#!/usr/bin/env python3
"""Create the missing Unity .meta files under ATLAS/Assets with stable GUIDs.

Unity creates a .meta with a random GUID the first time it imports a file, so the same new file gets a
different GUID on every machine unless the .meta is committed with it. This tool writes the missing ones
before the Editor does, with a GUID derived from the asset's path, so every clone agrees and generated
scenes can reference scripts by GUID. Existing .meta files are never touched.

  gen_meta.py            create missing .meta files, report orphaned ones
  gen_meta.py --check    exit 1 if any are missing or orphaned (changes nothing)
  gen_meta.py --guid Assets/Atlas/App/AtlasBootstrap.cs    print the GUID an asset has or will get
"""
import hashlib
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = os.path.join(REPO, "ATLAS")
ASSETS = os.path.join(PROJECT, "Assets")

FOLDER = "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
DEFAULT = "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
ASMDEF = "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
SHADER = ("ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n"
          "  preprocessorOverride: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
INCLUDE = "ShaderIncludeImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
TEXT = "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
BODY = {
    ".asmdef": ASMDEF, ".shader": SHADER, ".hlsl": INCLUDE, ".cginc": INCLUDE,
    ".json": TEXT, ".txt": TEXT, ".md": TEXT, ".csv": TEXT, ".bytes": TEXT, ".xml": TEXT,
    ".unity": DEFAULT,
}


def guid_for(rel):
    """rel: path relative to the Unity project folder, forward slashes."""
    return hashlib.md5(("atlas:" + rel).encode("utf-8")).hexdigest()


def existing_guid(meta_path):
    try:
        with open(meta_path) as f:
            m = re.search(r"^guid: ([0-9a-f]{32})", f.read(), re.M)
            return m.group(1) if m else None
    except OSError:
        return None


def skip(name):
    return name.startswith(".") or name.endswith("~") or name.endswith(".meta") or name.endswith(".tmp")


def walk():
    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = sorted(d for d in dirs if not skip(d))
        for d in dirs:
            yield os.path.join(root, d), True
        for f in sorted(files):
            if not skip(f):
                yield os.path.join(root, f), False


def main():
    args = sys.argv[1:]
    if args[:1] == ["--guid"]:
        rel = args[1].replace("\\", "/")
        print(existing_guid(os.path.join(PROJECT, rel) + ".meta") or guid_for(rel))
        return
    check = "--check" in args
    missing, orphans = [], []
    for path, is_dir in walk():
        meta = path + ".meta"
        if os.path.exists(meta):
            continue
        rel = os.path.relpath(path, PROJECT).replace(os.sep, "/")
        missing.append(rel)
        if check:
            continue
        body = FOLDER if is_dir else BODY.get(os.path.splitext(path)[1].lower(), "")
        with open(meta, "w", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: %s\n%s" % (guid_for(rel), body))
    for root, dirs, files in os.walk(ASSETS):
        dirs[:] = [d for d in dirs if not skip(d)]
        for f in files:
            if f.endswith(".meta") and not os.path.exists(os.path.join(root, f[:-5])):
                orphans.append(os.path.relpath(os.path.join(root, f), PROJECT))
    print("%s %d missing .meta file(s); %d orphaned" % ("found" if check else "created", len(missing), len(orphans)))
    for o in orphans:
        print("  orphan: " + o)
    if check and (missing or orphans):
        for m in missing[:50]:
            print("  missing: " + m)
        sys.exit(1)


if __name__ == "__main__":
    main()
