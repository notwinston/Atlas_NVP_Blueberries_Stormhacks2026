using System;
using Atlas.Input;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Scan;
using Atlas.World;
using Unity.Mathematics;
using WorldTier = Atlas.World.QualityTier;

namespace Atlas.App
{
    /// <summary>What the scan parameters choose. Read by whoever begins a scan (onboarding, the rehearsal flow).</summary>
    public sealed class ScanSettings
    {
        /// <summary>scan.route: the highest route attempted, 1 to 4.</summary>
        public int Route = 3;
        /// <summary>scan.teamPrescan: the session is a team pre-scan, so its room and world map are kept.</summary>
        public bool TeamPrescan;

        /// <summary>Route 1: the manual outline only. Route 2: the LiDAR mesh. Route 3 and above: mesh and boxes.</summary>
        public ScanOptions Options
        {
            get
            {
                ScanOptions options = ScanOptions.Default;
                options.UseMesh = Route >= 2;
                options.UseBoxes = Route >= 3;
                return options;
            }
        }
    }

    /// <summary>
    /// The registry entries of the assemblies that may not reference Atlas.Link (contract README F6):
    /// rendering, scan, pose and input expose plain properties, and the composition root declares the
    /// parameters that drive them. Also declares the kill switches of Master Plan section 12.4 that have no
    /// owner in this build, so that the Console and the menu list them from the first frame. Names, kinds,
    /// ranges and defaults follow lane D's table; the wire id is the order of declaration.
    /// </summary>
    public static class AppParams
    {
        public const string Space = "space.id";
        public const string PinTier = "thermal.pinTier";
        public const string RenderScale = "render.scale";
        public const string RenderMsaa = "render.msaa";
        public const string Ipd = "render.ipd";
        public const string LineWidth = "render.lineWidth";
        public const string Lens = "render.lens";
        public const string LensK1 = "render.lensK1";
        public const string LensK2 = "render.lensK2";
        public const string LensSeparation = "render.lensSeparation";
        public const string ScreenToLens = "render.screenToLens";
        public const string LensOffsetY = "render.lensOffsetY";
        public const string HeadOffsetX = "render.headOffsetX";
        public const string HeadOffsetY = "render.headOffsetY";
        public const string HeadOffsetZ = "render.headOffsetZ";
        public const string Furniture = "render.furniture";
        public const string CullRho = "curved.cullRho";
        public const string CurvedMode = "curved.mode";
        public const string GluedCopies = "glued.copies";
        public const string PortalDepth = "portal.depth";
        public const string SlicerSource = "slicer.source";
        public const string GyroPredict = "pose.gyroPredict";
        public const string GyroLeadMs = "pose.gyroLeadMs";
        public const string PoseSource = "pose.source";
        public const string ReplaySpeed = "replay.speed";
        public const string ReplayLoop = "replay.loop";
        public const string PoseRecord = "pose.record";
        public const string ScanRoute = "scan.route";
        public const string TeamPrescan = "scan.teamPrescan";
        public const string ForgeBackend = "forge.backend";
        public const string Dwell = "input.dwell";
        public const string DevRoom = "dev.room";
        public const string DevOverlay = "dev.overlay";

        public const int SourceTracked = 0, SourceReplay = 1, SourceWalker = 2;

        static readonly string[] s_spaceNames =
        {
            "S0 Home", "S1 Glued", "S2 Curved Walk", "S3 Aligned Cells", "S4 Small Planet", "S5 Impossible Rooms", "S6 Slicer", "S7 Network"
        };
        static readonly string[] s_curvedModes = { "Mode 1 Curved Walk", "Mode 2 Aligned Cells" };
        static readonly string[] s_slicerSources = { "live", "stepped", "tesseract" };
        static readonly string[] s_forgeBackends = { "local", "hosted", "cache", "off" };
        static readonly string[] s_poseSources = { "tracked", "replay", "walker" };
        static readonly string[] s_furnitureModes = { "auto", "light", "full" };

        // ---- space.id -------------------------------------------------------------------------------------

        /// <summary>space.id and its write-back. Requests are ignored until <see cref="Enabled"/> is set.</summary>
        public sealed class SpaceBinding
        {
            public ParamEntry Entry;
            /// <summary>False during boot, so pushing the defaults does not ask for a space.</summary>
            public bool Enabled;
            internal bool Writing;
        }

