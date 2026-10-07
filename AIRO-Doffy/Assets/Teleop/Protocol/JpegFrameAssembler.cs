using System;
using System.Collections.Generic;

namespace Doffy.Protocol
{
    /// <summary>Bounded classic JPEG/PNG reassembly. Call from one receive thread.</summary>
    public sealed class JpegFrameAssembler
    {
        private sealed class Frame
        {
            public int Size, Received, Bytes;
            public double Started;
            public byte[][] Chunks;
        }

        private readonly Dictionary<uint, Frame> frames = new Dictionary<uint, Frame>();
        private readonly List<uint> expired = new List<uint>();
        private readonly int maximumBytes, maximumFrames;
        private readonly double expirySeconds;
        private uint lastDelivered;
        private bool hasDelivered;
        private double lastDeliveredAt;
        public int RejectedPackets { get; private set; }
        public int IncompleteFrames => frames.Count;

        public JpegFrameAssembler(int maximumBytes = 8 * 1024 * 1024, int maximumFrames = 8,
            double expirySeconds = .5)
        {
            if (maximumBytes < 1 || maximumFrames < 1 || expirySeconds <= 0)
                throw new ArgumentOutOfRangeException();
            this.maximumBytes = maximumBytes;
            this.maximumFrames = maximumFrames;
            this.expirySeconds = expirySeconds;
        }

        public bool TryAdd(byte[] packet, double now, out byte[] image)
        {
            image = null;
            Expire(now);
            // A restarted PC starts its frame counter at zero. Recover after a quiet gap.
            if (hasDelivered && now - lastDeliveredAt > expirySeconds * 4) hasDelivered = false;
            if (packet == null || packet.Length == 0 || packet.Length > 65507) return Reject();
            bool jpeg = packet.Length >= 3 && packet[0] == 255 && packet[1] == 216 && packet[2] == 255;
            bool png = packet.Length >= 8 && packet[0] == 137 && packet[1] == 80 && packet[2] == 78 && packet[3] == 71;
            if (jpeg || png)
            {
                if (packet.Length > maximumBytes) return Reject();
                image = packet;
                return true;
            }
            if (packet.Length <= 12) return Reject();
            uint id = Read32(packet, 0), sizeValue = Read32(packet, 8);
            int index = Read16(packet, 4), count = Read16(packet, 6), payload = packet.Length - 12;
            if (sizeValue == 0 || sizeValue > maximumBytes || count == 0 || count > 8192 ||
                index >= count || count > sizeValue || payload > sizeValue) return Reject();
            if (hasDelivered && unchecked((int)(id - lastDelivered)) <= 0) return false;
            Frame frame;
            if (!frames.TryGetValue(id, out frame))
            {
                if (frames.Count >= maximumFrames) EvictOldest();
                frame = new Frame { Size = (int)sizeValue, Chunks = new byte[count][], Started = now };
                frames.Add(id, frame);
            }
            if (frame.Size != sizeValue || frame.Chunks.Length != count)
            {
                frames.Remove(id);
                return Reject();
            }
            if (frame.Chunks[index] != null) return false;
            if (frame.Bytes + payload > frame.Size)
            {
                frames.Remove(id);
                return Reject();
            }
            byte[] chunk = new byte[payload];
            Buffer.BlockCopy(packet, 12, chunk, 0, payload);
            frame.Chunks[index] = chunk;
            frame.Bytes += payload;
            if (++frame.Received != count) return false;
            frames.Remove(id);
            if (frame.Bytes != frame.Size) return Reject();
            image = new byte[frame.Size];
            int offset = 0;
            foreach (byte[] part in frame.Chunks)
            {
                Buffer.BlockCopy(part, 0, image, offset, part.Length);
                offset += part.Length;
            }
            lastDelivered = id;
            lastDeliveredAt = now;
            hasDelivered = true;
            return true;
        }

        public void Clear() { frames.Clear(); hasDelivered = false; }

        public void Expire(double now)
        {
            expired.Clear();
            foreach (var item in frames)
                if (now - item.Value.Started >= expirySeconds) expired.Add(item.Key);
            foreach (uint key in expired) frames.Remove(key);
        }

        private void EvictOldest()
        {
            uint oldest = 0;
            double started = double.PositiveInfinity;
            foreach (var item in frames)
                if (item.Value.Started < started) { oldest = item.Key; started = item.Value.Started; }
            frames.Remove(oldest);
        }

        private bool Reject() { RejectedPackets++; return false; }
        private static int Read16(byte[] p, int i) => (p[i] << 8) | p[i + 1];
        private static uint Read32(byte[] p, int i) => ((uint)p[i] << 24) | ((uint)p[i + 1] << 16) | ((uint)p[i + 2] << 8) | p[i + 3];
    }
}
