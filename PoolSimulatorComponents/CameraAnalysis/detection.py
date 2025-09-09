import cv2
import numpy as np
import time
from datetime import datetime
import csv
from enum import Enum
import json
import re

# Custom imports
from droid_cam_controller import DroidCamController
from object_detector import ObjectDetector
from calibration import Calibrator
from objects_in_environment import get_environment_config, EnvironmentConfig

class DetectionMode(Enum):
    Tresholding = 1
    YOLO = 2
    Both = 3

# Max resolution
RESOLUTION = "1920x1080"
PERFORMANCE_RESOLUTION = "1280x720"
FALLBACK_RESOLUTION = "1280x720"

PATTERN = [
    "20mm_13x9",
    "25mm_10x7",
    "30mm_6x8",
    "35mm_7x4",
    ]

BALL_RADIUS_RANGE_PX = (10, 30)

# Grayscale tresholds
WHITE_TRESHOLD = 200 # For cue ball and striped balls.
EIGHTBALL_TRESHOLD = 50
STRIPE_WHITE_RATIO = 0.2 # % of white pixels to count as stripe.

MAX_RETRY_COUNT = 300 # 300 frames worth of hickups consecutively means there is a problem.

DEBUG = True
PERFORMANCE_MODE = True
DETECTION_MODE = DetectionMode.Both

USE_UNDISTORTED_VIEW = True

# Runtime state
_controller = None
_calib = None
_K = None
_Knew = None
_dist = None
_map1 = None
_map2 = None
_env = None

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

# Camera and stream
def validate_ip(ip:str):
    pattern = r"^\d{1,3}(\.\d{1,3}){3}$"
    return re.match(pattern, ip) is not None

def open_stream():
    if not _controller or not _controller.is_host_reachable(2):
        print(f"Device at {_controller.ip}:{_controller.port} is not reachable. Check network settings. Exiting.") 
    
    resolution = RESOLUTION
    
    if PERFORMANCE_MODE is True:
        resolution = PERFORMANCE_RESOLUTION
        
    capture = cv2.VideoCapture(send_camera_command("get_stream_url", resolution ))
    
    if not capture.isOpened():
        print(f"Failed to open stream with {resolution} resolution, trying with {FALLBACK_RESOLUTION}...")
        capture = cv2.VideoCapture(send_camera_command("get_stream_url", FALLBACK_RESOLUTION))
        if not capture.isOpened():
            print(f"Failed to open stream with {FALLBACK_RESOLUTION} resolution.")
            return (None, None)
    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP {_controller.ip} and PORT {_controller.port}.")
        capture.release()
        return (None, None)
    
    return (capture, resolution)

# Calibration part
 
def _load_intrinsics_for_camera(dimensions: str):
    global _K, _Knew, _dist, _map1, _map2, _controller
    
    meta = _controller.CAMERA_MAP[_controller.current_camera]
    cam_key = (meta or {}).get("folder_alias", "main")
    
    if not cam_key:
        _K = _Knew = _dist = _map1 = _map2 = None
        return
        
    intr = _calib.get_intrinsics_auto(cam_key, dimensions, candidates=PATTERN)
    _K = intr.K(); 
    _dist = np.array(intr.dist, np.float64)
    w, h = map(int, dimensions.split('x'))
    
    if USE_UNDISTORTED_VIEW:
        if _Knew is None or _map1 is None or _map2 is None:
            print(f"[calib] Building undistortion maps for {cam_key} at {dimensions}")
            _Knew, _ = cv2.getOptimalNewCameraMatrix(_K, _dist, (w, h), 1.0, (w, h))
            _map1, _map2 = cv2.initUndistortRectifyMap(
                _K, _dist, None, _Knew, (w, h), cv2.CV_16SC2
            )
    else:
        _Knew = None
        _map1 = _map2 = None
        if DEBUG and _K is not None:
            print("[K (distorted)]\n", _K)
            print("[dist] ", _dist.ravel())
    
def undistort_frame_if_needed(frame):
    if USE_UNDISTORTED_VIEW and _map1 is not None:
        return cv2.remap(frame, _map1, _map2, cv2.INTER_LINEAR)
    return frame

