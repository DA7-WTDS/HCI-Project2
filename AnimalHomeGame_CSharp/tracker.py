"""
tracker.py — Unified multi-modal input tracker for Animal Home Game
====================================================================
Kid-friendly Hand Menu (no keyboard) lets you pick ONE input modality:

    1. OBJECT    -> YOLOv8 detects a real animal toy/picture
    2. SKELETON  -> MediaPipe tracks your hand
    3. LASER     -> OpenCV tracks a green laser dot

All three feed the SAME unified output layer, sending the SAME JSON
on the SAME UDP port (5006) that yolo_tracker.py uses, so the C# game
treats them identically to TUIO.

Animal switching is done by SCREEN ZONE (no keyboard): the camera
frame is split into 4 quadrants, one per animal.

Trajectory is SMOOTHED so the animal glides instead of jumping.
A GRACE PERIOD keeps the animal held during brief tracking dropouts.
Press Q in the window to quit.
"""

import cv2
import socket
import json
import time
import sys
import math

# ──────────────────────────────────────────────────────────────────────────
# CONFIG
# ──────────────────────────────────────────────────────────────────────────
UDP_IP   = "127.0.0.1"
UDP_PORT = 5006          # same port yolo_tracker.py uses

def quadrant_to_animal(nx, ny):
    if ny < 0.5:
        return 0 if nx < 0.5 else 1      # top-left Bird / top-right Dog
    else:
        return 2 if nx < 0.5 else 3      # bottom-left Fish / bottom-right Farm

SMOOTH = 0.35            # trajectory smoothing (0=frozen, 1=no smoothing)

CLASS_TO_ANIMAL_ID = {
    "bird":  0,
    "dog":   1,
    "fish":  2,
    "sheep": 3,
}

MIRROR_WIN = "Camera (mirror)"
MAIN_WIN   = "Choose how to play!"

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)


# ──────────────────────────────────────────────────────────────────────────
# UNIFIED OUTPUT LAYER  (grace period prevents premature "removed")
# ──────────────────────────────────────────────────────────────────────────
class TrajectoryEmitter:
    def __init__(self):
        self.active_id    = None
        self.smooth_x     = None
        self.smooth_y     = None
        self.lost_since   = None      # when the input first disappeared
        self.GRACE_SECS   = 0.6       # keep holding the animal this long

    def _send(self, event, animal_id, x, y):
        msg = json.dumps({
            "event": event,
            "id":    animal_id,
            "x":     round(float(x), 4),
            "y":     round(float(y), 4),
        })
        sock.sendto(msg.encode(), (UDP_IP, UDP_PORT))

    def update(self, nx, ny):
        self.lost_since = None        # input is visible again
        animal_id = quadrant_to_animal(nx, ny)
        if self.active_id is not None and animal_id != self.active_id:
            self._send("removed", self.active_id, 0.0, 0.0)
            self.active_id = None
            self.smooth_x = self.smooth_y = None
        if self.active_id is None:
            self.active_id = animal_id
            self.smooth_x, self.smooth_y = nx, ny
            self._send("added", animal_id, nx, ny)
        else:
            self.smooth_x += SMOOTH * (nx - self.smooth_x)
            self.smooth_y += SMOOTH * (ny - self.smooth_y)
            self._send("update", animal_id, self.smooth_x, self.smooth_y)

    def lost(self):
        if self.active_id is None:
            return
        if self.lost_since is None:
            self.lost_since = time.time()
            return
        if time.time() - self.lost_since < self.GRACE_SECS:
            return                    # still within grace — keep the animal
        self._send("removed", self.active_id, 0.0, 0.0)
        self.active_id = None
        self.smooth_x = self.smooth_y = None
        self.lost_since = None


emitter = TrajectoryEmitter()


