import cv2
import socket
import os
import math
import time
import glob
import threading
import numpy as np
from collections import deque
from deepface import DeepFace
import mediapipe as mp
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision as mp_vision
# =========================================================================
# GAZE TRACKING  (MediaPipe Face Landmarker — Tasks API, iris landmarks)
# Uses the same Tasks API already used for hand tracking — mp.solutions
# was removed in newer mediapipe versions so we use mp_vision directly.
# Auto-downloads face_landmarker.task on first run (~1.8 MB).
#
# Iris landmark indices (478-point model):
#   Left iris centre = 468, Right iris centre = 473
#   Left  eye corners: inner=133, outer=33
#   Right eye corners: inner=362, outer=263
#   ratio ~ 0.0 → camera-right (user looking left)
#   ratio ~ 1.0 → camera-left  (user looking right)
#   ratio ~ 0.5 → centre
# =========================================================================
class FaceMeshGazeTracker:
    """Gaze tracker using MediaPipe Face Landmarker Tasks API (iris landmarks)."""

    _L_IRIS  = 468
    _R_IRIS  = 473
    _L_INNER = 133
    _L_OUTER = 33
    _R_INNER = 362
    _R_OUTER = 263

    _MODEL_URL = (
        "https://storage.googleapis.com/mediapipe-models/"
        "face_landmarker/face_landmarker/float16/1/face_landmarker.task"
    )

    def __init__(self):
        script_dir = os.path.dirname(os.path.abspath(__file__))
        model_path = os.path.join(script_dir, "face_landmarker.task")

        if not os.path.exists(model_path):
            print("[GAZE] face_landmarker.task not found — downloading (~1.8 MB)...")
            try:
                import urllib.request
                urllib.request.urlretrieve(self._MODEL_URL, model_path)
                print(f"[GAZE] Saved to {model_path}")
            except Exception as exc:
                raise RuntimeError(
                    f"Download failed: {exc}\n"
                    f"Manually download from:\n  {self._MODEL_URL}\n"
                    f"Save it to: {model_path}"
                ) from exc

        options = mp_vision.FaceLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=model_path),
            output_face_blendshapes=False,
            output_facial_transformation_matrixes=False,
            num_faces=1,
            min_face_detection_confidence=0.5,
            min_face_presence_confidence=0.5,
            min_tracking_confidence=0.5,
            running_mode=mp_vision.RunningMode.VIDEO,
        )
        self._landmarker    = mp_vision.FaceLandmarker.create_from_options(options)
        self._timestamp_ms  = 0
        self._frame         = None
        self._ratio         = None
        self._ratio_y       = None   # nose-tip Y as vertical estimate
        self._annotated     = None

    def refresh(self, frame):
        """Process a new BGR frame."""
        self._frame     = frame.copy()
        self._annotated = frame.copy()
        self._ratio     = None
        self._ratio_y   = None
        self._timestamp_ms += 33   # ~30 fps

        h, w = frame.shape[:2]
        rgb    = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_img = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        result = self._landmarker.detect_for_video(mp_img, self._timestamp_ms)

        if not result.face_landmarks:
            return

        lm = result.face_landmarks[0]

        def pt(idx):
            return int(lm[idx].x * w), int(lm[idx].y * h)

        ratios = []
        for iris_idx, inner_idx, outer_idx in [
            (self._L_IRIS,  self._L_INNER, self._L_OUTER),
            (self._R_IRIS,  self._R_INNER, self._R_OUTER),
        ]:
            ix      = lm[iris_idx].x
            inner_x = lm[inner_idx].x
            outer_x = lm[outer_idx].x
            eye_w   = abs(outer_x - inner_x)
            if eye_w < 1e-4:
                continue
            left_x = min(inner_x, outer_x)
            ratio  = (ix - left_x) / eye_w
            ratios.append(ratio)
            cv2.circle(self._annotated, pt(iris_idx), 4, (0, 255, 255), -1)

        if ratios:
            self._ratio = sum(ratios) / len(ratios)

        # ── Vertical estimate: nose-tip landmark (index 1) Y coordinate ──
        # Normalised 0 (top of frame) → 1 (bottom). Closer to 0 = user
        # tilting head up / looking up; closer to 1 = looking down.
        # This is a head-pose proxy, not true vertical iris tracking.
        try:
            self._ratio_y = lm[1].y   # nose tip, already 0-1 normalised
        except Exception:
            self._ratio_y = None

    def horizontal_ratio(self):
        """Return averaged iris ratio (0–1), or None if no face detected."""
        return self._ratio

    def vertical_ratio(self):
        """Return nose-tip Y ratio (0–1) as vertical gaze proxy, or None."""
        return self._ratio_y

    def annotated_frame(self):
        """Return frame with iris dots drawn."""
        if self._annotated is not None:
            return self._annotated
        return self._frame

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

        # Three equal 120° zones — matching the visual pie drawn in C#:
        #   Hint   : 30°–150°  (centre 90°  = hand pointing UP)
        #   Restart: 150°–270° (centre 210° = hand pointing DOWN-LEFT)
        #   Logout : 270°–360° + 0°–30° (centre 330° = hand pointing RIGHT)
        if 30 <= angle < 150:                       return "Hint"
        elif 150 <= angle < 270:                    return "Restart"
        else:                                       return "Logout"  # 270-360 + 0-30

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
# HEATMAP SAVE HELPER
# =========================================================================
def _save_heatmap(heatmap_accum: np.ndarray, player_name: str) -> None:
    """
    Render the accumulated float heatmap and save it as a PNG to
    Desktop\\GazeHeatmaps\\gaze_<player>_<timestamp>.png.
    """
    try:
        fh, fw = heatmap_accum.shape[:2]

        # 1. Blur + normalise + colourmap
        blurred  = cv2.GaussianBlur(heatmap_accum, (0, 0), sigmaX=25)
        norm     = cv2.normalize(blurred, None, 0, 255, cv2.NORM_MINMAX)
        norm_u8  = norm.astype(np.uint8)
        heat_img = cv2.applyColorMap(norm_u8, cv2.COLORMAP_JET)

        # 2. Black canvas → blend heatmap at 70% opacity
        canvas = np.zeros((fh, fw, 3), dtype=np.uint8)
        mask   = (norm_u8 > 5).astype(np.float32)[:, :, np.newaxis]
        canvas = (canvas * (1 - mask * 0.7) + heat_img * (mask * 0.7)).astype(np.uint8)

        # 3. Colour-scale legend bar (bottom-right)
        leg_w, leg_h = 200, 18
        leg_x, leg_y = fw - leg_w - 10, fh - 32
        for i in range(leg_w):
            t   = i / leg_w
            val = int(t * 255)
            col = cv2.applyColorMap(np.array([[[val]]], dtype=np.uint8), cv2.COLORMAP_JET)[0, 0].tolist()
            canvas[leg_y:leg_y + leg_h, leg_x + i] = col
        cv2.rectangle(canvas, (leg_x, leg_y), (leg_x + leg_w, leg_y + leg_h), (255, 255, 255), 1)
        cv2.putText(canvas, "Low",  (leg_x - 28, leg_y + 13), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (200, 200, 200), 1)
        cv2.putText(canvas, "High", (leg_x + leg_w + 3, leg_y + 13), cv2.FONT_HERSHEY_SIMPLEX, 0.4, (200, 200, 200), 1)
        cv2.putText(canvas, "Gaze Heatmap", (leg_x + 40, leg_y - 6), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 255, 255), 1)

        # 4. Timestamp + player watermark
        import datetime
        stamp = f"{player_name}  {datetime.datetime.now().strftime('%Y-%m-%d %H:%M:%S')}"
        cv2.putText(canvas, stamp, (8, fh - 8), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (220, 220, 220), 1)

        # 5. Save
        desktop  = os.path.join(os.path.expanduser("~"), "Desktop")
        save_dir = os.path.join(desktop, "GazeHeatmaps")
        os.makedirs(save_dir, exist_ok=True)
        safe_name = "".join(c for c in player_name if c.isalnum() or c in "-_") or "Player"
        ts        = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        filepath  = os.path.join(save_dir, f"gaze_{safe_name}_{ts}.png")
        cv2.imwrite(filepath, canvas)
        print(f"[HEATMAP] Saved → {filepath}")
    except Exception as exc:
        print(f"[HEATMAP] Save failed: {exc}")


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
    PORT_WIN       = 5009   # C# sends WIN:<PlayerName> here when game is won

    # ── Win-signal listener (background thread) ───────────────────────────
    # Set by the listener thread; main loop checks and saves heatmap.
    _win_event      = threading.Event()
    _win_player     = ["Player"]   # mutable container for thread-safe name passing

    def _listen_for_win():
        try:
            srv = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            srv.bind(("127.0.0.1", PORT_WIN))
            srv.settimeout(1.0)
            while not _win_event.is_set():
                try:
                    data, _ = srv.recvfrom(256)
                    msg = data.decode("utf-8", errors="ignore")
                    if msg.startswith("WIN:"):
                        _win_player[0] = msg[4:].strip() or "Player"
                        _win_event.set()
                except socket.timeout:
                    pass
            srv.close()
        except Exception as e:
            print(f"[WIN-listener] {e}")

    threading.Thread(target=_listen_for_win, daemon=True).start()

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
    last_emotion = "none"

    # Initialize Gaze Tracker (MediaPipe FaceMesh iris landmarks)
    gaze = FaceMeshGazeTracker()
    print("FaceMeshGazeTracker initialized (MediaPipe iris landmarks).")

    # ── Lighting smoothing (rolling average + hysteresis) ─────────────────
    # Avoids flickering between day/night in a normally-lit room.
    # Switch to "dark" only when the 60-frame average brightness drops below 55.
    # Switch back to "bright" only when average rises above 90.
    lighting_history = deque(maxlen=60)   # ~2 seconds at 30 fps
    lighting_state   = "bright"           # start assuming a lit room

    # ── Gaze smoothing ──────────────────────────────────────────────────────
    gaze_history = deque(maxlen=12)   # ~0.4 sec at 30 fps
    gaze_dir     = "Center"           # last stable gaze direction
    menu_active  = False              # True while the hand menu is open
    gaze_frame   = 0                  # frame counter for gaze sub-sampling

    # ── Live gaze heatmap accumulation buffer ──────────────────────────────
    heatmap_accum = None   # float32 ndarray (frame_h, frame_w), lazy-init on first frame

    print("AI System Started. Press 'q' to quit.")

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        # Flip the frame immediately so the display and tracking match
        frame = cv2.flip(frame, 1)

        # ── Gaze Tracking ─────────────────────────────────────────────────
        # Skip entirely when hand menu is open (user isn't looking at animals).
        # Also run only every 2nd frame to halve the Face Landmarker load.
        gaze_frame += 1
        ratio   = None
        ratio_y = None
        if not menu_active and (gaze_frame % 2 == 0):
            gaze.refresh(frame)
            ratio   = gaze.horizontal_ratio()
            ratio_y = gaze.vertical_ratio()

            if ratio is not None:
                if ratio > 0.53:
                    raw_dir = "Left"
                elif ratio < 0.47:
                    raw_dir = "Right"
                else:
                    raw_dir = "Center"
                gaze_history.append(raw_dir)

                counts = {"Left": 0, "Center": 0, "Right": 0}
                for d in gaze_history:
                    counts[d] += 1
                gaze_dir = max(counts, key=lambda k: counts[k])

        # ── Accumulate gaze into heatmap buffer ───────────────────────────
        fh, fw = frame.shape[:2]
        if heatmap_accum is None:
            heatmap_accum = np.zeros((fh, fw), dtype=np.float32)

        if ratio is not None and ratio_y is not None:
            # Flip X: iris ratio 1 = left = low screen X → invert
            cx = int((1.0 - ratio)   * (fw - 1))
            cy = int(ratio_y         * (fh - 1))
            cx = max(0, min(fw - 1, cx))
            cy = max(0, min(fh - 1, cy))
            heatmap_accum[cy, cx] += 1.0

        # ── Ambient Lighting Detection (rolling average + hysteresis) ──────
        gray_light = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        mean_brightness = gray_light.mean()
        lighting_history.append(mean_brightness)
        avg_brightness = sum(lighting_history) / len(lighting_history)

        # Hysteresis: only flip state when average clearly crosses threshold.
        # This prevents a single dark/bright frame from causing day↔night chaos.
        if lighting_state == "bright" and avg_brightness < 55:
            lighting_state = "dark"
        elif lighting_state == "dark" and avg_brightness > 90:
            lighting_state = "bright"

        frame_count += 1
        timestamp_ms += 33  # ~30 fps
        _annotated = gaze.annotated_frame()
        display_frame = (_annotated if _annotated is not None else frame).copy()

        # (Heatmap is accumulated silently and saved on WIN signal — no live overlay)

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
            # Track menu state so we can pause gaze when menu is open
            if command.startswith("OPEN_MENU:"):
                menu_active = True
            elif command.startswith("SELECT:") or command == "CANCEL":
                menu_active = False

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
                    last_emotion = dominant_emotion

            except Exception:
                pass  # Ignore DeepFace exceptions (same as original)

        # ── Gaze & Lighting OSD overlays ──────────────────────────────────
        ratio_str = f"{ratio:.3f}" if ratio is not None else "n/a"
        cv2.putText(display_frame, f"Gaze: {gaze_dir}  ratio={ratio_str}", (20, 40),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)
        cv2.putText(display_frame, f"Lighting: {lighting_state} (avg={avg_brightness:.1f})", (20, 70),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 0), 2)

        # ── Send Unified UDP Payload (every frame, port 5005) ─────────────
        gaze_x_str = f"{ratio:.4f}"   if ratio   is not None else "0.5"
        gaze_y_str = f"{ratio_y:.4f}" if ratio_y is not None else "0.5"
        unified_payload = (
            f"EMOTION:{last_emotion}"
            f"|GAZE:{gaze_dir}"
            f"|GAZE_X:{gaze_x_str}"
            f"|GAZE_Y:{gaze_y_str}"
            f"|LIGHTING:{lighting_state}"
        )
        try:
            sock.sendto(unified_payload.encode('utf-8'), (udp_ip, PORT_EMOTION))
        except Exception:
            pass

        # Display
        cv2.imshow('Unified AI Vision', display_frame)

        # ── Check for win signal → save heatmap ───────────────────────────
        if _win_event.is_set() and heatmap_accum is not None:
            _save_heatmap(heatmap_accum, _win_player[0])
            heatmap_accum = np.zeros_like(heatmap_accum)  # reset for next round
            _win_event.clear()

        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    hand_landmarker.close()
    cap.release()
    cv2.destroyAllWindows()
    sock.close()


if __name__ == "__main__":
    main()
