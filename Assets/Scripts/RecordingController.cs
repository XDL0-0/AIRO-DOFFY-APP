using UnityEngine;
using TMPro;

/// <summary>
/// 录制开始/停止控制，通过 UDP 8003 端口发送 "Start"/"Stop" 指令。
/// 绑定 UI 按钮 OnClick → Recording()
/// </summary>
public class RecordingController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI recordButtonText;
    [SerializeField] private UdpSocket udpSocket;

    private void Start()
    {
        if (udpSocket == null)
            udpSocket = FindAnyObjectByType<UdpSocket>();
    }

    public void Recording()
    {
        if (recordButtonText.text == "Stop Record")
        {
            udpSocket.SendData8003("Stop");
            recordButtonText.text = "Start Record";
        }
        else
        {
            udpSocket.SendData8003("Start");
            recordButtonText.text = "Stop Record";
        }
    }
}
