# Airo-Doffy

Unity project for the Airo-Doffy v0.9.7 Quest app. The PC runtime is [Airo-Doffy](https://github.com/XDL0-0/AIRO-Doffy).

This folder contains `Assets/`, `Packages/` and `ProjectSettings/`, including the scripts, scene, prefabs, materials, textures, fonts, shaders, optional robot models and original Unity `.meta` files. Package versions are locked for restoration through Unity Package Manager. APKs, build outputs, SDK caches and local credentials are excluded.

## Open the project

1. Clone this repository and add the **AIRO-Doffy** folder to Unity Hub as its own project. The repository root contains the historical v0.6.0 project; keep the two projects separate because some scripts retain the same GUIDs.
2. Use Unity **6000.5.6f1**. For Quest builds, install Android Build Support with the SDK, NDK and OpenJDK through Unity Hub.
3. Allow Package Manager to restore the versions in `Packages/manifest.json` and `Packages/packages-lock.json`: Meta XR All-in-One **205.0.0**, Unity WebRTC **3.0.0**, OpenXR **1.18.0**, Newtonsoft JSON **3.2.2**, and their dependencies. Meta Audio **85.0.0** and Voice **85.0.1** are the versions selected by Meta 205.
4. Open `Assets/Scenes/Teleoperation.unity` and run `Tools → DOFFY → Validate scene`. The wrist/bracelet workspace is generated when entering Play.

## Code and operation

Custom code is under `Assets/Teleop`: Core, Input, Calibration, UpperLimb, Feedback, Networking, Protocol, Media, UI, Visualization, Diagnostics, Editor, Tests and Legacy.

1. In **Teleop Config → Connection & input**, enter the PC IPv4 address with **Edit IP**, press **Apply**, and choose **Controllers** or **Hand tracking**.
2. Use the **Alignment** tab for reference calibration and robot-base placement. Select the matching transport in **Camera** using **Use WebRTC** or **Use UDP**.
3. Press **Start Teleop**. Use **Start recording** and **Stop recording** in the record panel; **Undo episode → Confirm undo** removes the latest episode.

Optional WRM calibration is under **WRM Setting** and requires Controllers mode. BODY telemetry defaults to **OFF** on every launch; enable **System Setting → Body data: ON** to send BODY v1 over UDP **8015**. BODY viewing does not require **Start Teleop**.

The scene's configuration and runtime resources, including `TeleopConfigUpperLimb.asset`, `WristUISettings.asset` and `BraceletBody.shader`, are included. Optional robot model assets are retained for the original visualization modules. Unused Python preprocessing scripts and Python bytecode are excluded.

## Build

The project targets Android **ARM64** with **IL2CPP**. The build menus write inside this project, using portable paths:

| Menu under Tools → DOFFY | Output | Package / version code |
| --- | --- | --- |
| Build Quest APK | `Builds/AIRO_Doffy.apk` | `org.airolab.doffy.bracelet` / 16 |
| Build Meta update APK | `Builds/meta-update-v0.9.7/AIRO_Doffy_meta_v0.9.7.apk` | `com.AIROLab.AIRODOFFY` / 16 |
| Build Meta ARM64-only update APK | `Builds/meta-update-v0.9.7-arm64-code18/AIRO_Doffy_meta_v0.9.7_arm64.apk` | `com.AIROLab.AIRODOFFY` / 18 |

The dedicated ARM64-only Meta build filters the generated SDK archive and restores it afterward. Existing app updates require the appropriate signing key to be configured locally. The committed project contains no signing key or access token; SDK development-agent authentication and unrelated credential fields are cleared.

## Project status

On 2026-10-07, a clean project import, compilation and scene validation passed in Unity 6000.5.6f1. This check did not build a new APK or run the app on a headset. Headset interaction and robot operation still require device testing.
