import cv2
import socket
import os
import math
import time
import glob
from deepface import DeepFace
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision

# Hand skeleton connection pairs for drawing (replaces mp.solutions.hands.HAND_CONNECTIONS)
HAND_CONNECTIONS = [
    (0,1),(1,2),(2,3),(3,4),        # Thumb
    (0,5),(5,6),(6,7),(7,8),        # Index
    (0,9),(9,10),(10,11),(11,12),   # Middle
    (0,13),(13,14),(14,15),(15,16), # Ring
    (0,17),(17,18),(18,19),(19,20), # Pinky
    (5,9),(9,13),(13,17)            # Palm
]

# =========================================================================
# ORIGINAL LOGIC PRESERVED: CircularMenuController from Bluetooth.ipynb
# Only change: landmarks is now a plain list, so landmarks[i] instead of
# landmarks.landmark[i] — everything else is identical.
# =========================================================================
class CircularMenuController:
    def __init__(self):
        self.state = "HIDDEN"
        self.current_selection = None
        self.last_hand_time = time.time()
        self.DROP_TIMEOUT = 0.8

    def _get_open_fingers(self, landmarks):
        tips, pips = [8, 12, 16, 20], [6, 10, 14, 18]
        open_count = 0
        for tip, pip in zip(tips, pips):
            if landmarks[tip].y < landmarks[pip].y:
                open_count += 1
        return open_count

    def _get_menu_slice(self, landmarks):
        # Invert DX to handle the mirrored camera view
        dx = -(landmarks[9].x - landmarks[0].x)
        dy = landmarks[0].y - landmarks[9].y

        angle = math.degrees(math.atan2(dy, dx))
        if angle < 0: angle += 360

        if 60 <= angle <= 120:                          return "Hint"
        elif 120 < angle <= 240:                        return "Restart"
        elif 240 < angle <= 360 or 0 <= angle < 60:    return "Logout"
        return self.current_selection

    def process_hand(self, landmarks):
        if landmarks is None:
            if self.state == "ACTIVE" and (time.time() - self.last_hand_time) > self.DROP_TIMEOUT:
                self.state = "HIDDEN"
                return "CANCEL"
            return None

        self.last_hand_time = time.time()
        open_f = self._get_open_fingers(landmarks)

        if self.state == "HIDDEN" and open_f >= 3:
            self.state = "ACTIVE"
            self.current_selection = self._get_menu_slice(landmarks)
            return f"OPEN_MENU:{self.current_selection}"
        elif self.state == "ACTIVE":
            selection = self._get_menu_slice(landmarks)
            if open_f <= 1:  # Fist to select
                self.state = "HIDDEN"
                res = self.current_selection
                self.current_selection = None
                return f"SELECT:{res}"
            if selection != self.current_selection:
                self.current_selection = self.current_selection
                return f"HOVER:{selection}"
        return None


