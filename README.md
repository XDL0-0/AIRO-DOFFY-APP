# AIRO-DOFFY-APP

Quest 3 VR teleoperation app with force visualization and calibration.

## APK Installation

Copy `Teleoperation.apk` to your Quest 3 and install via SideQuest or `adb install`.

## Unity Project Structure

| Folder | Purpose |
|--------|---------|
| `Assets/` | Scripts, scenes, prefabs, materials |
| `Packages/` | Package manifest |
| `ProjectSettings/` | Unity project configuration |

## Key Features

- **TCP Pose Streaming** — receive robot end-effector pose via UDP JSON (port 8012)
- **Force Visualization** — real-time 6-axis force/torque arrows at TCP
- **Coordinate Calibration** — X+A to enter calibration mode, align virtual axes with physical robot base
- **Passthrough AR** — Quest 3 color passthrough for real-world alignment

## UDP Data Format

Single-port design (default: 8012):

```json
{
  "rightTCP": {
    "position": [x, y, z],
    "rotation": [w, x, y, z],
    "force": [Fx, Fy, Fz]
  }
}
```

- `position`: meters, in calibrated robot base frame
- `rotation`: [w, x, y, z], Unity convention
- `force`: Newtons, controls force arrow display

## Scene

Open `Assets/Scenes/V0.6.0 Realtime_Force.unity`.

Run `Tools > TactAR Features > Configure V0.6.0 Scene` to set up the calibration/TCP/force hierarchy.

## Build

- Unity 6000.5.6f1
- Meta Quest 3 (Android ARM64)
- IL2CPP backend
