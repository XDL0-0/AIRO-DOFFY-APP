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

## Network Interfaces

Default PC (workstation) IP is configured in `AppManager` / `UdpSocket` (192.168.43.198). Quest listens on the same LAN.

### Quest → PC (Quest sends)

| Port | Protocol | Data |
|------|----------|------|
| 8001 | UDP text | Teleop data (100 Hz). Controller pose: `C,{frameId},{timestampNs},{leftCtrl},{rightCtrl}`. Hand tracking: `H,L|R,{frameId},{timestampNs},{wristPos},{wristRot},{bones...}` or binary `HB,{base64}` |
| 8003 | UDP text | Recording control: `Start` / `Stop` |
| 8005 | UDP text | UI state: `{port},{resolution};{port},{resolution};...;{focusModeLabel};` e.g. `8000,x1.0;8002,x1.5;Fine Control Mode,ON;` |

### PC → Quest (Quest receives)

| Port | Protocol | Data |
|------|----------|------|
| 8000, 8002, 8004, 8006, 8008 | UDP video | Video streams (base port 8000 + `i*2`, up to 5 windows) |
| 8011 | UDP text | Virtual robot joint states: `VRJS,{frameId},{dof},{actual_0..actual_{dof-1}},{command_0..command_{dof-1}},{gripperOpen}` (radians, CSV) |
| 8012 | UDP JSON | **TCP pose + 6D force (single-port merge)** — see below |
| 8013 | UDP binary / JSON | Force backup port (ForceSensorReceiver, disabled by default in the configured scene) |
| 8765 | WebSocket | WebRTC signaling (SDP/ICE exchange) |

### Port 8012 — TCP Pose + Force (JSON)

```json
{
  "rightTCP": {
    "position": [x, y, z],
    "rotation": [w, x, y, z],
    "force": [Fx, Fy, Fz],
    "torque": [Mx, My, Mz]
  }
}
```

- `position`: meters, in the calibrated robot base frame (Unity left-handed, `TCP_DISPLAY_AXES = [[0,-1,0],[0,0,1],[1,0,0]]` applied on the Python side)
- `rotation`: [w, x, y, z], Unity convention
- `force` / `torque`: Newtons / N·m, drive the force arrow at the TCP (display length = force × `forceDisplayScale` 0.01)
- `leftTCP` accepted for bimanual setups; this project runs single-arm, so only `rightTCP` is used

### Port 8013 — 6D Force backup (binary)

Fallback listener when TCP pose comes from another channel:

```
6 × int32 little-endian (24 bytes): Fx Fy Fz Mx My Mz
```

raw value × `forceSensitivity` (0.00001) = arrow length in meters. TactAR-style JSON `{"device_id": "...", "arrow": {"start": [...], "end": [...]}, "scale": [...]}` is also accepted.

## Scene

Open `Assets/Scenes/V0.6.0 Realtime_Force.unity`.

Run `Tools > TactAR Features > Configure V0.6.0 Scene` to set up the calibration/TCP/force hierarchy.

## Build

- Unity 6000.5.6f1
- Meta Quest 3 (Android ARM64)
- IL2CPP backend
