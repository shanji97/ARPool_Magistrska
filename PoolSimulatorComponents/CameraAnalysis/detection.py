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
from connection import UsbTcpSender
from formatters import build_transfer_block, LABEL_MAP

class DetectionMode(Enum):
    Tresholding = 1
    YOLO = 2
    Both = 3

PATTERNS = [
    "20mm_13x9",
    "25mm_10x7",
    "30mm_6x8",
    "35mm_7x4",
    ]

# Grayscale tresholds
WHITE_TRESHOLD = 200 # For cue ball and striped balls.
EIGHTBALL_TRESHOLD = 50
STRIPE_WHITE_RATIO = 0.2 # % of white pixels to count as stripe.

MAX_RETRY_COUNT_FRAMES = 300 # 300 frames worth of hickups consecutively means there is a problem.
TABLE_FAILS_BEFORE_RESCAN_FRAMES = 120
SEND_EVERY_N_FRAMES = 1
DEBUG = True
DETECTION_MODE = DetectionMode.Both

# Runtime state
_controller = None
_calib = None
_detector = None
_Km = None
_Knew = None
_dist = None
_map1 = None
_map2 = None
_use_undistorted_view = False
_is_changing_camera = False
_H_new = None
_pockets_px_cached = None
_pockets_ready = False
_force_rescan = False

# General helpers
def _purge_cache():
    import subprocess
    try:
        print("Trying to clean python package cache with 'python -m pip cache purge' to remove GiB worth of cached packets.")
        result = subprocess.run(["python", "-m", "pip", "cache", "purge"], check=True)
        print(result.stdout)
    except subprocess.CalledProcessError as e:
        print(f"Error clearing pip cache: {e}. Try cleaning it manually.")

def _install_dependencies_from_sub_folder(sub_folders = ["pix2pockets"]):
    import subprocess
    import os
    print("Installing dependencies for other projects.....")
    for folder in sub_folders:
        req_file = os.path.join(folder,"requirements.txt" )
        if not subprocess.run(["pip", "install", "-r", req_file], check=True):
            print(f"Failed to install other project dependencies which are neccessary for this project. Requirements txt: {req_file}.")


# Camera and stream
def _validate_ip(ip:str):
    pattern = r"^\d{1,3}(\.\d{1,3}){3}$"
    return re.match(pattern, ip) is not None

def setup_connection():
    ip = input("Enter DroidCam IP address (e.g., 192.168.0.40): ").strip()
    while not _validate_ip(ip):
        print("Invalid IP format. Try again.")
        ip = input("Enter DroidCam IP address: ").strip()
        
    port = input("Enter DroidCam port [default=4747]: ").strip() or "4747"
    return (ip, port)  

def open_stream(dimensions:str = "1920x1080"):
    return open_stream(dimensions, False, dimensions, dimensions)

def open_stream(work_resolution:str = "1920x1080",
         performance_mode = False,
         perf_resoulution: str ="1280x720",
         fallback_resoulution: str ="1280x720",
         ):
    if not _controller or not _controller.is_host_reachable(2):
        print(f"Device at {_controller.ip}:{_controller.port} is not reachable. Check network settings. Exiting.") 
    
    resolution = work_resolution if performance_mode is False else perf_resoulution
        
    capture = cv2.VideoCapture(send_camera_command("get_stream_url", resolution))
    
    if not capture.isOpened():
        print(f"Failed to open stream with {resolution} resolution, trying with {fallback_resoulution}...")
        capture = cv2.VideoCapture(send_camera_command("get_stream_url", fallback_resoulution))
        if not capture.isOpened():
            print(f"Failed to open stream with {fallback_resoulution} resolution.")
            return (None, None)
    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP {_controller.ip} and PORT {_controller.port}.")
        capture.release()
        return (None, None)
    
    return (capture, resolution)

# Calibration part
def run_calibration_only(dimensions: str = "1920x1080"):
    calib = Calibrator(dimensions)
    summary = {}
    try:
        for cam_key in calib.CAMERA_FOLDERS.keys():
            patterns = calib.available_patterns(cam_key) or [""]
            rows = []
            for pattern in patterns:
                intr = calib.get_intrinsics(cam_key, dimensions, pattern=pattern)
                rows.append((pattern or "<root>", intr.rms))
            summary[cam_key] = rows
    except Exception as e:
        print(f"Error: {e}")
        
    finally:
        print_precompute_results(summary)

