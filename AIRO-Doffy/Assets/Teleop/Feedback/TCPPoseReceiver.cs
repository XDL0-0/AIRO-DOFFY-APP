using System;
using System.Threading;
using Doffy.Networking;
using Doffy.Protocol;
using UnityEngine;

/// <summary>Apply validated latest TCP/wrench samples on the Unity main thread.</summary>
public class TCPPoseReceiver : MonoBehaviour
{
    [Serializable] public class TcpPose : TcpStatePacket.Pose { }
    [Serializable] public class BimanualTcpMessage { public TcpPose leftTCP, rightTCP; }
    [SerializeField] private int listenPort = 8012;
    [SerializeField, Min(.05f)] private float staleAfterSeconds = .5f;
    public Transform leftTCP, rightTCP, leftForce, rightForce;
    public float forceDisplayScale = .01f;
    public bool autoCreateTcpObjects = true;
    private DatagramReceiver receiver;
    private TcpStatePacket pending;
    private ForceArrow leftArrow, rightArrow;
    private float lastReceived;
    private int rejected;
    public bool HasReceivedData { get; private set; }
    public bool IsFresh => HasReceivedData && Time.unscaledTime - lastReceived <= staleAfterSeconds;
    public int RejectedPackets => rejected;
    public int ListenPort => listenPort;

    private void OnEnable()
    {
        EnsureTcpObjects();
        leftArrow = leftForce != null ? leftForce.GetComponent<ForceArrow>() : null;
        rightArrow = rightForce != null ? rightForce.GetComponent<ForceArrow>() : null;
        try
        {
            receiver = new DatagramReceiver(listenPort, bytes =>
            {
                TcpStatePacket sample;
                if (TcpStatePacket.TryParse(bytes, out sample)) Interlocked.Exchange(ref pending, sample);
                else Interlocked.Increment(ref rejected);
            });
        }
        catch (Exception error) { Debug.LogWarning("TCP feedback: " + error.Message, this); }
    }

    private void Update()
    {
        TcpStatePacket sample = Interlocked.Exchange(ref pending, null);
        if (sample != null)
        {
            Apply(leftTCP, ref leftArrow, leftForce, sample.leftTCP);
            Apply(rightTCP, ref rightArrow, rightForce, sample.rightTCP);
            HasReceivedData = true;
            lastReceived = Time.unscaledTime;
        }
        else if (HasReceivedData && !IsFresh)
        {
            leftArrow?.UpdateForce(Vector3.zero);
            rightArrow?.UpdateForce(Vector3.zero);
        }
    }

    private void Apply(Transform target, ref ForceArrow arrow, Transform force, TcpStatePacket.Pose pose)
    {
        if (pose == null) return;
        if (target != null)
        {
            if (pose.position != null) target.localPosition = Vector(pose.position);
            if (pose.rotation != null)
                target.localRotation = new Quaternion(pose.rotation[1], pose.rotation[2], pose.rotation[3], pose.rotation[0]);
        }
        if (arrow == null && force != null) arrow = force.GetComponent<ForceArrow>();
        if (pose.force != null && arrow != null) arrow.UpdateForce(Vector(pose.force) * forceDisplayScale);
    }

    private static Vector3 Vector(float[] values) => new Vector3(values[0], values[1], values[2]);
    private void EnsureTcpObjects()
    {
        if (!autoCreateTcpObjects) return;
        if (leftTCP == null) leftTCP = FindOrCreate("LeftTCP");
        if (rightTCP == null) rightTCP = FindOrCreate("RightTCP");
    }
    private Transform FindOrCreate(string name)
    {
        Transform existing = transform.Find(name);
        if (existing != null) return existing;
        var created = new GameObject(name).transform;
        created.SetParent(transform, false);
        return created;
    }
    private void OnDisable()
    {
        receiver?.Dispose();
        receiver = null;
        Interlocked.Exchange(ref pending, null);
        HasReceivedData = false;
    }
    private void OnValidate() => listenPort = Mathf.Clamp(listenPort, 1024, 65535);
}