# ──────────────────────────────────────────────────────────────────────────
# SMALL CORNER MIRROR
# ──────────────────────────────────────────────────────────────────────────
def show_mirror(frame, mode_label):
    small = cv2.resize(frame, (240, 180))
    cv2.putText(small, mode_label, (8, 20),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1)
    cv2.imshow(MIRROR_WIN, small)
    cv2.moveWindow(MIRROR_WIN, 10, 10)


def draw_zones(frame):
    h, w = frame.shape[:2]
    cv2.line(frame, (w // 2, 0), (w // 2, h), (80, 80, 80), 1)
    cv2.line(frame, (0, h // 2), (w, h // 2), (80, 80, 80), 1)


# ──────────────────────────────────────────────────────────────────────────
# KID-FRIENDLY HAND MENU
# ──────────────────────────────────────────────────────────────────────────
def run_hand_menu(cap):
    import mediapipe as mp
    mp_hands = mp.solutions.hands
    hands = mp_hands.Hands(max_num_hands=1, min_detection_confidence=0.6)

    options = [
        ("OBJECT",   "object",   "[O]", (60, 90, 245)),
        ("SKELETON", "skeleton", "[/]", (90, 200, 60)),
        ("LASER",    "laser",    "[*]", (245, 150, 40)),
    ]

    hover_key   = None
    hover_start = 0.0
    HOLD_SECS   = 2.0

    cv2.namedWindow(MAIN_WIN, cv2.WINDOW_NORMAL)

    while True:
        ret, frame = cap.read()
        if not ret:
            continue
        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]

        overlay = frame.copy()
        cv2.rectangle(overlay, (0, 0), (w, h), (40, 20, 40), -1)
        frame = cv2.addWeighted(overlay, 0.45, frame, 0.55, 0)

        cv2.putText(frame, "CHOOSE HOW TO PLAY!", (int(w * 0.13), 55),
                    cv2.FONT_HERSHEY_DUPLEX, 1.1, (255, 255, 255), 3)

        bands = []
        for i, (label, key, sym, color) in enumerate(options):
            y1 = int(h * (0.20 + i * 0.25))
            y2 = y1 + int(h * 0.18)
            bands.append((key, label, sym, color, y1, y2))

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        res = hands.process(rgb)
        hand_pt = None
        if res.multi_hand_landmarks:
            lm = res.multi_hand_landmarks[0].landmark[9]
            hand_pt = (int(lm.x * w), int(lm.y * h))

        current_key = None
        for key, label, sym, color, y1, y2 in bands:
            hovering = hand_pt is not None and y1 < hand_pt[1] < y2
            bx1, bx2 = int(w * 0.15), int(w * 0.85)
            pad = 14 if hovering else 0
            fill = tuple(min(255, c + 40) for c in color) if hovering else color
            cv2.rectangle(frame, (bx1 - pad, y1 - pad), (bx2 + pad, y2 + pad),
                          fill, -1)
            cv2.rectangle(frame, (bx1 - pad, y1 - pad), (bx2 + pad, y2 + pad),
                          (255, 255, 255), 3)
            txt_scale = 1.5 if hovering else 1.2
            cv2.putText(frame, f"{sym}  {label}",
                        (bx1 + 30, (y1 + y2) // 2 + 12),
                        cv2.FONT_HERSHEY_DUPLEX, txt_scale, (255, 255, 255), 3)
            if hovering:
                current_key = key
                if key == hover_key:
                    held = time.time() - hover_start
                    frac = min(1.0, held / HOLD_SECS)
                    cv2.ellipse(frame, hand_pt, (45, 45), 0, -90,
                                -90 + int(360 * frac), (0, 255, 255), 6)

        if current_key is not None:
            if current_key != hover_key:
                hover_key = current_key
                hover_start = time.time()
            if time.time() - hover_start >= HOLD_SECS:
                hands.close()
                cv2.destroyWindow(MAIN_WIN)
                return hover_key
        else:
            hover_key = None

        if hand_pt is not None:
            cv2.circle(frame, hand_pt, 10, (0, 255, 255), -1)

        cv2.putText(frame, "Hold your hand on a button for 2 seconds",
                    (int(w * 0.12), h - 25),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

        cv2.imshow(MAIN_WIN, frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            hands.close()
            sys.exit(0)


# ──────────────────────────────────────────────────────────────────────────
# MODALITY 1: OBJECT  (YOLO)
# ──────────────────────────────────────────────────────────────────────────
def run_object(cap):
    from ultralytics import YOLO
    model = YOLO("yolov8n.pt")
    print("[OBJECT] YOLO ready")
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        frame = cv2.flip(frame, 1)
        results = model.track(frame, persist=True, verbose=False)[0]
        found = False
        for box in results.boxes:
            cls_name = model.names[int(box.cls)]
            if cls_name not in CLASS_TO_ANIMAL_ID:
                continue
            cx, cy = box.xywhn[0][:2].tolist()
            emitter.update(cx, cy)
            found = True
            x1, y1, x2, y2 = map(int, box.xyxy[0])
            cv2.rectangle(frame, (x1, y1), (x2, y2), (50, 200, 80), 2)
            break
        if not found:
            emitter.lost()
        draw_zones(frame)
        show_mirror(frame, "OBJECT")
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break


# ──────────────────────────────────────────────────────────────────────────
# MODALITY 2: SKELETON  (MediaPipe hand)  — X axis fixed
# ──────────────────────────────────────────────────────────────────────────
def run_skeleton(cap):
    import mediapipe as mp
    mp_hands = mp.solutions.hands
    mp_draw  = mp.solutions.drawing_utils
    hands = mp_hands.Hands(max_num_hands=1, min_detection_confidence=0.6,
                           min_tracking_confidence=0.6)
    print("[SKELETON] MediaPipe ready")
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        res = hands.process(rgb)
        if res.multi_hand_landmarks:
            hand = res.multi_hand_landmarks[0]
            mp_draw.draw_landmarks(frame, hand, mp_hands.HAND_CONNECTIONS)
            lm = hand.landmark[9]
            emitter.update(1.0 - lm.x, lm.y)
        else:
            emitter.lost()
        draw_zones(frame)
        show_mirror(frame, "SKELETON")
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break
    hands.close()


# ──────────────────────────────────────────────────────────────────────────
# MODALITY 3: LASER  (green dot tracking)
# ──────────────────────────────────────────────────────────────────────────
def run_laser(cap):
    import numpy as np
    print("[LASER] tracking green dot")
    LOWER = np.array([40, 80, 80])
    UPPER = np.array([90, 255, 255])
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, LOWER, UPPER)
        mask = cv2.erode(mask, None, iterations=2)
        mask = cv2.dilate(mask, None, iterations=2)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL,
                                       cv2.CHAIN_APPROX_SIMPLE)
        if contours:
            c = max(contours, key=cv2.contourArea)
            if cv2.contourArea(c) > 30:
                M = cv2.moments(c)
                if M["m00"] != 0:
                    px = int(M["m10"] / M["m00"])
                    py = int(M["m01"] / M["m00"])
                    emitter.update(px / w, py / h)
                    cv2.circle(frame, (px, py), 12, (0, 0, 255), 2)
                else:
                    emitter.lost()
            else:
                emitter.lost()
        else:
            emitter.lost()
        draw_zones(frame)
        show_mirror(frame, "LASER")
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break


# ──────────────────────────────────────────────────────────────────────────
# MAIN
# ──────────────────────────────────────────────────────────────────────────
def main():
    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("ERROR: Cannot open camera (index 0). Try index 1.")
        return

    print("Opening Hand Menu — hover a button for 2 seconds to choose.")
    mode = run_hand_menu(cap)
    print(f"Selected mode: {mode}")

    if   mode == "object":
        run_object(cap)
    elif mode == "skeleton":
        run_skeleton(cap)
    elif mode == "laser":
        run_laser(cap)

    cap.release()
    cv2.destroyAllWindows()
    sock.close()
    print("Tracker stopped.")


if __name__ == "__main__":
    main()