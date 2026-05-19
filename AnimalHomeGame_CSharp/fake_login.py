"""
fake_login.py — test helper ONLY (not part of the game)
Sends a fake face-login message so we can skip the Face Login
screen and reach the actual game to test the trackers.
"""

import socket

UDP_IP   = "127.0.0.1"
UDP_PORT = 5008          # MainForm listens for face login here

username = "TestPlayer"
msg = f"LOGIN:{username}"

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.sendto(msg.encode(), (UDP_IP, UDP_PORT))
sock.close()

print(f"Sent fake login for '{username}' to {UDP_IP}:{UDP_PORT}")
print("The game should now log in and open the menu.")