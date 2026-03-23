import socket

mySocket = socket.socket()

def connect_socket():
    global conn, addr
    mySocket.bind(("localhost", 5000))
    mySocket.listen(5)
    print("Waiting for C# connection on port 5000...")
    conn, addr = mySocket.accept()
    print("Device Connected from", addr)

connect_socket()

# Example of sending data
msg = bytes("Hello from the Python Server", "utf-8")
conn.send(msg)
msg = bytes("q", "utf-8")
conn.send(msg)

conn.close()
mySocket.close()
