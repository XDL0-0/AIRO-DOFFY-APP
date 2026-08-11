# TactAR 功能 UDP 数据格式说明(Quest ← workstation)

本地 V0.6.0 场景:**合并到单端口 8012**,TCP 位姿 + 6D 力一起发。

| 端口 | 用途 | 格式 |
|------|------|------|
| **8012** | **TCP 位姿 + 力传感器(合并)** | JSON 文本 |
| 8013 | TCP 位姿(不含力) / ForceSensorReceiver 备选 | JSON / 二进制 |

> TCPPoseReceiver 监听 8012, 同时接收 TCP 位姿和力数据。
> ForceSensorReceiver(8013) 默认禁用。

---

## TCP 位姿 + 力 — UDP 8012(JSON,主端口)

```json
{
  "rightTCP": {
    "position": [0.45, 0.12, 0.30],
    "rotation": [1.0, 0.0, 0.0, 0.0],
    "force": [1.5, 0.3, -0.2],
    "torque": [0.1, 0.0, 0.0]
  }
}
```

- `position`: [x, y, z], 米, **机器人基座坐标系**(校准后空间)
- `rotation`: [w, x, y, z], w 在前
- `force`: [Fx, Fy, Fz], **牛顿**, 力箭头长度 = N × `forceDisplayScale`(默认 0.01, 即 1N=1cm)
- `torque`: [Mx, My, Mz], 牛·米(预留,暂不显示)
- `force`/`torque` 字段可选,不传则只更新 TCP 位置
- 单臂只发 `rightTCP`; 双臂可加 `leftTCP`

### Python 发送(单臂)

```python
import json, socket, time

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

def send_state(quest_ip, pos, quat_wxyz, force, torque=(0,0,0)):
    msg = {
        "rightTCP": {
            "position": list(pos),
            "rotation": list(quat_wxyz),  # [w, x, y, z]
            "force": list(force),          # [Fx, Fy, Fz] N
            "torque": list(torque)         # [Mx, My, Mz] Nm
        }
    }
    sock.sendto(json.dumps(msg).encode(), (quest_ip, 8012))

# 从 RealMan teleop:
# robot = teleop.state_snapshot()
# send_state(quest_ip, robot.tcp_pose[:3,3], quat_wxyz, robot.wrench[:3])
```,与触觉数据同帧率

---

## 坐标系约定

1. **TCP 位姿空间**:机器人基座坐标系(校准后空间),与手部姿态发送一致
2. **力矢量**:TCP 局部坐标(传感器坐标系)
3. **四元数顺序**:`[w, x, y, z]`(w 在前)
4. 校准 gizmo(X+A)对齐机器人基座原点后,TCP + 力箭头自动映射到 Quest 世界正确位置
