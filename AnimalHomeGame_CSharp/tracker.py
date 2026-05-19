"""
tracker.py — FINAL VERSION
OBJECT + SKELETON + RED LASER
Animal Home Game
"""

import cv2
import socket
import json
import time
import sys

# =========================================================
# CONFIG
# =========================================================

UDP_IP = "127.0.0.1"
UDP_PORT = 5006

SMOOTH = 0.35
GRACE = 0.5

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# =========================================================
# YOLO CLASS MAP
# =========================================================

CLASS_TO_ANIMAL_ID = {
    "bird": 0,
    "dog": 1,
    "fish": 2,
    "cow": 3
}

# =========================================================

MIRROR_WIN = "Camera"
MENU_WIN = "Choose how to play!"

# =========================================================
# ZONES
# =========================================================

def zone_to_animal(ny):

    if ny < 0.25:
        return 0   # bird

    elif ny < 0.50:
        return 1   # dog

    elif ny < 0.75:
        return 2   # fish

    else:
        return 3   # cow

# =========================================================
# UDP EMITTER
# =========================================================

class Emitter:

    def __init__(self):

        self.held_id = None
        self.sx = None
        self.sy = None
        self.lost_since = None

    def _send(self, ev, aid, x, y):

        msg = {
            "event": ev,
            "id": aid,
            "x": round(float(x), 4),
            "y": round(float(y), 4)
        }

        sock.sendto(
            json.dumps(msg).encode(),
            (UDP_IP, UDP_PORT)
        )

    def update(self, nx, ny, forced_id=None):

        self.lost_since = None

        aid = forced_id if forced_id is not None else zone_to_animal(ny)

        # first grab
        if self.held_id is None:

            self.held_id = aid
            self.sx = nx
            self.sy = ny

            self._send("added", aid, nx, ny)

        # switch animal
        elif aid != self.held_id:

            self._send("removed", self.held_id, 0.0, 0.0)

            self.held_id = aid

            self.sx = nx
            self.sy = ny

            self._send("added", aid, nx, ny)

        # update same animal
        else:

            self.sx += SMOOTH * (nx - self.sx)
            self.sy += SMOOTH * (ny - self.sy)

            self._send(
                "update",
                self.held_id,
                self.sx,
                self.sy
            )

    def release(self):

        if self.held_id is None:
            return

        if self.lost_since is None:

            self.lost_since = time.time()
            return

        if time.time() - self.lost_since < GRACE:
            return

        self._send(
            "removed",
            self.held_id,
            0.0,
            0.0
        )

        self.held_id = None
        self.sx = None
        self.sy = None
        self.lost_since = None

# =========================================================

emitter = Emitter()

# =========================================================
# CAMERA PREVIEW
# =========================================================

def show_mirror(frame, label):

    small = cv2.resize(frame, (320, 240))

    cv2.putText(
        small,
        label,
        (10, 25),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        (0, 255, 255),
        2
    )

    cv2.imshow(MIRROR_WIN, small)

# =========================================================
# MENU
# =========================================================

