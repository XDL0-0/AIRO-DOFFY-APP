using UnityEngine;

/// <summary>
/// 已废弃：原脚本同时承担退出、录制、姿态转发和 UI 文本拼接等多种职责，
/// 现已解耦为独立组件：
/// 1. DualControllerSender    - 同帧采集双手 controller 并添加统一时间戳发送
/// 2. RecordingController     - 录制开始/停止控制
/// 3. AppController           - 退出应用
///
/// 保留此空壳类仅为避免场景中已有组件引用时报错。
/// 建议在场景中逐步移除对本组件的依赖。
/// </summary>
public class PositionTransfer : MonoBehaviour
{
}
