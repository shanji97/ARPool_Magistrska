from __future__ import annotations
import json
import os
from dataclasses import dataclass, asdict, field
from typing import Dict, List, Optional, Tuple
import time
from datetime import datetime

import cv2
import numpy as np

from metrics import compute_blur_laplacian, compute_coverage_fraction, classify_quality
from stats_logger import StatsLogger

MANIFEST_PREFIX = "calibcache_"
CACHE_PREFIX = "_downscaled_"

CALIBRATION_PATTERNS = [
    "20mm_13x9",
    "25mm_10x7",
    "30mm_6x8",
    "35mm_7x4",
    ]

@dataclass
class Intrinsics:
    fx: float
    fy: float
    cx: float
    cy: float
    dist: List[float]
    width: int
    height: int
    rms: float
    model: str = "opencv_standard"
    resolution: str = ""
    created_at: float = field(default_factory=time.time)
    
    def K(self):
        return np.array([[self.fx, 0.0, self.cx],
                         [0.0, self.fy, self.cy],
                         [0.0, 0.0, 1.0]], dtype=np.float64)
    
class Calibrator:
    
    CAMERA_FOLDERS = {
    "main": ("main", "main_intrinsics"),
    "tp": ("tp","tp_intrinsics"),                      
    "uw_wth_lens_dist": ("uw_wth_lens_dist","uw_wth_lens_dist_intrinsics"),
    }
    
    DEFAULT_PATTERN_BY_CAM = {
    "main": "25mm_10x7",
    "tp": "35mm_7x4",
    "uw_wth_lens_dist": "20mm_13x9",
    }
    
    def __init__(self, 
                 work_resolution = "1920x1080",
                 sq_size_meters = 0.020,
                 checkerboard_inner_corners = (13 , 9), 
                 force_recalib = False,
                 fish_eye_for_uw =  False,
                 device = "i16pm",
                 use_rational_model = False,
                 base_directory_parts = ["Images","Calibration","in_ex"],
                 allow_center_crop = True):
        self.width, self.height = self._parse_resolution(work_resolution)
        self.base_directory =  os.path.join(os.path.dirname(__file__), 
                                            base_directory_parts[0],
                                            base_directory_parts[1], 
                                            base_directory_parts[2],
                                            device, 
                                            f"{self.height}p")     
        self.inner_corners = checkerboard_inner_corners
        self.sq_size_meters = sq_size_meters
        self.use_fisheye_uw =  fish_eye_for_uw
        self.force_recalib = force_recalib
        self.use_rational_model = use_rational_model
        self.allow_center_crop = allow_center_crop
        self.statsLogger = StatsLogger(
            os.path.join(os.path.dirname(__file__), 
                         base_directory_parts[0],
                         base_directory_parts[1], 
                         base_directory_parts[2]), 
            f"{self.height}p",
            device,
            True,
            True)
        self._cache: Dict[str, Intrinsics] = {}
    
    def change_base_directory(self, new_base_directory: str):
        self.base_directory = new_base_directory
        
    def downscale_image(self, image_path: str, desired_resolution: str = "1920x1080"):
        image = cv2.imread(image_path)
        if image is None:
            raise FileNotFoundError(f"Image not found: {image_path}.")
        try:
            w, h = self._parse_resolution(desired_resolution)
        except Exception:
            w, h = self.width, self.height
            desired_resolution = f"{self.width}x{self.height}"
            
        image_resized = cv2.resize(image, (w, h), interpolation=cv2.INTER_LANCZOS4)
        ext = os.path.splitext(image_path)[1]
        output_path = f"{os.path.splitext(image_path)[0]}_{w}x{h}{ext}"
        cv2.imwrite(output_path, image_resized)
        
        return output_path
        
    def toggle_center_crop(self, allow: bool):
        if not isinstance(allow, bool):
            raise ValueError("allow_center_crop must be a boolean value.")
        self.allow_center_crop = allow        
    
    def set_custom_sq_size(self, sq_size_meters: float):
        if sq_size_meters <= 0:
            raise ValueError("Square size must be a positive number.")
        self.sq_size_meters = sq_size_meters
    
    def set_custom_inner_corners(self, inner_corners: Tuple[int, int]):
        if len(inner_corners) != 2 or any(x <= 0 for x in inner_corners):
            raise ValueError("Inner corners must be a tuple of two positive integers.")
        self.inner_corners = inner_corners
    
    def toggle_force_racalibration(self, recalibration_on: bool):
        if not isinstance(recalibration_on, bool):
            raise ValueError("Force recalibration must be a boolean value.")
        self.force_recalib = recalibration_on
        
    def toggle_rational_model(self, use_rational_model: bool):
        if not isinstance(use_rational_model, bool):
            raise ValueError("Use rational model must be a boolean value.")
        self.use_rational_model = use_rational_model
        
    def available_patterns(self, cam_key: str) -> List[str]:
        camera_directory = os.path.join(self.base_directory, self.CAMERA_FOLDERS[cam_key][0])
        if not os.path.exists(camera_directory):
            return []
        extensions = {".jpg", ".jpeg", ".png"}
        found_directories = []
        for name in sorted(os.listdir(camera_directory)):
            path = os.path.join(camera_directory, name)
            if not os.path.isdir(path):
                continue
            if any(
                os.path.splitext(file)[1].lower() in extensions
                for file in os.listdir(path)
                if os.path.isfile(os.path.join(path, file))):
                
                found_directories.append(name)
        return found_directories
    
    def pick_pattern(self, cam_key: str, candidates: Optional[List[str]] = None) -> Optional[str]:
        available_patterns = self.available_patterns(cam_key)
        aset = set(available_patterns)
        
        if candidates:
            for candidate in candidates:
                if candidate in aset:
                    return candidate
                
        default_pattern = self.DEFAULT_PATTERN_BY_CAM.get(cam_key)
        if default_pattern in aset:
            return default_pattern
        return available_patterns[0] if available_patterns else None
    
    def precompute_all(self, target_resolution: str, force: bool = False) -> Dict[str, List[Tuple[str, float]]]:
        report: Dict[str, List[Tuple[str, float]]] = {}
        old_force = self.force_recalib
        try:
            self.force_recalib = force
            for cam_key in self.CAMERA_FOLDERS.keys():
                patterns = self.available_patterns(cam_key)
                if not patterns:
                    patterns = [""] #camera root
                rep: List[Tuple[str, float]] = []
                for pattern in patterns:
                    intr = self.get_intrinsics(cam_key, target_resolution, pattern=pattern)
                    rep.append((pattern or "<root>", intr.rms))
                report[cam_key] = rep
        finally:
            self.force_recalib = old_force
        return report
    
    def get_intrinsics_auto(self, cam_key: str, target_resolution: str, candidates: Optional[List[str]] = None) -> Intrinsics:
        pattern = self.pick_pattern(cam_key, candidates)
        if pattern is None:
            return self.get_intrinsics(cam_key, target_resolution)
        return self.get_intrinsics(cam_key, target_resolution, pattern)
            
    
    def get_intrinsics(self, camera: str, target_resolution = "1920x1080", pattern: str = "") -> Intrinsics:
        cam_key = self._normalize_camera(camera)
        target_res = target_resolution or f"{self.width}x{self.height}"
        
        try:
            intrinsics = self._load_json(cam_key, target_res, pattern)
            if intrinsics and self.force_recalib is False:
                self._cache[cam_key] = intrinsics
                return intrinsics
        
            sq_size_m, cols, rows = self._resolve_pattern(cam_key, pattern)
            prev_inner_corners = self.inner_corners
            self.inner_corners = (cols, rows)
            self.sq_size_meters = sq_size_m
            prev_sq_size_m = self.sq_size_meters
            
            if target_res == f"{self.width}x{self.height}" or self.force_recalib:
                cache_directory = self._prepare_image_set(cam_key, target_res, pattern)
                intrinsics = self._compute_from_folder(cam_key, pattern, cache_directory, self.statsLogger)
                
                self._save_json(cam_key, intrinsics, pattern)
                self._cache[cam_key] = intrinsics
                
                return intrinsics

            base_intrinsics = self.get_intrinsics(cam_key, f"{self.width}x{self.height}", pattern)
            scaled_intrinsics = self._scale_intrinsics(base_intrinsics, target_res)
            
            self._save_json(cam_key, scaled_intrinsics, pattern)
            return scaled_intrinsics
        finally:
            self.sq_size_meters = prev_sq_size_m
            self.inner_corners = prev_inner_corners
    
    def undistort(self, img: np.ndarray, camera: str, target_resolution = None) -> np.ndarray:
        intrinsics = self.get_intrinsics(camera, target_resolution)
        K = intrinsics.K()
        dist = np.array(intrinsics.dist, dtype=np.float32)
        new_K, _ = cv2.getOptimalNewCameraMatrix(K,
                                                 dist,
                                                 (intrinsics.width, intrinsics.height),
                                                 1.0, 
                                                 (intrinsics.width, intrinsics.height)) 
        return cv2.undistort(img, K, dist, None, new_K)      
            
    def _prepare_image_set(self, cam_key: str, target_resolution: str, pattern: str = "") -> str:
        tw, th = self._parse_resolution(target_resolution)
        exts = {".jpg", ".jpeg", ".png", ".JPG", ".JPEG", ".PNG"}
        
        target_aspect_ratio = self._aspect_ratio(tw, th)
        original_directory = self._pattern_join(cam_key, pattern)
        if not os.path.exists(original_directory):
            raise FileNotFoundError(f"Calibration folder not found: {original_directory}.")
        
        cache_directory = os.path.join(original_directory, f"{CACHE_PREFIX}{tw}x{th}") 
        os.makedirs(cache_directory, exist_ok=True)
        
        manifest_path = os.path.join(original_directory, f"{MANIFEST_PREFIX}{tw}x{th}.json")
        
        manifest: Dict[str, float] = {}
        if os.path.exists(manifest_path):
            try:
                with open(manifest_path, "r", encoding="utf-8") as file:
                    manifest = json.load(file)
            except Exception as e:
                print(f"Failed to load manifest {manifest_path}: {e}")
                manifest = {}
      
        original_images = sorted([os.path.join(original_directory, f) for f in os.listdir(original_directory)
                            if os.path.splitext(f)[1].lower() in exts])
        updated: Dict[str, float] = {}
        for original_image_path in original_images:
            try:
                timestamp = os.path.getmtime(original_image_path)
                cached_ok = (manifest.get(original_image_path) == timestamp)
                output_path = os.path.join(cache_directory, os.path.basename(original_image_path))
                if cached_ok and os.path.exists(output_path):
                    updated[original_image_path] = timestamp
                    continue
                image = cv2.imread(original_image_path)
                if image is None:
                    print(f"Failed to read image {original_image_path}. Skipping.")
                    continue
                ih, iw = image.shape[:2]
                current_aspect_ratio = self._aspect_ratio(iw, ih)
                
                if abs(current_aspect_ratio - target_aspect_ratio) > 1e-6:
                    if not self.allow_center_crop:
                        continue
                    image = self._center_crop(image, target_aspect_ratio)
                    
                ih, iw = image.shape[:2]
                if (iw, ih) != (tw, th):
                    image = cv2.resize(image, (tw, th), interpolation=cv2.INTER_LANCZOS4)
                cv2.imwrite(output_path, image)
                updated[original_image_path] = timestamp
            except Exception as e:
                # Log the error but continue processing other images
                print(f"Error processing image {original_image_path}: {e}")
                continue
        with open(manifest_path, "w", encoding="utf-8") as file:
            json.dump(updated, file, indent=2)
            
        return cache_directory
                
    def _compute_from_folder(self, cam_key: str, pattern: str, folder: Optional[str] = None, stats_logger: Optional[StatsLogger] = None, object_points_number_for_valid_detection = 10) -> Intrinsics:
        if folder is None:
            folder = os.path.join(self.base_directory, self.CAMERA_FOLDERS[cam_key][0])
        if not os.path.exists(folder):
            raise FileNotFoundError(f"Calibration folder not found: {folder}.")

        image_paths = sorted([
            os.path.join(folder, f)
            for f in os.listdir(folder)
            if os.path.splitext(f)[1].lower() in (".png", ".jpg", ".jpeg")
        ])
        if not image_paths:
            raise FileNotFoundError(f"No calibration images in {folder}.")
        
        cols, rows = self.inner_corners
        object_points = np.zeros((rows*cols, 3), np.float32)
        object_points[:, :2] = np.mgrid[0:cols, 0:rows].T.reshape(-1, 2).astype(np.float32)
        object_points *= np.float32(self.sq_size_meters)
        
        objpoints: List[np.ndarray] = []
        image_points: List[np.ndarray] = []
        
        # Criteria for corner sub-pixel refinement
        criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 30, 1e-3)

        for img_path in image_paths:
            img = cv2.imread(img_path)
            if img is None:
                continue
            gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
            found, corners = cv2.findChessboardCorners(
                gray,
                (cols, rows),
                flags=cv2.CALIB_CB_ADAPTIVE_THRESH | cv2.CALIB_CB_NORMALIZE_IMAGE,
            )
            if not found:
                continue
            # refine (expects float32), then FLATTEN to (N, 2)
            corners = cv2.cornerSubPix(gray, corners, (11, 11), (-1, -1), criteria)
            objpoints.append(object_points.copy())
            image_points.append(corners.astype(np.float32))

        if len(objpoints) < object_points_number_for_valid_detection:
            raise RuntimeError(f"Only {len(objpoints)} valid detections found in {folder}; need >= {object_points_number_for_valid_detection}.")

        image_size = (gray.shape[1], gray.shape[0])
        
        print("obj/img counts:", len(objpoints), len(image_points))
        print("shapes/dtypes:", objpoints[0].shape, image_points[0].shape, objpoints[0].dtype, image_points[0].dtype)
        
        flags = 0
        if self.use_rational_model:
            flags |= cv2.CALIB_RATIONAL_MODEL

        rms, K, dist, rvecs, tvecs = cv2.calibrateCamera(objpoints, image_points, image_size, None, None, flags=flags)
        print(f"[calib] {cam_key} used {len(objpoints)} imgs, RMS={rms:.4f}")

        if stats_logger is not None and stats_logger.debug:
            self._log_stats(cam_key, pattern, image_paths, image_points, image_size, object_points, rvecs, tvecs, K, dist, stats_logger)
            
        return Intrinsics(
            fx=float(K[0, 0]), fy=float(K[1, 1]),
            cx=float(K[0, 2]), cy=float(K[1, 2]),
            dist=dist.ravel().astype(float).tolist(),
            width=self.width, height=self.height,
            rms=float(rms), resolution=f"{self.width}x{self.height}",
            created_at=time.time(),
        )
    
    def _log_stats(self, cam_key: str, pattern: str, image_paths, image_points, image_size, object_points, rvecs, tvecs, K, dist, stats_logger: StatsLogger):
        stats_logger.begin(cam_key, pattern)
        for i, image_path in enumerate(image_paths[:len(image_points)]):
           projection, _ = cv2.projectPoints(object_points, rvecs[i], tvecs[i], K, dist)
           projection = projection.reshape(-1, 2)
           points = image_points[i].reshape(-1,2)
           error = np.linalg.norm(points - projection, axis=1)
           rms_per_image = float(np.sqrt(np.mean(error**2))) # Positive errors
           
           image = cv2.imread(image_path)
           corners_px = points.astype(np.float32)
           self._analyze_and_log_image(
               image,
               image_path,
               cam_key,
               pattern,
               K,
               dist,
               rms_per_image,
               corners_px,
               {"rvec": rvecs[i].ravel().tolist(), "tvec": tvecs[i].ravel().tolist()}, # Checkerboard pose
               stats_logger
           )
        stats_logger.mark_for_end()
        stats_logger.end()
    
    def _scale_intrinsics(self, intr: Intrinsics, target_res: str) -> Intrinsics:
        tw, th = self._parse_resolution(target_res)
        sx = tw / intr.width
        sy = th / intr.height
        fx = intr.fx * sx
        fy = intr.fy * sy
        cx = intr.cx * sx
        cy = intr.cy * sy
        return Intrinsics(
            fx=fx, fy=fy, cx=cx, cy=cy,
            dist=list(intr.dist), width=tw, height=th,
            rms=intr.rms, model=intr.model, resolution=target_res, created_at=time.time()
        )    
    
    def _normalize_camera(self, cam: str):
        key = cam.strip().lower()
        if key not in self.CAMERA_FOLDERS:
            raise KeyError(f"Unknown camera key '{cam}'. Expected one of {list(self.CAMERA_FOLDERS)}")
        return key
    
    def _parse_pattern_name(self, pattern_name: str):
        try:
            s = pattern_name.lower().replace("-","_")
            parts = s.split("_")
            mm = next((p for p in parts if p.endswith("mm")), None)
            grid = next((p for p in parts if "x" in p), None)
            if not mm or not grid:
                return None
            sq_m = float(mm[:-2]) / 1000.0
            c, r = grid.split("x")
            return (sq_m, int(c), int(r))
        except Exception as e:
            print(f"Failed to parse pattern name '{pattern_name}': {e}")
            return None
             
    def _pattern_join(self, cam_key: str, *parts: str, cam_key_index = 0) -> str:
        base_camera_directory = os.path.join(self.base_directory, self.CAMERA_FOLDERS[cam_key][0])
        return os.path.join(base_camera_directory, *[p for p in parts if p])
    
    def _base_dir_for_resolution(self, resolution: Optional[str]) -> str:
        if not resolution:
            return self.base_directory
        try:
            _, h = self._parse_resolution(resolution)
        except Exception:
            h = self.height
        parent, last = os.path.split(self.base_directory)
        if last.endswith("p"):
            return os.path.join(parent, f"{h}p")
        return self.base_directory
    
    def _json_path(self, cam_key: str, resolution: Optional[str] = None, pattern: str = "") -> str:
        dimensions = resolution or f"{self.width}x{self.height}"
        base_dir_for_res = self._base_dir_for_resolution(dimensions)
        base_camera_directory = os.path.join(base_dir_for_res, self.CAMERA_FOLDERS[cam_key][0])
        intr_dir = os.path.join(base_camera_directory, *[p for p in (pattern, "_intrinsics") if p])
        os.makedirs(intr_dir, exist_ok=True)
        return os.path.join(intr_dir, f"intrinsics_{cam_key}_{dimensions}.json")
    
    def _save_json(self, cam_key: str, intrinsics: Intrinsics, pattern: str = ""):
        p = self._json_path(cam_key, intrinsics.resolution, pattern)
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(asdict(intrinsics), f, indent=2)
                
    def _load_json(self, cam_key: str, resolution: str = None, pattern: str = "") -> Optional[Intrinsics]:
        p = self._json_path(cam_key, resolution, pattern)
        if not os.path.exists(p):
            return None
        with open(p, "r", encoding="utf-8") as file:
            data = json.load(file)
        return Intrinsics(**data)
    
    def _load_pattern_json(self, cam_key: str, pattern: str = ""):
        config = os.path.join(self.base_directory, self.CAMERA_FOLDERS[cam_key][0], pattern, "pattern.json")
        if not os.path.isfile(config):
            return None
        try:
            with open(config, "r", encoding="utf-8") as file:
                data = json.load(file)
            sq_m = float(data.get("sq_size_mm", 0)) / 1000.0
            columns, rows = data.get("inner_corners", (0, 0))
            if sq_m > 0 and columns > 0 and rows > 0:
                return (sq_m, int(columns), int(rows))
        except Exception as e:
            print(f"Failed to load pattern config {config}: {e}")
        return None
    
    def _resolve_pattern(self, cam_key: str, pattern: str):
        if pattern:
            json_data = self._load_pattern_json(cam_key, pattern)
            if json_data: return json_data
            parsed_pattern = self._parse_pattern_name(pattern)
            if parsed_pattern: return parsed_pattern
        return (self.sq_size_meters, *self.inner_corners)
    
    @staticmethod
    def _center_crop(image: np.ndarray, target_aspect: float) -> np.ndarray:
        h, w = image.shape[:2]
        current_aspect = w / h
        if abs(current_aspect - target_aspect) < 1e-6:
            return image
        if current_aspect > target_aspect:
            new_w = int(h * target_aspect)
            x0 = (w - new_w) // 2
            return image[:, x0:x0 + new_w]
        else:
            new_h = int(w / target_aspect)
            y0 = (h - new_h) // 2
            return image[y0:y0 + new_h, :]
    
    @staticmethod
    def _aspect_ratio(width: int, height: int) -> float:
        return round(width / height, 6)

    @staticmethod
    def _parse_resolution(resolution: str):
        try:
            w, h = resolution.lower().split("x")
            return int(w), int(h)
        except Exception:
            raise ValueError(f"Invalid resolution string: {resolution}.")
        
        
    def _analyze_and_log_image(self, image, image_path:str, cam_key: str, pattern: str, K, distorsions, rms_per_image, corners_px, board_pose, statistics: StatsLogger):
        
        if statistics is None or not statistics.debug:
            return
        h, w = image.shape[:2]
        blur = compute_blur_laplacian(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY))
        coverage_percent = compute_coverage_fraction(corners_px, w,h) if corners_px is not None else 0.0
        found_ok = corners_px is not None
        rms_px = float(rms_per_image) if rms_per_image is not None else float("nan")
        tilt_deg = float("nan")
        quality = classify_quality(rms_px if not np.isnan(rms_px) else 9e9, coverage_percent, blur)
        
        row = dict(
            image_filename=os.path.basename(image_path),
            pattern=pattern,
            camera=cam_key,
            width=w, height=h,
            corners_expected=int(self.inner_corners[0] * self.inner_corners[1]),
            corners_found=int(corners_px.shape[0]) if corners_px is not None else 0,
            found_ok=bool(found_ok),
            rms_px=rms_px,
            blur_lapl_var=float(blur),
            coverage_frac=float(coverage_percent),
            tilt_deg=tilt_deg,
            quality_class=quality,
            notes=""
        )
        statistics.log_row(row)

    # NDJSON heavy payload for deferred visualization
        nd = dict(
            image_filename=row["image_filename"],
            pattern=pattern,
            camera=cam_key,
            size=[w,h],
            K=K.tolist() if K is not None else None,
            dist=distorsions.tolist() if distorsions is not None else None,
            per_image_rms=rms_px,
            detected_corners_px=corners_px.tolist() if corners_px is not None else None,
            board_pose=board_pose,  # ensure it’s JSON safe (convert np arrays)
            timestamp=datetime.fromtimestamp(os.path.getmtime(image_path)).isoformat()
        )
        statistics.append_ndjson(nd)
        
    def run_calibration_only(self, dimensions: str = "1920x1080"):
        summary = {}
        try:
            for cam_key in self.CAMERA_FOLDERS.keys():
                patterns = self.available_patterns(cam_key) or [""]
                rows = []
                for pattern in patterns:
                    intr = self.get_intrinsics(cam_key, dimensions, pattern=pattern)
                    rows.append((pattern or "<root>", intr.rms))
                summary[cam_key] = rows
        except Exception as e:
            print(f"Error: {e}")
            
        finally:
            self.print_precompute_results(summary)
        
    def undistort_frame_if_needed(frame, _use_undistorted_view, map1, map2):
        if _use_undistorted_view and map1 is not None:
            return cv2.remap(frame, map1, map2, cv2.INTER_LINEAR)
        return frame

    def undistort_points(points_xy, Km, dist_coeff, Knew):
        if Km is None:
            return points_xy
        points = np.asarray(points_xy, dtype=np.float32).reshape(-1, 1, 2)
        undistorted_points = cv2.undistortPoints(points, Km, dist_coeff, P = Knew)
        return undistorted_points.reshape(-1, 2)
            
    def print_precompute_results(self, precompute_results: dict):
        print("\n=== Calibration summary (per camera · per pattern) ===")
        for cam in sorted(precompute_results.keys()):
            print(f"\n[{cam}]")
            rows = sorted(precompute_results[cam], key=lambda x: x[0].lower())  # (pattern, rms)
            for pattern, rms in rows:
                print(f"  - {pattern:<14}  RMS={rms:.4f}")
        print("\nDone.\n")
        
