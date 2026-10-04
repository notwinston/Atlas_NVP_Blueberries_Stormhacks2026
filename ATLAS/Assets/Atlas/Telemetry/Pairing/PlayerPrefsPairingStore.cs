using UnityEngine;

namespace Atlas.Telemetry
{
    /// <summary>The Station address in <c>PlayerPrefs</c>. Main thread only, as <c>PlayerPrefs</c> is.</summary>
    public sealed class PlayerPrefsPairingStore : IPairingStore
    {
        public string GetString(string key, string fallback) { return PlayerPrefs.GetString(key, fallback); }
        public void SetString(string key, string value) { PlayerPrefs.SetString(key, value); }
        public int GetInt(string key, int fallback) { return PlayerPrefs.GetInt(key, fallback); }
        public void SetInt(string key, int value) { PlayerPrefs.SetInt(key, value); }
        public bool HasKey(string key) { return PlayerPrefs.HasKey(key); }
        public void Save() { PlayerPrefs.Save(); }
    }
}