def undistort_points(points_xy):
    if _K is None:
        return points_xy
    points = np.asarray(points_xy, dtype=np.float32).reshape(-1, 1, 2)
    undistorted_points = cv2.undistortPoints(points, _K, _dist, P=_Knew)
    return undistorted_points.reshape(-1, 2)

def print_precompute_results(precompute_results: dict):
    print("\n=== Calibration summary (per camera · per pattern) ===")
    for cam in sorted(precompute_results.keys()):
        print(f"\n[{cam}]")
        rows = sorted(precompute_results[cam], key=lambda x: x[0].lower())  # (pattern, rms)
        for pattern, rms in rows:
            print(f"  - {pattern:<14}  RMS={rms:.4f}")
    print("\nDone.\n")

# Camera control part
def send_camera_command(command: str, *args):
    if command == "toggle_torch":
        _controller.toggle_torch()
    elif command == "reset_torch":
        _controller.reset_all_torch_states()
    elif command == "set_focus_mode":
        if args:
            _controller.set_focus_mode(args[0])
    elif command == "set_manual_focus_value":
        if args:
            _controller.set_manual_focus_value(args[0])
    elif command == "set_zoom":
        if args:
            _controller.set_zoom(args[0])
    elif command == "set_exposure":
        if args:
            _controller.set_exposure(args[0])
    elif command == "set_white_balance":
        if args:
            _controller.set_white_balance(args[0])
    elif command == "sync_all_locks":
        _controller.sync_all_locks()
    elif command == "apply_defaults":
        _controller.apply_default_settings()
    elif command == "select_camera":
        if args:
            _controller.select_camera(args[0])   
            _load_intrinsics_for_camera(args[1])
    elif command == "get_stream_url":
            return _controller.get_stream_url(args[0])
    elif command == "dump_camera_info":
            info = _controller.get_camera_info()
            if info:
                print(json.dumps(info, indent=2))
                return info
            else:
                print("Failed to get camera info.")
                return None
    else:
        print(f"Unknown command: {command}")
        
def check_keys(dimensions: str = "1920x1080"):
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
        next_cam = (_controller.current_camera + 1) % len(_controller.CAMERA_MAP)
        send_camera_command("select_camera", next_cam, dimensions)
        camera_info = send_camera_command("dump_camera_info")
    elif key in [ord('0'), ord('1'), ord('2'), ord('3')]:
        camera_number = int(chr(key))
        send_camera_command("select_camera", camera_number, dimensions)
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

def log_csv_row(writer, 
                frame,
                table_mask,
                pockets,
                start_time,
                table_bbox,
                classical_results,
                yolo_results,
                resolution_str: str = "1920x1080",
                cuda_available = "True",
                cuda_version = "12.8",
                vram_mb_int =  0,
                enviromentInfo: EnvironmentConfig = None
                ):
    vram_mb = str(vram_mb_int)
    hsv_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    mean_h, mean_s, mean_v, _ = cv2.mean(hsv_frame, mask=table_mask)
    now = datetime.now().strftime('%Y-%m-%d %H:%M:%S.%f')[:-3]

    row = [
        now,
        int(mean_h), int(mean_s), int(mean_v)
    ]

    (length, width) = enviromentInfo.table.playfield_mm
    if table_bbox:
        _, _, w, h = table_bbox
        row += [w, h, width, length]
    else:
        row += [None, None, width, length]

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

