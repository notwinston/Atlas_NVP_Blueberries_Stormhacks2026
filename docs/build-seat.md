# The build seat

One Mac builds to the phones (Master Plan §9.3, Appendix C.8). These rules keep it from becoming the queue
everyone waits in.

## One command

```
tools/build_ios.sh                # build the working tree and install it on every connected phone
tools/build_ios.sh main           # fetch, switch to main, fast-forward, build, install
ATLAS_DEV=1 tools/build_ios.sh    # development build: Metal HUD on
```

First build on a new clone: run `tools/setup_git.sh` once, close the project in the Unity Editor, and pass the
signing owner's team id once: `ATLAS_TEAM_ID=XXXXXXXXXX tools/build_ios.sh`. The script exports the Xcode project
with Unity in batch mode (append mode, so Xcode rebuilds incrementally), builds it with `xcodebuild
-allowProvisioningUpdates`, and installs the app with `xcrun devicectl`. Logs are in `ATLAS/Build/`.

If the script cannot install: open `ATLAS/Build/iOS/Unity-iPhone.xcodeproj` in Xcode and press Run.

## Rules

1. Everyone runs the Unity Editor on their own machine. Prove it in the Editor or in a unit test first
   (`tools/headless/verify.py all`); the phone is for acceptance.
2. One integration build at every gate. Between gates, device builds are short slots taken from the queue below.
3. One bundle id (`com.winstonthov.atlas`) and one signing account. A build signed by a different account cannot
   install over the existing one.
4. Published numbers come from a release build, debugger detached, Metal API Validation off, phone A in the
   closed headset. Note the thermal state at the start of every measurement.
5. Hand-held before head-mounted: scan, link and input are debugged with the phone in the hand.
6. A soak leaves a phone hot. Soaks are booked; they are never squeezed between someone else's measurements.
7. Personal Team builds stop launching 7 days after install. Any reinstall renews them.

## Phones

| Phone | Role |
|---|---|
| A · iPhone 15 Pro Max (USB-C) | Reference device for every published number |
| B · iPhone 14 Pro Max (Lightning) | Lane C's working device, the hot-swap spare, the second walker in Phase 3 |

## Queue

Add a line before you take the seat; fill in the result when you leave it. Newest at the bottom.

| When | Who | Branch @ commit | What is tested | Phone | Result |
|---|---|---|---|---|---|
| 2026-10-04 03:46 | Lucas | ui/squircle-look @ e70df96 | Squircle menus, signs, cards and onboarding card (release build) | Batphone | Built and installed; not yet looked at on the phone |
| 2026-10-04 04:16 | Lucas | ui/polish @ ea710d0 | Hexagon flip transition between spaces, Apply step on the Spaces menu (release build) | Batphone | Built and installed; not yet looked at on the phone |
| 2026-10-04 04:30 | Lucas | ui/polish @ 550db23 | Hexagon ripple from the gaze tile, dome over bump warnings, menu opens in place (release build) | Batphone | Built and installed; not yet looked at on the phone |
