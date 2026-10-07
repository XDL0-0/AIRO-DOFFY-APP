using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Reads one bounded, complete text message from a ClientWebSocket.</summary>
internal sealed class WebSocketMessageReader
{
    internal const int MaxMessageBytes = 1024 * 1024;
    private readonly byte[] _buffer = new byte[16 * 1024];
    private readonly UTF8Encoding _utf8 = new UTF8Encoding(false, true);

    public async Task<string> ReadTextAsync(ClientWebSocket socket, CancellationToken token)
    {
        using (MemoryStream message = new MemoryStream())
        {
            bool endOfMessage = false;
            WebSocketMessageType messageType = WebSocketMessageType.Text;
            while (!endOfMessage && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(_buffer), token).ConfigureAwait(false);
                messageType = result.MessageType;
                if (messageType == WebSocketMessageType.Close)
                    return null;
                if (message.Length + result.Count > MaxMessageBytes)
                    throw new InvalidDataException(
                        $"Signaling message exceeds {MaxMessageBytes} bytes");
                message.Write(_buffer, 0, result.Count);
                endOfMessage = result.EndOfMessage;
            }

            if (token.IsCancellationRequested || !endOfMessage)
                return null;
            if (messageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Unexpected binary signaling message");

            byte[] bytes = message.ToArray();
            return _utf8.GetString(bytes, 0, bytes.Length);
        }
    }
}
