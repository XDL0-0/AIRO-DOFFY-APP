using System;
using UnityEngine;
using Oculus;

public class MetaControllerData : MonoBehaviour
{
    public OVRInput.Controller controllerType; // 设置为 LTouch 或 RTouch
    [SerializeField] PythonTest pythonTest;


    void Update()
    {
        // 获取手柄的位置和旋转
        Vector3 position = OVRInput.GetLocalControllerPosition(controllerType);
        Quaternion rotation = OVRInput.GetLocalControllerRotation(controllerType);

        // 获取两个 Trigger 值
        float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controllerType); // 主手触发器

        // 获取 Joystick 的 X 和 Y 轴值
        Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, controllerType); // 主手摇杆

        int grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controllerType) ? 1 : 0;
        int buttonAX = OVRInput.Get(OVRInput.Button.One, controllerType) ? 1 : 0;
        int buttonBY = OVRInput.Get(OVRInput.Button.Two, controllerType) ? 1 : 0;
        int joystickPress = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, controllerType) ? 1 : 0;

        //var buttonStates = new
        //{
        //    Joystick = Thumbstick,
        //    IndexTrigger = Trigger, 
        //    GripTrigger = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controllerType) ? 1 : 0,
        //    Button_AX = OVRInput.Get(OVRInput.Button.One, controllerType) ? 1 : 0,
        //    Button_BY = OVRInput.Get(OVRInput.Button.Two, controllerType) ? 1 : 0,
        //    Joystick_Press = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, controllerType) ? 1 : 0

        //};

        // 将数据整理成表格
        //var controllerData = new
        //{
        //    ControllerType = controllerType.ToString(),
        //    Position = (position.x.ToString("F6"), position.y.ToString("F6"), position.z.ToString("F6")),
        //    Rotation = rotation,
        //    Joystick = Thumbstick,
        //    IndexTrigger = Trigger,
        //    GripTrigger = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controllerType) ? 1 : 0,
        //    Button_AX = OVRInput.Get(OVRInput.Button.One, controllerType) ? 1 : 0,
        //    Button_BY = OVRInput.Get(OVRInput.Button.Two, controllerType) ? 1 : 0,
        //    Joystick_Press = OVRInput.Get(OVRInput.Button.PrimaryThumbstick, controllerType) ? 1 : 0
        //};
        //Debug.Log($"Controller Data:\n{controllerData}");
        //pythonTest.UpdatePythonRcvdText(controllerData.ToString());
        // 输出 JSON 格式的控制器数据
        //string jsonData = JsonUtility.ToJson(controllerData, true);
        //Debug.Log($"Controller Data:\n{jsonData}");


        string line =
    $"{controllerType}," +
    $"{position.x:F6},{position.y:F6},{position.z:F6}," +
    $"{rotation.x:F6},{rotation.y:F6},{rotation.z:F6},{rotation.w:F6}," +
    $"{thumbstick.x:F6},{thumbstick.y:F6}," +
    $"{trigger:F6},{grip},{buttonAX},{buttonBY},{joystickPress}";

        pythonTest.UpdatePythonRcvdText(line);
    }



    private void Start()
    {
        //pythonTest = FindObjectOfType<PythonTest>(); // Instead of using a public variable

    }
}
