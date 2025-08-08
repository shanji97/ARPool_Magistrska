import cv2
import numpy as np
import time
from datetime import datetime
import csv
from enum import Enum
import json

#Custom imports
from .droid_cam_controller import DroidCamController
from .object_detector import ObjectDetector

class DetectionMode(Enum):
    TRESHOLDING = 1
    YOLO = 2
    BOTH = 3

# Globals
CAPTURING_DEVICE_IP = "10.15.12.7"
PORT = "4747"
controller = DroidCamController(CAPTURING_DEVICE_IP, PORT)
# Max resolution
RESOLUTION = "1920x1080"
NUMBER_OF_CAMERAS  = 4
# Buy full version of DroidCam and or use 720p or watch ads every 1 hour. PUT requests (at least for iPhone 16 Pro Max) are locked behind the PRO app version.
# Future work (another thesis): write a free/opensource software for iOS (and Android)
# that uses an USB connection to avoid slow/congested/not reliable/f'd up by admins/ WiFi connection 
# or weak signal strenght. Also DroidCam uses TCP, I think a few
# dropped frames could work, so UDP or maybe even QUIC could work. Alternatively everything
# could be offloaded to be computed on the phone. If the phone has a special 
# hardware (like LiDAR) take also the data from other sensors to get more accurate representation of the state on the table. Also position of the sticks
# could be inferred this way.

MAIN_CAMERA_FOCAL_LENGTH_MILIMETERS = 24
# MAINCAMERA_HEIGHT_IN_MILIMETERS =? 

TABLE_WIDTH_MILIMETERS = 1000 # Set for the specific table.
TABLE_LENGTH_MILIMETERS = 2000
RATIO = 2.0 # Always constant.
#STANDARD_TABLE_DIMENSIONS_MILIMETERS = [(2438, 1219),
#                                        (2743, 1372),
#                                        (3048, 1524)]


# Future work: Since the table size are standard a size detection algorithm (Painters?) should
# be used to manually compute the table dimensions along with the
# height and the distance from the camera to the table.

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

DEBUG_LOGGING = True
PERFORMANCE_MODE = True
DETECTION_MODE = DetectionMode.BOTH

# Connectivity / camera control flags (for AR user adjustments).
user_confirmed_pocket_positions = False

# Semaphore/Flag indicating pockets locked by user.
# Flags for user adjusting pockets (e.g., via AR interface like Quest 3)
# Top left
user_is_holding_top_left = False
user_adjusted_top_left = False
# Bottom right
user_is_holding_bottom_right = False
user_adjusted_bottom_right = False

#Camera and stream
def open_stream():
    #Check if device is on same network.
    if not controller.is_host_reachable(2):
        print(f"Device at {CAPTURING_DEVICE_IP}:{PORT} is not reachable. Check network settings. Exiting.") 
        return None  
    if PERFORMANCE_MODE is True:
        RESOLUTION="1280x720"
    capture = cv2.VideoCapture(send_camera_command("get_stream_url", RESOLUTION))
    
    if not capture.isOpened():
        print("Failed to open stream with custom resolution, trying with 720p...")
        capture = cv2.VideoCapture(send_camera_command("get_stream_url", "1280x720"))
        if not capture.isOpened():
            print("Failed to open stream with 720p resolution.")
            return None
    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP and PORT.")
        capture.release()
        return None
    
    return capture

# Camera control part
def send_camera_command(command: str, *args):
    if command == "toggle_torch":
        controller.toggle_torch()
    elif command == "reset_torch":
        controller.reset_all_torch_states()
    elif command == "set_focus_mode":
        if args:
            controller.set_focus_mode(args[0])
    elif command == "set_manual_focus_value":
        if args:
            controller.set_manual_focus_value(args[0])
    elif command == "set_zoom":
        if args:
            controller.set_zoom(args[0])
    elif command == "set_exposure":
        if args:
            controller.set_exposure(args[0])
    elif command == "set_white_balance":
        if args:
            controller.set_white_balance(args[0])
    elif command == "sync_all_locks":
        controller.sync_all_locks()
    elif command == "apply_defaults":
        controller.apply_default_settings()
    elif command == "select_camera":
        if args:
            controller.select_camera(args[0])   
    elif command == "get_stream_url":
            return controller.get_stream_url(args[0])
    elif command == "dump_camera_info":
            info = controller.get_camera_info()
            if info:
                print(json.dumps(info, indent=2))
                return info
            else:
                print("Failed to get camera info.")
                return None
    else:
        print(f"Unknown command: {command}")
        
