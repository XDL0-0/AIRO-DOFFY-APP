using System;
using System.Threading;

namespace Doffy.Networking
{
    /// <summary>Optional legacy 41-taxel stream; separate from TCP/wrench JSON.</summary>
    public sealed class TactileArrayStream : IDisposable
    {
        private readonly DatagramReceiver receiver;
        private int[,] latest;

        public TactileArrayStream(int port)
        {
            receiver = new DatagramReceiver(port, bytes =>
            {
                if (bytes.Length < 41 * 3 * 4) return;
                var sample = new int[41, 3];
                int offset = 0;
                for (int row = 0; row < 41; row++)
                    for (int axis = 0; axis < 3; axis++, offset += 4)
                        sample[row, axis] = bytes[offset] | bytes[offset + 1] << 8 |
                            bytes[offset + 2] << 16 | bytes[offset + 3] << 24;
                Interlocked.Exchange(ref latest, sample);
            });
        }

        public int[,] TakeLatest() => Interlocked.Exchange(ref latest, null);
        public void Dispose() => receiver.Dispose();
    }
}
