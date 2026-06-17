from __future__ import annotations

import argparse
import time
import warnings
from typing import Optional

import cv2
import numpy as np

from ball_transport_aggregator import BallTransportAggregator
from calibration import CALIBRATION_PATTERNS, Calibrator
from connection import UsbTcpSender
from droid_cam_controller import DroidCamController
from formatters import (
    LABEL_MAP,
    build_bootstrap_payloads,
    build_conf_transfer_block,
    p2p_classification_to_balltype,
)
from helpers import (
    install_dependecies_for_other_projects,
    open_ports,
    resolve_debug_image_path,
    resolve_debug_video_path,
    setup_connection,
)
from object_detector import ObjectDetector
from objects_in_environment import EnvironmentConfigRepository


BALL_YOLO_IMAGE_SIZE = 960
MAX_RETRY_COUNT_FRAMES = 300
SEND_EVERY_N_FRAMES = 1

BALL_SEND_POSITION_DECIMALS = 4
BALL_SEND_CONF_DECIMALS = 3
BALL_SEND_VELOCITY_DECIMALS = 3
BALL_BATCH_SIZE_FRAMES = 3
BALL_RESET_MAX_POSITION_DELTA_M = 0.08
BALL_FORCE_SEND_INTERVAL_SEC = 0.25

QUEST_SECONDARY_QUEST_IP = "192.168.0.41"
QUEST_SECONDARY_QUEST_PORT = "5005"
QUEST_CONNECT_TIMEOUT_SEC = 0.25
QUEST_SEND_TIMEOUT_SEC = 0.25
QUEST_RETRY_INITIAL_DELAY_SEC = 0.25
QUEST_RETRY_MAX_DELAY_SEC = 1.0
QUEST_BOOTSTRAP_RESEND_INTERVAL_SEC = 2.0

DEFAULT_ENVIRONMENT_CONFIG_NAME = "last_environment.json"

_controller: Optional[DroidCamController] = None
_calib: Optional[Calibrator] = None
_Km = None
_Knew = None
_dist = None
_map1 = None
_map2 = None
_use_undistorted_view = False
_is_changing_camera = False
_detector: Optional[ObjectDetector] = None
_frame_index = 0


def _build_ball_debug_view(frame_bgr, yolo_detections):
    debug_view = frame_bgr.copy()

    for det in yolo_detections:
        x1 = int(det["x1"])
        y1 = int(det["y1"])
        x2 = int(det["x2"])
        y2 = int(det["y2"])
        cx = int(det["cx"])
        cy = int(det["cy"])
        cls_id = int(det.get("cls", -1))
        conf = float(det.get("confidence", 0.0))
        ball_type = p2p_classification_to_balltype(cls_id)

        cv2.rectangle(debug_view, (x1, y1), (x2, y2), (255, 255, 0), 2)
        cv2.circle(debug_view, (cx, cy), 3, (255, 255, 0), -1)
        cv2.putText(
            debug_view,
            f"{ball_type} {conf:.2f}",
            (x1, max(0, y1 - 6)),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (255, 255, 0),
            1,
        )

    cv2.putText(
        debug_view,
        f"detections={len(yolo_detections)} | table registration not active yet",
        (20, 30),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.7,
        (255, 255, 0),
        2,
    )

    return debug_view


def _show_ball_debug_windows(frame_bgr, yolo_detections):
    cv2.imshow("debug-ball-detections", _build_ball_debug_view(frame_bgr, yolo_detections))


def _detections_to_pixel_entries(yolo_detections, process_unknowns: bool = False):
    entries = []

    for det in yolo_detections:
        ball_type = p2p_classification_to_balltype(int(det.get("cls", -1)))

        if ball_type == "u" and not process_unknowns:
            continue

        entries.append(
            {
                "type": ball_type,
                "x_px": float(det["cx"]),
                "y_px": float(det["cy"]),
                "conf": float(det.get("confidence", 0.0)),
                "bbox_px": (
                    float(det["x1"]),
                    float(det["y1"]),
                    float(det["x2"]),
                    float(det["y2"]),
                ),
            }
        )

    return entries


