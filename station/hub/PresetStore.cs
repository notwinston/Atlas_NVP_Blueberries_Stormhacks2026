using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>
    /// Parameter presets as JSON files on the Station, <c>{"values": {name: value}}</c> (Master Plan section
    /// 7.3.2 and Appendix B.3). One file per preset, so they can be committed to the repository.
    /// </summary>
    public sealed class PresetStore
    {
        readonly string _folder;
        readonly object _lock = new object();

        public PresetStore(string folder) { _folder = Path.GetFullPath(folder); }

        public string Folder { get { return _folder; } }

        public List<string> List()
        {
            var names = new List<string>();
            lock (_lock)
            {
                if (!Directory.Exists(_folder)) return names;
                foreach (string path in Directory.GetFiles(_folder, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (Names.IsPreset(name)) names.Add(name);
                }
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        public bool TryLoad(string name, out JsonValue preset)
        {
            preset = null;
            if (!Names.IsPreset(name)) return false;
            lock (_lock)
            {
                string path = Path.Combine(_folder, name + ".json");
                if (!File.Exists(path)) return false;
                JsonValue json;
                if (!AtlasJson.TryParse(File.ReadAllText(path, Encoding.UTF8), out json) || !json.IsObject || !json["values"].IsObject) return false;
                preset = json;
                return true;
            }
        }

        /// <summary>Writes the preset through a temporary file, so a crash never leaves half a preset.</summary>
        public void Save(string name, JsonValue preset)
        {
            if (!Names.IsPreset(name)) throw new ArgumentException("not a preset name", "name");
            lock (_lock)
            {
                Directory.CreateDirectory(_folder);
                string path = Path.Combine(_folder, name + ".json");
                string temp = path + ".tmp";
                File.WriteAllText(temp, preset.ToJson(true) + "\n", new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
        }

        /// <summary>When the preset was last saved (UTC); the minimum value when it does not exist.</summary>
        public DateTime Modified(string name)
        {
            if (!Names.IsPreset(name)) return DateTime.MinValue;
            string path = Path.Combine(_folder, name + ".json");
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }

        /// <summary>Where Bake writes a preset inside the Unity project (lane D contract 1.7).</summary>
        public const string BakedDefaultsPath = "Assets/Atlas/Telemetry/Resources/AtlasParams/defaults.json";

        /// <summary>
        /// Bake (Master Plan section 7.3.2): writes the preset into the project's defaults, so a build started
        /// with the Hub off has those values. False when the preset or the project folder does not exist.
        /// </summary>
        public bool Bake(string name, string projectFolder, out string writtenPath)
        {
            writtenPath = null;
            JsonValue preset;
            if (!TryLoad(name, out preset) || string.IsNullOrEmpty(projectFolder) || !Directory.Exists(Path.Combine(projectFolder, "Assets"))) return false;
            lock (_lock)
            {
                string path = Path.Combine(projectFolder, BakedDefaultsPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, JsonValue.Object().Set("values", preset["values"]).ToJson(true) + "\n", new UTF8Encoding(false));
                File.Move(temp, path, true);
                writtenPath = path;
            }
            return true;
        }

        public bool Delete(string name)
        {
            if (!Names.IsPreset(name)) return false;
            lock (_lock)
            {
                string path = Path.Combine(_folder, name + ".json");
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
        }
    }
}