def run_menu(cap):

    import mediapipe as mp

    hands = mp.solutions.hands.Hands(
        max_num_hands=1,
        min_detection_confidence=0.6
    )

    options = [
        ("OBJECT", "object", (60, 90, 245)),
        ("SKELETON", "skeleton", (90, 200, 60)),
        ("LASER", "laser", (245, 150, 40))
    ]

    hover_key = None
    hover_start = 0

    HOLD_TIME = 2.0

    cv2.namedWindow(MENU_WIN, cv2.WINDOW_NORMAL)

    while True:

        ok, frame = cap.read()

        if not ok:
            continue

        frame = cv2.flip(frame, 1)

        h, w = frame.shape[:2]

        overlay = frame.copy()

        cv2.rectangle(
            overlay,
            (0, 0),
            (w, h),
            (40, 20, 40),
            -1
        )

        frame = cv2.addWeighted(
            overlay,
            0.45,
            frame,
            0.55,
            0
        )

        cv2.putText(
            frame,
            "CHOOSE HOW TO PLAY!",
            (int(w * 0.12), 55),
            cv2.FONT_HERSHEY_DUPLEX,
            1.1,
            (255, 255, 255),
            3
        )

        bands = []

        for i, (label, key, color) in enumerate(options):

            y1 = int(h * (0.20 + i * 0.25))
            y2 = y1 + int(h * 0.18)

            bands.append((key, label, color, y1, y2))

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

        result = hands.process(rgb)

        hand_pos = None

        if result.multi_hand_landmarks:

            lm = result.multi_hand_landmarks[0].landmark[9]

            hand_pos = (
                int(lm.x * w),
                int(lm.y * h)
            )

        current_hover = None

        for key, label, color, y1, y2 in bands:

            hovered = (
                hand_pos is not None
                and y1 < hand_pos[1] < y2
            )

            bx1 = int(w * 0.15)
            bx2 = int(w * 0.85)

            pad = 14 if hovered else 0

            fill = (
                tuple(min(255, c + 40) for c in color)
                if hovered else color
            )

            cv2.rectangle(
                frame,
                (bx1 - pad, y1 - pad),
                (bx2 + pad, y2 + pad),
                fill,
                -1
            )

            cv2.rectangle(
                frame,
                (bx1 - pad, y1 - pad),
                (bx2 + pad, y2 + pad),
                (255, 255, 255),
                3
            )

            scale = 1.5 if hovered else 1.2

            cv2.putText(
                frame,
                label,
                (bx1 + 30, (y1 + y2) // 2 + 12),
                cv2.FONT_HERSHEY_DUPLEX,
                scale,
                (255, 255, 255),
                3
            )

            if hovered:

                current_hover = key

                if key == hover_key:

                    progress = min(
                        1.0,
                        (time.time() - hover_start) / HOLD_TIME
                    )

                    cv2.ellipse(
                        frame,
                        hand_pos,
                        (45, 45),
                        0,
                        -90,
                        -90 + int(360 * progress),
                        (0, 255, 255),
                        6
                    )

        if current_hover:

            if current_hover != hover_key:

                hover_key = current_hover
                hover_start = time.time()

            if time.time() - hover_start >= HOLD_TIME:

                hands.close()

                cv2.destroyWindow(MENU_WIN)

                return current_hover

        else:

            hover_key = None

        if hand_pos:

            cv2.circle(
                frame,
                hand_pos,
                10,
                (0, 255, 255),
                -1
            )

        cv2.putText(
            frame,
            "Hold your hand on a button for 2s",
            (int(w * 0.10), h - 25),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.7,
            (255, 255, 255),
            2
        )

        cv2.imshow(MENU_WIN, frame)

        if cv2.waitKey(1) & 0xFF == ord('q'):

            hands.close()
            sys.exit(0)

# =========================================================
# SKELETON
# =========================================================

def run_skeleton(cap):

    import mediapipe as mp

    hands = mp.solutions.hands.Hands(
        max_num_hands=1,
        min_detection_confidence=0.6,
        min_tracking_confidence=0.6
    )

    draw = mp.solutions.drawing_utils

    HC = mp.solutions.hands.HAND_CONNECTIONS

    print("[SKELETON] ready")

    while True:

        ok, frame = cap.read()

        if not ok:
            break

        frame = cv2.flip(frame, 1)

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

        result = hands.process(rgb)

        if result.multi_hand_landmarks:

            hand = result.multi_hand_landmarks[0]

            draw.draw_landmarks(frame, hand, HC)

            lm = hand.landmark[9]

            emitter.update(
                1.0 - lm.x,
                lm.y
            )

        else:

            emitter.release()

        show_mirror(frame, "SKELETON")

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    hands.close()

# =========================================================
# RED LASER
# =========================================================

def run_laser(cap):

    import numpy as np

    print("[RED LASER] ready")

    LOW1 = np.array([0, 120, 70])
    HIGH1 = np.array([10, 255, 255])

    LOW2 = np.array([170, 120, 70])
    HIGH2 = np.array([180, 255, 255])

    locked_id = None

    smooth_x = None
    smooth_y = None

    LASER_SMOOTH = 0.18

    while True:

        ok, frame = cap.read()

        if not ok:
            break

        frame = cv2.flip(frame, 1)

        h, w = frame.shape[:2]

        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)

        mask1 = cv2.inRange(hsv, LOW1, HIGH1)
        mask2 = cv2.inRange(hsv, LOW2, HIGH2)

        mask = mask1 | mask2

        mask = cv2.GaussianBlur(mask, (9, 9), 0)

        mask = cv2.erode(mask, None, iterations=2)
        mask = cv2.dilate(mask, None, iterations=2)

        contours, _ = cv2.findContours(
            mask,
            cv2.RETR_EXTERNAL,
            cv2.CHAIN_APPROX_SIMPLE
        )

        if contours:

            c = max(contours, key=cv2.contourArea)

            area = cv2.contourArea(c)

            if area > 25:

                M = cv2.moments(c)

                if M["m00"] != 0:

                    px = int(M["m10"] / M["m00"])
                    py = int(M["m01"] / M["m00"])

                    if smooth_x is None:

                        smooth_x = px
                        smooth_y = py

                    else:

                        smooth_x += LASER_SMOOTH * (px - smooth_x)
                        smooth_y += LASER_SMOOTH * (py - smooth_y)

                    sx = int(smooth_x)
                    sy = int(smooth_y)

                    nx = sx / w
                    ny = sy / h

                    if locked_id is None:
                        locked_id = zone_to_animal(ny)

                    emitter.update(
                        nx,
                        ny,
                        forced_id=locked_id
                    )

                    cv2.circle(
                        frame,
                        (sx, sy),
                        18,
                        (0, 255, 255),
                        3
                    )

                    animal_name = [
                        "BIRD",
                        "DOG",
                        "FISH",
                        "COW"
                    ][locked_id]

                    cv2.putText(
                        frame,
                        f"LOCKED: {animal_name}",
                        (20, 40),
                        cv2.FONT_HERSHEY_SIMPLEX,
                        1,
                        (0, 255, 255),
                        2
                    )

                else:

                    emitter.release()

                    locked_id = None
                    smooth_x = None
                    smooth_y = None

            else:

                emitter.release()

                locked_id = None
                smooth_x = None
                smooth_y = None

        else:

            emitter.release()

            locked_id = None
            smooth_x = None
            smooth_y = None

        show_mirror(frame, "RED LASER")

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