if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(
        description="Offline camera calibration tool"
    )
    parser.add_argument("--device", type=str, default="i16pm",
                        help="Device alias (default: i16pm - iPhone 16 Pro Max)")
    parser.add_argument("--res", type=str, default="1920x1080",
                        help="Target resolution, e.g. 1920x1080 or 1080p")
    parser.add_argument("--pattern", type=str, default="",
                        help="Optional pattern folder (e.g., 20mm_13x9). If empty, compute all.")
    parser.add_argument("--force", action="store_true",
                        help="Force recalibration even if cache exists.")

    args = parser.parse_args()

    calib = Calibrator(
        work_resolution=args.res,
        device=args.device,
        allow_center_crop=True,
        force_recalib=args.force,
    )

    if args.pattern:
        intr = calib.get_intrinsics("main", args.res, pattern=args.pattern)
        print(f"[main] RMS={intr.rms:.4f}, K={intr.K().tolist()}, dist={intr.dist}")
        
        intr2 = calib.get_intrinsics("tp", args.res, pattern=args.pattern)
        print(f"[telephoto] RMS={intr2.rms:.4f}, K={intr2.K().tolist()}, dist={intr2.dist}")
        
        intr3 = calib.get_intrinsics("uw_wth_lens_dist", args.res, pattern=args.pattern)
        print(f"[uw_wth_lens_dist] RMS={intr3.rms:.4f}, K={intr3.K().tolist()}, dist={intr3.dist}")
    else:
        report = calib.precompute_all(args.res, force=args.force)
        print("Calibration report:")
        for cam, results in report.items():
            for pattern, rms in results:
                print(f"  {cam}/{pattern}: RMS={rms:.4f}")