def _try_map_pixel_entries_to_table_entries(pixel_entries, frame_bgr, config):
    """
    Converts YOLO pixel-space detections into table-space metre entries.

    This function is intentionally a guarded placeholder in the current
    config-only fiducial baseline. It must not return raw pixel coordinates
    as x/y because the Quest-side receiver expects table-space metres.

    Future implementation steps:
    1. detect configured ArUco or QR fiducials from the frame,
    2. estimate the camera-to-table transform or planar homography,
    3. map x_px/y_px into table-space x/y metres,
    4. return entries shaped for build_conf_transfer_block(...).

    Expected output shape once implemented:
    [
        {
            "type": "c" | "e" | "so" | "st" | "u",
            "x": table_x_m,
            "y": table_y_m,
            "conf": confidence,
        }
    ]
    """

    _ = pixel_entries
    _ = frame_bgr
    _ = config

    return []


def _send_table_detection_entries(
    usb_sender,
    ball_transport,
    table_entries,
    ball_diameter_m: float,
    camera_height_m: float,
    debug: bool,
):
    if usb_sender is None:
        return False

    if not table_entries:
        ball_transport.reset()
        return False

    now_sec = time.time()
    entries_to_send = ball_transport.push(table_entries, now_sec)

    if entries_to_send is None:
        return False

    data = build_conf_transfer_block(
        pockets=None,
        table_LW_m=None,
        ball_diameter_m=ball_diameter_m,
        camera_height_m=camera_height_m,
        detection_entries=entries_to_send,
        discard_diamonds=True,
        pos_decimals=BALL_SEND_POSITION_DECIMALS,
        conf_decimals=BALL_SEND_CONF_DECIMALS,
        vel_decimals=BALL_SEND_VELOCITY_DECIMALS,
        use_aggregate_ball_line=True,
    )

    if not data.strip():
        return False

    sent = usb_sender.send(data)

    if debug and sent:
        print(data)
    elif debug:
        print("[USB] Ball transfer failed. The sender will retry on the next send.")

    return sent


def _summarize_environment_config(config, configuration_name: str) -> None:
    length_mm, width_mm, playfield_height_mm = config.table.playfield_mm
    marker_plane_height_mm = config.table.effective_marker_plane_height_mm()
    ball_center_height_mm = config.table.ball_center_height_mm(config.ball_spec.diameter_m)

    print("[config] Loaded environment:", configuration_name)
    print(
        "[config] Table playfield: "
        f"L={float(length_mm):.1f} mm, W={float(width_mm):.1f} mm, "
        f"playfieldZ={float(playfield_height_mm):.1f} mm"
    )
    print(
        "[config] Heights: "
        f"markerPlaneZ={float(marker_plane_height_mm):.1f} mm, "
        f"ballCenterZ={float(ball_center_height_mm):.3f} mm, "
        f"cameraZ={float(config.camera.height_from_floor_m) * 1000.0:.1f} mm"
    )

    for marker_type in ("aruco", "qr"):
        marker_set = config.get_fiducial_set(marker_type)
        if marker_set is None:
            print(f"[config] Fiducial set '{marker_type}': not configured")
            continue

        enabled_markers = marker_set.enabled_markers()
        ids = [marker.id for marker in enabled_markers]
        dictionary_text = f", dictionary={marker_set.dictionary_name}" if marker_set.dictionary_name else ""
        payload_text = f", payloadPrefix={marker_set.payload_prefix}" if marker_set.payload_prefix else ""

        print(
            f"[config] Fiducial set '{marker_type}': "
            f"count={len(enabled_markers)}, size={marker_set.marker_size_mm:.1f} mm, "
            f"margin={marker_set.white_margin_mm:.1f} mm, cutout={marker_set.cutout_size_mm:.1f} mm, "
            f"ids={ids}{dictionary_text}{payload_text}"
        )


