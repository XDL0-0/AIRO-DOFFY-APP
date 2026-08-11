using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// 高清图像UDP接收器 - 支持分片重组，突破480p限制
/// 
/// 协议格式（每个UDP包头部 = 12字节）:
///   [0..3]  frameId      (uint32, 帧序号)
///   [4..5]  chunkIndex   (uint16, 当前分片索引, 0-based)
///   [6..7]  totalChunks  (uint16, 该帧总分片数)
///   [8..11] totalBytes   (uint32, 该帧完整图像字节数)
///   [12..]  payload      (JPEG/PNG 图像数据片段)
///
/// Python发送端示例（配套使用）:
///   见项目根目录 python_sender_hd.py
/// </summary>
public class UdpSocketMultiHD : MonoBehaviour
{
    // ---- 头部常量 ----
    private const int HEADER_SIZE    = 12;
    private const int MAX_CHUNK_SIZE = 60000; // 每片最大60KB，留余量给头部

    // ---- 运行时状态 ----
    private UdpClient   _client;
    private Thread      _receiveThread;
    private volatile bool _isRunning = false;

    private readonly object _textureLock = new object();
    private byte[]   _pendingImageData = null;
    private bool     _hasNewFrame      = false;

    // ---- 分片重组缓冲 ----
    // key = frameId, value = 重组上下文
    private readonly Dictionary<uint, FrameBuffer> _frameBuffers = new Dictionary<uint, FrameBuffer>();
    private readonly object _bufferLock = new object();

    // 最多缓存多少帧（防止内存泄漏）
    private const int MAX_BUFFERED_FRAMES = 8;

    // ---- Inspector 参数 ----
    [SerializeField] private string IP   ;
     private int    rxPort  ;

    //     [SerializeField] private string IP      = "0.0.0.0";
    // [SerializeField] private int    rxPort  = 8000;

    // 纹理引用（挂载在同一 GameObject 的 Renderer 上）
    private Texture2D _texture;

    // ---- 公开初始化接口（与旧版 UdpSocketMulti 兼容）----
    public void Initialize(string ip, int receivePort)
    {
        IP     = ip;
        rxPort = receivePort;
        SetupUDP();
    }

    // ================================================================
    void Start()
    {
        _texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        var rend = GetComponent<Renderer>();
        if (rend != null) rend.material.mainTexture = _texture;

        SetupUDP();
    }

    // ================================================================
    private void SetupUDP()
    {
        Cleanup();
        try
        {
            _client    = new UdpClient(rxPort);
            _isRunning = true;

            _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
            _receiveThread.Start();

            Debug.Log($"[UdpSocketMultiHD] 监听端口 {rxPort}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UdpSocketMultiHD] 启动失败: {e.Message}");
        }
    }

    // ================================================================
    // 接收线程：收包 → 解析头部 → 写入分片缓冲
    // ================================================================
    private void ReceiveLoop()
    {
        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        while (_isRunning)
        {
            try
            {
                byte[] packet = _client.Receive(ref remote);

                if (packet.Length <= HEADER_SIZE)
                {
                    // 兼容旧协议：无头部的单包图像直接推送
                    PushCompleteFrame(packet);
                    continue;
                }

                // ---- 解析头部（big-endian，与Python struct "!IHHI" 一致）----
                uint   frameId     = ReadUInt32BE(packet, 0);
                ushort chunkIndex  = ReadUInt16BE(packet, 4);
                ushort totalChunks = ReadUInt16BE(packet, 6);
                uint   totalBytes  = ReadUInt32BE(packet, 8);

                // 单包帧（totalChunks == 1）直接推送，无需缓冲
                if (totalChunks == 1)
                {
                    int payloadLen = packet.Length - HEADER_SIZE;
                    byte[] singleFrame = new byte[payloadLen];
                    Buffer.BlockCopy(packet, HEADER_SIZE, singleFrame, 0, payloadLen);
                    PushCompleteFrame(singleFrame);
                    continue;
                }

                // ---- 多分片重组 ----
                lock (_bufferLock)
                {
                    // 清理过旧的帧缓冲，防止内存泄漏
                    if (_frameBuffers.Count >= MAX_BUFFERED_FRAMES)
                        EvictOldestFrame(frameId);

                    if (!_frameBuffers.TryGetValue(frameId, out FrameBuffer fb))
                    {
                        fb = new FrameBuffer(frameId, totalChunks, (int)totalBytes);
                        _frameBuffers[frameId] = fb;
                    }

                    int payloadLen = packet.Length - HEADER_SIZE;
                    fb.AddChunk(chunkIndex, packet, HEADER_SIZE, payloadLen);

                    if (fb.IsComplete)
                    {
                        byte[] fullImage = fb.Assemble();
                        _frameBuffers.Remove(frameId);
                        PushCompleteFrame(fullImage);
                    }
                }
            }
            catch (SocketException se)
            {
                if (_isRunning && se.SocketErrorCode != SocketError.Interrupted)
                    Debug.LogWarning($"[UdpSocketMultiHD] Socket异常: {se.Message}");
            }
            catch (Exception e)
            {
                if (_isRunning)
                    Debug.LogError($"[UdpSocketMultiHD] 接收错误: {e.Message}");
            }
        }
    }

