import cv2
import numpy as np
import time
from datetime import datetime
from enum import Enum
from typing import Optional
import warnings

# Custom imports
from droid_cam_controller import DroidCamController
from object_detector import ObjectDetector
from calibration import Calibrator, CALIBRATION_PATTERNS
from objects_in_environment import EnvironmentConfig
from connection import UsbTcpSender
from formatters import build_conf_transfer_block, line_configuration_name, line_pockets,type_to_str, p2p_classification_to_balltype, LABEL_MAP
from detection_mode import DetectionMode
from helpers import (
    install_dependecies_for_other_projects,
    setup_connection,
    send_config_name_to_quest,
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
POCKETED_DISTANCE_FACTOR_OF_BALL_DIAMETER = .85
YOLO_CIRCLE_MATCH_DIST_MULTIPLIER = 1.75


# Runtime state
_controller = None
_calib = None
_Km = None
_Knew = None
_dist = None
_map1 = None
_map2 = None
_use_undistorted_view = False
_is_changing_camera = False
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
    
    global _controller
    if _controller is None:
        _controller = DroidCamController(setup_connection())
        print("Controller is not initialized; cannot open stream.")
        return (None, None)
    if not _controller.is_host_reachable(2):
        try:
            print(f"Device at {_controller.ip}:{_controller.port} is not reachable. Check network settings. Exiting.")
        except Exception:
            print("Device not reachable. Check network settings. Exiting.")
        return (None, None)
    
    resolution = work_resolution if performance_mode is False else perf_resoulution
        
    capture = cv2.VideoCapture(_controller.send_camera_command("get_stream_url", resolution))
    
    if not capture.isOpened():
        print(f"Failed to open stream with {resolution} resolution, trying with {fallback_resoulution}...")
        capture = cv2.VideoCapture(_controller.send_camera_command("get_stream_url", fallback_resoulution))
        if not capture.isOpened():
            print(f"Failed to open stream with {fallback_resoulution} resolution.")
            return (None, None)
    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP {_controller.ip} and PORT {_controller.port}.")
        capture.release()
        return (None, None)
    
    return (capture, resolution)

def _load_intrinsics_for_camera(dimensions: str, debug: bool = False):
    
    global _Km, _Knew, _dist, _map1, _map2, _controller, _use_undistorted_view
    
    if _controller is None:
        _controller = DroidCamController(setup_connection())
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
def check_keys(dimensions: str = "1920x1080"):
    global _controller, _is_changing_camera
    if _controller is None:
        ip, port = setup_connection()
        _controller = DroidCamController(ip, port)
    
    camera_info, _is_changing_camera, reset_pocket_globals = _controller.send_camera_command("dump_camera_info")
    key = cv2.waitKey(1)
    if key == ord('q'):
        return (False, camera_info)
    elif key == ord('t'):
        _controller.send_camera_command("toggle_torch")
    elif key == ord('f'):
        _controller.send_camera_command("set_focus_mode", 2)  # Manual focus mode
        _controller.send_camera_command("set_manual_focus_value", 0.5)
    elif key == ord('z'):
        _controller.send_camera_command("set_zoom", 2.0)
    elif key == ord('e'):
        _controller.send_camera_command("set_exposure", 1.0)
    elif key == ord('c'):
        # Cycle through cameras 0 -> 1 -> 2 -> 3 -> 0 ...
        next_cam = (_controller.current_camera + 1) % len(_controller.CAMERA_MAP)
        _, _is_changing_camera, reset_pocket_globals = _controller.send_camera_command("select_camera", next_cam, dimensions)
        camera_info = _controller.send_camera_command("dump_camera_info")
    elif key in [ord('0'), ord('1'), ord('2'), ord('3')]:
        camera_number = int(chr(key))
        _controller.send_camera_command("select_camera", camera_number, dimensions)
        camera_info, _is_changing_camera, reset_pocket_globals = _controller.send_camera_command("dump_camera_info")
    elif key == ord('i'):
       camera_info, _is_changing_camera, reset_pocket_globals = _controller.send_camera_command("dump_camera_info")  # Camera info.
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
         debug_static: bool = False,
         debug: bool = False,
         ball_radius_range_px = (10,30), 
         work_resolution: str = "1920x1080",
         performance_mode: bool = False,
         perf_resoulution: str ="1280x720",
         fallback_resoulution: str ="1280x720",
         detection_mode: Enum = DetectionMode.YOLO,
         is_editor_build: bool = False
         ):

    debug_frame = None
    dimensions = None
    capture = None
    config = None   

    global _calib
    _calib = Calibrator(allow_center_crop=True, force_recalib=False)
    
    # Compute environment and static things, such as pockets.
    env = EnvironmentConfig.__new__(EnvironmentConfig)
    
    if debug and debug_config_name:
        config = env.get_debug_env_config(debug_config_name)
    else:
        config = env.get_environment_config(interactive= True, use_last_known= True)
    if config is not None:
        quest_ip, port = setup_connection(True, is_editor_build)
        send_config_name_to_quest(config.get_json_name_for_unity(), quest_ip, port)
    
    corner_inset_mm, side_inset_mm = config.pockets.derive_insets()
    pockets_mm = config.table.pocket_mm_positions(corner_inset_mm, side_inset_mm)
    (Lhsv, Uhsv)  = (config.table.cloth_lower_hsv, config.table.cloth_upper_hsv)
    Lmm, Wmm, Hmm = config.table.playfield_mm
    ball_diameter_m = config.ball_spec.diameter_m
    expected_aspect_ratio = Lmm / Wmm     # Consider the units in the future. Regardless of this, the 
    
    del config
    
    # Set up connection and open stream 
    
    if debug_static and debug_image_path:
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
        ip, port = setup_connection(False, False)
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
        # open_ports()
        # if not usb_sender.connect():
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
    
    global _is_changing_camera, _H_cached, _pockets_px_cached, _pockets_ready, _force_rescan, _frame_index, _pockets_adjusted, _pockets_px_adjusted_cached, _pockets_xy_m_adjusted_cached
    global _map1, _map2
    pocketed_distance_m = float(ball_diameter_m) * float(POCKETED_DISTANCE_FACTOR_OF_BALL_DIAMETER)
    pockets_xy_m = None
    
    
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
            
        if not debug_static:
            ret, frame = capture.read()
        elif debug_static and debug_image_path:
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
            capture, dimensions = open_stream(work_resolution, performance_mode, perf_resoulution, fallback_resoulution)

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
                
        frame = _calib.undistort_frame_if_needed(frame, _map1, _map2) if not debug else frame 

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

        # 3) Compute inner cushion corners (markerless)
        inner_corners, edges_dbg = _detector.detect_inner_cushion_corners(
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
        
        raw_frame = frame
        dbg_frame = raw_frame.copy() if (debug and (debug_pocket_display or True)) else None
        
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
                pockets_px, stable, max_delta_px = _detector.stabilize_pockets(pockets_px_raw, max_delta_px=POCKET_STABLE_MAX_DELTA_PX, required_stable_frames=POCKET_STABLE_REQUIRED_FRAMES)

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
                pockets_xy_m = _detector.warp_px_to_m(_H_cached, _pockets_px_cached)
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
                            cv2.circle(raw_frame, (int(x), int(y)), 14, (0, 255, 255), 2)
                    if _pockets_px_cached is not None:
                         for i, p in enumerate(_pockets_px_cached):
                            if p is None:
                                continue
                            x, y = p
                            xm, ym = pockets_xy_m[i]
                            cv2.circle(raw_frame, (int(x), int(y)), 9, (0, 255, 0), -1)  # filled green
                            text = f"SEND:{labels[i]} ({xm:.3f}m,{ym:.3f}m)"
                            cv2.putText(raw_frame, text, (int(x) + 8, int(y) - 8),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 1)
                    cv2.imshow("debug",raw_frame)
                    if (cv2.waitKey(1) & 0xFF) == ord("q"):
                        break

        if not _pockets_ready or (_H_cached is None) or (_pockets_px_cached is None):
            continue
        
        # BALL DETECTION and DETERMINATION IF THEY SHOULD BE CONSIDERED
        yolo_detections = []
        try:
            yolo_detections = _detector.detect_balls_yolov5(frame_bgr=frame,img_size=960)
        except Exception as e:
            print("[yolov5] ball detection failed:", e)
            yolo_detections = []
            
        # Convert detections from pixels -> meters using the current homography.
        centers_px = [(int(d["cx"]), int(d["cy"])) for d in yolo_detections]
        centers_m  = _detector.warp_px_to_m(_H_cached, centers_px)
        
        entries = []
        
        for det, (xm, ym) in zip(yolo_detections, centers_m):
            if xm is None or ym is None:
                continue
            # Maybe do this on Quest 3
            # Filter out of bounds / pocketed (pocket positions are in meters)
            # if not _detector.is_in_table_bounds(xm, ym, pockets_xy_m, float(ball_diameter_m)):
            #     continue
            # if _detector.is_in_playfield_bounds_xy(xm, ym, pockets_xy_m, float(ball_diameter_m)):
            #     continue
            # if _detector.is_pocketed(xm, ym, pockets_xy_m, pocketed_distance_m):
            #     continue
            
            entries.append({
                "type": p2p_classification_to_balltype(int(det.get("cls", -1))),
                "x": float(xm),
                "y": float(ym),
                "conf": float(det.get("confidence", 0.0)),
            })
    
        if debug_static:
        # UPDATED: cv2-only debug overlay (no matplotlib).
            for det in yolo_detections:
                x1 = int(det["x1"]); y1 = int(det["y1"]); x2 = int(det["x2"]); y2 = int(det["y2"])
                cx = int(det["cx"]); cy = int(det["cy"])
                cls_id = int(det.get("cls", -1))
                conf = float(det.get("confidence", 0.0))

                cv2.rectangle(raw_frame (x1, y1), (x2, y2), (255, 255, 0), 2)  # cyan
                cv2.circle(raw_frame, (cx, cy), 3, (255, 255, 0), -1)
                cv2.putText(
                    raw_frame,
                    f"{type_to_str(cls_id)} {conf:.2f}",
                    (x1, max(0, y1 - 6)),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.5,
                    (255, 255, 0),
                    1
                )

            cv2.imshow("debug-detections", raw_frame)
            if (cv2.waitKey(1) & 0xFF) == ord("q"):
                break
    if debug:
        # log_file.close()
        cv2.destroyAllWindows()
    if capture is not None:
        capture.release()
    usb_sender.close()
    _detector.dispose()
    
if __name__ == "__main__":
    import argparse
    
    # Supress warnings
    warnings.filterwarnings("ignore", category=FutureWarning, message=".*autocast.*")  
    
    parser = argparse.ArgumentParser(description="Detection / Calibration runner")
    
    # Debug switches
    parser.add_argument("--debug-conf", type=str, default="../Configuration/predator_9ft_virtual_debug.json", help="Path (relative or absolute) to a debug configuration used as a virtual debug video feed. Needs --debug mode flag set.")
    parser.add_argument("--debug-image", type=str, default="./pix2pockets/8-Ball-Pool-3/train/images/test.png", help="Path (relative or absolute) to a static image used as a virtual debug video feed. Needs --debug mode flag set.")
    parser.add_argument("--debug-pocket-display", action="store_true", help="If true, you are displaying a window with the pockets marked on the debug image.")
    parser.add_argument("--debug-editor", action="store_true", help="If true, the script assumes you are running the application inside of the Unity Editor.")
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
    parser.add_argument("--ball-radius-range", type=str, default="6,80", help="Comma-separated min,max radius for Hough circles, e.g. 8,28")
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
        calibrator = Calibrator(calib_dims)
        calibrator.run_calibration_only(calib_dims)
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
            main(args.debug_conf,
                 args.debug_image,
                 args.debug_pocket_display,
                 args.debug_static,
                 args.debug,
                 radius_range,                
                 args.work_res,                
                 args.performance,               
                 args.perf_res,                
                 args.fallback_res,                
                 args.detection_mode,
                 args.debug_editor)
        except Exception as e:
            print(f"Error while executing main loop. Check parameters....Exception: {e}")