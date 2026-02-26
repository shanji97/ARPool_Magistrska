import cv2
import numpy as np
import time
from datetime import datetime
import csv
from enum import Enum
import json
from typing import Optional


# Custom imports
from droid_cam_controller import DroidCamController
from object_detector import ObjectDetector
from calibration import Calibrator, CALIBRATION_PATTERNS
from objects_in_environment import EnvironmentConfig
from connection import UsbTcpSender
from formatters import build_conf_transfer_block, line_configuration_name, line_pockets, LABEL_MAP
from detection_mode import DetectionMode
from helpers import (
    install_dependecies_for_other_projects,
    setup_connection,
    send_config_name_to_quest,
    open_ports
)
from testing import synth_test


# Grayscale tresholds
WHITE_TRESHOLD = 200 # For cue ball and striped balls.
EIGHTBALL_TRESHOLD = 50
STRIPE_WHITE_RATIO = 0.2 # % of white pixels to count as stripe.

MAX_RETRY_COUNT_FRAMES = 300 # 300 frames worth of hickups consecutively means there is a problem.
TABLE_FAILS_BEFORE_RESCAN_FRAMES = 120
SEND_EVERY_N_FRAMES = 1
DETECTION_MODE = DetectionMode.Both
POCKET_STABLE_MAX_DELTA_PX = 1.5
POCKET_STABLE_REQUIRED_FRAMES = 8

POCKET_SCAN_INTERVAL_FRAMES = 5
POCKET_RESEND_INTERVAL_SEC = 2.0
RESCAN_DEBOUNCE_TIME = 0.75


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
_last_rescan_request_time = 0.0
_frame_index = 0
_pockets_adjusted = False
_pockets_px_adjusted_cached = None
_pockets_xy_m_adjusted_cached = None

# Camera and stream

def open_stream(work_resolution:str = "1920x1080",
         performance_mode: bool = False,
         perf_resoulution: str ="1280x720",
         fallback_resoulution: str ="1280x720",
         debug: bool = False,
         debug_static_image_present: bool = False):
     
    if debug and debug_static_image_present:
        return (None, None)
    
    if _controller is None:
        print("Controller is not initialized; cannot open stream.")
        return (None, None)
    if not _controller.is_host_reachable(2):
        try:
            print(f"Device at {_controller.ip}:{_controller.port} is not reachable. Check network settings. Exiting.")
        except Exception:
            print("Device not reachable. Check network settings. Exiting.")
        return (None, None)
    
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

def _load_intrinsics_for_camera(dimensions: str, debug: bool = False):
    
    global _Km, _Knew, _dist, _map1, _map2, _controller, _use_undistorted_view
    
    if _controller is None:
        print("Controller is not initialized, so no intrinsics can be loaded. Aborting...")
        return
    
    meta = _controller.CAMERA_MAP[_controller.current_camera]
    _use_undistorted_view = (meta or {}).get("lens_correction_on", False) # If lens correction on device is off (lens_correction_on is True in the JSON), then correct it (for UW and front camera).
    cam_folder_alias = (meta or {}).get("folder_alias", "main")
    
    if not cam_folder_alias:
        _Km = _Knew = _dist = _map1 = _map2 = None
        return
        
    intr = _calib.get_intrinsics_auto(cam_folder_alias, dimensions, candidates=CALIBRATION_PATTERNS)
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
        if debug and _Km is not None:
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
    global _is_changing_camera, _H_cached, _pockets_px_cached, _pockets_ready, _force_rescan, _detector
    _is_changing_camera = False
    _H_cached = None
    _pockets_px_cached = None
    _pockets_ready = False
    _force_rescan = False
    if _detector is not None:
        _detector.reset_pocket_tracking()
    return

# Camera control part
def send_camera_command(command: str, *args):
    global _controller
    
    if _controller is None:
        print("No controller initialited, no command will be set.")
        return None
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
    global _controller
    if _controller is None:
        print("No controller initialized. Aborting....")
    
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
        global _force_rescan, _last_rescan_request_time
        now = time.time()
        if(now - _last_rescan_request_time) >= RESCAN_DEBOUNCE_TIME:
            _force_rescan = True
            _last_rescan_request_time = now
        print("[pockets] Re-scan requested (r)")
    return (True, camera_info)

