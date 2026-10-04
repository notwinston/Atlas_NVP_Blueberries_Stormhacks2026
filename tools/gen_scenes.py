#!/usr/bin/env python3
"""Generate the one-object scenes of the base and list them in the build settings.

Everything in ATLAS is composed in code (docs/contracts/README.md, F3). A space scene therefore holds exactly
one root object carrying the space's module component; SpaceLab loads it additively and falls back to the
space's registered factory when the scene is absent. This tool writes those scenes with stable file ids and
the script GUIDs from the .meta files, so nobody has to open the Editor to create them.

  gen_scenes.py            write missing or outdated scenes, their .meta files, and EditorBuildSettings
  gen_scenes.py --check    exit 1 if anything would change

Run tools/gen_meta.py first so every script has its .meta.
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_meta  # noqa: E402

PROJECT = gen_meta.PROJECT
SCENES = "Assets/Atlas/Scenes"
BOOT = SCENES + "/Atlas_Boot.unity"

# scene name, root object name, script (path relative to the Unity project), in the iOS build list?
MANIFEST = [
    ("Space_Home", "Space_Home", "Assets/Atlas/Spaces/Home/HomeSpace.cs", True),
    ("Space_Glued", "Space_Glued", "Assets/Atlas/Spaces/Glued/GluedSpace.cs", True),
    ("Space_CurvedWalk", "Space_CurvedWalk", "Assets/Atlas/Spaces/CurvedWalk/CurvedWalkSpace.cs", True),
    ("Space_AlignedCells", "Space_AlignedCells", "Assets/Atlas/Spaces/AlignedCells/AlignedCellsSpace.cs", True),
    ("Space_SmallPlanet", "Space_SmallPlanet", "Assets/Atlas/Spaces/SmallPlanet/SmallPlanetSpace.cs", True),
    ("Space_Portal", "Space_Portal", "Assets/Atlas/Spaces/Portal/PortalSpace.cs", True),
    ("Space_Slicer", "Space_Slicer", "Assets/Atlas/Spaces/Slicer/SlicerSpace.cs", True),
    # The Station's scene: the Spectator boots in any scene of this name, or one that holds this marker object.
    # It carries no script and is never in the iOS build list (AtlasBuild.Spectator names it explicitly).
    ("PC_Spectator", "AtlasSpectatorBoot", None, False),
]

OBJECT = """--- !u!1 &100000
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  serializedVersion: 6
  m_Component:
  - component: {{fileID: 100001}}
  - component: {{fileID: 100002}}
  m_Layer: 0
  m_Name: {name}
  m_TagString: Untagged
  m_Icon: {{fileID: 0}}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &100001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 100000}}
  serializedVersion: 2
  m_LocalRotation: {{x: 0, y: 0, z: 0, w: 1}}
  m_LocalPosition: {{x: 0, y: 0, z: 0}}
  m_LocalScale: {{x: 1, y: 1, z: 1}}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {{fileID: 0}}
  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: 0}}
--- !u!114 &100002
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 100000}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: 
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots:
  - {{fileID: 100001}}
"""


# A root object with a Transform and nothing else.
MARKER = OBJECT[:OBJECT.index("--- !u!114 &100002")].replace("  - component: {{fileID: 100002}}\n", "") \
    + OBJECT[OBJECT.index("--- !u!1660057539"):]


def scene_settings():
    """The four settings blocks Unity writes at the top of every scene, copied from the boot scene."""
    with open(os.path.join(PROJECT, BOOT)) as f:
        text = f.read()
    end = text.index("--- !u!1 &")
    return text[:end]


def guid_of(rel):
    path = os.path.join(PROJECT, rel)
    return gen_meta.existing_guid(path + ".meta") or gen_meta.guid_for(rel)


def write(path, text, check, changed):
    try:
        with open(path) as f:
            if f.read() == text:
                return
    except OSError:
        pass
    changed.append(os.path.relpath(path, PROJECT))
    if not check:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", newline="\n") as f:
            f.write(text)


def main():
    check = "--check" in sys.argv[1:]
    changed, missing = [], []
    head = scene_settings()
    build = [(BOOT, guid_of(BOOT))]
    for scene, root, script, in_build in MANIFEST:
        if script is not None and not os.path.exists(os.path.join(PROJECT, script)):
            missing.append(script)
            continue
        rel = "%s/%s.unity" % (SCENES, scene)
        body = OBJECT.format(name=root, guid=guid_of(script)) if script is not None else MARKER.format(name=root)
        write(os.path.join(PROJECT, rel), head + body, check, changed)
        meta = os.path.join(PROJECT, rel) + ".meta"
        if not os.path.exists(meta):
            write(meta, "fileFormatVersion: 2\nguid: %s\n%s" % (gen_meta.guid_for(rel), gen_meta.DEFAULT), check, changed)
        if in_build:
            build.append((rel, guid_of(rel)))

    settings = os.path.join(PROJECT, "ProjectSettings", "EditorBuildSettings.asset")
    with open(settings) as f:
        text = f.read()
    scenes = "  m_Scenes:\n" + "".join("  - enabled: 1\n    path: %s\n    guid: %s\n" % s for s in build)
    new = re.sub(r"  m_Scenes:\n(?:  - enabled: \d\n    path: .*\n    guid: .*\n)*", scenes, text, count=1)
    write(settings, new, check, changed)

    for m in missing:
        print("skipped (script not found): " + m)
    print("%s %d file(s)%s" % ("would change" if check else "wrote", len(changed), ": " + ", ".join(changed) if changed else ""))
    if check and changed:
        sys.exit(1)


if __name__ == "__main__":
    main()