    // 将完整帧数据推送给主线程
    private void PushCompleteFrame(byte[] imageData)
    {
        lock (_textureLock)
        {
            _pendingImageData = imageData;
            _hasNewFrame      = true;
        }
    }

    // 清理最旧的帧（按frameId最小值）
    private void EvictOldestFrame(uint currentFrameId)
    {
        uint oldest = currentFrameId;
        foreach (var key in _frameBuffers.Keys)
        {
            // 考虑uint溢出回绕：差值超过半圈视为旧帧
            if ((int)(key - oldest) < 0) oldest = key;
        }
        _frameBuffers.Remove(oldest);
    }

    // ================================================================
    // 主线程：将完整帧数据加载到纹理
    // ================================================================
    void Update()
    {
        if (!_hasNewFrame) return;

        byte[] data = null;
        lock (_textureLock)
        {
            if (_hasNewFrame && _pendingImageData != null)
            {
                data              = _pendingImageData;
                _pendingImageData = null;
                _hasNewFrame      = false;
            }
        }

        if (data != null && _texture != null)
        {
            if (_texture.LoadImage(data))
                _texture.Apply();
        }
    }

    // ================================================================
    private void Cleanup()
    {
        _isRunning = false;
        if (_client != null)
        {
            _client.Close();
            _client = null;
        }
        lock (_bufferLock)
        {
            _frameBuffers.Clear();
        }
    }

    void OnDisable()        => Cleanup();
    void OnApplicationQuit()=> Cleanup();

    // ---- big-endian 读取辅助（匹配 Python struct "!" 网络字节序）----
    private static uint ReadUInt32BE(byte[] buf, int offset)
    {
        return ((uint)buf[offset]     << 24) |
               ((uint)buf[offset + 1] << 16) |
               ((uint)buf[offset + 2] <<  8) |
               ((uint)buf[offset + 3]);
    }

    private static ushort ReadUInt16BE(byte[] buf, int offset)
    {
        return (ushort)(((ushort)buf[offset] << 8) | buf[offset + 1]);
    }

    // ================================================================
    // 内部类：单帧分片缓冲
    // ================================================================
    private class FrameBuffer
    {
        public readonly uint   FrameId;
        private readonly int   _totalChunks;
        private readonly int   _totalBytes;
        private readonly byte[][] _chunks;
        private readonly int[]    _chunkLens;
        private int _receivedCount = 0;

        public bool IsComplete => _receivedCount >= _totalChunks;

        public FrameBuffer(uint frameId, int totalChunks, int totalBytes)
        {
            FrameId      = frameId;
            _totalChunks = totalChunks;
            _totalBytes  = totalBytes;
            _chunks      = new byte[totalChunks][];
            _chunkLens   = new int[totalChunks];
        }

        public void AddChunk(int index, byte[] src, int offset, int length)
        {
            if (index < 0 || index >= _totalChunks) return;
            if (_chunks[index] != null) return; // 已收到，忽略重复包

            _chunks[index]    = new byte[length];
            _chunkLens[index] = length;
            Buffer.BlockCopy(src, offset, _chunks[index], 0, length);
            _receivedCount++;
        }

        public byte[] Assemble()
        {
            // 计算实际总长度（以实际收到的为准，防止totalBytes不准确）
            int total = 0;
            for (int i = 0; i < _totalChunks; i++)
                total += (_chunks[i] != null) ? _chunkLens[i] : 0;

            byte[] result = new byte[total];
            int pos = 0;
            for (int i = 0; i < _totalChunks; i++)
            {
                if (_chunks[i] == null) continue;
                Buffer.BlockCopy(_chunks[i], 0, result, pos, _chunkLens[i]);
                pos += _chunkLens[i];
            }
            return result;
        }
    }
}