def _load_intrinsics_for_camera(dimensions: str):
    global _Km, _Knew, _dist, _map1, _map2, _controller, _use_undistorted_view
    
    meta = _controller.CAMERA_MAP[_controller.current_camera]
    _use_undistorted_view = (meta or {}).get("lens_correction_on", False) # If lens correction is off, then correct it (for UW and front camera).
    cam_folder_alias = (meta or {}).get("folder_alias", "main")
    
    if not cam_folder_alias:
        _Km = _Knew = _dist = _map1 = _map2 = None
        return
        
    intr = _calib.get_intrinsics_auto(cam_folder_alias, dimensions, candidates=PATTERNS)
    _Km = intr.K(); 
    _dist = np.array(intr.dist, np.float64)
    w, h = map(int, dimensions.split('x'))
    
    if _use_undistorted_view:
        if _Knew is None or _map1 is None or _map2 is None:
            print(f"[calib] Building undistortion maps for {cam_folder_alias} at {dimensions}")
            _Knew, _ = cv2.getOptimalNewCameraMatrix(_Km, _dist, (w, h), 1.0, (w, h))
            _map1, _map2 = cv2.initUndistortRectifyMap(
                _Km, _dist, None, _Knew, (w, h), cv2.CV_16SC2
            )
    else:
        _Knew = None
        _map1 = _map2 = None
        if DEBUG and _Km is not None:
            print("[K (distorted)]\n", _Km)
            print("[dist] ", _dist.ravel())
    
def undistort_frame_if_needed(frame):
    if _use_undistorted_view and _map1 is not None:
        return cv2.remap(frame, _map1, _map2, cv2.INTER_LINEAR)
    return frame

def undistort_points(points_xy):
    if _Km is None:
        return points_xy
    points = np.asarray(points_xy, dtype=np.float32).reshape(-1, 1, 2)
    undistorted_points = cv2.undistortPoints(points, _Km, _dist, P=_Knew)
    return undistorted_points.reshape(-1, 2)

def print_precompute_results(precompute_results: dict):
    print("\n=== Calibration summary (per camera · per pattern) ===")
    for cam in sorted(precompute_results.keys()):
        print(f"\n[{cam}]")
        rows = sorted(precompute_results[cam], key=lambda x: x[0].lower())  # (pattern, rms)
        for pattern, rms in rows:
            print(f"  - {pattern:<14}  RMS={rms:.4f}")
    print("\nDone.\n")
    
def commit_cache(homography_new, points_new, pockets_ready, force_rescan):
    global _H_cached, _pockets_px_cached, _pockets_ready, _force_rescan
    _H_cached = homography_new
    _pockets_px_cached = points_new
    _pockets_ready = pockets_ready
    _force_rescan = force_rescan

def reset_pocket_globals():
    global _is_changing_camera, _H_cached, _pockets_px_cached, _pockets_ready
    _is_changing_camera = False
    _H_cached = None
    _pockets_px_cached = None
    _pockets_ready = False
    return

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
            global _is_changing_camera, _H_new, _pockets_px_cached, _pockets_ready
            _is_changing_camera = True
            _controller.select_camera(args[0])   
            _load_intrinsics_for_camera(args[1])
            reset_pocket_globals()
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
    camera_info = send_camera_command("dump_camera_info")
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
    elif key == ord('r'):
        global _force_rescan
        _force_rescan = True
        print("[pockets] Re-scan requested (r)")
    return (True, camera_info)

def prepare_log_file():
    global _detector
    if _detector is None:
        print("Ball detector not instantiated properly")
        return
    
    filename = f"debug_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
    file = open(filename, 'w', newline='')
    writer = csv.writer(file)
    cuda_available, cuda_version, vram  = _detector.get_gpu_info()
    
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
        resolution_str, "PERFORMANCE_MODE", DETECTION_MODE.name,
        cuda_available, cuda_version, vram_mb, elapsed_ms
    ]
    writer.writerow(row)
    
# Environment helper
def _mm_to_m(x_mm: float) -> float:
    return float(x_mm) / 1000.0

