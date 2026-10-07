using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Doffy.Networking
{
    /// <summary>One socket and one worker per lifetime. No Unity API calls on the worker.</summary>
    public sealed class DatagramReceiver : IDisposable
    {
        private readonly UdpClient client;
        private readonly Thread worker;
        private readonly Action<byte[]> receive;
        private readonly IPAddress peer;
        private volatile bool running = true;
        private volatile string errorMessage;
        private int disposed;
        public string Error => errorMessage;
        public bool IsRunning => running;

        public DatagramReceiver(int port, Action<byte[]> receive, string expectedPeer = null)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            this.receive = receive ?? throw new ArgumentNullException(nameof(receive));
            if (!string.IsNullOrWhiteSpace(expectedPeer)) IPAddress.TryParse(expectedPeer, out peer);
            client = new UdpClient(port);
            client.Client.ReceiveBufferSize = 4 * 1024 * 1024;
            client.Client.ReceiveTimeout = 250;
            worker = new Thread(Run) { IsBackground = true, Name = "doffy-udp-" + port };
            worker.Start();
        }

        private void Run()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref remote);
                    if (running && (peer == null || peer.Equals(remote.Address))) receive(data);
                }
                catch (SocketException error)
                {
                    if (running && error.SocketErrorCode != SocketError.TimedOut)
                    { errorMessage = error.Message; running = false; }
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception error) { if (running) errorMessage = error.Message; }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            running = false;
            client.Close();
            if (Thread.CurrentThread != worker && !worker.Join(1000))
                errorMessage = "Receive callback did not stop within one second.";
        }
    }
}