def open_stream(
    work_resolution: str = "1920x1080",
    performance_mode: bool = False,
    perf_resoulution: str = "1280x720",
    fallback_resoulution: str = "1280x720",
    debug: bool = False,
    debug_static_image_present: bool = False,
    debug_offline: bool = False,
):
    if debug and debug_static_image_present:
        return None, None

    global _controller
    if _controller is None:
        ip, port = setup_connection(False, False, debug_offline)
        _controller = DroidCamController(ip, port)
        if _controller is None:
            print("Controller is not initialized; cannot open stream.")
            return None, None

    if not _controller.is_host_reachable(2):
        try:
            print(f"Device at {_controller.ip}:{_controller.port} is not reachable. Check network settings. Exiting.")
        except Exception:
            print("Device not reachable. Check network settings. Exiting.")
        return None, None

    resolution = work_resolution if performance_mode is False else perf_resoulution
    capture = cv2.VideoCapture(_controller.send_camera_command("get_stream_url", resolution))
    _controller.apply_default_settings()

    if not capture.isOpened():
        print(f"Failed to open stream with {resolution} resolution, trying with {fallback_resoulution}...")
        capture = cv2.VideoCapture(_controller.send_camera_command("get_stream_url", fallback_resoulution))
        if not capture.isOpened():
            print(f"Failed to open stream with {fallback_resoulution} resolution.")
            return None, None

    ret, _ = capture.read()
    if not ret:
        print(f"Could not connect to DroidCam server. Check IP {_controller.ip} and PORT {_controller.port}.")
        capture.release()
        return None, None

    return capture, resolution


def _load_intrinsics_for_camera(dimensions: str, debug: bool = False, debug_offline: bool = False):
    global _Km, _Knew, _dist, _map1, _map2, _controller, _use_undistorted_view

    if _controller is None:
        ip, port = setup_connection(False, False, debug_offline)
        _controller = DroidCamController(ip, port)
        if _controller is None:
            print("Controller is not initialized, so no intrinsics can be loaded. Aborting...")
            return

    meta = _controller.CAMERA_MAP[_controller.current_camera]
    _use_undistorted_view = (meta or {}).get("lens_correction_on", False)
    cam_folder_alias = (meta or {}).get("folder_alias", "main")

    if not cam_folder_alias:
        _Km = _Knew = _dist = _map1 = _map2 = None
        return

    intr = _calib.get_intrinsics_auto(cam_folder_alias, dimensions, candidates=CALIBRATION_PATTERNS)
    _Km = intr.K()
    _dist = np.array(intr.dist, np.float64)
    w, h = map(int, dimensions.split("x"))

    if _use_undistorted_view:
        if _Knew is None or _map1 is None or _map2 is None:
            print(f"[calib] Building undistortion maps for {cam_folder_alias} at {dimensions}")
            _Knew, _ = cv2.getOptimalNewCameraMatrix(_Km, _dist, (w, h), 1.0, (w, h))
            _map1, _map2 = cv2.initUndistortRectifyMap(_Km, _dist, None, _Knew, (w, h), cv2.CV_16SC2)
    else:
        _Knew = None
        _map1 = _map2 = None
        if debug and _Km is not None:
            print("[K (distorted)]\n", _Km)
            print("[dist] ", _dist.ravel())