def check_keys():
    camera_info = None
    key = cv2.waitKey(1)
    if key == ord('q'):
        return (False, camera_info)
    elif key == ord('t'):
        send_camera_command("toggle_torch")
    elif key == ord('f'):
        send_camera_command("set_focus_mode", 2)  # Manual focus mode
        send_camera_command("set_manual_focus_value", 0.5)
    elif key == ord('z'):
        send_camera_command("set_zoom", 2.0)
    elif key == ord('e'):
        send_camera_command("set_exposure", 1.0)
    elif key == ord('c'):
        # Cycle through cameras 0 -> 1 -> 2 -> 3 -> 0 ...
        next_cam = (controller.current_camera + 1) % len(controller.CAMERA_MAP)
        send_camera_command("select_camera", next_cam)
    elif key == ord('0'):
        send_camera_command("select_camera", 0)  # Front
    elif key == ord('1'):
        send_camera_command("select_camera", 1)  # Main
    elif key == ord('2'):
        send_camera_command("select_camera", 2)  # Telephoto
    elif key == ord('3'):
        send_camera_command("select_camera", 3)  # Ultrawide
    elif key == ord('i'):
       camera_info = send_camera_command("dump_camera_info")  # Camera info.
    return (True, camera_info)

def get_resolution_string(capture):
    width = capture.get(cv2.CAP_PROP_FRAME_WIDTH)
    height = capture.get(cv2.CAP_PROP_FRAME_HEIGHT)
    return f'{int(width)}x{int(height)}'

def prepare_log_file(ball_detector: ObjectDetector):
    filename = f"debug_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
    file = open(filename, 'w', newline='')
    writer = csv.writer(file)
    cuda_available, cuda_version, vram  = ball_detector.get_gpu_info()
    
    header = [
        "timestamp", "cloth_H", "cloth_S", "cloth_V",
        "table_width_px", "table_height_px", "table_width_mm", "table_length_mm",
        "pocket1_x", "pocket1_y", "pocket2_x", "pocket2_y",
    ]

    if DETECTION_MODE in (DetectionMode.TRESHOLDING, DetectionMode.BOTH):
        for i in range(1, 17):
            header.extend([f"ball{i}_x", f"ball{i}_y", f"ball{i}_type"])

    if DETECTION_MODE in (DetectionMode.YOLO, DetectionMode.BOTH):
        for i in range(1, 17):
            header.extend([f"yolo_ball{i}_x", f"yolo_ball{i}_y", f"yolo_ball{i}_type"])

    header.extend([
        "resolution", "performance_mode", "detection_mode",
        "cuda_available", "cuda_version", "vram_MB", "proc_time_ms"
    ])
    
    writer.writerow(header)
    return file, writer, cuda_available, cuda_version, vram

def log_csv_row(writer, frame, table_mask, pockets, resolution_str, start_time,
                table_bbox, classical_results, yolo_results,
                cuda_available, cuda_version, vram_mb):
    hsv_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    mean_h, mean_s, mean_v, _ = cv2.mean(hsv_frame, mask=table_mask)
    now = datetime.now().strftime('%Y-%m-%d %H:%M:%S.%f')[:-3]

    row = [
        now,
        int(mean_h), int(mean_s), int(mean_v)
    ]

    if table_bbox:
        _, _, w, h = table_bbox
        row += [w, h, TABLE_WIDTH_MILIMETERS, TABLE_LENGTH_MILIMETERS]
    else:
        row += [None, None, TABLE_WIDTH_MILIMETERS, TABLE_LENGTH_MILIMETERS]

    for pt in pockets:
        if pt is None or pt == (None, None):
            row += [None, None]
        else:
            row += [pt[0], pt[1]]
            
    def append_ball_results(results):
        for i in range(16):
            if i < len(results):
                x, y, label = results[i]
                row.extend([x, y, label])
            else:
                row.extend([None, None, None])

    if DETECTION_MODE in (DetectionMode.TRESHOLDING, DetectionMode.BOTH):
        append_ball_results(classical_results)
    if DETECTION_MODE in (DetectionMode.YOLO, DetectionMode.BOTH):
        append_ball_results(yolo_results)

    elapsed_ms = round((time.perf_counter() - start_time) * 1000.0, 2)
    row += [
        resolution_str, PERFORMANCE_MODE, DETECTION_MODE.name,
        cuda_available, cuda_version, vram_mb, elapsed_ms
    ]

    writer.writerow(row)