        /// <summary>
        /// space.id forces a space; the value is written back after every switch, so the Console shows where
        /// the walker is. A space that is not built is refused by the host and the value snaps back.
        /// </summary>
        public static SpaceBinding DeclareSpace(ParamRegistry registry, ISpaceHost host)
        {
            var binding = new SpaceBinding();
            binding.Entry = registry.DeclareEnum(Space, "app", s_spaceNames, 0, value =>
            {
                if (!binding.Enabled || binding.Writing) return;
                if (!host.RequestSwitch((Atlas.World.SpaceId)value) && host.HasSpace && (int)host.Current != value)
                    WriteBack(binding, host.Current);
            }, true);
            host.SpaceEntered += id => WriteBack(binding, id);
            return binding;
        }

        static void WriteBack(SpaceBinding binding, Atlas.World.SpaceId id)
        {
            binding.Writing = true;
            binding.Entry.Set((int)id);
            binding.Writing = false;
        }

        // ---- rendering and quality ------------------------------------------------------------------------

        /// <summary>
        /// The quality and render parameters of lane A's section 9.6, bound to the ladder, the eye textures,
        /// the rig, the lens model and the look. eyes, rig and the two setters may be null where there is no
        /// stereo rig. lensOn is the start value of render.lens (off in the Editor, where there are no lenses).
        /// furniture is the policy that puts render.furniture on the room content; null where there is no room.
        /// </summary>
        public static void DeclareRender(ParamRegistry registry, QualityLadder ladder, IWalkerKernel kernel,
            IEyeTextures eyes, IStereoRig rig, LensModel lens, bool lensOn, Action<bool> setLens, Action<float> setLineWidth,
            FurnitureLevelPolicy furniture = null)
        {
            QualityApplier applier = ladder.Applier;
            bool writing = false;

            registry.DeclareInt(PinTier, "render", -1, 4, -1, tier => applier.ForcedTier = tier, true);

            ParamEntry scale = null, msaa = null;
            Action select = () =>
            {
                if (!writing && eyes != null && scale != null && msaa != null) eyes.Select(scale.Float, msaa.Int);
            };
            scale = registry.DeclareFloat(RenderScale, "render", 0.6f, 0.8f, 0.8f, v => select(), true);
            msaa = registry.DeclareInt(RenderMsaa, "render", 1, 4, 4, v => select(), true);

            if (rig != null) registry.DeclareFloat(Ipd, "render", StereoRig.MinIpd, StereoRig.MaxIpd, StereoRig.DefaultIpd, v => rig.Ipd = v);
            registry.DeclareFloat(LineWidth, "render", 0.5f, 3f, 1.5f, setLineWidth);
            registry.DeclareBool(Lens, "render", lensOn, setLens);
            if (lens != null)
            {
                registry.DeclareFloat(LensK1, "render", 0f, 1f, LensModel.DefaultK1, v => lens.K1 = v);
                registry.DeclareFloat(LensK2, "render", 0f, 1f, LensModel.DefaultK2, v => lens.K2 = v);
                registry.DeclareFloat(LensSeparation, "render", 0.055f, 0.075f, LensModel.DefaultLensSeparation, v => lens.LensSeparation = v);
                registry.DeclareFloat(ScreenToLens, "render", 0.025f, 0.060f, LensModel.DefaultScreenToLens, v => lens.ScreenToLens = v);
                registry.DeclareFloat(LensOffsetY, "render", -0.02f, 0.02f, LensModel.DefaultVerticalOffset, v => lens.VerticalOffset = v);
            }
            if (rig != null)
            {
                ParamEntry x = null, y = null, z = null;
                Action<float> offset = v =>
                {
                    if (x != null && y != null && z != null) rig.HeadOffset = new float3(x.Float, y.Float, z.Float);
                };
                x = registry.DeclareFloat(HeadOffsetX, "render", -0.10f, 0.10f, 0f, offset);
                y = registry.DeclareFloat(HeadOffsetY, "render", -0.10f, 0.10f, 0f, offset);
                z = registry.DeclareFloat(HeadOffsetZ, "render", -0.10f, 0.10f, 0f, offset);
            }

            // render.furniture: auto (the space's level, then the ladder), light, or full (overrides the ladder).
            registry.DeclareEnum(Furniture, "render", s_furnitureModes, (int)FurnitureMode.Auto, value =>
            {
                if (furniture != null) furniture.Mode = (FurnitureMode)value;
            });

            ParamEntry rho = registry.DeclareFloat(CullRho, "curved", 2f, 3.5f, 3f, v =>
            {
                kernel.CullRho = v;
                applier.CullRho = v;
            });
            ParamEntry copies = registry.DeclareInt(GluedCopies, "glued", 3, 7, 5, v => applier.GluedCopies = v, true);

            // The ladder writes the values of its rung back, so the Console shows what is in force. The eye
            // textures were already swapped by the applier: the two render callbacks stay out of it.
            ladder.TierChanged += (WorldTier tier) =>
            {
                writing = true;
                scale.Set(ladder.RenderScale);
                msaa.Set(ladder.Msaa);
                writing = false;
                var row = applier.Row;
                rho.Set(row.CullRho);
                copies.Set(row.GluedCopies);
            };
        }

