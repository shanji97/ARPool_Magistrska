"""
Live ArUco detection test for the iPhone DroidCam feed.

Purpose:
- Connect to the same DroidCam API used by the ARPool detection pipeline.
- Open the configured DroidCam video stream.
- Detect OpenCV ArUco markers in the live frame.
- Draw marker outlines, IDs, centers, and estimated pixel side lengths.
- Optionally save debug snapshots for later inspection.

Placement:
- Copy this file into PoolSimulatorComponents/CameraAnalysis/ next to:
  - droid_cam_controller.py
  - helpers.py
  - calibration.py
- Run it from that folder so the existing relative imports and Configuration path work.

Python 3.12 dependencies:
- opencv-contrib-python is required for cv2.aruco.
- The plain opencv-python package may not include the aruco module.

Example:
    python aruco_droidcam_test.py --resolution 1920x1080 --dictionary DICT_4X4_50

Useful keys while running:
    q / ESC  Quit
    s        Save current annotated frame and raw frame
    i        Print DroidCam camera info
    t        Toggle torch
    0..3     Switch DroidCam camera by id
"""

from __future__ import annotations

import argparse
import os
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional

import cv2
import numpy as np

from droid_cam_controller import DroidCamController
from helpers import setup_connection


SUPPORTED_ARUCO_DICTIONARIES: dict[str, int] = {
    "DICT_4X4_50": cv2.aruco.DICT_4X4_50,
    "DICT_4X4_100": cv2.aruco.DICT_4X4_100,
    "DICT_4X4_250": cv2.aruco.DICT_4X4_250,
    "DICT_4X4_1000": cv2.aruco.DICT_4X4_1000,
    "DICT_5X5_50": cv2.aruco.DICT_5X5_50,
    "DICT_5X5_100": cv2.aruco.DICT_5X5_100,
    "DICT_5X5_250": cv2.aruco.DICT_5X5_250,
    "DICT_5X5_1000": cv2.aruco.DICT_5X5_1000,
    "DICT_6X6_50": cv2.aruco.DICT_6X6_50,
    "DICT_6X6_100": cv2.aruco.DICT_6X6_100,
    "DICT_6X6_250": cv2.aruco.DICT_6X6_250,
    "DICT_6X6_1000": cv2.aruco.DICT_6X6_1000,
    "DICT_APRILTAG_16h5": cv2.aruco.DICT_APRILTAG_16h5,
    "DICT_APRILTAG_25h9": cv2.aruco.DICT_APRILTAG_25h9,
    "DICT_APRILTAG_36h10": cv2.aruco.DICT_APRILTAG_36h10,
    "DICT_APRILTAG_36h11": cv2.aruco.DICT_APRILTAG_36h11,
}


@dataclass(frozen=True)
class MarkerObservation:
    marker_id: int
    center_px: tuple[float, float]
    side_lengths_px: tuple[float, float, float, float]
    mean_side_px: float
    corners_px: np.ndarray


def create_aruco_detector(dictionary_name: str):
    if not hasattr(cv2, "aruco"):
        raise RuntimeError(
            "cv2.aruco is not available. Install opencv-contrib-python, not only opencv-python."
        )

    if dictionary_name not in SUPPORTED_ARUCO_DICTIONARIES:
        supported = ", ".join(sorted(SUPPORTED_ARUCO_DICTIONARIES.keys()))
        raise ValueError(f"Unsupported dictionary '{dictionary_name}'. Supported: {supported}")

    aruco_dict = cv2.aruco.getPredefinedDictionary(SUPPORTED_ARUCO_DICTIONARIES[dictionary_name])

    # OpenCV 4.7+ API.
    if hasattr(cv2.aruco, "ArucoDetector"):
        parameters = cv2.aruco.DetectorParameters()
        parameters.cornerRefinementMethod = cv2.aruco.CORNER_REFINE_SUBPIX
        parameters.cornerRefinementWinSize = 5
        parameters.cornerRefinementMaxIterations = 30
        parameters.cornerRefinementMinAccuracy = 0.01
        return cv2.aruco.ArucoDetector(aruco_dict, parameters)

    # Older OpenCV API fallback.
    parameters = cv2.aruco.DetectorParameters_create()
    parameters.cornerRefinementMethod = cv2.aruco.CORNER_REFINE_SUBPIX
    return aruco_dict, parameters