def main(
         debug_config_name: Optional[str],
         debug_image_path: Optional[str],
         debug_pocket_display: bool = False,
         debug: bool = False,
         ball_radius_range_px = (10,30), 
         work_resolution: str = "1920x1080",
         performance_mode: str = False,
         perf_resoulution: str ="1280x720",
         fallback_resoulution: str ="1280x720",
         detection_mode: Enum = DetectionMode.YOLO
         ):

    debug_frame = None
    dimensions = None
    capture = None
    config = None   

    global _calib
    _calib = Calibrator(allow_center_crop=True, force_recalib=False)
    
    # Compute environment and static things, such as pockets.
    env = EnvironmentConfig.__new__(EnvironmentConfig)
    
    if debug:
        config = env.get_debug_env_config(debug_config_name)
    else:
        config = env.get_environment_config(interactive= True, use_last_known= True)
    if config is not None:
        quest_ip, port = setup_connection(True)
        send_config_name_to_quest(config.get_json_name_for_unity(), quest_ip, port)
    
    corner_inset_mm, side_inset_mm = config.pockets.derive_insets()
    pockets_mm = config.table.pocket_mm_positions(corner_inset_mm, side_inset_mm)
    (Lhsv, Uhsv)  = (config.table.cloth_lower_hsv, config.table.cloth_upper_hsv)
    Lmm, Wmm, Hmm = config.table.playfield_mm
    ball_diameter_m = config.ball_spec.diameter_m
    camera_height_m = config.camera.height_from_floor_m
    expected_aspect_ratio = Lmm / Wmm     # Consider the units in the future. Regardless of this, the 
    
    del config
    
    # Set up connection and open stream 
    
    if debug and debug_image_path:
        debug_frame = cv2.imread(debug_image_path, cv2.IMREAD_COLOR) if debug_image_path else None
        if debug_frame is None:
            raise FileNotFoundError(f"[debug] Could not read debug image: {debug_image_path}")
        work_w, work_h = map(int, work_resolution.split("x"))
        debug_frame = cv2.resize(debug_frame, (work_w, work_h), interpolation=cv2.INTER_AREA)
        dimensions = work_resolution
        print(f"[debug] Using static image as fake feed: {debug_image_path}.")
        print(f"[debug] Resized debug image to work-res: {dimensions}")
        del work_w
        del work_h
    else:
        ip, port = setup_connection(False)
        global _controller
        _controller = DroidCamController(ip, port)
        capture, dimensions = open_stream(work_resolution, performance_mode, perf_resoulution, fallback_resoulution)
        
        if capture is None:
            print("Could not open stream.")
            return

    if (dimensions is not None) and (not debug):
        try:
            pre = _calib.precompute_all(dimensions, force=False)
            _load_intrinsics_for_camera(dimensions)
            if debug:
                print_precompute_results(pre)
        except Exception as e:
                print("Precompute failed:", e)
    
    quest_ip, q_port = setup_connection(True)
    usb_sender = UsbTcpSender(host=quest_ip, port=q_port)
    if not usb_sender.connect():
        open_ports()
        if not usb_sender.connect():
            print("Could not connect to Quest 3. Check port forwarding.")
        exit()
    
    global _detector
    _detector = ObjectDetector(LABEL_MAP)
    _detector.reset_pocket_tracking()

    retry_count = 0
    pockets_px_raw = None
    table_fail_streak = 0
    frame_counter = 0
    H_new = None
    
    global _is_changing_camera, _H_cached, _pockets_px_cached, _pockets_ready, _force_rescan,_frame_index, _pockets_adjusted, _pockets_px_adjusted_cached, _pockets_xy_m_adjusted_cached
    
    start_time = time.time() if debug else None
    stable = False
    
    # Main execution loop detection
    while True:
        _frame_index+=1
        # Camera switching lock
        if _is_changing_camera:
            print("Changing camera - skipping current frame(s).")
            retry_count = 0
            # Sometime in the future this won't be need, since the pockets are stationary and a loopback from the Quest
            # is going to be available that the pockets have been comnputed.
            continue
            
        if not debug:
            ret, frame = capture.read()
        elif debug and debug_image_path:
            ret, frame = True, debug_frame.copy() 
        
        # Frame error lock
        if not ret or frame is None:
            if debug:
                # Static image = no recovery possible, just stop
                print("[debug] Static debug frame invalid. Exiting loop.")
                break

            retry_count += 1
            if retry_count >= MAX_RETRY_COUNT_FRAMES:
                print(f"Frame capture failed too many times ({MAX_RETRY_COUNT_FRAMES} frames), exiting.")
                break

            capture.release()
            capture, dimensions = open_stream(
                work_resolution,
                performance_mode,
                perf_resoulution,
                fallback_resoulution
            )

            ret, frame = capture.read()
            if not ret or frame is None:
                print("Something wrong with open cv initialization - possible networking or device issues. Aborting.")
                break
            retry_count = 0
            continue

        if debug:
            frame_counter += 1
            if frame_counter % 30 == 0:
                elapsed = time.time() - start_time
                fps = frame_counter / elapsed
                print(f"[INFO] FPS: {fps:.2f}")
                
        frame = undistort_frame_if_needed(frame) if not debug else frame # Variables change based on camera switching
        
        # 1) Detect cloth area (rough)
        table_bounding_box, table_mask, corners = _detector.detect_table(frame, (Lhsv,Uhsv))
        if table_bounding_box is None or corners is None:
            retry_count += 1
            table_fail_streak += 1
            if table_fail_streak >= TABLE_FAILS_BEFORE_RESCAN_FRAMES:
                _pockets_ready = False
                _detector.reset_pocket_tracking()
            continue
        # 2) Smooth cloth-corners (still useful as a fallback / ROI)
        corners = _detector.gate_and_smooth_corners(corners, expected_aspect_ratio)

        # 3) Compute inner cushion corners (markerless) — THIS IS THE KEY CHANGE
        inner_corners, edges_dbg = ObjectDetector.detect_inner_cushion_corners(
            frame_bgr=frame,
            approx_table_corners_px=corners,
            roi_expand=0.08,
            canny1=60,
            canny2=160,
            hough_thresh=120,
            min_line_len_frac=0.35,
            max_line_gap=35,
            debug=debug_pocket_display
        )
        
        if inner_corners is None:
            # Fall back to cloth corners if inner cannot be computed this frame
            inner_corners = corners

        # 4) Homography based on INNER rectangle
        H_new = _detector.homography_mm_to_px(inner_corners, Lmm, Wmm)

        if H_new is None:
            continue
        
        
        
        # CURRENT LOCATION
        # Detect pockets and get a 2D calculation.
        # If the pockets are not yet calcucated recalculate them and once they are stable enough:
            # 1) "Finalize them"
            # 2) "Compute the 2D position from the pixels to real coordinates in relation to the table (if neede, I can implement some marker system)."
            # 3)  Send them to the Quest via the connection and skip the calculation entirely for the rest of the python application execution time.
        
        if _force_rescan:
            _detector.reset_pocket_tracking()
            _pockets_ready = False
            _pockets_have_been_sent = False
            _last_pocket_send_time = 0.0
            _pockets_px_cached = None
            _H_cached = None
            _force_rescan = False
            print("[pockets] Re-scan started.")

        # Compute only until locked, and only every N frame
        should_scan_this_frame = (not _pockets_ready) and ((_frame_index % POCKET_SCAN_INTERVAL_FRAMES) == 0)
        
        
        if not _pockets_ready:
            if should_scan_this_frame:
                # Detect pockets from image evidence in rectified plane
                pockets_px_raw, pockets_plane_dbg, dbg = _detector.detect_pockets_markerless(
                    frame_bgr=frame,
                    corners_px_inner=inner_corners,
                    playfield_L_mm=Lmm,
                    playfield_W_mm=Wmm,
                    v_thresh=70,
                    sat_max=180,
                    roi_frac_corner=0.18,
                    roi_frac_side_w=0.22,
                    roi_frac_side_h=0.16,
                    min_area_px=180,
                    debug=debug_pocket_display
                )

                # Fallback: if any pockets are missing, fall back to projected pockets_mm for those
                if pockets_px_raw is None:
                    pockets_px_raw = _detector.warp_mm_points_to_px(H_new, pockets_mm)
                else:
                    fallback_px = _detector.warp_mm_points_to_px(H_new, pockets_mm)
                    pockets_px_raw = [
                        p if (p is not None) else fallback_px[i]
                        for i, p in enumerate(pockets_px_raw)
                    ]

                # Stabilize
                pockets_px, stable, max_delta_px = _detector.stabilize_pockets(
                    pockets_px_raw,
                    max_delta_px=POCKET_STABLE_MAX_DELTA_PX,
                    required_stable_frames=POCKET_STABLE_REQUIRED_FRAMES
                )

                # Cache the latest *attempt* so debug drawing can still work
                _pockets_px_cached = pockets_px
                _H_cached = H_new

                if stable:
                    # LOCK NOW (independent of sending)
                    _pockets_ready = True
                    _pockets_have_been_sent = False
                    _last_pocket_send_time = 0.0
                    print(f"[pockets] Locked (max delta {max_delta_px:.3f}px).")
                    if (not _pockets_adjusted) and (_pockets_px_cached is not None):
                            _pockets_px_before_adjust_cached = [tuple(p) if p is not None else None for p in _pockets_px_cached]
                            TL, TR, BM, TM, BL, BR = _pockets_px_before_adjust_cached
                            if (TL is not None) and (TR is not None) and (BL is not None) and (BR is not None) and (TM is not None) and (BM is not None):
                                x_left  = min(TL[0], BL[0])
                                x_right = max(TR[0], BR[0])
                                y_top    = min(TL[1], TM[1], TR[1])
                                y_bottom = max(BL[1], BM[1], BR[1])
                                x_mid = 0.5 * (x_left + x_right)
                                pockets_px_new = [
                                (x_left,  y_top),     # TL
                                (x_right, y_top),     # TR
                                (x_mid,   y_bottom),  # BM
                                (x_mid,   y_top),     # TM
                                (x_left,  y_bottom),  # BL
                                (x_right, y_bottom),  # BR
                                ]
                                _pockets_px_cached = pockets_px_new 
                                _pockets_adjusted = True
                # 2) If pockets READY -> always reuse cached (no recompute)
                else:
                    H_new = _H_cached if _H_cached is not None else H_new
                    pockets_px_raw = _pockets_px_cached

        # 3) Sending cached pockets (cheap, independent of computing)
        if _pockets_ready and (_H_cached is not None) and (_pockets_px_cached is not None):
            now = time.time()
            if (not _pockets_have_been_sent) or ((now - _last_pocket_send_time) >= POCKET_RESEND_INTERVAL_SEC):
                pockets_xy_m = ObjectDetector.warp_px_to_m(_H_cached, _pockets_px_cached)
                sent = usb_sender.send(line_pockets(pockets_xy_m))
                if sent:
                    _pockets_have_been_sent = True
                    _last_pocket_send_time = now

                if debug and debug_pocket_display:
                    labels = ["TL", "TR", "BM", "TM", "BL", "BR"]

                    # Draw OLD (yellow) if we have it
                    if _pockets_px_before_adjust_cached is not None:
                        for i, p in enumerate(_pockets_px_before_adjust_cached):
                            if p is None:
                                continue
                            x, y = p
                            cv2.circle(frame, (int(x), int(y)), 14, (0, 255, 255), 2)
                    if _pockets_px_cached is not None:
                         for i, p in enumerate(_pockets_px_cached):
                            if p is None:
                                continue
                            x, y = p
                            xm, ym = pockets_xy_m[i]
                            cv2.circle(frame, (int(x), int(y)), 9, (0, 255, 0), -1)  # filled green
                            text = f"SEND:{labels[i]} ({xm:.3f}m,{ym:.3f}m)"
                            cv2.putText(frame, text, (int(x) + 8, int(y) - 8),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
                    cv2.imshow("debug",frame)
                    if (cv2.waitKey(1) & 0xFF) == ord("q"):
                        break



                
        # circles = _detector.detect_balls(
        #         frame, 
        #         table_mask,
        #         ball_radius_range_px[0],
        #         ball_radius_range_px[1]
        #     ) or []
       
        # centers_px = [(int(c[0]), int(c[1])) for c in circles]
        # centers_m = ObjectDetector.warp_px_to_m(H_new, centers_px)

        # entries = []
        # for circle, (xm, ym) in zip(circles, centers_m):
        #     # Skip until homography is available
        #     if xm is None or ym is None:
        #         continue
        #     entry = _detector.circle_to_entry(
        #         frame,
        #         circle,
        #         (xm, ym),                # pass center in meters
        #         WHITE_TRESHOLD,
        #         EIGHTBALL_TRESHOLD,
        #         STRIPE_WHITE_RATIO
        #     )
        #     entries.append(entry)
        
        # yolo_px = []
        # yolo_entries = []
        # if detection_mode in (DetectionMode.YOLO.value, DetectionMode.Both.value):
        #     if _detector.yolo is None:
        #       _detector.load_yolo()
        #       try: 
        #         yolo_px = _detector.detect_balls_yolo(frame)
        #         Hinv = lambda pts: ObjectDetector.warp_px_to_m(H_new, pts)
        #         yolo_entries = _detector.yolo_to_entries(yolo_px, Hinv)
        #       except Exception as e:
        #           print("[YOLO] detection error:", e)
        #           yolo_px = []
        #           yolo_entries = []
                     
        # entries_to_send = entries
        # # entries_to_send = yolo_entries
        # if(frame_counter % SEND_EVERY_N_FRAMES) == 0: # Modulus is expensive
        #     usb_sender.send(
        #         build_transfer_block(
        #             [(_mm_to_m(x), _mm_to_m(y)) for (x, y) in pockets_mm],
        #             (_mm_to_m(Lmm), _mm_to_m(Wmm), _mm_to_m(Hmm)),
        #             ball_diameter_m,
        #             camera_height_m,
        #             entries_to_send
        #         )
        #     )
        # frame_counter += 1
       
    if debug:
        # log_file.close()
        cv2.destroyAllWindows()
    if capture is not None:
        capture.release()
    usb_sender.close()
    
if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description="Detection / Calibration runner")
    
    # Debug switches
    parser.add_argument("--debug-conf", type=str, default="../Configuration/predator_9ft_virtual_debug.json", help="Path (relative or absolute) to a debug configuration used as a virtual debug video feed. Needs --debug mode flag set.")
    
    parser.add_argument("--debug-image", type=str, default="./pix2pockets/8-Ball-Pool-3/train/images/test.png", help="Path (relative or absolute) to a static image used as a virtual debug video feed. Needs --debug mode flag set.")
    
    parser.add_argument("--debug-pocket-display", action="store_true", help="If true, you are displaying a window with the pockets marked on the debug image.")
    
    parser.add_argument("--debug", action="store_true", help="If true, you are running debug mode. Mix with other debug flags.")
    
    parser.add_argument("--debug-static", action="store_true", help="If true, you are running debug mode with a static image. Mix with other debug flags.")
    parser.add_argument("--debug-phone", action="store_true", help="If true, you are running debug mode with a phone (live) capture. Mix with other debug flags.")
    
    # Calibration
    parser.add_argument("--calibrate-only", action="store_true", help="Run calibration precompute for a given resolution and exit.")
    
    parser.add_argument("--calib-res", type=str, default="1920x1080",help='Calibration resolution string like "1280x720" or "1920x1080". Defaults to PERFORMANCE_RESOLUTION when omitted.')
    
    # Main settings
    parser.add_argument("--work-res", type=str, default="1920x1080", help='Work resolution string like "1280x720" or "1920x1080".')
    
    parser.add_argument("--perf-res", type=str, default="1280x720", help='Performance resolution string like "1280x720" or "1920x1080".')
    
    parser.add_argument("--fallback-res", type=str, default="1280x720", help='Fallback resolution string like "1280x720" or "1920x1080".')
    
    parser.add_argument("--performance", action="store_true", help="Uses performance mode.")
    
    parser.add_argument("--force-calib", action="store_true", help="Force re-calibration (recompute even if cached).")
    
    parser.add_argument("--synthetic", action="store_true", help="Send synthetic 9ft table pockets (no camera)")
    
    parser.add_argument("--ball-radius-range", type=str, default="10,30", help="Comma-separated min,max radius for Hough circles, e.g. 8,28")
    
    parser.add_argument("--detection-mode", type=int, default=DetectionMode.YOLO.value, help="Detection mode.\r\n1) Tresholding\r\n2) YOLOv8\r\n3) Both")
    
    args = parser.parse_args()
    
    if args.debug_static and args.debug_phone:
        print("Static image analysis and live capture cannot run at the same time.")
    if (not args.debug_static and not args.debug_phone) and args.debug:
        print("Either static image analysis or live capture must be enabled while running in debug mode.")
    
    
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
            install_dependecies_for_other_projects(["pix2pockets"])
            radius_range = tuple(map(int, args.ball_radius_range.split(",")))
            main(                args.debug_conf,                args.debug_image,                args.debug_pocket_display,
                args.debug,                 radius_range,                args.work_res,                args.performance,
                args.perf_res,                args.fallback_res,                args.detection_mode)
        except Exception as e:
            print(f"Error while executing main loop. Check parameters....Exception: {e}")