# =========================================================
# OBJECT MODE
# =========================================================

def run_object(cap):

    from ultralytics import YOLO

    model = YOLO("yolov8n.pt")

    print("[OBJECT] ready")

    while True:

        ok, frame = cap.read()

        if not ok:
            break

        frame = cv2.flip(frame, 1)

        results = model.track(
            frame,
            persist=True,
            verbose=False
        )[0]

        found = False

        for box in results.boxes:

            cls = model.names[int(box.cls)]

            if cls not in CLASS_TO_ANIMAL_ID:
                continue

            aid = CLASS_TO_ANIMAL_ID[cls]

            cx, cy = box.xywhn[0][:2].tolist()

            emitter.update(
                cx,
                cy,
                forced_id=aid
            )

            found = True

            x1, y1, x2, y2 = map(int, box.xyxy[0])

            cv2.rectangle(
                frame,
                (x1, y1),
                (x2, y2),
                (50, 200, 80),
                2
            )

            cv2.putText(
                frame,
                cls,
                (x1, max(y1 - 8, 14)),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.7,
                (50, 200, 80),
                2
            )

            break

        if not found:
            emitter.release()

        show_mirror(frame, "OBJECT")

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

# =========================================================
# MAIN
# =========================================================

def main():

    cap = cv2.VideoCapture(0)

    if not cap.isOpened():

        print("ERROR: camera not found")
        return

    mode = run_menu(cap)

    print("Mode:", mode)

    if mode == "object":

        run_object(cap)

    elif mode == "skeleton":

        run_skeleton(cap)

    elif mode == "laser":

        run_laser(cap)

    cap.release()

    cv2.destroyAllWindows()

    sock.close()

    print("Stopped.")

# =========================================================

if __name__ == "__main__":
    main()