using System;
using System.Diagnostics;
using Doffy.Networking;
using Doffy.Protocol;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/// <summary>Classic image receiver: network reassembly off-thread, texture upload on-thread.</summary>
public class UdpSocketMultiHD : MonoBehaviour
{
    [SerializeField] private string IP;
    private int rxPort;
    private DatagramReceiver receiver;
    private readonly object frameLock = new object();
    private byte[] pendingImage;
    private Texture2D texture;
    private Material material;
    private float zoom = 1;
    private string bindError;
    public int FramesPresented { get; private set; }
    public float LastFrameAt { get; private set; }
    public Texture CurrentTexture => texture;
    public string LastError => bindError ?? receiver?.Error;

    public void Initialize(string ip, int receivePort)
    {
        if (receivePort < 1 || receivePort > 65535)
            throw new ArgumentOutOfRangeException(nameof(receivePort));
        if (receiver != null && receiver.IsRunning && IP == ip && rxPort == receivePort) return;
        Cleanup();
        FramesPresented = 0;
        LastFrameAt = 0;
        IP = ip;
        rxPort = receivePort;
        if (isActiveAndEnabled) Bind();
    }

    private void Start()
    {
        texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        var renderer = GetComponent<Renderer>();
        if (renderer != null) { material = renderer.material; material.mainTexture = texture; }
        var image = GetComponent<RawImage>();
        if (image != null) image.texture = texture;
        SetZoom(zoom);
        if (receiver == null && rxPort > 0) Bind();
    }

    private void Bind()
    {
        var assembler = new JpegFrameAssembler();
        try
        {
            bindError = null;
            receiver = new DatagramReceiver(rxPort, packet =>
            {
                byte[] completed;
                double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                if (assembler.TryAdd(packet, now, out completed))
                    lock (frameLock) pendingImage = completed;
            }, IP);
        }
        catch (Exception error) { bindError = error.Message; Debug.LogWarning("Video receiver: " + error.Message, this); }
    }

    private void Update()
    {
        byte[] image;
        lock (frameLock) { image = pendingImage; pendingImage = null; }
        if (image == null || texture == null || !texture.LoadImage(image)) return;
        FramesPresented++;
        LastFrameAt = Time.unscaledTime;
    }

    private void OnEnable() { if (receiver == null && rxPort > 0) Bind(); }
    public void SetZoom(float value)
    {
        zoom = Mathf.Clamp(value, 1, 3);
        float size = 1 / zoom, offset = (1 - size) / 2;
        if (material != null)
        {
            material.mainTextureScale = Vector2.one * size;
            material.mainTextureOffset = Vector2.one * offset;
        }
        var image = GetComponent<RawImage>();
        if (image != null) image.uvRect = new Rect(offset, offset, size, size);
    }
    private void OnDisable() => Cleanup();
    private void OnApplicationQuit() => Cleanup();
    private void Cleanup()
    {
        receiver?.Dispose();
        receiver = null;
        lock (frameLock) pendingImage = null;
    }
    private void OnDestroy()
    {
        Cleanup();
        if (texture != null) Destroy(texture);
        if (material != null) Destroy(material);
    }
}
