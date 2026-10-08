using System.Globalization;
using System.Text;
using Atlas.Scan;
using Atlas.Telemetry;
using Atlas.World;

namespace Atlas.App.Onboarding
{
    /// <summary>One button of an onboarding screen.</summary>
    public struct OnboardingButton
    {
        public string Label;
        public OnboardingAction Action;
        /// <summary>The way on: drawn light on dark, the others dark on light.</summary>
        public bool Primary;
    }

    /// <summary>What one onboarding screen says: filled by OnboardingScreens, drawn by the view.</summary>
    public sealed class OnboardingScreen
    {
        public const int MaxButtons = 6;

        public string Title = "";
        public string Body = "";
        /// <summary>What went wrong or needs attention; empty when nothing does.</summary>
        public string Notice = "";
        public readonly OnboardingButton[] Buttons = new OnboardingButton[MaxButtons];
        public int ButtonCount;
        /// <summary>The Station address may be typed on this screen.</summary>
        public bool AddressField;
        /// <summary>Nothing is shown at all: the walker is in the headset.</summary>
        public bool Hidden;

        internal void Clear()
        {
            Title = "";
            Body = "";
            Notice = "";
            ButtonCount = 0;
            AddressField = false;
            Hidden = false;
        }

        internal void Add(string label, OnboardingAction action, bool primary = false)
        {
            if (ButtonCount >= MaxButtons) return;
            Buttons[ButtonCount++] = new OnboardingButton { Label = label, Action = action, Primary = primary };
        }

        /// <summary>True when the screen offers the action.</summary>
        public bool Offers(OnboardingAction action)
        {
            for (int i = 0; i < ButtonCount; i++)
                if (Buttons[i].Action == action) return true;
            return false;
        }

        /// <summary>The action of the button drawn as the way on; None when there is none.</summary>
        public OnboardingAction PrimaryAction
        {
            get
            {
                for (int i = 0; i < ButtonCount; i++)
                    if (Buttons[i].Primary) return Buttons[i].Action;
                return OnboardingAction.None;
            }
        }
    }

    /// <summary>
    /// The words and buttons of every onboarding step (Master Plan task P1-C12). Engine-free, so the texts a
    /// walker reads are covered by headless tests. Plain ASCII only: the built-in font is the one font that
    /// is certain to be on the phone.
    /// </summary>
    public static class OnboardingScreens
    {
        static readonly string[] StepNames =
        {
            "idle", "welcome", "camera", "station", "room", "relocalise", "sweep", "confirm", "build", "headset", "home",
        };

        /// <summary>The step's name in the event log.</summary>
        public static string StepName(OnboardingStep step)
        {
            int index = (int)step;
            return index >= 0 && index < StepNames.Length ? StepNames[index] : "unknown";
        }

        /// <summary>Fills screen with what the flow's step shows now. text is scratch space.</summary>
        public static void Fill(OnboardingFlow flow, OnboardingScreen screen, StringBuilder text)
        {
            screen.Clear();
            text.Length = 0;
            screen.Notice = NoticeText(flow.Notice);
            switch (flow.Step)
            {
                case OnboardingStep.Welcome: Welcome(flow, screen); break;
                case OnboardingStep.Camera: Camera(flow, screen); break;
                case OnboardingStep.Station: Station(flow, screen, text); break;
                case OnboardingStep.Room: Room(flow, screen); break;
                case OnboardingStep.Relocalise: Relocalise(flow, screen, text); break;
                case OnboardingStep.Sweep: Sweep(flow, screen, text); break;
                case OnboardingStep.Confirm: Confirm(flow, screen, text); break;
                case OnboardingStep.Build:
                    screen.Title = "Building your room";
                    screen.Body = "One moment.";
                    break;
                case OnboardingStep.Headset: Headset(flow, screen, text); break;
                default:
                    screen.Hidden = true;
                    break;
            }
        }

        static void Welcome(OnboardingFlow flow, OnboardingScreen screen)
        {
            screen.Title = "Welcome to ATLAS";
            screen.Body =
                "You scan this room with the phone in your hand, then walk it in the headset.\n\n" +
                "Before you start:\n" +
                "- Clear the floor: bags, cables, chairs.\n" +
                "- One level floor: no steps, no ramps.\n" +
                "- Not on the team? A spotter stays with you while you walk.";
            screen.Add("Start", OnboardingAction.Start, true);
            if (flow.SamplesAllowed) screen.Add("Sample room (no scan)", OnboardingAction.UseSampleRoom);
        }

        static void Camera(OnboardingFlow flow, OnboardingScreen screen)
        {
            screen.Title = "Camera";
            if (flow.Notice == OnboardingNotice.CameraDenied)
            {
                screen.Body = "Without the camera the phone cannot track you or find the floor.";
                screen.Add("Open Settings", OnboardingAction.OpenSettings, true);
                screen.Add("Try again", OnboardingAction.RetryCamera);
            }
            else
            {
                screen.Body =
                    "ATLAS uses the camera to track where you are and to find the floor. " +
                    "Nothing the camera sees is stored.\n\nAllow the camera when the phone asks.";
            }
            screen.Add("Back", OnboardingAction.Back);
            if (flow.SamplesAllowed) screen.Add("Sample room (no scan)", OnboardingAction.UseSampleRoom);
        }

