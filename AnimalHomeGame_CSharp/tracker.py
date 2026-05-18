"""
tracker.py — Unified Multi-Modal Animal Tracker (FIXED VERSION)
"""

import cv2
import socket
import json
import time
import sys

# ─────────────────────────────────────────────
# CONFIG
# ─────────────────────────────────────────────
UDP_IP = "127.0.0.1"
UDP_PORT = 5006

SMOOTH = 0.35
GRACE_SECS = 0.6

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

def quadrant_to_animal(nx, ny):
    if ny < 0.5:
        return 0 if nx < 0.5 else 1
    else:
        return 2 if nx < 0.5 else 3

CLASS_TO_ANIMAL_ID = {
    "bird": 0,
    "dog": 1,
    "fish": 2,
    "sheep": 3,
}

MIRROR_WIN = "Camera"
MAIN_WIN = "Menu"


# ─────────────────────────────────────────────
# TRAJECTORY EMITTER (FIXED MULTI-ANIMAL)
# ─────────────────────────────────────────────
class TrajectoryEmitter:
    def __init__(self):
        self.states = {}

    def _send(self, event, animal_id, x, y):
        msg = json.dumps({
            "event": event,
            "id": animal_id,
            "x": round(float(x), 4),
            "y": round(float(y), 4),
        })
        sock.sendto(msg.encode(), (UDP_IP, UDP_PORT))

    def update_for(self, animal_id, nx, ny):
        if animal_id not in self.states:
            self.states[animal_id] = {
                "smooth_x": nx,
                "smooth_y": ny,
                "lost_since": None,
            }
            self._send("added", animal_id, nx, ny)
            return

        s = self.states[animal_id]
        s["lost_since"] = None

        s["smooth_x"] += SMOOTH * (nx - s["smooth_x"])
        s["smooth_y"] += SMOOTH * (ny - s["smooth_y"])

        self._send("update", animal_id, s["smooth_x"], s["smooth_y"])

    def lost_for(self, animal_id):
        if animal_id not in self.states:
            return

        s = self.states[animal_id]

        if s["lost_since"] is None:
            s["lost_since"] = time.time()
            return

        if time.time() - s["lost_since"] < GRACE_SECS:
            return

        self._send("removed", animal_id, 0.0, 0.0)
        del self.states[animal_id]

    def update(self, nx, ny):
        aid = quadrant_to_animal(nx, ny)

        for a in list(self.states.keys()):
            if a != aid:
                self._send("removed", a, 0.0, 0.0)
                del self.states[a]

        self.update_for(aid, nx, ny)

    def lost(self):
        for a in list(self.states.keys()):
            self.lost_for(a)


emitter = TrajectoryEmitter()


# ─────────────────────────────────────────────
# DRAW HELPERS
# ─────────────────────────────────────────────
def draw_zones(frame):
    h, w = frame.shape[:2]
    cv2.line(frame, (w//2, 0), (w//2, h), (100, 100, 100), 1)
    cv2.line(frame, (0, h//2), (w, h//2), (100, 100, 100), 1)


def show(frame, label):
    cv2.putText(frame, label, (10, 30),
                cv2.FONT_HERSHEY_SIMPLEX, 1, (0,255,255), 2)
    cv2.imshow(MIRROR_WIN, frame)


# ─────────────────────────────────────────────
# YOLO MODE
# ─────────────────────────────────────────────
def run_object(cap):
    from ultralytics import YOLO
    model = YOLO("yolov8n.pt")

    prev = set()

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        frame = cv2.flip(frame, 1)
        res = model.track(frame, persist=True, verbose=False)[0]

        current = set()

        for box in res.boxes:
            cls = model.names[int(box.cls)]
            if cls not in CLASS_TO_ANIMAL_ID:
                continue

            aid = CLASS_TO_ANIMAL_ID[cls]
            cx, cy = box.xywhn[0][:2].tolist()

            current.add(aid)
            emitter.update_for(aid, cx, cy)

            x1, y1, x2, y2 = map(int, box.xyxy[0])
            cv2.rectangle(frame, (x1,y1),(x2,y2),(0,255,0),2)

        for lost in prev - current:
            emitter.lost_for(lost)

        prev = current

        draw_zones(frame)
        show(frame, "OBJECT")

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break


# ─────────────────────────────────────────────
# SKELETON MODE
# ─────────────────────────────────────────────
def run_skeleton(cap):
    import mediapipe as mp

    hands = mp.solutions.hands.Hands(
        max_num_hands=1,
        min_detection_confidence=0.6
    )

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        frame = cv2.flip(frame, 1)
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

        res = hands.process(rgb)

        if res.multi_hand_landmarks:
            lm = res.multi_hand_landmarks[0].landmark[9]
            emitter.update(1-lm.x, lm.y)
        else:
            emitter.lost()

        draw_zones(frame)
        show(frame, "SKELETON")

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    hands.close()


# ─────────────────────────────────────────────
# LASER MODE
# ─────────────────────────────────────────────
def run_laser(cap):
    import numpy as np

    lower = np.array([40,80,80])
    upper = np.array([90,255,255])

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        frame = cv2.flip(frame, 1)
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)

        mask = cv2.inRange(hsv, lower, upper)

        cnts,_ = cv2.findContours(mask, cv2.RETR_EXTERNAL,
                                  cv2.CHAIN_APPROX_SIMPLE)

        if cnts:
            c = max(cnts, key=cv2.contourArea)

            if cv2.contourArea(c) > 30:
                M = cv2.moments(c)

                if M["m00"] != 0:
                    x = int(M["m10"]/M["m00"])
                    y = int(M["m01"]/M["m00"])

                    h,w = frame.shape[:2]
                    emitter.update(x/w, y/h)
                else:
                    emitter.lost()
            else:
                emitter.lost()
        else:
            emitter.lost()

        draw_zones(frame)
        show(frame, "LASER")

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break


# ─────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────
def main():
    cap = cv2.VideoCapture(0)

    print("Starting tracker...")

    mode = input("Choose mode (object / skeleton / laser): ")

    if mode == "object":
        run_object(cap)
    elif mode == "skeleton":
        run_skeleton(cap)
    elif mode == "laser":
        run_laser(cap)

    cap.release()
    cv2.destroyAllWindows()
    sock.close()


if __name__ == "__main__":
    main()