using System;
using System.IO;
using System.Threading;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The GLB cache on the phone: <c>persistentDataPath/assets/&lt;sha256&gt;.glb</c> (Master Plan section 7.4).
    /// A file is written as <c>.part</c> and renamed, so a file with its final name is always whole. Called on
    /// the worker thread; the folder path is read once, on the main thread, by whoever creates this.
    /// </summary>
    public sealed class FileAssetCache : IAssetCache
    {
        readonly string _folder;

        public FileAssetCache(string folder)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("folder");
            _folder = folder;
        }

        public string Folder { get { return _folder; } }

        public byte[] TryRead(string sha256)
        {
            if (!ForgeProtocol.IsSha256(sha256)) return null;
            try
            {
                string path = PathOf(sha256);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Write(string sha256, byte[] glb)
        {
            if (!ForgeProtocol.IsSha256(sha256) || glb == null) return;
            try
            {
                Directory.CreateDirectory(_folder);
                string path = PathOf(sha256), part = path + ".part";
                File.WriteAllBytes(part, glb);
                if (File.Exists(path)) File.Delete(path);
                File.Move(part, path);
            }
            // A full or read-only disk costs the cache, not the mesh: it is already in memory.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Delete(string sha256)
        {
            if (!ForgeProtocol.IsSha256(sha256)) return;
            try
            {
                File.Delete(PathOf(sha256));
                File.Delete(PathOf(sha256) + ".part");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        string PathOf(string sha256) { return Path.Combine(_folder, sha256 + ".glb"); }
    }

    /// <summary>Runs the work on the thread pool, so hashing and validation never cost a frame.</summary>
    public sealed class ThreadPoolAssetWorker : IAssetWorker
    {
        public void Run(Action<object> work, object state)
        {
            if (!ThreadPool.QueueUserWorkItem(new WaitCallback(work), state)) work(state);
        }
    }
}