def check_keys(dimensions: str = "1920x1080"):
    global _controller, _is_changing_camera

    if _controller is None:
        ip, port = setup_connection(False, False, False)
        _controller = DroidCamController(ip, port)

    key = cv2.waitKey(1)
    camera_info = None

    if key == ord("q"):
        return False, camera_info

    if key == ord("t"):
        _controller.send_camera_command("toggle_torch")
    elif key == ord("f"):
        _controller.send_camera_command("set_focus_mode", 2)
        _controller.send_camera_command("set_manual_focus_value", 0.5)
    elif key == ord("z"):
        _controller.send_camera_command("set_zoom", 2.0)
    elif key == ord("e"):
        _controller.send_camera_command("set_exposure", 1.0)
    elif key == ord("c"):
        next_cam = (_controller.current_camera + 1) % len(_controller.CAMERA_MAP)
        _, _is_changing_camera, _ = _controller.send_camera_command("select_camera", next_cam, dimensions)
        camera_info = _controller.send_camera_command("dump_camera_info")
    elif key in [ord("0"), ord("1"), ord("2"), ord("3")]:
        camera_number = int(chr(key))
        _controller.send_camera_command("select_camera", camera_number, dimensions)
        camera_info, _is_changing_camera, _ = _controller.send_camera_command("dump_camera_info")
    elif key == ord("i"):
        camera_info, _is_changing_camera, _ = _controller.send_camera_command("dump_camera_info")

    return True, camera_info


def _resolve_input_source(
    debug_image_path: Optional[str],
    debug_video_path: Optional[str],
    debug_recorded: bool,
    debug_static: bool,
    debug: bool,
    work_resolution: str,
    performance_mode: bool,
    perf_resoulution: str,
    fallback_resoulution: str,
    debug_offline: bool,
):
    debug_frame = None
    dimensions = None
    capture = None

    if debug_static:
        resolved_debug_image_path = resolve_debug_image_path(debug_image_path)
        if not resolved_debug_image_path:
            print("[debug-image] Exiting because no valid debug image was selected.")
            return None, None, None

        debug_frame = cv2.imread(resolved_debug_image_path, cv2.IMREAD_COLOR)
        if debug_frame is None:
            raise FileNotFoundError(f"[debug] Could not read debug image: {resolved_debug_image_path}")

        work_w, work_h = map(int, work_resolution.split("x"))
        debug_frame = cv2.resize(debug_frame, (work_w, work_h), interpolation=cv2.INTER_AREA)
        dimensions = work_resolution

        print(f"[debug] Using static image as fake feed: {resolved_debug_image_path}.")
        print(f"[debug] Resized debug image to work-res: {dimensions}")

        return capture, dimensions, debug_frame

    if debug_recorded:
        resolved_debug_video_path = resolve_debug_video_path(debug_video_path)
        if not resolved_debug_video_path:
            print("[debug-recorded] Exiting because no valid recorded video was selected.")
            return None, None, None

        capture = cv2.VideoCapture(resolved_debug_video_path)
        if capture is None or not capture.isOpened():
            raise RuntimeError(f"[debug-recorded] Could not open recorded video: {resolved_debug_video_path}")

        dimensions = work_resolution
        source_w = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH) or 0)
        source_h = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT) or 0)

        print(f"[debug-recorded] Using recorded video feed: {resolved_debug_video_path}.")
        if source_w > 0 and source_h > 0:
            print(f"[debug-recorded] Source resolution: {source_w}x{source_h} -> work-res: {dimensions}")
        else:
            print(f"[debug-recorded] Using work-res: {dimensions}")

        return capture, dimensions, debug_frame

    global _controller
    ip, port = setup_connection(False, False, debug_offline)
    _controller = DroidCamController(ip, port)

    capture, dimensions = open_stream(
        work_resolution,
        performance_mode,
        perf_resoulution,
        fallback_resoulution,
        debug,
        debug_static,
        debug_offline,
    )

    if capture is None:
        print("Could not open stream.")
        return None, None, None

    return capture, dimensions, debug_frame


def _read_next_frame(capture, debug_frame, debug_static: bool, debug_recorded: bool, work_resolution: str):
    if debug_static and debug_frame is not None:
        return True, debug_frame.copy()

    ret, frame = capture.read()
    if not ret or frame is None:
        return ret, frame

    if debug_recorded:
        work_w, work_h = map(int, work_resolution.split("x"))
        if frame.shape[1] != work_w or frame.shape[0] != work_h:
            frame = cv2.resize(frame, (work_w, work_h), interpolation=cv2.INTER_AREA)

    return ret, frame


