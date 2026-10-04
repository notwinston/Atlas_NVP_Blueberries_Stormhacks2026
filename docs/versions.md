# Pinned versions

The numbers actually installed, per Master Plan Appendix C.2. "Verified" means confirmed on the machine named.

## Build seat (MacBook Pro M3 Max, macOS 26.1), verified 3 Oct 2026

| Component | Installed | Plan pin | Status |
|---|---|---|---|
| Unity | 6000.3.25f1 (e1dba0a9aba4), iOS Build Support | 6000.3 LTS, ≥ 6000.3.21f1 | Verified |
| Xcode | 26.1 (17B55), iOS SDK 26.1 | 26 | Verified |
| URP | 17.3.0 | 17.x as bundled | Verified |
| AR Foundation | 6.5.0 | 6.5.x | Verified |
| ARKit XR Plugin | 6.5.0 | 6.5.x, same as AR Foundation | Verified |
| XR Plug-in Management | 4.6.1 | as resolved | Verified |
| iOS deployment target | 17.0 | 17.0 | Verified in the exported Xcode project |
| LiteNetLib | 2.1.4 (OpenUPM `com.revenantx.litenetlib`) | 2.1.4 | Compiles in the Editor; IL2CPP on device not yet verified |
| glTFast | 6.20.0 | `com.unity.cloud.gltfast` | Verified |
| Burst | 1.8.30 | as resolved for 6000.3 | Verified |
| Collections | 2.6.8 | as resolved for 6000.3 | Verified |
| Mathematics | 1.3.3 | as resolved for 6000.3 | Verified |
| Input System | 1.20.0 | as resolved for 6000.3 | Verified |
| Test Framework | 1.6.0 | as resolved for 6000.3 | Verified |
| ProBuilder | 6.1.2 | add | Verified |
| Profiling Core | 1.0.3 | keep | Verified |
| Git LFS | 3.8.0 | required | Verified |

Came with the AR Mobile template and not in the plan's keep/drop table: XR Interaction Toolkit 3.5.1, `com.unity.learn.iet-framework` 6.0.2, Android Logcat 1.4.7.
Added by the team outside the plan's table: MCP for Unity (`com.coplaydev.unity-mcp`, Git `#main`, not pinned to a commit).

## Not yet verified

- iOS version on phone A and phone B, and the tracking drift test (C.2).
- apple/unityplugins Core + CoreHaptics: not added yet (built from source, C.3).
- Station PC: .NET SDK 8, Python 3.11, PyTorch ≥ 2.7 cu128, TripoSG commit, Forge lock file.
- Unity Editor at the same patch on the other three machines.
