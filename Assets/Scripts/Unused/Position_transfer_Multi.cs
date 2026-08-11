//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using TMPro;

//public class Position_transfer_Multi : MonoBehaviour
//{
//    [SerializeField] TextMeshProUGUI sendToPythonControllerL = null;
//    [SerializeField] TextMeshProUGUI sendToPythonControllerR = null;
//    [SerializeField] TextMeshProUGUI TextshowinUnity = null;
//    [SerializeField] TextMeshProUGUI PostureSendingState = null;
    
//    string tempStr = "";
//    bool StatePostureSending = true;
    
//    private UdpSocketMulti udpSocket; // 改成 UdpSocketMulti

//    public void QuitApp()
//    {
//        print("Quitting");
//        Application.Quit();
//    }

//    public void SwitchCamera()
//    {
//        TextshowinUnity.text = "Switching";
//        StatePostureSending = false;
//        PostureSendingState.text = "Posture Transmission\r\nOFF";
        
//        if (udpSocket != null)
//        {
//            udpSocket.SendData("Switching");
//            TextshowinUnity.text = "Switched";
//        }
//        else
//        {
//            TextshowinUnity.text = "UDP Socket 未找到";
//        }
//    }

//    public void PositionTransferState()
//    {
//        StatePostureSending = !StatePostureSending;
//        PostureSendingState.text = StatePostureSending ? "Posture Transmission\r\nON" : "Posture Transmission\r\nOFF";
//    }

//    public void UpdateSendToPythonText(string strL, string strR)
//    {
//        tempStr = strL + strR;
//    }

//    private void Start()
//    {
//        udpSocket = FindObjectOfType<UdpSocketMulti>(); // 查找 UdpSocketMulti
//        if (udpSocket == null)
//        {
//            TextshowinUnity.text = "UdpSocketMulti 未找到";
//        }
//    }

//    void Update()
//    {
//        UpdateSendToPythonText(sendToPythonControllerL.text, sendToPythonControllerR.text);
//        TextshowinUnity.text = tempStr;

//        if (udpSocket != null && StatePostureSending)
//        {
//            TextshowinUnity.text = "Sending controllers Data...";
//            udpSocket.SendData(tempStr);
//        }
//    }
//}