        // ---- pose and replay ------------------------------------------------------------------------------

        /// <summary>
        /// pose.gyroPredict (the kill switch) and pose.gyroLeadMs together drive the pipeline's prediction;
        /// pose.source, replay.speed, replay.loop and pose.record go to the callers' own setters.
        /// </summary>
        public static void DeclarePose(ParamRegistry registry, IPosePipeline pipeline, Action<int> setSource,
            Action<float> setReplaySpeed, Action<bool> setReplayLoop, Action<bool> setRecord)
        {
            ParamEntry on = null, lead = null;
            Action apply = () =>
            {
                if (pipeline != null && on != null && lead != null) pipeline.GyroPredictMs = on.Bool ? lead.Float : 0f;
            };
            on = registry.DeclareBool(GyroPredict, "pose", false, v => apply());
            lead = registry.DeclareFloat(GyroLeadMs, "pose", 0f, 60f, 40f, v => apply());
            registry.DeclareEnum(PoseSource, "pose", s_poseSources, SourceTracked, setSource, true);
            registry.DeclareFloat(ReplaySpeed, "pose", 0f, 4f, 1f, setReplaySpeed);
            registry.DeclareBool(ReplayLoop, "pose", true, setReplayLoop);
            registry.DeclareBool(PoseRecord, "pose", false, setRecord);
        }

        // ---- scan, input ----------------------------------------------------------------------------------

        /// <summary>
        /// scan.wallBridge (standalone-phone plan D13): RoomPlan's walls, doors and windows from the scan's one RoomPlan
        /// session are merged into the confirmed room (route 4). On by default. Off is the outline's walls as they were;
        /// the RoomPlan session still runs for the furniture boxes.
        /// </summary>
        public const string ScanWallBridge = "scan.wallBridge";

        public static void DeclareScan(ParamRegistry registry, ScanSettings settings)
        {
            registry.DeclareInt(ScanRoute, "scan", 1, 4, 3, v => settings.Route = v);
            registry.DeclareBool(TeamPrescan, "scan", false, v => settings.TeamPrescan = v);
            registry.DeclareBool(ScanWallBridge, "scan", true, Atlas.Scan.WallBridge.Apply);
        }

        public static void DeclareInput(ParamRegistry registry, IAtlasInput input)
        {
            registry.DeclareFloat(Dwell, "input", 0.6f, 0.8f, 0.7f, v => input.DwellSeconds = v);
        }

        // ---- kill switches with no owner in this build ------------------------------------------------------

        /// <summary>
        /// The kill switches of section 12.4 whose owners (the curved spaces, S5, S6, the asset client) are
        /// not in this build. Declared with the kinds of lane D's table, so an owner that declares the same
        /// name later takes the entry over with its value.
        /// </summary>
        public static void DeclareUnownedKillSwitches(ParamRegistry registry)
        {
            if (registry.Find(CurvedMode) == null) registry.DeclareEnum(CurvedMode, "curved", s_curvedModes, 0, null, true);
            if (registry.Find(PortalDepth) == null) registry.DeclareInt(PortalDepth, "portal", 0, 1, 1);
            if (registry.Find(SlicerSource) == null) registry.DeclareEnum(SlicerSource, "slicer", s_slicerSources, 0, null, true);
            if (registry.Find(ForgeBackend) == null) registry.DeclareEnum(ForgeBackend, "forge", s_forgeBackends, 3);
        }

        // ---- the Editor-first path --------------------------------------------------------------------------

        /// <summary>The names of dev.room: "none" (wait for a scan), then the fixtures.</summary>
        public static string[] RoomChoices()
        {
            string[] fixtures = RoomFixtures.All;
            var names = new string[fixtures.Length + 1];
            names[0] = "none";
            Array.Copy(fixtures, 0, names, 1, fixtures.Length);
            return names;
        }

        /// <summary>dev.room picks a fixture RoomModel with no scan; dev.overlay shows the small overlay.</summary>
        public static void DeclareDev(ParamRegistry registry, int roomDefault, Action<int> setRoom, bool overlayDefault,
            Action<bool> setOverlay)
        {
            registry.DeclareEnum(DevRoom, "dev", RoomChoices(), roomDefault, setRoom, true);
            registry.DeclareBool(DevOverlay, "dev", overlayDefault, setOverlay);
        }
    }
}
