using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class VirtualRobotJointStateReceiver : MonoBehaviour
{
    private enum DisplaySource
    {
        CommandedAction,
        ActualState
    }

    [SerializeField] private int listenPort = 8011;
    [SerializeField] private DisplaySource displaySource = DisplaySource.CommandedAction;
    [SerializeField] private VirtualUrdfJointDriver armDriver;
    [SerializeField] private Robotiq2F85MimicJointDriver gripperDriver;
    [SerializeField] private bool invertGripperFromOpenAmount = true;

    private readonly object _lock = new object();
    private UdpClient _client;
    private Thread _thread;
    private volatile bool _running;
    private float[] _actualJoints;
    private float[] _commandJoints;
    private float _gripper;
    private bool _hasPacket;

    private void Awake()
    {
        if (armDriver == null)
            armDriver = GetComponentInChildren<VirtualUrdfJointDriver>(true);
        if (gripperDriver == null)
            gripperDriver = GetComponentInChildren<Robotiq2F85MimicJointDriver>(true);
    }

    private void OnEnable()
    {
        _running = true;
        _client = new UdpClient(listenPort);
        _thread = new Thread(ReceiveLoop) { IsBackground = true };
        _thread.Start();
    }

    private void OnDisable()
    {
        _running = false;
        try { _client?.Close(); } catch { }
        if (_thread != null && _thread.IsAlive)
            _thread.Join(250);
    }

    private void Update()
    {
        float[] actual;
        float[] command;
        float gripper;
        bool hasPacket;

        lock (_lock)
        {
            hasPacket = _hasPacket;
            actual = _actualJoints != null ? (float[])_actualJoints.Clone() : null;
            command = _commandJoints != null ? (float[])_commandJoints.Clone() : null;
            gripper = _gripper;
        }

        if (!hasPacket)
            return;

        float[] selected = displaySource == DisplaySource.ActualState ? actual : command;
        armDriver?.ApplyJointsRadians(selected);

        if (gripperDriver != null)
        {
            float closedAmount = invertGripperFromOpenAmount ? 1f - gripper : gripper;
            gripperDriver.ApplyNormalized(closedAmount);
        }
    }

    private void ReceiveLoop()
    {
        IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                byte[] data = _client.Receive(ref endpoint);
                ParsePacket(Encoding.UTF8.GetString(data));
            }
            catch (SocketException)
            {
                if (_running)
                    Debug.LogWarning("[VirtualRobotJointStateReceiver] UDP receive failed.");
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void ParsePacket(string packet)
    {
        string[] parts = packet.Split(',');
        if (parts.Length < 5 || parts[0] != "VRJS")
            return;

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int dof))
            return;

        int expected = 4 + dof * 2;
        if (parts.Length < expected)
            return;

        float[] actual = new float[dof];
        float[] command = new float[dof];
        for (int i = 0; i < dof; i++)
        {
            if (!TryParseFloat(parts[3 + i], out actual[i]))
                return;
            if (!TryParseFloat(parts[3 + dof + i], out command[i]))
                return;
        }

        if (!TryParseFloat(parts[3 + dof * 2], out float gripper))
            return;

        lock (_lock)
        {
            _actualJoints = actual;
            _commandJoints = command;
            _gripper = Clamp01(gripper);
            _hasPacket = true;
        }
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static float Clamp01(float value)
    {
        if (value < 0f)
            return 0f;
        if (value > 1f)
            return 1f;
        return value;
    }
}
