namespace Atlas.World
{
    /// <summary>
    /// Where the scanned LiDAR mesh of a finished room goes (standalone-phone plan D8, D10): the on-phone object
    /// builder cuts unknown objects out of it, and a later textured twin could be built from it.
    /// </summary>
    public interface IScanMeshSink
    {
        /// <summary>Main thread. Called once per finished room, before IObjectPhotoSink.BeginRoom for that room.
        /// mesh is null when the scan had no LiDAR mesh (manual outline, fixture, saved room).</summary>
        void SetScanMesh(string roomId, ScanMesh mesh);
    }

    /// <summary>The default sink: keeps nothing.</summary>
    public sealed class NullScanMeshSink : IScanMeshSink
    {
        public void SetScanMesh(string roomId, ScanMesh mesh) { }
    }
}
