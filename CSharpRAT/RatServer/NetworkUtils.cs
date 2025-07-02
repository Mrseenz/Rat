using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RatClient
{
    public static class NetworkUtils
    {
        private const int LengthPrefixSize = 4; // Size of the length prefix in bytes (int32)

        public static async Task ReliableSendAsync(Socket socket, byte[] data)
        {
            if (socket == null || !socket.Connected)
            {
                throw new ArgumentException("Socket is not connected or null.");
            }
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            try
            {
                // 1. Prefix the data with its length
                byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
                if (BitConverter.IsLittleEndian)
                {
                    // Ensure network byte order (big-endian) if system is little-endian
                    // However, for simplicity within this system, we can assume sender/receiver are same endianness
                    // or decide on a consistent order. Most modern systems are little-endian.
                    // Let's stick to little-endian for now for BitConverter's default.
                    // If cross-platform/architecture is a major concern, use IPAddress.HostToNetworkOrder.
                }

                // 2. Send the length prefix
                await socket.SendAsync(new ArraySegment<byte>(lengthPrefix), SocketFlags.None);

                // 3. Send the actual data
                if (data.Length > 0)
                {
                    await socket.SendAsync(new ArraySegment<byte>(data), SocketFlags.None);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException during send: {ex.Message}");
                // Potentially re-throw or handle as a disconnection
                throw;
            }
            catch (ObjectDisposedException ex)
            {
                Console.WriteLine($"ObjectDisposedException during send (socket closed): {ex.Message}");
                throw;
            }
        }

        public static async Task<byte[]> ReliableReceiveAsync(Socket socket)
        {
            if (socket == null || !socket.Connected)
            {
                // Return null or throw to indicate disconnection/error
                // Console.WriteLine("ReliableReceiveAsync: Socket is not connected or null.");
                return null;
            }

            byte[] lengthPrefixBuffer = new byte[LengthPrefixSize];
            int totalBytesReceivedForLength = 0;

            try
            {
                // 1. Receive the length prefix
                while (totalBytesReceivedForLength < LengthPrefixSize)
                {
                    int bytesReceived = await socket.ReceiveAsync(new ArraySegment<byte>(lengthPrefixBuffer, totalBytesReceivedForLength, LengthPrefixSize - totalBytesReceivedForLength), SocketFlags.None);
                    if (bytesReceived == 0)
                    {
                        // Connection closed gracefully by the remote host
                        // Console.WriteLine("ReliableReceiveAsync: Connection closed by remote host while receiving length.");
                        return null;
                    }
                    totalBytesReceivedForLength += bytesReceived;
                }

                int dataLength = BitConverter.ToInt32(lengthPrefixBuffer, 0);

                if (dataLength < 0) // Or some other sanity check for length
                {
                    // Invalid length received, could be data corruption or malicious client
                    Console.WriteLine($"ReliableReceiveAsync: Invalid data length received: {dataLength}. Closing connection.");
                    socket.Close();
                    return null;
                }

                if (dataLength == 0)
                {
                    return Array.Empty<byte>(); // No actual data payload, just length prefix
                }

                // 2. Receive the actual data
                byte[] dataBuffer = new byte[dataLength];
                int totalBytesReceivedForData = 0;
                while (totalBytesReceivedForData < dataLength)
                {
                    int bytesReceived = await socket.ReceiveAsync(new ArraySegment<byte>(dataBuffer, totalBytesReceivedForData, dataLength - totalBytesReceivedForData), SocketFlags.None);
                    if (bytesReceived == 0)
                    {
                        // Connection closed gracefully by the remote host
                        // Console.WriteLine("ReliableReceiveAsync: Connection closed by remote host while receiving data.");
                        return null;
                    }
                    totalBytesReceivedForData += bytesReceived;
                }
                return dataBuffer;
            }
            catch (SocketException ex)
            {
                // Connection forcibly closed or other socket error
                Console.WriteLine($"SocketException during receive: {ex.Message}");
                return null;
            }
            catch (ObjectDisposedException ex)
            {
                Console.WriteLine($"ObjectDisposedException during receive (socket closed): {ex.Message}");
                return null;
            }
        }
    }
}
