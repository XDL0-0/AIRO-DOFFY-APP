# AIRO-DOFFY-APP

Quest 3 VR teleoperation app with force visualization and calibration.

## Release/source scope

This is the historical source snapshot tagged `v0.6.0` at
[`994a672175af58fc4f14b92f2ca10b4595c8e0a9`](https://github.com/XDL0-0/AIRO-DOFFY-APP/tree/994a672175af58fc4f14b92f2ca10b4595c8e0a9)
(2026-08-12). It does not contain the later BODY telemetry implementation and
**is not the complete source for AIRO Doffy v0.9.7 / code 18**.

The October APK is distributed in
[AIRO-Doffy at `a3d1233`](https://github.com/XDL0-0/AIRO-Doffy/tree/a3d1233c53d82f35394f68f0c8d2faa2a4857c81/apk).
Its complete Unity source revision was not found in the public branches, tags
or reachable history of either repository during the 2026-10-07 audit. Local
build notes refer to a separate `CodexBracelet` project; that project was not
available for this audit. Partial BODY component copies in the PC repository
do not constitute a complete build project. Do not use this historical tag or
the PC distribution commit as a source pin for that APK.

At the historical SHA, Unity settings contain `bundleVersion: 0.5.0` and
`AndroidBundleVersionCode: 1`; the `v0.6.0` tag name alone does not verify even
the historical binary/source association. The installation and build guidance
below describes this historical project. A future APK needs an explicit full
source SHA, matching version/code, package lock and recorded APK SHA256.

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

### PC → Quest (Quest receives)

| Port | Protocol | Data |
|------|----------|------|
| 8000, 8002, 8004, 8006, 8008 | UDP video | Video streams (base port 8000 + `i*2`, up to 5 windows) |
| 8012 | UDP JSON | **TCP pose + 6D force (single-port merge)** — see below |
| 8765 | WebSocket | WebRTC signaling (SDP/ICE exchange) |

### Legacy / disabled ports

| Port | Status | Notes |
|------|--------|-------|
| 8005 | Deprecated | UI/resolution state (`{port},{res};...;{focusModeLabel};`). Was sent by `UdpWindowManager.Resolution_loop`; no longer sent since the precision/focus-mode button was removed from the scene |
| 8011 | Not in scene | Virtual robot joint states `VRJS,{frameId},{dof},{actual...},{command...},{gripper}` — code exists but `VirtualRobotJointStateReceiver` is not attached in the shipped scene |
| 8013 | Disabled backup | Force fallback listener (see below), ForceSensorReceiver is disabled in the configured scene |

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