# Testing
def synth_test():
    from ball_type import BallType
    usb_sender = UsbTcpSender()
    usb_sender.connect()

    pockets_xy_m = [
        (0.0320000, 1.2400000),
        (2.5080001, 1.2400000),
        (1.2700000, 0.0600000),
        (1.2700000, 1.2100000),
        (0.0320000, 0.0320000),
        (2.5080001, 0.0320000),
    ]
    

    # Synthetic balls — mix of solids, stripes, cue, eight
    entries = [
        # EIGHT
        {"type": BallType.EIGHT.value, "x": 1.2500000, "y": 0.6350000, "number": 8, "confidence": 0.97, "vx": 0.0,  "vy": 0.0},
        # CUE
        {"type": BallType.CUE.value,   "x": 1.2700000, "y": 0.4000000, "number": "/", "confidence": 0.92, "vx": 0.15, "vy": -0.10},

        # STRIPES (9–15)
        {"type": BallType.STRIPE.value,"x": 0.3000000, "y": 0.5000000, "number": 9,  "confidence": 0.88, "vx": 0.20, "vy": -0.05},
        {"type": BallType.STRIPE.value,"x": 0.4500000, "y": 0.5200000, "number": 10, "confidence": None, "vx": None, "vy": None},
        {"type": BallType.STRIPE.value,"x": 0.6000000, "y": 0.5400000, "number": 11, "confidence": None, "vx": -0.10,"vy": 0.00},
        {"type": BallType.STRIPE.value,"x": 0.7500000, "y": 0.5600000, "number": 12, "confidence": 0.66, "vx": 0.00, "vy": 0.00},
        {"type": BallType.STRIPE.value,"x": 0.9000000, "y": 0.5800000, "number": 13, "confidence": 0.80, "vx": 0.05, "vy": 0.02},
        {"type": BallType.STRIPE.value,"x": 1.0500000, "y": 0.6000000, "number": 14, "confidence": 0.74, "vx": -0.02,"vy": 0.03},
        {"type": BallType.STRIPE.value,"x": 1.2000000, "y": 0.6200000, "number": 15, "confidence": 0.60, "vx": None, "vy": 0.00},

        # SOLIDS (1–7)
        {"type": BallType.SOLID.value, "x": 0.3500000, "y": 0.3000000, "number": 1, "confidence": 0.95, "vx": 0.10, "vy": 0.00},
        {"type": BallType.SOLID.value, "x": 0.5000000, "y": 0.3200000, "number": 2, "confidence": 0.93, "vx": -0.12,"vy": 0.04},
        {"type": BallType.SOLID.value, "x": 0.6500000, "y": 0.3400000, "number": 3, "confidence": None, "vx": -0.05,"vy": None},
        {"type": BallType.SOLID.value, "x": 0.8000000, "y": 0.3600000, "number": 4, "confidence": 0.85, "vx": 0.00, "vy": 0.00},
        {"type": BallType.SOLID.value, "x": 0.9500000, "y": 0.3800000, "number": 5, "confidence": 0.70, "vx": None, "vy": None},
        {"type": BallType.SOLID.value, "x": 1.1000000, "y": 0.4000000, "number": 6, "confidence": 0.78, "vx": 0.03, "vy": -0.01},
        {"type": BallType.SOLID.value, "x": 1.2500000, "y": 0.4200000, "number": 7, "confidence": 0.82, "vx": 0.01, "vy": 0.02},
    ]

    payload = build_transfer_block(
        pockets=pockets_xy_m,
        table_LW_m=(2.5400000, 1.2700000, 0.7850000),
        ball_diameter_m=0.0571500,
        camera_height_m=2.5,
        detection_entries=entries
    )

    while True:
        usb_sender.send(payload)
        time.sleep(0.1)