        static void Station(OnboardingFlow flow, OnboardingScreen screen, StringBuilder text)
        {
            IStationPairing station = flow.Station;
            screen.Title = "Station";
            text.Append("Station address: ").Append(station.Address).Append("\n\n");
            if (station.Connected)
            {
                text.Append("Station connected.");
            }
            else
            {
                PairingPhase phase = station.Phase;
                if (phase == PairingPhase.TimedOut) text.Append("The Station did not answer.");
                else text.Append("Connecting... If the phone asks to find devices on the local network, allow it.");
                screen.Add("Continue without Station", OnboardingAction.ContinueWithoutStation, true);
                if (phase == PairingPhase.TimedOut) screen.Add("Try again", OnboardingAction.RetryStation);
                // "Change IP": the typed fallback of the QR code (lane D contract, section 5.6).
                screen.AddressField = true;
                screen.Add("Use this address", OnboardingAction.UseAddress);
            }
            screen.Body = text.ToString();
        }

        static void Room(OnboardingFlow flow, OnboardingScreen screen)
        {
            bool saved = flow.Rooms.HasSavedRoom;
            bool failed = flow.Notice != OnboardingNotice.None && flow.Notice != OnboardingNotice.BuildFailed;
            screen.Title = "Your room";
            screen.Body = saved
                ? "A saved scan was found. If you are standing in that room, use it. Otherwise scan this room."
                : "Scan this room, or outline its free floor by hand.";
            if (saved) screen.Add("Use the saved room", OnboardingAction.UseSavedRoom, !failed);
            screen.Add("Scan this room", OnboardingAction.ScanAutomatic, !saved && !failed);
            // After a scan that failed, the outline by hand is the way that always works.
            screen.Add("Outline by hand", OnboardingAction.ScanManual, failed);
            if (flow.Room != null) screen.Add("Back", OnboardingAction.Back);
            if (flow.SamplesAllowed) screen.Add("Sample room (no scan)", OnboardingAction.UseSampleRoom);
        }

        static void Relocalise(OnboardingFlow flow, OnboardingScreen screen, StringBuilder text)
        {
            screen.Title = "Finding the room";
            text.Append("Look around slowly so the phone recognises the room.\n\n");
            Number(text, flow.StepSeconds, "0").Append(" s");
            screen.Body = text.ToString();
            screen.Add("Cancel", OnboardingAction.Back);
        }