def detect_markers(frame_bgr: np.ndarray, detector) -> tuple[list[np.ndarray], Optional[np.ndarray], list[np.ndarray]]:
    gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)

    if hasattr(cv2.aruco, "ArucoDetector") and hasattr(detector, "detectMarkers"):
        corners, ids, rejected = detector.detectMarkers(gray)
    else:
        aruco_dict, parameters = detector
        corners, ids, rejected = cv2.aruco.detectMarkers(gray, aruco_dict, parameters=parameters)

    corners = [] if corners is None else list(corners)
    rejected = [] if rejected is None else list(rejected)
    return corners, ids, rejected


def _side_lengths(corners_4x2: np.ndarray) -> tuple[float, float, float, float]:
    pts = np.asarray(corners_4x2, dtype=np.float32).reshape(4, 2)
    return tuple(float(np.linalg.norm(pts[(i + 1) % 4] - pts[i])) for i in range(4))


def build_observations(corners: Iterable[np.ndarray], ids: Optional[np.ndarray]) -> list[MarkerObservation]:
    if ids is None:
        return []

    observations: list[MarkerObservation] = []
    flat_ids = ids.flatten().astype(int)

    for marker_corners, marker_id in zip(corners, flat_ids):
        pts = np.asarray(marker_corners, dtype=np.float32).reshape(4, 2)
        center = np.mean(pts, axis=0)
        sides = _side_lengths(pts)
        observations.append(
            MarkerObservation(
                marker_id=int(marker_id),
                center_px=(float(center[0]), float(center[1])),
                side_lengths_px=sides,
                mean_side_px=float(sum(sides) / len(sides)),
                corners_px=pts,
            )
        )

    return sorted(observations, key=lambda item: item.marker_id)


def draw_marker_overlay(
    frame_bgr: np.ndarray,
    corners: list[np.ndarray],
    ids: Optional[np.ndarray],
    rejected: list[np.ndarray],
    observations: list[MarkerObservation],
    draw_rejected: bool,
    fps: float,
    dictionary_name: str,
    resolution: str,
) -> np.ndarray:
    annotated = frame_bgr.copy()

    if corners and ids is not None:
        cv2.aruco.drawDetectedMarkers(annotated, corners, ids)

    if draw_rejected and rejected:
        cv2.aruco.drawDetectedMarkers(annotated, rejected, borderColor=(0, 0, 255))

    for obs in observations:
        cx, cy = obs.center_px
        cv2.circle(annotated, (int(round(cx)), int(round(cy))), 5, (0, 255, 255), -1)
        cv2.putText(
            annotated,
            f"ID {obs.marker_id} side={obs.mean_side_px:.1f}px",
            (int(round(cx)) + 8, int(round(cy)) - 8),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.55,
            (0, 255, 255),
            2,
            cv2.LINE_AA,
        )

    cv2.putText(
        annotated,
        f"{dictionary_name} | {resolution} | detected={len(observations)} | rejected={len(rejected)} | fps={fps:.1f}",
        (20, 32),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.75,
        (255, 255, 255),
        2,
        cv2.LINE_AA,
    )
    cv2.putText(
        annotated,
        "q/ESC quit | s save | i info | t torch | 0..3 camera",
        (20, 64),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.65,
        (255, 255, 255),
        2,
        cv2.LINE_AA,
    )

    return annotated


def open_droidcam_stream(controller: DroidCamController, resolution: str) -> cv2.VideoCapture:
    if not controller.is_host_reachable(2):
        raise RuntimeError(f"DroidCam host is not reachable: {controller.ip}:{controller.port}")

    stream_url = controller.get_stream_url(resolution)
    capture = cv2.VideoCapture(stream_url)

    if not capture.isOpened():
        raise RuntimeError(f"Failed to open DroidCam stream: {stream_url}")

    ok, _ = capture.read()
    if not ok:
        capture.release()
        raise RuntimeError(f"DroidCam stream opened but did not return frames: {stream_url}")

    return capture


