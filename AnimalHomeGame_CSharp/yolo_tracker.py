import cv2
import socket
import json
from ultralytics import YOLO

# ---------------------------------------------------------------------------
# CLASS_TO_ANIMAL_ID  — maps YOLO class names to game animal IDs
#   IDs must match AnimalDefs in GamePlayForm.cs:
#     0 = Bird  |  1 = Dog  |  2 = Fish  |  3 = Farm animal
#
# YOLOv8n already knows: bird, dog, cat, fish, sheep, cow, horse, bear …
# Use printed-out animal pictures or real toys — no custom training needed.
# ---------------------------------------------------------------------------
CLASS_TO_ANIMAL_ID = {
    "bird":  0,
    "dog":   1,
    "fish":  2,
    "cow": 3,   # swap for "cow" / "horse" / "bear" if that's your farm toy
}



UDP_IP   = "127.0.0.1"
UDP_PORT = 5006          # separate port — won't clash with DeepFace (5005) or TUIO (3333)

sock  = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
model = YOLO("yolov8n.pt")   # downloaded automatically on first run (~6 MB)
cap   = cv2.VideoCapture(0)  # change to 1 if this conflicts with expression_detection.py

prev_visible: set[int] = set()   # animal IDs visible in the previous frame

# Badge colours for the OpenCV preview window (BGR)
SOURCE_COLOR = (50, 200, 80)   # green

print(f"[YOLO tracker] broadcasting to {UDP_IP}:{UDP_PORT}  — press Q to quit")

while True:
    ret, frame = cap.read()
    if not ret:
        print("[YOLO tracker] Camera read failed.")
        break

    # persist=True keeps the same track-ID across frames (ByteTrack)
    results = model.track(frame, persist=True, verbose=False)[0]

    current_visible: set[int] = set()

    for box in results.boxes:
        cls_name = model.names[int(box.cls)]
        if cls_name not in CLASS_TO_ANIMAL_ID:
            continue

        animal_id = CLASS_TO_ANIMAL_ID[cls_name]
        # Normalised centre of the bounding box (0.0–1.0)
        cx, cy = box.xywhn[0][:2].tolist()

        current_visible.add(animal_id)

        # "added" on first detection, "update" on every subsequent frame
        event = "added" if animal_id not in prev_visible else "update"
        msg   = json.dumps({"event": event, "id": animal_id, "x": round(cx, 4), "y": round(cy, 4)})
        sock.sendto(msg.encode(), (UDP_IP, UDP_PORT))

        # Draw on preview window
        x1, y1, x2, y2 = map(int, box.xyxy[0])
        cv2.rectangle(frame, (x1, y1), (x2, y2), SOURCE_COLOR, 2)
        cv2.putText(
            frame,
            f"{cls_name} [id={animal_id}]  ({cx:.2f}, {cy:.2f})",
            (x1, max(y1 - 8, 14)),
            cv2.FONT_HERSHEY_SIMPLEX, 0.55, SOURCE_COLOR, 2
        )

    # Fire "removed" for any animal that disappeared this frame
    for gone_id in prev_visible - current_visible:
        msg = json.dumps({"event": "removed", "id": gone_id, "x": 0.0, "y": 0.0})
        sock.sendto(msg.encode(), (UDP_IP, UDP_PORT))

    prev_visible = current_visible

    cv2.imshow("YOLO Object Tracker", frame)
    if cv2.waitKey(1) & 0xFF == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()
sock.close()
print("[YOLO tracker] stopped.")