# =========================================================================
# MAIN AI LOOP
# =========================================================================
def main():
    print("Initializing Unified AI Vision System...")

    # Ensure faces directory exists
    FACES_DIR = os.path.join("Assets", "faces")
    if not os.path.exists(FACES_DIR):
        os.makedirs(FACES_DIR)
        print(f"Created {FACES_DIR} directory. Put user photos here for recognition.")

    # Setup UDP Sockets
    udp_ip = "127.0.0.1"
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    # Ports matching C#
    PORT_EMOTION   = 5005
    PORT_HAND_MENU = 5007
    PORT_LOGIN     = 5008

    # Initialize Camera
    cap = cv2.VideoCapture(0)  # Change to 1 if using a secondary/mobile camera
    if not cap.isOpened():
        print("Error: Could not open camera.")
        return

    # -----------------------------------------------------------------------
    # Initialize MediaPipe Hand Landmarker (Tasks API — mediapipe 0.10+)
    # The .task model file must be in the same folder as this script.
    # -----------------------------------------------------------------------
    MODEL_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "hand_landmarker.task")
    if not os.path.exists(MODEL_PATH):
        print(f"ERROR: Model file not found: {MODEL_PATH}")
        print("Download from: https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task")
        cap.release()
        return

    hand_options = mp_vision.HandLandmarkerOptions(
        base_options=mp_python.BaseOptions(model_asset_path=MODEL_PATH),
        num_hands=1,
        min_hand_detection_confidence=0.7,
        min_hand_presence_confidence=0.7,
        min_tracking_confidence=0.7,
        running_mode=mp_vision.RunningMode.VIDEO
    )
    hand_landmarker = mp_vision.HandLandmarker.create_from_options(hand_options)

    menu = CircularMenuController()

    # We only want to run DeepFace periodically because it is slow
    frame_count = 0
    timestamp_ms = 0
    last_registration_time = 0

    print("AI System Started. Press 'q' to quit.")

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # Flip the frame immediately so the display and tracking match
        frame = cv2.flip(frame, 1)

        frame_count += 1
        timestamp_ms += 33  # ~30 fps
        display_frame = frame.copy()

        # -----------------------------------------------------------------
        # 1. MediaPipe Hand Tracking & Menu  (logic from Bluetooth.ipynb)
        # -----------------------------------------------------------------
        rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_frame)

        hand_result = hand_landmarker.detect_for_video(mp_image, timestamp_ms)

        # hand_result.hand_landmarks is a list of landmark lists (one per hand)
        landmarks = hand_result.hand_landmarks[0] if hand_result.hand_landmarks else None

        command = menu.process_hand(landmarks)
        if command:
            try:
                sock.sendto(command.encode('utf-8'), (udp_ip, PORT_HAND_MENU))
            except Exception:
                pass

        # Draw hand skeleton with plain OpenCV
        if hand_result.hand_landmarks:
            h, w = display_frame.shape[:2]
            for hand_lm in hand_result.hand_landmarks:
                for start_idx, end_idx in HAND_CONNECTIONS:
                    x1, y1 = int(hand_lm[start_idx].x * w), int(hand_lm[start_idx].y * h)
                    x2, y2 = int(hand_lm[end_idx].x * w),   int(hand_lm[end_idx].y * h)
                    cv2.line(display_frame, (x1, y1), (x2, y2), (0, 200, 255), 2)
                for lm in hand_lm:
                    cx, cy = int(lm.x * w), int(lm.y * h)
                    cv2.circle(display_frame, (cx, cy), 5, (255, 255, 255), -1)
                    cv2.circle(display_frame, (cx, cy), 5, (0, 150, 255),   1)

        # -----------------------------------------------------------------
        # 2. DeepFace Facial Expressions & Login  (from expression_detection.py)
        # Run every 5th frame to preserve performance
        # -----------------------------------------------------------------
        if frame_count % 5 == 0:
            try:
                results = DeepFace.analyze(
                    img_path=frame,
                    actions=['emotion'],
                    enforce_detection=True,
                    silent=True
                )

                result = results[0] if isinstance(results, list) else results

                dominant_emotion = result.get('dominant_emotion', 'None')
                region = result.get('region', {})

                if region:
                    x = region.get('x', 0)
                    y = region.get('y', 0)
                    w = region.get('w', 0)
                    h = region.get('h', 0)
                    cv2.rectangle(display_frame, (x, y), (x + w, y + h), (0, 255, 0), 2)
                    cv2.putText(display_frame, dominant_emotion, (x, y - 10),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 255, 0), 2, cv2.LINE_AA)

                    # Face recognition — check if user is known
                    existing_faces = glob.glob(os.path.join(FACES_DIR, "*.jpg"))
                    recognized = False

                    if len(existing_faces) > 0:
                        df_list = DeepFace.find(img_path=frame, db_path=FACES_DIR,
                                                enforce_detection=True, silent=True)
                        if len(df_list) > 0 and len(df_list[0]) > 0:
                            matched_path = df_list[0].iloc[0]['identity']
                            username = os.path.splitext(os.path.basename(matched_path))[0]
                            sock.sendto(f"LOGIN:{username}".encode('utf-8'), (udp_ip, PORT_LOGIN))
                            cv2.putText(display_frame, f"User: {username}", (x, y + h + 20),
                                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 0, 0), 2, cv2.LINE_AA)
                            recognized = True
                    
                    if not recognized:
                        # Auto-register unrecognized user (with 5 second cooldown to prevent spam)
                        if w > 0 and h > 0 and (time.time() - last_registration_time > 5.0):
                            new_user_num = len(existing_faces) + 1
                            new_path = os.path.join(FACES_DIR, f"User_{new_user_num}.jpg")
                            # Save the FULL frame, not the cropped face. 
                            # DeepFace's detector fails on tightly cropped images.
                            cv2.imwrite(new_path, frame)
                            print(f"Auto-registered new user to {new_path}")
                            last_registration_time = time.time()
                            
                            # Clear DeepFace cache so the new user is recognized next time
                            for pkl in glob.glob(os.path.join(FACES_DIR, "*.pkl")):
                                try:
                                    os.remove(pkl)
                                except:
                                    pass

                if dominant_emotion != 'None':
                    sock.sendto(dominant_emotion.encode('utf-8'), (udp_ip, PORT_EMOTION))

            except Exception:
                pass  # Ignore DeepFace exceptions (same as original)

        # Display
        cv2.imshow('Unified AI Vision', display_frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    hand_landmarker.close()
    cap.release()
    cv2.destroyAllWindows()
    sock.close()


if __name__ == "__main__":
    main()