def main():
    capture = open_stream()
    if capture is None:
        print("Could not open stream.")
        return
    
    send_camera_command("apply_defaults")
    
    resolution_str = get_resolution_string(capture)
    initial_camera_info = send_camera_command("dump_camera_info")
    ball_detector = ObjectDetector()
    log_file, writer, cuda_available, cuda_version, vram_mb = prepare_log_file(ball_detector)
    
    results_tresholding = []
    results_yolo = []
    pockets = [(0,0),(0,0)]
    retry_count = 0
        
    ret, frame = capture.read()
    while True:
        
        should_break, camera_info = check_keys()
        if not should_break:
            break
        
        if camera_info is None:
            camera_info  = initial_camera_info
        
        start_time = time.perf_counter()
        results_tresholding = []
        results_yolo = []
        
        if not ret:
            print("Frame capture failed.")
            capture.release()
            capture = open_stream()
            ret, frame = capture.read()
            retry_count+=1
            if retry_count >= MAX_RETRY_COUNT:
                print("Frame capture failed. Too many times.")
                break
            continue
        else:
            retry_count = 0
            
        table_bbox, table_mask = ball_detector.detect_table(frame, TABLE_LOWER_HSV, TABLE_UPPER_HSV)
        if table_bbox is None:
            print("No table detected")
            continue
        
        # When the pocket is confirmed by user stop calculating pockets.
        if user_confirmed_pocket_positions is False:
            pockets = ObjectDetector.detect_pockets(table_bbox)            
            # Future work: Also handle the repositioning so that the 
            # user won't be interuppted and pocket positions reset to the detected ones instead of the
            # user corrected ones. Add some interception system (that runs in parrallel?). Lock at Unity level, once the Quest 3 user moved them. Get
            # the info which quest moved them (neccessary?) and which pocket was moved. Lock the computation here. Async communication.
            
            # Also if the movement of pocket detectiong is sufficient small ()
            if user_is_holding_top_left is True or user_adjusted_top_left is True:
                pockets = [None, pockets[1]]
            if user_is_holding_bottom_right is True or user_adjusted_bottom_right is True:
                pockets = [pockets[0], None]      
        else:
            pockets = [(None,None)] 
        
        
        # balls = detect_balls(frame, table_mask)
        # for circle in balls:
        #     label = classify_balls(frame, circle)
        #     if DEBUG:
        #         x,y,r = circle
        #         cv2.circle(frame, (x, y), r, (0, 255, 0), 2)
        #         cv2.putText(frame, label, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        #         cv2.imshow("Detection Debug", frame)
        if DETECTION_MODE in (DetectionMode.TRESHOLDING, DetectionMode.BOTH):
            balls = ball_detector.detect_balls(frame, table_mask, BALL_RADIUS_RANGE_PX[0],BALL_RADIUS_RANGE_PX[1])

            for circle in balls:
                x, y, r = int(circle[0]), int(circle[1]), int(circle[2])
                label = ball_detector.classify_balls(frame, (x, y, r), WHITE_TRESHOLD, EIGHTBALL_TRESHOLD, STRIPE_WHITE_RATIO)
                results_tresholding.append((x, y, label))
                if DEBUG_LOGGING:
                    cv2.circle(frame, (x, y), r, (0, 255, 0), 2)
                    cv2.putText(frame, label, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)

            if DEBUG_LOGGING:
                log_csv_row(writer, frame, table_mask, pockets, resolution_str, start_time,
                            table_bbox, results_tresholding, results_yolo,
                            cuda_available, cuda_version, vram_mb)

                cv2.imshow("Detection Debug", frame)
            
        #Process data s json
            
        
       
       
            
    if DEBUG_LOGGING:
        log_file.close()
        cv2.destroyAllWindows()
    capture.release()
    
if __name__ == "__main__":
    main()
#Remarks: run "python -m pip cache purge" to purge GiB worth of cached packets.