        static void Sweep(OnboardingFlow flow, OnboardingScreen screen, StringBuilder text)
        {
            IOnboardingScan scan = flow.Scan;
            ScanProgress progress = scan.Progress;
            if (scan.State == ScanState.Processing)
            {
                screen.Title = "Scan the room";
                screen.Body = "Working out the free floor...";
                return;
            }

            if (flow.CornerMode)
            {
                screen.Title = "Outline the room";
                text.Append("Stand in each corner of the free floor in turn and press Add corner here, " +
                            "or tap the corner on the floor. Leave glass doors and mirrors outside.\n\n");
                text.Append("Corners: ").Append(progress.CornerCount.ToString(CultureInfo.InvariantCulture));
                text.Append(progress.CornerCount < 3 ? " (three or more)\n" : "\n");
            }
            else
            {
                screen.Title = "Scan the room";
                text.Append("Sweep the phone slowly over the floor, the walls and the furniture. About ");
                Number(text, OnboardingFlow.SweepBudget, "0").Append(" seconds.\n\n");
                string hint = HintText(progress.Hint);
                if (hint.Length > 0) text.Append(hint).Append("\n\n");
            }

            if (progress.FloorFound)
            {
                text.Append("Floor: found, ");
                Number(text, progress.FloorArea, "0.0").Append(" m2\n");
            }
            else
            {
                text.Append("Floor: not found yet\n");
            }
            if (!flow.CornerMode || progress.BoxCount > 0)
                text.Append("Objects: ").Append(progress.BoxCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (progress.PreviewVersion > 0)
            {
                text.Append("Not seen yet: ");
                Number(text, 100f * progress.UnknownShare, "0").Append(" %\n");
            }
            text.Append("Time: ");
            Number(text, progress.Seconds, "0").Append(" s");
            if (progress.Tracking != TrackingState.Normal && scan.State == ScanState.Sweeping)
                text.Append("\nTracking is weak: move slowly, point at something with detail.");
            screen.Body = text.ToString();

            screen.Add("Done", OnboardingAction.DoneSweeping, true);
            if (flow.CornerMode)
            {
                screen.Add("Add corner here", OnboardingAction.AddCorner);
                screen.Add("Undo corner", OnboardingAction.UndoCorner);
            }
            else
            {
                screen.Add("Outline by hand", OnboardingAction.ScanManual);
            }
            screen.Add("Back", OnboardingAction.Back);
        }

        static void Confirm(OnboardingFlow flow, OnboardingScreen screen, StringBuilder text)
        {
            RoomModel room = flow.Scan.Proposal;
            screen.Title = "Is this your free floor?";
            if (flow.Adjusting) text.Append("Drag a corner of the outline to where it belongs.\n\n");
            else text.Append("The outline on the floor is where you will walk. Nothing may stand inside it.\n\n");
            if (room != null) RoomFacts(text, room);
            else text.Append("Working out the free floor...");
            screen.Body = text.ToString();

            screen.Add("Looks right", OnboardingAction.Accept, true);
            screen.Add(flow.Adjusting ? "Done adjusting" : "Adjust", OnboardingAction.Adjust);
            screen.Add("Scan again", OnboardingAction.Back);
        }

        static void Headset(OnboardingFlow flow, OnboardingScreen screen, StringBuilder text)
        {
            screen.Title = "Put the phone in the headset";
            if (flow.Notice == OnboardingNotice.HeadsetRefused)
            {
                screen.Body = "";
            }
            else
            {
                text.Append("Slide the phone in with the screen towards the lenses and put the headset on.\n\n");
                text.Append("The view switches in ").Append(flow.CountdownSeconds.ToString(CultureInfo.InvariantCulture));
                text.Append(" s. Press the lever to switch now.\n\n");
                if (flow.Room != null) RoomFacts(text, flow.Room);
                screen.Body = text.ToString();
                screen.Add("Switch now", OnboardingAction.HeadsetNow, true);
            }
            screen.Add("Scan again", OnboardingAction.Back);
        }

        // The room's numbers, and whether standing mode will be used (lane A contract, section 4.4).
        static void RoomFacts(StringBuilder text, RoomModel room)
        {
            text.Append("Free floor: ");
            Number(text, room.FreeRect.Wf, "0.0").Append(" x ");
            Number(text, room.FreeRect.Df, "0.0").Append(" m\n");
            if (room.Objects != null && room.Objects.Length > 0)
                text.Append("Objects: ").Append(room.Objects.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (room.Standing)
            {
                text.Append("This floor is small, so you will use standing mode: stay on the spot and turn; do not walk.");
            }
            else
            {
                text.Append("You will walk a square of ");
                Number(text, room.LapSide, "0.0").Append(" m.");
            }
        }

        static StringBuilder Number(StringBuilder text, float value, string format)
        {
            return text.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }

        static string HintText(ScanHint hint)
        {
            switch (hint)
            {
                case ScanHint.PointAtFloor: return "Point the phone at the floor.";
                case ScanHint.KeepSweeping: return "Keep sweeping: the floor is filling in.";
                case ScanHint.TapCorners: return "Tap the corners of the free floor.";
                case ScanHint.SlowDown: return "Slow down.";
                case ScanHint.TurnOnLight: return "It is too dark: switch on a light.";
                case ScanHint.MoveCloseToWall: return "Move closer to the wall.";
                case ScanHint.MoveAwayFromWall: return "Step back from the wall.";
                default: return "";
            }
        }

        /// <summary>The words of a notice; empty for None.</summary>
        public static string NoticeText(OnboardingNotice notice)
        {
            switch (notice)
            {
                case OnboardingNotice.CameraDenied:
                    return "The camera is switched off for ATLAS. Open Settings > ATLAS > Camera, switch it on and come back.";
                case OnboardingNotice.StationHint:
                    return "No answer yet. Check Settings > Privacy & Security > Local Network > ATLAS, " +
                           "and that the phone is on the Station's Wi-Fi.";
                case OnboardingNotice.StationTimedOut:
                    return "No Station found. You can walk without it: nobody watches, and furniture stays as boxes.";
                case OnboardingNotice.AddressRefused:
                    return "That is not an address. Type it like 192.168.0.10 or 192.168.0.10:9050.";
                case OnboardingNotice.RoomNotRecognised:
                    return "The saved room was not recognised here, so this room is scanned anew.";
                case OnboardingNotice.NoFloor:
                    return "No floor was found. Outline the free floor by hand instead.";
                case OnboardingNotice.TooSmall:
                    return "The free floor is too small to stand in. Clear more floor, or outline it by hand.";
                case OnboardingNotice.TrackingLost:
                    return "The phone lost track of the room. Try again more slowly, or outline the floor by hand.";
                case OnboardingNotice.OutlineInvalid:
                    return "That outline crosses itself or has a corner too close to the next. It was not changed.";
                case OnboardingNotice.NeedFloor:
                    return "Not yet: point the phone at the floor until it is found, or add three corners.";
                case OnboardingNotice.BuildFailed:
                    return "The room could not be built. Scan it again.";
                case OnboardingNotice.HeadsetRefused:
                    return "Bump Guard is missing in this build, so the headset view is refused. Tell the team.";
                case OnboardingNotice.NoSampleRoom:
                    return "The sample room is not in this build.";
                default:
                    return "";
            }
        }
    }
}