def main(ball_radius_range_px = (10,30), 
         work_resolution:str = "1920x1080",
         performance_mode = False,
         perf_resoulution: str ="1280x720",
         fallback_resoulution: str ="1280x720",
         detection_mode = DetectionMode.YOLO
         ):

    global _calib
    _calib = Calibrator(allow_center_crop=True, force_recalib=False)

    # Compute environment and static things, such as pockets.
    env = get_environment_config(interactive=True, use_last_known=True) 
    
    corner_inset_mm, side_inset_mm = env.pockets.derive_insets()
    pockets_mm = env.table.pocket_mm_positions(corner_inset_mm, side_inset_mm)
    (Lhsv, Uhsv)  = (env.table.cloth_lower_hsv, env.table.cloth_upper_hsv)
    Lmm, Wmm, Hmm = env.table.playfield_mm
    ball_diameter_m = env.ball_spec.diameter_m
    camera_height_m = env.camera.height_from_floor_m
    expected_aspect_ratio = Lmm / Wmm     # Consider the units in the future.

    del env
    
    # Set up connection and open stream 
    ip, port = setup_connection()
    
    global _controller
    _controller = DroidCamController(ip, port)
    
    capture, dimensions = open_stream(work_resolution, performance_mode, perf_resoulution, fallback_resoulution)
    
    if dimensions is not None:
        try:
            pre = _calib.precompute_all(dimensions, force=False)
            _load_intrinsics_for_camera(dimensions)
            if DEBUG:
                print_precompute_results(pre)
        except Exception as e:
                print("Precompute failed:", e)
    
    if capture is None:
        print("Could not open stream.")
        return
    
    usb_sender = UsbTcpSender()
    if not usb_sender.connect():
        print("Could not connect to Quest 3. Check port forwarding.")
        exit()
    
    global _detector
    _detector = ObjectDetector(LABEL_MAP)

    retry_count = 0
    pockets_px_raw = None
    table_fail_streak = 0
    frame_counter = 0
    H_new = None
    
    global _is_changing_camera, _H_cached, _pockets_px_cached, _pockets_ready, _force_rescan
    start_time = time.time() if DEBUG else None
    
    # Main execution loop
    while True:
        
        # Camera switching lock
        if _is_changing_camera:
            print("Changing camera - skipping current frame(s).")
            retry_count = 0
            _pockets_ready = False
            continue
            
        ret, frame = capture.read()
        
        # Frame error lock
        if not ret or frame is None:
            retry_count += 1
            if retry_count >= MAX_RETRY_COUNT_FRAMES:
                print(f"Frame capture failed too many times ({MAX_RETRY_COUNT_FRAMES} frames), exiting.")
                break
            capture.release()
            capture, dimensions = open_stream(dimensions)
            ret, frame = capture.read()
            if not ret or frame is None:
                print("Something wrong with open cv initialization - possible networking or device issues. Aborting......")
                break
            continue
        retry_count = 0

        if DEBUG:
            frame_counter += 1
            if frame_counter % 30 == 0:
                elapsed = time.time() - start_time
                fps = frame_counter / elapsed
                print(f"[INFO] FPS: {fps:.2f}")
                
        frame = undistort_frame_if_needed(frame) # Variables change based on camera switching

        table_bounding_box, table_mask, corners = _detector.detect_table(frame, (Lhsv,Uhsv))
        
        # Pockets
        
        if table_bounding_box is None or corners is None:
            retry_count += 1
            table_fail_streak += 1
            if table_fail_streak >= TABLE_FAILS_BEFORE_RESCAN_FRAMES:
                _pockets_ready = False
            continue
        
        corners = _detector.gate_and_smooth_corners(corners, expected_aspect_ratio)
        
        if (not _pockets_ready) or _force_rescan:
            
            H_new = _detector.homography_mm_to_px(corners, Lmm, Wmm)
            pockets_px_raw = _detector.warp_mm_points_to_px(H_new, pockets_mm)
            pockets_px = _detector.smooth_pockets(pockets_px_raw)
            table_fail_streak = 0
            commit_cache(H_new, pockets_px, True, False)
        else:
            H_new = _H_cached
            pockets_px_raw = _pockets_px_cached        
        
        if DEBUG:
            labels = ["TL","TR","ML","MR","BL","BR"]   # matches your pocket_mm_positions order
            for (x,y), name in zip(pockets_px_raw, labels):
                cv2.circle(frame, (int(x), int(y)), 10, (0,255,255), 2)
                cv2.putText(frame, name, (int(x)+6, int(y)-6),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255,255,255), 1)
                
        if H_new is None:
            continue
                
        circles = _detector.detect_balls(
                frame, 
                table_mask,
                ball_radius_range_px[0],
                ball_radius_range_px[1]
            ) or []
       
        centers_px = [(int(c[0]), int(c[1])) for c in circles]
        centers_m = ObjectDetector.warp_px_to_m(H_new, centers_px)

        entries = []
        for circle, (xm, ym) in zip(circles, centers_m):
            # Skip until homography is available
            if xm is None or ym is None:
                continue
            entry = _detector.circle_to_entry(
                frame,
                circle,
                (xm, ym),                # pass center in meters
                WHITE_TRESHOLD,
                EIGHTBALL_TRESHOLD,
                STRIPE_WHITE_RATIO
            )
            entries.append(entry)
        
        yolo_px = []
        yolo_entries = []
        if detection_mode in (DetectionMode.YOLO.value, DetectionMode.Both.value):
            if _detector.yolo is None:
              _detector.load_yolo()
              try: 
                yolo_px = _detector.detect_balls_yolo(frame)
                Hinv = lambda pts: ObjectDetector.warp_px_to_m(H_new, pts)
                yolo_entries = _detector.yolo_to_entries(yolo_px, Hinv)
              except Exception as e:
                  print("[YOLO] detection error:", e)
                  yolo_px = []
                  yolo_entries = []
                     
        entries_to_send = entries
        # entries_to_send = yolo_entries
        if(frame_counter % SEND_EVERY_N_FRAMES) == 0: # Modulus is expensive
            usb_sender.send(
                build_transfer_block(
                    [(_mm_to_m(x), _mm_to_m(y)) for (x, y) in pockets_mm],
                    (_mm_to_m(Lmm), _mm_to_m(Wmm), _mm_to_m(Hmm)),
                    ball_diameter_m,
                    camera_height_m,
                    entries_to_send
                )
            )
        frame_counter += 1
       
    if DEBUG:
        # log_file.close()
        cv2.destroyAllWindows()
    capture.release()
    usb_sender.close()
    