def main():
    
    ip = input("Enter DroidCam IP address (e.g., 192.168.0.40): ").strip()
    while not validate_ip(ip):
        print("Invalid IP format. Try again.")
        ip = input("Enter DroidCam IP address: ").strip()
        
    port = input("Enter DroidCam port [default=4747]: ").strip() or "4747"
    
    global _controller
    _controller = DroidCamController(ip, port)
    
    global _env
    _env = get_environment_config(interactive=True, use_last_known=True)        
    
    capture, dimensions = open_stream()
    
    if dimensions is not None:
        # First run: compute everything it finds under each camera (all pattern subfolders)
        # Set force=True only the very first time to overwrite
        global _calib
        _calib = Calibrator(work_resolution=dimensions, device="i16pm", allow_center_crop=True, force_recalib=True)
        try:
            pre = _calib.precompute_all(dimensions, force=False)
            _load_intrinsics_for_camera(_controller, dimensions)
            if DEBUG:
                print_precompute_results(pre)
        except Exception as e:
                print("Precompute failed:", e)
    
    if capture is None:
        print("Could not open stream.")
        return
    


            #Calirate on all patterns.
    
    
    
    
    # JUST CALIBRATION
    #Object detection
    # ball_detector = ObjectDetector()
    # log_file, writer, cuda_available, cuda_version, vram_mb = prepare_log_file(ball_detector)
    
    # results_tresholding = []
    # results_yolo = []
    # pockets = [(0,0),(0,0)]
    # retry_count = 0
        
    # ret, frame = capture.read()
    # while True:
        
    #     should_break, camera_info = check_keys()
    #     if not should_break:
    #         break
        
    #     if camera_info is None:
    #         camera_info  = initial_camera_info
        
    #     start_time = time.perf_counter()
    #     results_tresholding = []
    #     results_yolo = []
        
    #     if not ret:
    #         print("Frame capture failed.")
    #         capture.release()
    #         capture, _ = open_stream()
    #         ret, frame = capture.read()
    #         retry_count += 1
    #         if retry_count >= MAX_RETRY_COUNT:
    #             print("Frame capture failed. Too many times.")
    #             break
    #         continue
    #     else:
    #         retry_count = 0
            
    #     table_bbox, table_mask = ball_detector.detect_table(frame, TABLE_LOWER_HSV, TABLE_UPPER_HSV)
    #     if table_bbox is None:
    #         print("No table detected")
    #         continue
        
    #     # When the pocket is confirmed by user stop calculating pockets.
    #     if user_confirmed_pocket_positions is False:
    #         pockets = ObjectDetector.detect_pockets(table_bbox)            

    #         if user_is_holding_top_left is True or user_adjusted_top_left is True:
    #             pockets = [None, pockets[1]]
    #         if user_is_holding_bottom_right is True or user_adjusted_bottom_right is True:
    #             pockets = [pockets[0], None]      
    #     else:
    #         pockets = [(None,None)] 
        # END PRECALIBRATION
        
        # balls = detect_balls(frame, table_mask)
        # for circle in balls:
        #     label = classify_balls(frame, circle)
        #     if DEBUG:
        #         x,y,r = circle
        #         cv2.circle(frame, (x, y), r, (0, 255, 0), 2)
        #         cv2.putText(frame, label, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        #         cv2.imshow("Detection Debug", frame)
        
        
        
        
        # if DETECTION_MODE in (DetectionMode.Tresholding, DetectionMode.Both):
        #     balls = ball_detector.detect_balls(frame, table_mask, BALL_RADIUS_RANGE_PX[0],BALL_RADIUS_RANGE_PX[1])

        #     for circle in balls:
        #         x, y, r = int(circle[0]), int(circle[1]), int(circle[2])
        #         label = ball_detector.classify_balls(frame, (x, y, r), WHITE_TRESHOLD, EIGHTBALL_TRESHOLD, STRIPE_WHITE_RATIO)
        #         results_tresholding.append((x, y, label))
        #         if DEBUG:
        #             cv2.circle(frame, (x, y), r, (0, 255, 0), 2)
        #             cv2.putText(frame, label, (x, y - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)

        #     if DEBUG:
        #         log_csv_row(writer, frame, table_mask, pockets, dimensions, start_time,
        #                     table_bbox, results_tresholding, results_yolo,
        #                     cuda_available, cuda_version, vram_mb)

        #         cv2.imshow("Detection Debug", frame)
       
    if DEBUG:
        # log_file.close()
        cv2.destroyAllWindows()
    capture.release()
    
if __name__ == "__main__":
    main()
#Remarks: run "python -m pip cache purge" to purge GiB worth of cached packets.