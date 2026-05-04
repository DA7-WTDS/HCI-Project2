import cv2
import dlib

# Set paths for image and the shape predictor model
img_path = "human.jpg" # Make sure to have a human.jpg in the same directory
predictor_path = "shape_predictor_68_face_landmarks.dat" # Make sure to download this file

# load the face detector and shape predictor
detector = dlib.get_frontal_face_detector()
predictor = dlib.shape_predictor(predictor_path)

img = cv2.imread(img_path)

if img is None:
    print(f"Error: Could not read image from {img_path}")
else:
    # image = cv2.resize(image, (600, 500))
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    # detect the faces
    faces = detector(gray)

    cpy = img.copy()

    for face in faces:
        # extract the coordinates of the bounding box
        x1 = face.left()
        y1 = face.top()
        x2 = face.right()
        y2 = face.bottom()
        cv2.rectangle(cpy, (x1, y1), (x2, y2), (0, 255, 0), 2)

        # apply the shape predictor to the face ROI
        shape = predictor(gray, face)
        
        # draw all points
        for n in range(0, 68):
            x = shape.part(n).x
            y = shape.part(n).y
            cv2.circle(cpy, (x, y), 1, (0, 255, 0), 1)
        
        # draw specific point
        # x = shape.part(26).x
        # y = shape.part(26).y
        # cv2.circle(cpy, (x, y), 2, (0, 255, 0), 1)

    # Use standard cv2.imshow for local environment instead of colab patches
    cv2.imshow("Gaze Tracking Landmarks", cpy)
    cv2.waitKey(0)
    cv2.destroyAllWindows()