if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Detection / Calibration runner")
    
    parser.add_argument("--calibrate-only", action="store_true",
                        help="Run calibration precompute for a given resolution and exit.")
    
    parser.add_argument("--calib-res", type=str, default=None,
                        help='Calibration resolution string like "1280x720" or "1920x1080". '
                             'Defaults to PERFORMANCE_RESOLUTION when omitted.')
    
    parser.add_argument("--work-res", type=str, default="1920x1080",
                        help='Work resolution string like "1280x720" or "1920x1080".')
    
    parser.add_argument("--perf-res", type=str, default="1280x720",
                        help='Performance resolution string like "1280x720" or "1920x1080".')
    
    parser.add_argument("--fallback-res", type=str, default="1280x720",
                        help='Fallback resolution string like "1280x720" or "1920x1080".')
    
    parser.add_argument("--performance", action="store_true", help="Uses performance mode.")
    
    parser.add_argument("--force-calib", action="store_true",
                        help="Force re-calibration (recompute even if cached).")
    
    parser.add_argument("--synthetic", action="store_true", help="Send synthetic 9ft table pockets (no camera)")
    
    parser.add_argument("--ball-radius-range", type=str, default="10,30",
                        help="Comma-separated min,max radius for Hough circles, e.g. 8,28")
    
    parser.add_argument("--detection-mode", type=int, default=DetectionMode.YOLO.value,
                        help="Detection mode.\r\n1) Tresholding\r\n2) YOLOv8\r\n3) Both")
    
    args = parser.parse_args()
    
    if args.calibrate_only:
        # Use provided calib-res or fall back to your PERFORMANCE_RESOLUTION
        calib_dims = args.calib_res or args.perf_res or args.fallback_res
        print(f"[calib-only] Running precompute_all for {calib_dims} (force={args.force_calib})")
        run_calibration_only(calib_dims)
        print("Done.")
    if args.synthetic:
        print("Testing synthetic data to verify table object drawing function.")
        synth_test()
    else:
        try:
            if args.detection_mode in [DetectionMode.YOLO.value, DetectionMode.Both.value]:
                print(f"Chosen detection mode {args.detection_mode}.")
            _install_dependencies_from_sub_folder()
            radius_range = tuple(map(int, args.ball_radius_range.split(",")))
            main(radius_range,
                args.work_res,
                args.performance,
                args.perf_res,
                args.fallback_res,
                args.detection_mode)
        except Exception as e:
            print(f"Error while executing main loop. Check parameters.")
            
    _purge_cache()