using Atlas.Link;
using Atlas.Rendering;
using Atlas.Scan;
using Atlas.World;
using UnityEngine;

namespace Atlas.App
{
    /// <summary>
    /// A small development overlay for rehearsing the flow with no onboarding and no Station (contract README
    /// F9): pick a fixture room, run a scan, go to the headset view, switch spaces, change the pose source.
    /// Shown by dev.overlay. It draws with immediate-mode GUI, which allocates, so the component is disabled
    /// while hidden and hides itself on a phone in the headset. On a phone with no Bump Guard it shows the
    /// "Bump Guard missing" line whatever dev.overlay is.
    /// </summary>
    public sealed class DevOverlay : MonoBehaviour
    {
        AtlasApp _app;
        string[] _rooms;
        bool _wanted;
        bool _warningOnly;

        public static DevOverlay Create(Transform parent, AtlasApp app)
        {
            var host = new GameObject("DevOverlay");
            host.transform.SetParent(parent, false);
            var overlay = host.AddComponent<DevOverlay>();
            overlay._app = app;
            overlay._rooms = AppParams.RoomChoices();
            overlay.Refresh();
            return overlay;
        }

        /// <summary>dev.overlay.</summary>
        public bool Visible
        {
            get { return _wanted; }
            set
            {
                _wanted = value;
                Refresh();
            }
        }

        /// <summary>Called when the display mode changes: no overlay over the lens view on a phone.</summary>
        public void Refresh()
        {
            bool device = _app != null && _app.Boot.IsDevice;
            bool headset = _app != null && _app.Rig != null && _app.Rig.Mode == DisplayMode.Headset;
            _warningOnly = !_wanted && _app != null && HeadsetGate.MustWarn(device, _app.GuardInstalled);
            enabled = HeadsetGate.OverlayDrawn(_wanted, device, headset, _app == null || _app.GuardInstalled);
        }

        /// <summary>
        /// True when the overlay is drawn and the point, in screen pixels from the bottom left, is inside its
        /// box: a press there belongs to the overlay's buttons and is not the headset lever.
        /// </summary>
        public bool Covers(float screenX, float screenY)
        {
            return enabled && DevOverlayLayout.Contains(screenX, screenY, Screen.height, _warningOnly);
        }

        void OnGUI()
        {
            if (_app == null) return;
            // One layout for every screen: 540 units tall.
            float scale = Screen.height / DevOverlayLayout.ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            if (_warningOnly)
            {
                // A phone with no Bump Guard says so whatever dev.overlay is, release builds included
                // (lane A contract, section 7.2).
                GUILayout.BeginArea(new Rect(DevOverlayLayout.Left, DevOverlayLayout.Top, DevOverlayLayout.Width,
                    DevOverlayLayout.WarningHeight), GUI.skin.box);
                GUILayout.Label(HeadsetGate.Message);
                GUILayout.Label("The headset view is refused.");
                GUILayout.EndArea();
                return;
            }
            GUILayout.BeginArea(new Rect(DevOverlayLayout.Left, DevOverlayLayout.Top, DevOverlayLayout.Width,
                DevOverlayLayout.Height), GUI.skin.box);

            ISpaceHost host = _app.Spaces;
            RehearsalFlow flow = _app.Flow;
            ScanSession session = _app.ScanSystems.Session;
            GUILayout.Label("ATLAS  " + (host.HasSpace ? SpaceIds.DisplayName(host.Current) : "no space") + "  " + host.State);
            if (!_app.GuardInstalled) GUILayout.Label(HeadsetGate.Message);
            if (flow.LastError != null) GUILayout.Label(flow.LastError);

            GUILayout.Label("Room: " + (host.Room != null ? host.Room.Id : "none"));
            for (int i = 1; i < _rooms.Length; i++)
                if (GUILayout.Button(_rooms[i])) SetParam(AppParams.DevRoom, i, flow);

            if (flow.OwnsFlow)
            {
                GUILayout.Label("Scan: " + session.State);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Scan")) flow.BeginScan();
                if (GUILayout.Button("End sweep")) session.EndSweep();
                if (GUILayout.Button("Confirm")) session.Confirm();
                GUILayout.EndHorizontal();
                if (GUILayout.Button(flow.InHeadset ? "Back in the hand" : "Headset on"))
                {
                    if (flow.InHeadset) flow.LeaveHeadset();
                    else flow.EnterHeadset();
                    Refresh();
                }
            }

            GUILayout.Label("Space");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < SpaceIds.Count; i++)
            {
                var id = (SpaceId)i;
                if (host.IsAvailable(id) && GUILayout.Button(SpaceIds.Code(id))) host.RequestSwitch(id);
            }
            if (GUILayout.Button("Home")) host.ReturnHome();
            GUILayout.EndHorizontal();

            GUILayout.Label("Head: " + PoseSourceSwitch.SourceName(_app.SourceSwitch.Source));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("tracked")) _app.Registry.Set(AppParams.PoseSource, AppParams.SourceTracked);
            if (GUILayout.Button("replay")) _app.Registry.Set(AppParams.PoseSource, AppParams.SourceReplay);
            if (GUILayout.Button("walker")) _app.Registry.Set(AppParams.PoseSource, AppParams.SourceWalker);
            GUILayout.EndHorizontal();
            if (_app.SourceSwitch.LastError != null) GUILayout.Label(_app.SourceSwitch.LastError);

            GUILayout.EndArea();
        }

        // Choosing the room that is already chosen still loads it again.
        void SetParam(string name, int value, RehearsalFlow flow)
        {
            ParamRegistry registry = _app.Registry;
            if (!registry.Set(name, value)) flow.SetRoomChoice(value);
        }
    }
}