def save_debug_frames(output_dir: Path, raw_frame: np.ndarray, annotated_frame: np.ndarray) -> None:
    output_dir.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%d_%H%M%S")
    raw_path = output_dir / f"aruco_raw_{stamp}.png"
    annotated_path = output_dir / f"aruco_annotated_{stamp}.png"
    cv2.imwrite(str(raw_path), raw_frame)
    cv2.imwrite(str(annotated_path), annotated_frame)
    print(f"[save] {raw_path}")
    print(f"[save] {annotated_path}")


def print_observations(observations: list[MarkerObservation]) -> None:
    if not observations:
        print("[aruco] detected=0")
        return

    print(f"[aruco] detected={len(observations)}")
    for obs in observations:
        print(
            f"  id={obs.marker_id:02d} "
            f"center=({obs.center_px[0]:.1f}, {obs.center_px[1]:.1f}) "
            f"mean_side={obs.mean_side_px:.1f}px "
            f"sides=({obs.side_lengths_px[0]:.1f}, {obs.side_lengths_px[1]:.1f}, "
            f"{obs.side_lengths_px[2]:.1f}, {obs.side_lengths_px[3]:.1f})"
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="DroidCam ArUco marker live test")
    parser.add_argument("--resolution", default="1920x1080", help="DroidCam stream resolution, e.g. 1920x1080 or 1280x720.")
    parser.add_argument("--dictionary", default="DICT_4X4_50", choices=sorted(SUPPORTED_ARUCO_DICTIONARIES.keys()))
    parser.add_argument("--camera", type=int, default=1, choices=[0, 1, 2, 3], help="DroidCam camera id: 0 front, 1 main, 2 telephoto, 3 ultrawide.")
    parser.add_argument("--no-defaults", action="store_true", help="Do not call apply_default_settings() before opening the stream.")
    parser.add_argument("--draw-rejected", action="store_true", help="Draw rejected marker candidates in red.")
    parser.add_argument("--output-dir", default="./aruco_debug", help="Directory for saved snapshots.")
    parser.add_argument("--print-every", type=int, default=30, help="Print detection stats every N frames. Use 0 to disable.")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    detector = create_aruco_detector(args.dictionary)

    ip, port = setup_connection(connect_to_quest=False, is_editor_build=False, is_offline_run=False)
    controller = DroidCamController(ip, port)

    if not args.no_defaults:
        controller.apply_default_settings()

    if args.camera != controller.current_camera:
        controller.select_camera(args.camera)

    capture = open_droidcam_stream(controller, args.resolution)
    output_dir = Path(args.output_dir)

    frame_index = 0
    last_time = time.perf_counter()
    fps = 0.0

    print("[aruco] Stream opened.")
    print("[aruco] Use q/ESC to quit, s to save, i for camera info, t for torch, 0..3 to switch camera.")

    try:
        while True:
            ok, frame = capture.read()
            if not ok or frame is None:
                print("[stream] Frame read failed. Reopening stream...")
                capture.release()
                capture = open_droidcam_stream(controller, args.resolution)
                continue

            frame_index += 1
            now = time.perf_counter()
            dt = max(1e-6, now - last_time)
            last_time = now
            fps = (0.90 * fps) + (0.10 * (1.0 / dt)) if fps > 0.0 else (1.0 / dt)

            corners, ids, rejected = detect_markers(frame, detector)
            observations = build_observations(corners, ids)
            annotated = draw_marker_overlay(
                frame_bgr=frame,
                corners=corners,
                ids=ids,
                rejected=rejected,
                observations=observations,
                draw_rejected=args.draw_rejected,
                fps=fps,
                dictionary_name=args.dictionary,
                resolution=args.resolution,
            )

            if args.print_every > 0 and frame_index % int(args.print_every) == 0:
                print_observations(observations)

            cv2.imshow("DroidCam ArUco Test", annotated)
            key = cv2.waitKey(1) & 0xFF

            if key in (ord("q"), 27):
                break

            if key == ord("s"):
                save_debug_frames(output_dir, frame, annotated)

            if key == ord("i"):
                info = controller.get_camera_info()
                print(info)

            if key == ord("t"):
                controller.toggle_torch()

            if key in (ord("0"), ord("1"), ord("2"), ord("3")):
                camera_id = int(chr(key))
                print(f"[camera] Switching to {camera_id}")
                capture.release()
                controller.select_camera(camera_id)
                capture = open_droidcam_stream(controller, args.resolution)

    finally:
        capture.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
