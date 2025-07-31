import cv2
import numpy as np
import time
import socket

# Globals
CAPTURING_DEVICE_IP = "10.15.28.224"
PORT = "4747"
# Max resolution
RESOLUTION = "1920x1080"
# Buy full version of DroidCam and or use 720p or watch ads every 1 hour.
# Future work (another thesis): write a free/opensource software for iOS/Android
# that uses an USB connection to avoid slow/congested WiFi connection
# or weak signal strenght. Also DroidCam uses TCP, I think a few
# dropped frames could work, so UDP or maybe even QUIC. Alternatively everything
# could be offloaded to be computed on the phone. If the phone has a special 
# hardware (like LiDAR) also the data from this could be inferred.

MAIN_CAMERA_FOCAL_LENGTH_MILIMETERS = 24

TABLE_WIDTH_MILIMETERS = 1000 # Set for the specific table.
TABLE_LENGTH_MILIMETERS = 2000
RATIO = 2.0 # Always constant.
#STANDARD_TABLE_DIMENSIONS_MILIMETERS = [(2438, 1219),
#                                        (2743, 1372),
#                                        (3048, 1524)]
#CAMERA_HEIGHT_IN_MILIMETERS =? 


# Future work: Since the table size are standard a size detection algorithm (Painters?) should
# be used to manually compute the table dimensions along with the
# height and the distance from the camera to the table.

POCKET_RADIUS_PX = 30

BALL_RADIUS_MILIMETERS = 28.6
BALL_RADIUS_RANGE_PX = (10, 30)

# Table colors
TABLE_LOWER_HSV = (35, 30, 40)  # green-ish
TABLE_UPPER_HSV = (85, 255, 255)
# Future work: Instead of the trasholds there should be some sort of
# values for different ambient conditions.

# Grayscale tresholds
WHITE_TRESHOLD = 200 # For cue ball and striped balls.
EIGHTBALL_TRESHOLD = 50
STRIPE_WHITE_RATIO = 0.2 # % of white pixels to count as stripe.

MAX_RETRY_COUNT = 300 # 300 frames worth of hickups consecutively means there is a problem.

DEBUG_DRAW = True

# Input from user via server or quest
user_confirmed_pocket_positions = False # Semaphore/Flag?
# Top left
user_is_holding_top_left = False
user_adjusted_top_left = False
# Bottom right
user_is_holding_bottom_right = False
user_adjusted_bottom_right = False

def is_host_reachable(timeout = 2):
    try:
        with socket.create_connection((CAPTURING_DEVICE_IP, int(PORT)), timeout):
            return True
    except(socket.timeout, socket.error):
        return False

def open_stream():
    
    #Check if device is on same network.
    if not is_host_reachable(2):
        print(f"Device at {CAPTURING_DEVICE_IP}:{PORT} is not reachable. Check network settings. Exiting.")
        return None
    
    url = f'http://{CAPTURING_DEVICE_IP}:{PORT}/video?{RESOLUTION}'
    capture = cv2.VideoCapture(url)
    
    if not capture.isOpened():
        print("Failed to open stream with custom resolution, trying with 720p...")
        url = f'http://{CAPTURING_DEVICE_IP}:{PORT}/video?1280x720'
        capture = cv2.VideoCapture(url)
        if not capture.isOpened():
            print("Failed to open stream with 720p resolution.")
            return None
    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP and PORT.")
        capture.release()
        return None
        
    return capture

def detect_table(frame):
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, TABLE_LOWER_HSV, TABLE_UPPER_HSV)
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None, None
    table_contour = max(contours, key=cv2.contourArea)
    x, y, w, h = cv2.boundingRect(table_contour)
    return (x, y, w, h), mask

def detect_pockets(table_bbox):
    x,y,w,h = table_bbox
    top_left = (x,y)
    bottom_right = (x + w, y + h)
    return [top_left, bottom_right]
    # Future work: Add some offset to match the center as closely as possible.
    
def detect_balls(frame, table_mask, gaussian_kernel = (9,9)):
    masked = cv2.bitwise_and(frame, frame, mask=table_mask)
    gray = cv2.cvtColor(masked, cv2.COLOR_BGR2GRAY)
    blurred = cv2.GaussianBlur(gray, gaussian_kernel, 2)
    circles = cv2.HoughCircles(blurred, 
                               cv2.HOUGH_GRADIENT,
                               dp = 1.2,
                               minDist=20,
                               param1= 50,
                               param2=30,
                               minRadius= BALL_RADIUS_RANGE_PX[0],
                               maxRadius= BALL_RADIUS_RANGE_PX[1])
    if circles is not None:
        return np.uint16(np.around(circles[0]))
    return []

def classify_balls(frame, circle):
    x,y,r = circle
    roi = frame[y - r:y + r, x - r:x + r]
    if roi.size == 0:
        return "unknown"
    gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
    white_pixels = np.sum(gray > WHITE_TRESHOLD)
    black_pixels = np.sum(gray < EIGHTBALL_TRESHOLD)
    total_pixels = roi.shape[0] * roi.shape[1]
    white_ratio = white_pixels / total_pixels
    if black_pixels / total_pixels > 0.5:
        return "8-ball"
    if white_ratio > 0.8:
        return "cue"
    if white_ratio > STRIPE_WHITE_RATIO:
        return "striped"
    return "solid"
    

def classify_balls_yolo():
    pass
    
    

def main():
    
    capture = open_stream()
    if capture is None:
        print("Could not open stream.")
        return
    
    ret, frame = capture.read()
    
    pockets = [(0,0),(0,0)]
    retry_count = 0
    while True:
        start_time = time.perf_counter()
        ret, frame = capture.read()
        if not ret:
            print("Frame capture failed.")
            retry_count+=1
            if retry_count >= MAX_RETRY_COUNT:
                print("Frame capture failed. Too many times.")
                break
            continue
        else:
            retry_count = 0
            
        table_bbox, table_mask = detect_table(frame)
        if table_bbox is None:
            print("No table detected")
            continue
        
        # When the pocket is confirmed by user stop calculating pockets.
        if user_confirmed_pocket_positions is False:
            pockets = detect_pockets(table_bbox)
            # Future work: Also handle the repositioning so that the 
            # user won't be interuppted and pocket positions reset to the detected ones instead of the
            # user corrected ones. Add some interception system (that runs in parrallel)
            if user_is_holding_top_left is True or user_adjusted_top_left is True:
                pockets = [None, pockets[1]]
            if user_is_holding_bottom_right is True or user_adjusted_bottom_right is True:
                pockets = [pockets[0], None]      
        else:
            pockets = [(None,None)] 
        
        
        balls = detect_balls(frame, table_mask)
        for circle in balls:
            label = classify_balls(frame, circle)
            if DEBUG_DRAW:
                x,y,r = circle
                cv2.circle(frame, (x, y), r, (0, 255, 0), 2)
                cv2.putText(frame, label, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                cv2.imshow("Detection Debug", frame)
            
        #Process data s json
            
        elapsed = time.perf_counter() - start_time
        print(f'Processing time: {elapsed * 1000:.2f} ms')
        if cv2.waitKey(1) == ord('q'):
            break  
      
    capture.release()
    
if __name__ == "__main__":
    main()
    