def main(
    debug_config_name: Optional[str],
    debug_image_path: Optional[str],
    debug_video_path: Optional[str],
    debug_recorded: bool = False,
    debug_offline: bool = False,
    debug_static: bool = False,
    debug: bool = False,
    work_resolution: str = "1920x1080",
    performance_mode: bool = False,
    perf_resoulution: str = "1280x720",
    fallback_resoulution: str = "1280x720",
    is_editor_build: bool = False,
    debug_detection: bool = False,
    process_unknowns: bool = False,
):
    usb_sender = None
    capture = None

    environment_repository = EnvironmentConfigRepository()
    config_name = debug_config_name or DEFAULT_ENVIRONMENT_CONFIG_NAME
    config = environment_repository.get_environment_config(
        interactive=True,
        use_last_known=True,
        config_name=config_name,
        debug=debug,
    )

    if config is None:
        print("[config] No environment configuration was loaded.")
        return

    configuration_name_for_unity = environment_repository.get_json_name_for_unity() or config_name
    _summarize_environment_config(config, configuration_name_for_unity)

    global _calib
    _calib = Calibrator(allow_center_crop=True, force_recalib=False)

    q_ip, q_port = setup_connection(True, is_editor_build, debug_offline)
    usb_sender = UsbTcpSender(
        host=q_ip,
        port=q_port,
        auto_reconnect=True,
        connect_timeout_s=QUEST_CONNECT_TIMEOUT_SEC,
        send_timeout_s=QUEST_SEND_TIMEOUT_SEC,
        retry_initial_delay_s=QUEST_RETRY_INITIAL_DELAY_SEC,
        retry_max_delay_s=QUEST_RETRY_MAX_DELAY_SEC,
        is_offline_run=debug_offline,
    )

    last_bootstrap_attempt_time = 0.0
    bootstrap_payloads = build_bootstrap_payloads(q_ip, QUEST_SECONDARY_QUEST_IP, configuration_name_for_unity)

    if not debug_offline:
        initial_usb_connection_ready = usb_sender.connect()

        if (not initial_usb_connection_ready) and is_editor_build:
            open_ports(5005, is_editor_build)
            initial_usb_connection_ready = usb_sender.connect()

        if not initial_usb_connection_ready:
            print("[USB] Initial Quest connection failed. Detection will continue and reconnect on send.")

    usb_sender.send_bootstrap_payloads(bootstrap_payloads, debug)

    capture, dimensions, debug_frame = _resolve_input_source(
        debug_image_path=debug_image_path,
        debug_video_path=debug_video_path,
        debug_recorded=debug_recorded,
        debug_static=debug_static,
        debug=debug,
        work_resolution=work_resolution,
        performance_mode=performance_mode,
        perf_resoulution=perf_resoulution,
        fallback_resoulution=fallback_resoulution,
        debug_offline=debug_offline,
    )

    if dimensions is None:
        print("[input] No capture dimensions were resolved.")
        if usb_sender is not None:
            usb_sender.close()
        return

    if dimensions is not None and not debug:
        try:
            pre = _calib.precompute_all(dimensions, force=False)
            _load_intrinsics_for_camera(dimensions, debug, debug_offline)
            if debug:
                print(pre)
        except Exception as e:
            print("Precompute failed:", e)

    global _detector
    _detector = ObjectDetector(LABEL_MAP)

    ball_transport = BallTransportAggregator(
        batch_size_frames=BALL_BATCH_SIZE_FRAMES,
        pos_decimals=BALL_SEND_POSITION_DECIMALS,
        conf_decimals=BALL_SEND_CONF_DECIMALS,
        vel_decimals=BALL_SEND_VELOCITY_DECIMALS,
        reset_max_position_delta_m=BALL_RESET_MAX_POSITION_DELTA_M,
        force_send_interval_sec=BALL_FORCE_SEND_INTERVAL_SEC,
    )

    retry_count = 0
    frame_counter = 0
    start_time = time.time() if debug else None

    global _frame_index
    global _is_changing_camera

    try:
        while True:
            _frame_index += 1

            now_bootstrap_sec = time.time()
            if (
                bootstrap_payloads
                and not debug_offline
                and (now_bootstrap_sec - last_bootstrap_attempt_time) >= QUEST_BOOTSTRAP_RESEND_INTERVAL_SEC
            ):
                last_bootstrap_attempt_time = now_bootstrap_sec
                usb_sender.send_bootstrap_payloads(bootstrap_payloads, debug)

            if _is_changing_camera:
                print("Changing camera - skipping current frame(s).")
                retry_count = 0
                continue

            ret, frame = _read_next_frame(capture, debug_frame, debug_static, debug_recorded, work_resolution)

            if not ret or frame is None or getattr(frame, "size", 0) == 0:
                if debug_static:
                    print("[debug] Static debug frame invalid. Exiting loop.")
                    break

                if debug_recorded:
                    print("[debug-recorded] End of file reached. Exiting loop.")
                    break

                retry_count += 1
                if retry_count >= MAX_RETRY_COUNT_FRAMES:
                    print(f"Frame capture failed too many times ({MAX_RETRY_COUNT_FRAMES} frames), exiting.")
                    break

                if capture is not None:
                    capture.release()

                capture, dimensions = open_stream(
                    work_resolution,
                    performance_mode,
                    perf_resoulution,
                    fallback_resoulution,
                    debug,
                    debug_static,
                    debug_offline,
                )
                continue

            retry_count = 0

            if debug:
                frame_counter += 1
                if frame_counter % 30 == 0 and start_time is not None:
                    elapsed = max(1e-6, time.time() - start_time)
                    fps = frame_counter / elapsed
                    print(f"[INFO] FPS: {fps:.2f}")

            frame = _calib.undistort_frame_if_needed(frame, _map1, _map2) if _calib is not None else frame

            yolo_detections = []
            try:
                yolo_detections = _detector.detect_balls_yolov5(frame_bgr=frame, img_size=BALL_YOLO_IMAGE_SIZE)
            except Exception as e:
                print("[yolov5] ball detection failed:", e)
                yolo_detections = []

            pixel_entries = _detections_to_pixel_entries(yolo_detections, process_unknowns)

            table_entries = _try_map_pixel_entries_to_table_entries(
                pixel_entries=pixel_entries,
                frame_bgr=frame,
                config=config,
            )

            _send_table_detection_entries(
                usb_sender=usb_sender,
                ball_transport=ball_transport,
                table_entries=table_entries,
                ball_diameter_m=config.ball_spec.diameter_m,
                camera_height_m=config.camera.height_from_floor_m,
                debug=debug,
            )

            if debug_detection:
                _show_ball_debug_windows(frame, yolo_detections)

            if debug_detection:
                should_continue, camera_info = check_keys(dimensions or work_resolution)
                if camera_info:
                    print(camera_info)
                if not should_continue:
                    break
    finally:
        if debug_detection or debug_static or debug_recorded or debug:
            cv2.destroyAllWindows()

        if capture is not None:
            capture.release()

        if usb_sender is not None:
            usb_sender.close()

        if _detector is not None:
            _detector.dispose()


