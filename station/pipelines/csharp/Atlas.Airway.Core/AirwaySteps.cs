// Rule S (Master Plan 10.2.3): "each generation is one turn tile (left or right) plus one straight tile; the next fork
// is the tile at the end". This file gives the address as that sequence of abstract steps and the lazy window of
// addresses. It does NOT compute tile ids: those are Phase 1's (TileId, ITileGraph, FrameOf; P1-B04). See README.md.
using System;

namespace Atlas.Airway
{
    /// <summary>One tile step of Rule S, relative to the direction of travel.</summary>
    public enum AirwayStep : byte
    {
        TurnLeft = 0,
        TurnRight = 1,
        Straight = 2,
    }

    /// <summary>What the caller's tile graph does with one step. A struct implementation is called without boxing.</summary>
    public interface IAirwayStepper<TState>
    {
        TState Apply(TState state, AirwayStep step);
    }

    public static class AirwaySteps
    {
        /// <summary>Tile steps per generation (Core: one turn tile plus one straight tile).</summary>
        public const int StepsPerGeneration = 2;
        /// <summary>The plan's walkable depth: 16 conducting generations = 32 tile steps.</summary>
        public const int WalkableGen = 16;

        /// <summary>Tile steps from the trachea's fork tile to the fork tile of address: 2 per letter.</summary>
        public static int Count(AirwayAddress address) { return StepsPerGeneration * address.Length; }

        /// <summary>Step i (0-based) of the address: even i is the turn of letter i / 2, odd i is the straight tile.</summary>
        public static AirwayStep StepAt(AirwayAddress address, int i)
        {
            if ((uint)i >= (uint)Count(address)) throw new ArgumentOutOfRangeException(nameof(i));
            if ((i & 1) == 1) return AirwayStep.Straight;
            return address.IsRight(i >> 1) ? AirwayStep.TurnRight : AirwayStep.TurnLeft;
        }

        /// <summary>Writes the steps of the address into dst; returns their number. No allocation.</summary>
        public static int Fill(AirwayAddress address, Span<AirwayStep> dst)
        {
            int n = Count(address);
            if (dst.Length < n) throw new ArgumentException("destination too short for " + n + " steps");
            for (int i = 0; i < n; i++) dst[i] = StepAt(address, i);
            return n;
        }

        /// <summary>Runs the steps of the address through the caller's stepper, starting from start (the trachea's
        /// tile and heading). This is the shape of FrameOf(address, trachea): the stepper owns the tile ids.</summary>
        public static TState Fold<TState, TStepper>(AirwayAddress address, TState start, TStepper stepper)
            where TStepper : IAirwayStepper<TState>
        {
            TState s = start;
            int n = Count(address);
            for (int i = 0; i < n; i++) s = stepper.Apply(s, StepAt(address, i));
            return s;
        }

        /// <summary>
        /// The edge (0..3) through which to leave a tile that was entered through entryEdge, in the edge numbering of
        /// docs/contracts/lane-b-kernel.md section 1.6 (edge k at angle k * 90 degrees, anticlockwise seen from above):
        /// left = (b + 3) mod 4 (the contract's own "turning left" rule), straight = (b + 2) mod 4, right = (b + 1) mod 4.
        /// The entry edge of the next tile is ITileGraph.BackEdge(tile, exit): ask the graph, never compute it.
        /// </summary>
        public static int ExitEdge(int entryEdge, AirwayStep step)
        {
            if ((uint)entryEdge > 3u) throw new ArgumentOutOfRangeException(nameof(entryEdge));
            int add = step == AirwayStep.TurnLeft ? 3 : step == AirwayStep.Straight ? 2 : 1;
            return (entryEdge + add) & 3;
        }
    }

    /// <summary>
    /// The lazy window of the plan (10.2.3): "current node, 2 ancestors, descendants to depth 3". As written that is at
    /// most 2 + (1 + 2 + 4 + 8) = 17 nodes, 2 tiles each; the plan's bound is 60 nodes and 120 tiles.
    /// </summary>
    public static class AirwayWindow
    {
        public const int Ancestors = 2;
        public const int DescendantDepth = 3;
        /// <summary>Largest window of <see cref="Fill(AirwayAddress, int, Span{AirwayAddress})"/>.</summary>
        public const int MaxNodes = Ancestors + (1 << (DescendantDepth + 1)) - 1;
        /// <summary>The plan's bound (row P2-B01).</summary>
        public const int PlanBoundNodes = 60;

        /// <summary>
        /// Writes the window around current into dst: the ancestors (nearest last), then current, then its descendants
        /// breadth-first, L before R, down to DescendantDepth and never deeper than maxGen. Returns the count.
        /// </summary>
        public static int Fill(AirwayAddress current, int maxGen, Span<AirwayAddress> dst)
        {
            return Fill(current, maxGen, Ancestors, DescendantDepth, dst);
        }

        public static int Fill(AirwayAddress current, int maxGen, int ancestors, int descendantDepth, Span<AirwayAddress> dst)
        {
            if (maxGen > AirwayAddress.MaxLength) maxGen = AirwayAddress.MaxLength;
            if (ancestors < 0 || descendantDepth < 0) throw new ArgumentOutOfRangeException();
            int n = 0;
            int up = current.Length < ancestors ? current.Length : ancestors;
            for (int k = current.Length - up; k < current.Length; k++) Put(dst, ref n, current.Prefix(k));
            Put(dst, ref n, current);
            for (int d = 1; d <= descendantDepth && current.Length + d <= maxGen; d++)
            {
                uint count = 1u << d;
                for (uint b = 0; b < count; b++)
                    Put(dst, ref n, new AirwayAddress((current.Bits << d) | b, current.Length + d));
            }
            return n;
        }

        static void Put(Span<AirwayAddress> dst, ref int n, AirwayAddress a)
        {
            if (n >= dst.Length) throw new ArgumentException("destination too short for the window");
            dst[n++] = a;
        }
    }
}
