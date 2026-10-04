using System;
using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7, §4.9, §8.5 S3 row; task P1-B12; lane C contract §9.2 (S3: walls Full, floor hidden, objects shown once).
    // The room is not copied: this drives the shared room content (IRoomContent). The walls stand in full on the edge of the grid,
    // the cells bring the floor, and every object keeps its own renderer at its true place, unclipped, enabled whenever any
    // instance of one of its cells is drawn. A mesh the asset client swaps in is therefore the mesh S3 shows.
    public sealed class AlignedCellsRoomView
    {
        readonly IRoomContent room;
        readonly CellGrid grid;
        readonly bool[] mask;
        readonly Action<RoomModel> onBuilt;
        readonly Action<int> onObjectChanged;
        CellRange[] ranges = Array.Empty<CellRange>();
        bool[] visible = Array.Empty<bool>();
        bool entered;

        public IRoomContent Room => room;
        public int ObjectCount { get; private set; }
        public int VisibleObjectCount { get; private set; }
        // The room content changed under the space (a rebuilt room, a swapped mesh): Apply must run again.
        public bool Dirty { get; private set; }

        public AlignedCellsRoomView(IRoomContent room, CellGrid grid)
        {
            this.room = room ?? throw new ArgumentNullException(nameof(room));
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            mask = new bool[grid.CountX * grid.CountZ];
            onBuilt = _ => { ReadObjects(); if (entered) Present(); };
            onObjectChanged = _ => Dirty = true;      // the content enables the renderer of a mesh it swaps in
            ReadObjects();
        }

        public CellRange ObjectCells(int i) => ranges[i];
        public bool ObjectVisible(int i) => visible[i];

        public void Enter()
        {
            entered = true;
            room.Built += onBuilt;
            room.ObjectChanged += onObjectChanged;
            Present();
        }

        // Walls Full at once, floor hidden, objects on; §4.7 "unclipped": no gate and no warp on the room's own materials.
        void Present()
        {
            Dirty = true;
            if (!room.IsBuilt) return;
            room.SetWallMode(WallMode.Full, 0f);
            room.FloorVisible = false;
            room.ObjectsVisible = true;
            Unclip(room.ShellMaterial);
            for (int i = 0; i < ObjectCount; i++) Unclip(room.GetObject(i)?.Material);
        }

        static void Unclip(Material material)
        {
            if (material == null) return;
            MaterialPool.ClearGates(material);
            MaterialPool.SetWarpMode(material, 0);
        }

        void ReadObjects()
        {
            ObjectCount = room.IsBuilt ? room.ObjectCount : 0;
            if (ranges.Length != ObjectCount) { ranges = new CellRange[ObjectCount]; visible = new bool[ObjectCount]; }
            for (int i = 0; i < ObjectCount; i++)
            {
                IRoomObjectView view = room.GetObject(i);
                ranges[i] = view != null && view.Data != null ? RoomObjectCells.Of(grid, view.Data) : new CellRange(new int2(-1), new int2(-1));
            }
            VisibleObjectCount = 0;
            Dirty = true;
        }

        // §4.7: visible iff any instance of one of its cells is drawn. No allocation, no engine call.
        public void Compute(CellInstanceList instances)
        {
            Array.Clear(mask, 0, mask.Length);
            int n = instances.Count, cx = grid.CountX;
            for (int k = 0; k < n; k++)
            {
                ref readonly CellInstance inst = ref instances[k];
                mask[inst.Cell.y * cx + inst.Cell.x] = true;
            }
            int count = 0;
            for (int i = 0; i < ObjectCount; i++)
            {
                CellRange r = ranges[i];
                bool v = false;
                for (int z = r.Min.y; z <= r.Max.y && !v; z++)
                    for (int x = r.Min.x; x <= r.Max.x; x++)
                        if (x >= 0 && z >= 0 && mask[z * cx + x]) { v = true; break; }
                visible[i] = v;
                if (v) count++;
            }
            VisibleObjectCount = count;
        }

        // Step 6: Compute, then each object's own renderer on or off; writes only on change.
        public void Apply(CellInstanceList instances)
        {
            Compute(instances);
            for (int i = 0; i < ObjectCount; i++)
            {
                Renderer r = room.GetObject(i)?.Renderer;
                if (r != null && r.enabled != visible[i]) r.enabled = visible[i];
            }
            Dirty = false;
        }

        // Every renderer back on; SpaceLab's ResetPresentation restores walls, floor and materials.
        public void Exit()
        {
            if (!entered) return;
            entered = false;
            room.Built -= onBuilt;
            room.ObjectChanged -= onObjectChanged;
            if (!room.IsBuilt) return;
            int n = Math.Min(ObjectCount, room.ObjectCount);
            for (int i = 0; i < n; i++)
            {
                Renderer r = room.GetObject(i)?.Renderer;
                if (r != null && !r.enabled) r.enabled = true;
            }
        }
    }
}
