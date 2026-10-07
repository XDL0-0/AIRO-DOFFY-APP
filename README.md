# Airo-Doffy Unity App

Unity app for Airo-Doffy VR teleoperation on Meta Quest. The Python runtime and APK are available in [Airo-Doffy](https://github.com/XDL0-0/AIRO-Doffy).

The current Unity project is in **[AIRO-Doffy/](AIRO-Doffy/README.md)**.

## Features

- Controller and hand tracking for robot teleoperation.
- Camera viewing over WebRTC or UDP.
- Robot alignment and force visualization.
- Dataset recording controls.
- Optional WRM upper-limb mapping and BODY telemetry.

## Open and build

1. Clone this repository and add the **AIRO-Doffy** subfolder to Unity Hub.
2. Use Unity **6000.5.6f1** with Android Build Support, SDK, NDK and OpenJDK.
3. Allow Package Manager to restore the locked dependencies, including Meta XR All-in-One **205.0.0**.
4. Open `Assets/Scenes/Teleoperation.unity` and run **Tools → DOFFY → Validate scene**.
5. Build v0.9.7 / code 18 using **Tools → DOFFY → Build Meta ARM64-only update APK**.

See the [project guide](AIRO-Doffy/README.md) for build outputs and app controls. To install the existing APK, follow the [release notes](https://github.com/XDL0-0/AIRO-Doffy/blob/main/apk/RELEASE.md).

## Connect to the PC

In **Teleop Config → Connection & input**, enter the PC address and press **Apply**. Match the input and camera transport to the Python runtime, complete **Alignment**, then press **Start Teleop**.

BODY viewing is independent: enable **System Setting → Body data: ON** after applying the PC address.

## Historical project

The root `Assets/`, `Packages/` and `ProjectSettings/` folders belong to the older v0.6.0 project. Open the current **AIRO-Doffy** folder as a separate Unity project.

- [Historical setup instructions](https://github.com/XDL0-0/AIRO-DOFFY-APP/blob/994a672175af58fc4f14b92f2ca10b4595c8e0a9/README.md)
- [Historical UDP protocol](Docs/UDP_DataFormat_TactAR_Features.md)
