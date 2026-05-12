# debug_yolo.py
import cv2
print("OpenCV version:", cv2.__version__)

cap = cv2.VideoCapture(0)
print("Camera opened:", cap.isOpened())

ret, frame = cap.read()
print("Frame read:", ret)
if ret:
    print("Frame shape:", frame.shape)
    cv2.imshow("Camera Test", frame)
    cv2.waitKey(3000)  # shows for 3 seconds then closes

cap.release()
cv2.destroyAllWindows()

# Now test YOLO
from ultralytics import YOLO
model = YOLO("yolov8n.pt")
print("YOLO loaded OK")

if ret:
    results = model(frame, verbose=True)[0]  # verbose=True prints what it finds
    print("Detections:", len(results.boxes))
    for box in results.boxes:
        print("  ->", model.names[int(box.cls)], "conf:", float(box.conf))