import cv2
import socket
from deepface import DeepFace
def main():
    # Initialize video capture using default camera (0)
    print("Initializing camera...")
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Error: Could not open camera.")
        return

    # Setup UDP socket for sending emotion data to C# game
    udp_ip = "127.0.0.1"
    udp_port = 5005
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"Broadcasting emotions to {udp_ip}:{udp_port}")

    print("Camera opened successfully. Press 'q' to quit.")

    while True:
        # Read frame from camera
        ret, frame = cap.read()
        if not ret:
            print("Error: Could not read frame.")
            break
            
        try:
            # Analyze frame for facial expressions using DeepFace
            # enforce_detection=False prevents it from throwing an exception if no face is found
            results = DeepFace.analyze(
                img_path=frame, 
                actions=['emotion'], 
                enforce_detection=False,
                silent=True
            )
            
            # DeepFace returns a list if multiple faces are found
            if isinstance(results, list):
                result = results[0]
            else:
                result = results
                
            # Get the dominant emotion and its region
            dominant_emotion = result.get('dominant_emotion', 'None')
            region = result.get('region', {})
            
            # Draw bounding box and emotion label on the frame
            if region:
                x = region.get('x', 0)
                y = region.get('y', 0)
                w = region.get('w', 0)
                h = region.get('h', 0)
                
                # Draw rectangle around face
                cv2.rectangle(frame, (x, y), (x + w, y + h), (0, 255, 0), 2)
                
                # Put the emotion text above the rectangle
                cv2.putText(
                    frame, 
                    dominant_emotion, 
                    (x, y - 10), 
                    cv2.FONT_HERSHEY_SIMPLEX, 
                    0.9, 
                    (0, 255, 0), 
                    2, 
                    cv2.LINE_AA
                )
            else:
                # Fallback if no specific region is provided but emotion is detected
                cv2.putText(frame, dominant_emotion, (50, 50), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2, cv2.LINE_AA)
                
            # Send the emotion over UDP to the C# Game
            if dominant_emotion != 'None':
                try:
                    sock.sendto(dominant_emotion.encode('utf-8'), (udp_ip, udp_port))
                except Exception as e:
                    # Ignore socket errors to prevent crashing the camera feed
                    pass
                
        except Exception as e:
            # If DeepFace encounters an unexpected error, just display it or ignore it
            # print(f"Error analyzing frame: {e}")
            pass
            
        # Display the resulting frame
        cv2.imshow('Facial Expression Detection', frame)
        
        # Break the loop if 'q' is pressed
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    # Release resources
    cap.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    main()
