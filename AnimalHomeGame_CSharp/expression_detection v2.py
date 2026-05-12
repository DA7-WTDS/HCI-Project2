import cv2
import socket
from deepface import DeepFace

def main():
    print("Initializing camera...")
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Error: Could not open camera.")
        return

    # Existing socket (C# game emotions)
    udp_ip = "127.0.0.1"
    udp_port = 5005
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    # NEW: Unity sad flag socket
    unity_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    unity_port = 5006

    print(f"Broadcasting emotions to C# on port {udp_port}")
    print(f"Broadcasting sad flag to Unity on port {unity_port}")

    print("Camera opened successfully. Press 'q' to quit.")

    while True:
        ret, frame = cap.read()
        if not ret:
            print("Error: Could not read frame.")
            break
            
        try:
            results = DeepFace.analyze(
                img_path=frame, 
                actions=['emotion'], 
                enforce_detection=False,
                silent=True
            )
            
            if isinstance(results, list):
                result = results[0]
            else:
                result = results
                
            dominant_emotion = result.get('dominant_emotion', 'None')
            region = result.get('region', {})
            
            if region:
                x = region.get('x', 0)
                y = region.get('y', 0)
                w = region.get('w', 0)
                h = region.get('h', 0)
                cv2.rectangle(frame, (x, y), (x + w, y + h), (0, 255, 0), 2)
                cv2.putText(frame, dominant_emotion, (x, y - 10), 
                    cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 255, 0), 2, cv2.LINE_AA)
            else:
                cv2.putText(frame, dominant_emotion, (50, 50), 
                    cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2, cv2.LINE_AA)
                
            if dominant_emotion != 'None':
                # Send emotion to C# game (existing)
                sock.sendto(dominant_emotion.encode('utf-8'), (udp_ip, udp_port))

                # NEW: Send sad flag to Unity
                is_sad = dominant_emotion.lower() == "sad"
                unity_sock.sendto(str(is_sad).encode('utf-8'), (udp_ip, unity_port))
                
                # Show sad status on camera feed
                sad_text = "SAD: TRUE" if is_sad else "SAD: FALSE"
                sad_color = (0, 0, 255) if is_sad else (0, 255, 0)
                cv2.putText(frame, sad_text, (10, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, sad_color, 2, cv2.LINE_AA)

        except Exception as e:
            pass
            
        cv2.imshow('Facial Expression Detection', frame)
        
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    cap.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    main()