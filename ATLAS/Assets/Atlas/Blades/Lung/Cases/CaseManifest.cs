// PLACEHOLDER (lane D, P2-D01): contract surface only; replaced by lane D's implementation
using System;
using System.IO;
using UnityEngine;

namespace Atlas.Blades.Lung
{
    // Master Plan §10.2.4 (manifest.json, atlas.case/1). Packages ship in StreamingAssets/cases/<id>/.
    [Serializable]
    public sealed class CaseManifest
    {
        public string schema;      // "atlas.case/1"
        public string id;
        public string kind;        // "real" | "illustrative"
        public CaseLabel[] labels;
        public string licence;
        public string credit;
        public float M;            // 15
        public float Lambda_m;     // 1.5
        public CaseFile[] files;

        public static CaseManifest Load(string caseRoot) =>
            JsonUtility.FromJson<CaseManifest>(File.ReadAllText(Path.Combine(caseRoot, "manifest.json")));

        public static string Root(string id) => Path.Combine(Application.streamingAssetsPath, "cases", id);
    }

    [Serializable] public struct CaseLabel { public string content; public string label; }
    [Serializable] public struct CaseFile { public string path; public string sha256; }
}