if __name__ == "__main__":
    warnings.filterwarnings("ignore", category=FutureWarning, message=".*autocast.*")

    parser = argparse.ArgumentParser(description="ARPool detection runner - config-only fiducial baseline")

    parser.add_argument("--debug", action="store_true", help="Run debug mode. Requires one debug source flag.")
    parser.add_argument("--debug-conf", type=str, default=None, help="Environment JSON name. Defaults to last_environment_fiducials.json.")
    parser.add_argument("--debug-use-config", action="store_true", help="Use last_environment_fiducials.json explicitly.")
    parser.add_argument("--debug-detection", action="store_true", help="Show YOLO ball detection overlay.")
    parser.add_argument("--debug-editor", action="store_true", help="Use editor Quest connection mode.")
    parser.add_argument("--debug-image", type=str, default="../../../candidate_testing_images", help="Debug image file or folder.")
    parser.add_argument("--debug-video", type=str, default="../../../video_test", help="Debug video file or folder.")
    parser.add_argument("--debug-recorded", action="store_true", help="Use a recorded debug video input.")
    parser.add_argument("--debug-offline", action="store_true", help="Disable Quest transport while keeping camera/debug input available.")
    parser.add_argument("--debug-phone", action="store_true", help="Use live phone capture in debug mode.")
    parser.add_argument("--debug-static", action="store_true", help="Use a static image as fake input feed.")

    parser.add_argument("--calibrate-only", action="store_true", help="Run camera calibration precompute only.")
    parser.add_argument("--calib-res", type=str, default="1920x1080", help="Calibration resolution, e.g. 1920x1080.")
    parser.add_argument("--work-res", type=str, default="1920x1080", help="Work resolution, e.g. 1920x1080.")
    parser.add_argument("--perf-res", type=str, default="1280x720", help="Performance resolution.")
    parser.add_argument("--fallback-res", type=str, default="1280x720", help="Fallback resolution.")
    parser.add_argument("--performance", action="store_true", help="Use performance resolution.")
    parser.add_argument("--force-calib", action="store_true", help="Force camera calibration recomputation.")
    parser.add_argument("--process-unknowns", action="store_true", help="Keep YOLO unknown class detections in pixel debug entries.")

    args = parser.parse_args()

    debug_source_count = sum(
        [
            1 if args.debug_static else 0,
            1 if args.debug_recorded else 0,
            1 if args.debug_phone else 0,
        ]
    )

    if debug_source_count > 0 and not args.debug:
        print("Debug source flags require --debug.")
        raise SystemExit(1)

    if debug_source_count > 1:
        print("Static image analysis, recorded video playback, and live phone capture cannot run at the same time.")
        raise SystemExit(1)

    if args.debug and debug_source_count == 0:
        print("Either static image analysis, recorded video playback, or live capture must be enabled while running in debug mode.")
        raise SystemExit(1)

    if args.debug_use_config:
        args.debug_conf = DEFAULT_ENVIRONMENT_CONFIG_NAME

    if args.debug_offline and not args.debug:
        print("You need to run this in general debug mode.")
        raise SystemExit(1)

    if args.work_res is not None and args.performance:
        print("Performance is set to true, so working resolution is going to be overridden to 720p.")
        args.work_res = "1280x720"

    if args.calibrate_only:
        calib_dims = args.calib_res or args.perf_res or args.fallback_res
        print(f"[calib-only] Running precompute_all for {calib_dims} (force={args.force_calib})")
        calibrator = Calibrator(allow_center_crop=True, force_recalib=args.force_calib)
        calibrator.run_calibration_only(calib_dims)
        print("Done. Re-run the application without the --calibrate-only flag.")
        raise SystemExit(0)

    try:
        install_dependecies_for_other_projects(["pix2pockets"])
        main(
            debug_config_name=args.debug_conf,
            debug_image_path=args.debug_image,
            debug_video_path=args.debug_video,
            debug_recorded=args.debug_recorded,
            debug_offline=args.debug_offline,
            debug_static=args.debug_static,
            debug=args.debug,
            work_resolution=args.work_res,
            performance_mode=args.performance,
            perf_resoulution=args.perf_res,
            fallback_resoulution=args.fallback_res,
            is_editor_build=args.debug_editor,
            debug_detection=args.debug_detection,
            process_unknowns=args.process_unknowns,
        )
    except Exception as e:
        print(f"Error while executing main loop. Check parameters. Exception: {e}")