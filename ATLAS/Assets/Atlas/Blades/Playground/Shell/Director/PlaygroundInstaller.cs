using System;
using Atlas.Blades.Playground.Exhibits;
using Atlas.Input;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.Spaces.Portal;
using Atlas.Spaces.Slicer;
using Atlas.World;
using UnityEngine;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// The Unity side of the Playground's composition root (task P2-C01): builds the <see cref="PlaygroundShell"/>
    /// from the parts the app and the integrator hand in, joins it to the frame loop, binds the two exhibits to the
    /// spaces they wrap, and draws the chips, the card and the menu under the HUD. It adds no scene: the app calls
    /// <see cref="Install"/> once, after lane B's ports exist. The director and every shell object are in the engine-free
    /// shell; this class only carries engine objects to them. Main thread only.
    /// </summary>
    public sealed class PlaygroundInstaller : MonoBehaviour, IAfterFrameStep
    {
        [SerializeField] PlaygroundBeatTable m_Beats;
        [SerializeField] RoomsExhibit m_Rooms;
        // The Glued exhibit and lane A's Slicer toybox (P2-A01) are engine-free and need no scene object: each follows its
        // space once entered, where the steps, the card and the buttons live.
        readonly GluedExhibit m_Glued = new GluedExhibit();
        readonly ToyboxExhibit m_Toybox = new ToyboxExhibit();

        PlaygroundShell m_Shell;
        ISpaceHost m_Host;
        IFrameLoop m_Loop;
        ParamRegistry m_Params;
        ActionMapDriver m_Driver;
        PgGazeTargets m_Gaze;
        CaptionChipView m_CaptionView;
        EquationCardView m_CardView;
        PromptChipView m_PromptView;
        ExhibitMenuView m_MenuView;
        MaterialPool m_Pool;
        Transform m_RoomRoot;
        RoomModel m_PlacedRoom;

        public PlaygroundShell Shell { get { return m_Shell; } }

        /// <summary>
        /// Builds the shell and starts listening. <paramref name="hud"/> and <paramref name="pool"/> may be null (no
        /// views). The run starts with <see cref="PlaygroundShell.Begin"/>, which the app calls when the walker
        /// enters the Playground.
        /// </summary>
        public PlaygroundShell Install(PlaygroundParts parts, IFrameLoop loop, Transform hud = null, MaterialPool pool = null)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            if (loop == null) throw new ArgumentNullException(nameof(loop));
            if (m_Shell != null) throw new InvalidOperationException("PlaygroundInstaller: Install runs once.");
            if (parts.BeatNames == null && m_Beats != null && m_Beats.IsComplete()) parts.BeatNames = m_Beats.Names();

            m_Shell = new PlaygroundShell(parts);
            m_Host = parts.Host;
            m_Loop = loop;
            m_Params = parts.Params;
            m_Pool = pool;
            m_RoomRoot = parts.Content.Root;

            m_Shell.Exhibits.Register(ExhibitId.Glued, new GluedExhibitLink(m_Glued));
            if (m_Rooms != null) m_Shell.Exhibits.Register(ExhibitId.ImpossibleRooms, new RoomsExhibitLink(m_Rooms));
            m_Shell.Exhibits.Register(ExhibitId.Slicer, new ToyboxExhibitLink(m_Toybox));
            EquationSources.Register(new SlicerEquationSource(m_Toybox));
            EquationSources.Register(new GluedEquationSource(m_Glued));

            if (parts.Input != null)
            {
                m_Driver = new ActionMapDriver(parts.Input, m_Shell.Map);
                m_Gaze = new PgGazeTargets(parts.Input);
            }
            if (hud != null)
            {
                m_CaptionView = CaptionChipView.Create(m_Shell.Caption, hud, pool, parts.Input);
                m_CardView = EquationCardView.Create(m_Shell.Card, hud, pool);
                m_PromptView = PromptChipView.Create(m_Shell.Chip, hud, pool, parts.Input);
            }

            m_Host.SpaceEntered += OnSpaceEntered;
            m_Host.SpaceExited += OnSpaceExited;
            m_Host.RoomChanged += OnRoomChanged;
            m_Loop.AddAfterFrame(this);
            if (m_Host.Room != null) PlaceMenu(m_Host.Room);
            return m_Shell;
        }

        /// <summary>Step 8b: the shell, the exhibits and the views, in that order.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            if (m_Shell == null) return;
            if (m_Driver != null) m_Driver.Tick(frame.DeltaTime);
            m_Shell.Update(new PlaygroundFrame
            {
                Time = frame.Time,
                DeltaTime = frame.DeltaTime,
                HeadPosition = frame.HeadPosition,
                HeadRotation = frame.HeadRotation,
                Tracking = frame.Tracking,
                Walker = frame.Walker
            });
            if (m_Host.HasSpace && m_Host.Current == SpaceId.Portal && m_Rooms != null) m_Rooms.Tick();
            if (m_CaptionView != null) m_CaptionView.Update(frame.DeltaTime);
            if (m_CardView != null) m_CardView.Update(frame.DeltaTime);
            if (m_PromptView != null) m_PromptView.Update();
            if (m_MenuView != null) m_MenuView.Update();
        }

        // An exhibit is bound to its space once the space is entered (the exhibit needs the live module), and the
        // shell is told, so the exhibit's first line reaches the prompt chip.
        void OnSpaceEntered(SpaceId id)
        {
            if (id == SpaceId.Glued)
            {
                GluedSpace space = m_Host.Module as GluedSpace;
                if (space != null) m_Glued.Bind(space);
                m_Shell.OnExhibitReady();
            }
            else if (id == SpaceId.Portal && m_Rooms != null && m_Host.Room != null)
            {
                PortalSpace space = m_Host.Module as PortalSpace;
                if (space != null) m_Rooms.Bind(space, m_Host.Room, m_Params);
                m_Shell.OnExhibitReady();
            }
            else if (id == SpaceId.Slicer)
            {
                SlicerSpace space = m_Host.Module as SlicerSpace;
                if (space != null) m_Toybox.Bind(space);
                m_Shell.OnExhibitReady();
            }
        }

        void OnSpaceExited(SpaceId id)
        {
            if (id == SpaceId.Glued) m_Glued.Unbind();
            else if (id == SpaceId.Portal && m_Rooms != null) m_Rooms.Unbind();
            else if (id == SpaceId.Slicer) m_Toybox.Unbind();
        }

        void OnRoomChanged(RoomModel room)
        {
            if (room != null) PlaceMenu(room);
        }

        void PlaceMenu(RoomModel room)
        {
            if (ReferenceEquals(room, m_PlacedRoom)) return;
            m_PlacedRoom = room;
            m_Shell.Menu.PlaceSigns(room, m_Gaze);
            if (m_MenuView != null) m_MenuView.Dispose();
            m_MenuView = m_RoomRoot != null ? ExhibitMenuView.Create(m_Shell.Menu, room, m_RoomRoot, m_Pool) : null;
        }

        void OnDestroy()
        {
            if (m_Host != null)
            {
                m_Host.SpaceEntered -= OnSpaceEntered;
                m_Host.SpaceExited -= OnSpaceExited;
                m_Host.RoomChanged -= OnRoomChanged;
            }
            if (m_Driver != null) m_Driver.Dispose();
            if (m_Gaze != null) m_Gaze.Dispose();
            if (m_CaptionView != null) m_CaptionView.Dispose();
            if (m_CardView != null) m_CardView.Dispose();
            if (m_PromptView != null) m_PromptView.Dispose();
            if (m_MenuView != null) m_MenuView.Dispose();
            if (m_Shell != null) m_Shell.Dispose();
        }
    }
}
