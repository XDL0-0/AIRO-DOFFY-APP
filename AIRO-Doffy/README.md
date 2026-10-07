# AIRO Doffy

The current AIRO Doffy Quest VR teleoperation project, corresponding to the v0.9.7 / code 18 implementation. The matching PC runtime is [doffy-teleop](https://github.com/XDL0-0/AIRO-Doffy).

This folder contains `Assets/`, `Packages/` and `ProjectSettings/`, including the scripts, scene, prefabs, materials, textures, fonts, shaders, optional robot models and original Unity `.meta` files. Package versions are locked for restoration through Unity Package Manager. APKs, build outputs, SDK caches and local credentials are excluded.

## Open the project

1. Clone this repository and add the **AIRO-Doffy** folder to Unity Hub as its own project. The repository root contains the historical v0.6.0 project; keep the two projects separate because some scripts retain the same GUIDs.
2. Use Unity **6000.5.6f1**. For Quest builds, install Android Build Support with the SDK, NDK and OpenJDK through Unity Hub.
3. Allow Package Manager to restore the versions in `Packages/manifest.json` and `Packages/packages-lock.json`: Meta XR All-in-One **205.0.0**, Unity WebRTC **3.0.0**, OpenXR **1.18.0**, Newtonsoft JSON **3.2.2**, and their dependencies. Meta Audio **85.0.0** and Voice **85.0.1** are the versions selected by Meta 205.
4. Open `Assets/Scenes/Teleoperation.unity` and run `Tools → DOFFY → Validate scene`. The wrist/bracelet workspace is generated when entering Play.

## Code and operation

Custom code is under `Assets/Teleop`: Core, Input, Calibration, UpperLimb, Feedback, Networking, Protocol, Media, UI, Visualization, Diagnostics, Editor, Tests and Legacy.

In Session, set and Apply the PC's IPv4 address, choose Controllers or Hands, then Start session. Cameras supports UDP or WebRTC. Alignment provides reference calibration and robot-base placement. WRM upper-limb calibration requires Controllers mode. BODY telemetry defaults to **OFF** on each launch; enable it in Session to send BODY v1 over UDP port **8015**. Viewing BODY telemetry does not require Start session.

The scene's configuration and runtime resources, including `TeleopConfigUpperLimb.asset`, `WristUISettings.asset` and `BraceletBody.shader`, are included. Optional robot model assets are retained for the original visualization modules. Unused Python preprocessing scripts and Python bytecode are excluded.

## Build

The project targets Android **ARM64** with **IL2CPP**. The build menus write inside this project, using portable paths:

| Menu under Tools → DOFFY | Output | Package / version code |
| --- | --- | --- |
| Build Quest APK | `Builds/AIRO_Doffy.apk` | `org.airolab.doffy.bracelet` / 16 |
| Build Meta update APK | `Builds/meta-update-v0.9.7/AIRO_Doffy_meta_v0.9.7.apk` | `com.AIROLab.AIRODOFFY` / 16 |
| Build Meta ARM64-only update APK | `Builds/meta-update-v0.9.7-arm64-code18/AIRO_Doffy_meta_v0.9.7_arm64.apk` | `com.AIROLab.AIRODOFFY` / 18 |

The dedicated ARM64-only Meta build filters the generated SDK archive and restores it afterward. Existing app updates require the appropriate signing key to be configured locally. The committed project contains no signing key or access token; SDK development-agent authentication and unrelated credential fields are cleared.

## Validation

On 2026-10-07, a fresh project extracted from the staged Git tree, with no existing `Library/`, successfully resolved packages, imported and compiled in Unity 6000.5.6f1. `Doffy.Editor.TeleopBuild.ValidateScene` passed and Unity exited with code 0. Unity's API Updater automatically migrated the legacy GUID API in Meta Audio 85 during first import; no SDK cache patch is committed. A separate static audit checked 850 serialized script references and the enabled scene with no errors.

This check did not build a new APK or run the app on a headset.

Historical Quest, BODY and build evidence is recorded in the [PC repository](https://github.com/XDL0-0/AIRO-Doffy/blob/main/docs/body_visualization/validation.md). Headset interaction and robot acceptance are separate from project import and source compilation.
