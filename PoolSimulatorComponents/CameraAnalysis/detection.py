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
from .calibration import Calibrator

class DetectionMode(Enum):
    Tresholding = 1
    YOLO = 2
    Both = 3

# Globals
CAPTURING_DEVICE_IP = "192.168.0.40"
PORT = "4747"
controller = DroidCamController(CAPTURING_DEVICE_IP, PORT)
# Max resolution
RESOLUTION = "1920x1080"
PERFORMANCE_RESOLUTION = "1280x720"
FALLBACK_RESOLUTION = "1280x720"

TABLE_WIDTH_MILIMETERS = 1000 # Set for the specific table.
TABLE_LENGTH_MILIMETERS = 2000
#STANDARD_TABLE_DIMENSIONS_MILIMETERS = [(2438, 1219),
#                                        (2743, 1372),
#                                        (3048, 1524)]
BALL_RADIUS_RANGE_PX = (10, 30)

# Table colors
TABLE_LOWER_HSV = (35, 30, 40)
TABLE_UPPER_HSV = (85, 255, 255)

# Grayscale tresholds
WHITE_TRESHOLD = 200 # For cue ball and striped balls.
EIGHTBALL_TRESHOLD = 50
STRIPE_WHITE_RATIO = 0.2 # % of white pixels to count as stripe.

MAX_RETRY_COUNT = 300 # 300 frames worth of hickups consecutively means there is a problem.

DEBUG_LOGGING = True
PERFORMANCE_MODE = True
DETECTION_MODE = DetectionMode.Both

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

    if not controller.is_host_reachable(2):
        print(f"Device at {CAPTURING_DEVICE_IP}:{PORT} is not reachable. Check network settings. Exiting.") 
        return None  
    
    resolution = RESOLUTION
    
    if PERFORMANCE_MODE is True:
        resolution = PERFORMANCE_RESOLUTION
        
    capture = cv2.VideoCapture(send_camera_command("get_stream_url", resolution ))
    
    if not capture.isOpened():
        print(f"Failed to open stream with {resolution} resolution, trying with {FALLBACK_RESOLUTION}...")
        capture = cv2.VideoCapture(send_camera_command("get_stream_url", FALLBACK_RESOLUTION))
        if not capture.isOpened():
            print(f"Failed to open stream with {FALLBACK_RESOLUTION} resolution.")
            return None
    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP {CAPTURING_DEVICE_IP} and PORT {PORT}.")
        capture.release()
        return None
    
    return (capture, resolution)

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
        camera_info = send_camera_command("dump_camera_info")
    elif key == ord('0'):
        send_camera_command("select_camera", 0)  # Front
        camera_info = send_camera_command("dump_camera_info")
    elif key == ord('1'):
        send_camera_command("select_camera", 1)  # Main
        camera_info = send_camera_command("dump_camera_info")
    elif key == ord('2'):
        send_camera_command("select_camera", 2)  # Telephoto
        camera_info = send_camera_command("dump_camera_info")
    elif key == ord('3'):
        send_camera_command("select_camera", 3)  # Ultrawide
        camera_info = send_camera_command("dump_camera_info")
    elif key == ord('i'):
       camera_info = send_camera_command("dump_camera_info")  # Camera info.
    return (True, camera_info)

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

    if DETECTION_MODE in (DetectionMode.Tresholding, DetectionMode.Both):
        for i in range(1, 17):
            header.extend([f"ball{i}_x", f"ball{i}_y", f"ball{i}_type"])

    if DETECTION_MODE in (DetectionMode.YOLO, DetectionMode.Both):
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

    if DETECTION_MODE in (DetectionMode.Tresholding, DetectionMode.Both):
        append_ball_results(classical_results)
    if DETECTION_MODE in (DetectionMode.YOLO, DetectionMode.Both):
        append_ball_results(yolo_results)

    elapsed_ms = round((time.perf_counter() - start_time) * 1000.0, 2)
    row += [
        resolution_str, PERFORMANCE_MODE, DETECTION_MODE.name,
        cuda_available, cuda_version, vram_mb, elapsed_ms
    ]

    writer.writerow(row)

def get_intrinsics_for_all_cameras(camera_info: str,
                                   dimensions: str, 
                                   sq_size_meters: float = 0.025,
                                   inner_corners: tuple = (10, 7),
                                   device_model: str = "i16pm", 
                                   force_recalib: bool = False,
                                   use_rational_model: bool = False,
                                   base_path_for_images: str = "CameraAnalysis/Images/Calibration/in_ex"):
    intrinsics = []
    send_camera_command("apply_defaults")
    
    calibrator = Calibrator(dimensions,
                        sq_size_meters,
                        inner_corners,
                        force_recalib,
                        False,
                        device_model,
                        use_rational_model,
                        base_path_for_images)
    intrinsics_main = calibrator.get_intrinsics("main", dimensions)
    
    #Telephoto
    send_camera_command("select_camera", 2)
    calibrator.set_custom_inner_corners((6, 4))
    intrinsics_tp = calibrator.get_intrinsics("tp", dimensions)
    
    #Ultrawide
    send_camera_command("select_camera", 3)  # Ultrawide camera
    calibrator.set_custom_sq_size(0.1)
    calibrator.set_custom_inner_corners((6, 4))
    intrinsics_uw_uw_wth_lens_dist = calibrator.get_intrinsics("uw_wth_lens_dist", dimensions)



def main():
    
    capture, dimensions = open_stream()
    if capture is None:
        print("Could not open stream.")
        return
    
    initial_camera_info = send_camera_command("dump_camera_info")
    intrinsics = get_intrinsics_for_all_cameras(initial_camera_info,
                                                    dimensions)
    # Main camera is set by default.
   
    
    
    
    

    #Object detection
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
            capture, _ = open_stream()
            ret, frame = capture.read()
            retry_count += 1
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
        if DETECTION_MODE in (DetectionMode.Tresholding, DetectionMode.Both):
            balls = ball_detector.detect_balls(frame, table_mask, BALL_RADIUS_RANGE_PX[0],BALL_RADIUS_RANGE_PX[1])

            for circle in balls:
                x, y, r = int(circle[0]), int(circle[1]), int(circle[2])
                label = ball_detector.classify_balls(frame, (x, y, r), WHITE_TRESHOLD, EIGHTBALL_TRESHOLD, STRIPE_WHITE_RATIO)
                results_tresholding.append((x, y, label))
                if DEBUG_LOGGING:
                    cv2.circle(frame, (x, y), r, (0, 255, 0), 2)
                    cv2.putText(frame, label, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)

            if DEBUG_LOGGING:
                log_csv_row(writer, frame, table_mask, pockets, dimensions, start_time,
                            table_bbox, results_tresholding, results_yolo,
                            cuda_available, cuda_version, vram_mb)

                cv2.imshow("Detection Debug", frame)
       
    if DEBUG_LOGGING:
        log_file.close()
        cv2.destroyAllWindows()
    capture.release()
    
if __name__ == "__main__":
    main()
#Remarks: run "python -m pip cache purge" to purge GiB worth of cached packets.