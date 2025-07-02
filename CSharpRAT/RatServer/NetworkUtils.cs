using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RatServer // Correct namespace for server project
{
    public static class NetworkUtils
    {
        private const int LengthPrefixSize = 4; // Size of the length prefix in bytes (int32)

        public static async Task ReliableSendAsync(Socket socket, byte[] data)
        {
            if (socket == null || !socket.Connected)
            {
                throw new ArgumentException("Socket is not connected or null for send.");
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            try
            {
                byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
                await socket.SendAsync(new ArraySegment<byte>(lengthPrefix), SocketFlags.None);
                if (data.Length > 0)
                {
                    await socket.SendAsync(new ArraySegment<byte>(data), SocketFlags.None);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"RatServer.NetworkUtils: SocketException during send: {ex.Message}, ErrorCode: {ex.SocketErrorCode}");
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                Console.WriteLine($"RatServer.NetworkUtils: ObjectDisposedException during send (socket closed): {ex.Message}");
                throw;
            }
        }

        public static async Task<byte[]> ReliableReceiveAsync(Socket socket)
        {
            if (socket == null || !socket.Connected)
            {
                Console.WriteLine("RatServer.NetworkUtils: Socket is not connected or null for receive.");
                return null;
            }

            byte[] lengthPrefixBuffer = new byte[LengthPrefixSize];
            int totalBytesReceivedForLength = 0;

            try
            {
                while (totalBytesReceivedForLength < LengthPrefixSize)
                {
                    int bytesReceived = await socket.ReceiveAsync(new ArraySegment<byte>(lengthPrefixBuffer, totalBytesReceivedForLength, LengthPrefixSize - totalBytesReceivedForLength), SocketFlags.None);
                    if (bytesReceived == 0)
                    {
                        Console.WriteLine("RatServer.NetworkUtils: Connection closed by remote host while receiving length prefix.");
                        return null;
                    }
                    totalBytesReceivedForLength += bytesReceived;
                }

                int dataLength = BitConverter.ToInt32(lengthPrefixBuffer, 0);

                if (dataLength < 0)
                {
                    Console.WriteLine($"RatServer.NetworkUtils: Invalid data length received: {dataLength}.");
                    return null;
                }

                if (dataLength == 0)
                {
                    return Array.Empty<byte>();
                }

                byte[] dataBuffer = new byte[dataLength];
                int totalBytesReceivedForData = 0;
                while (totalBytesReceivedForData < dataLength)
                {
                    int bytesReceived = await socket.ReceiveAsync(new ArraySegment<byte>(dataBuffer, totalBytesReceivedForData, dataLength - totalBytesReceivedForData), SocketFlags.None);
                    if (bytesReceived == 0)
                    {
                        Console.WriteLine("RatServer.NetworkUtils: Connection closed by remote host while receiving data payload.");
                        return null;
                    }
                    totalBytesReceivedForData += bytesReceived;
                }
                return dataBuffer;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"RatServer.NetworkUtils: SocketException during receive: {ex.Message}, ErrorCode: {ex.SocketErrorCode}");
                return null;
            }
            catch (ObjectDisposedException ex)
            {
                Console.WriteLine($"RatServer.NetworkUtils: ObjectDisposedException during receive (socket closed): {ex.Message}");
                return null;
            }
        }
    }
}
