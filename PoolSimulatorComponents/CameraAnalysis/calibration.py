from __future__ import annotations
import json
import os
from dataclasses import dataclass, asdict, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple
import time

import cv2
import numpy as np

CAMERA_FOLDERS = {
    "main": ("main", "main_intrinsics"),
    "tp": ("tp","tp_intrinsics"),                      
    "uw_wth_lens_dist": ("uw_wth_lens_dist","uw_wth_lens_dist_intrinsics"),
}

MANIFEST_PREFIX = ".calibcache_"
CACHE_PREFIX = "_downscaled_"

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
    
    def __init__(self, 
                 work_resolution = "1920x1080",
                 sq_size_meters = 0.020,
                 checkerboard_inner_corners = (13 , 9), 
                 force_recalib = False,
                 fish_eye_for_uw =  False,
                 device = "i16pm",
                 use_rational_model = False,
                 base_directory = "CameraAnalysis/Images/Calibration/in_ex",
                 allow_center_crop = True):
        self.width, self.height = self._parse_resolution(work_resolution)
        self.base_directory = os.path.join(base_directory, device, f"{self.height}p")
        self.inner_corners = checkerboard_inner_corners
        self.sq_size_meters = sq_size_meters
        self.use_fisheye_uw =  fish_eye_for_uw
        self.force_recalib = force_recalib
        self.use_rational_model = use_rational_model
        self.allow_center_crop = allow_center_crop
        self._cache: Dict[str, Intrinsics] = {}
        
    def change_base_directory(self, new_base_directory: str):
        self.base_directory = new_base_directory
        
    def downscale_image(self, image_path: str, desired_resolution: str = "1920x1080"):
        image = cv2.imread(image_path)
        if image is None:
            raise FileNotFoundError(f"Image not found: {image_path}.")
        if desired_resolution is not None:
            w, h = self._parse_resolution(desired_resolution)
            if w is None or h is None or w <= 0 or h <= 0:
                 w = self.width
                 h = self.height
                 print(f"Invalid resolution {desired_resolution} for image {image_path}. Using default {self.width}x{self.height}.")
            image_resized = cv2.resize(image, (w, h), interpolation=cv2.INTER_LANCZOS4)
        else:
            print(f"No desired resolution provided for image {image_path}. Using default {self.width}x{self.height}.")
            image_resized = cv2.resize(image, (self.width, self.height), interpolation=cv2.INTER_LANCZOS4)
        
        if desired_resolution is None or self._parse_resolution(desired_resolution) != (self.width, self.height):
            desired_resolution = f"{self.width}x{self.height}"
        
        ext = os.path.splitext(image_path)[1]
        output_path = f"{os.path.splitext(image_path)[0]}_{desired_resolution}{ext}"
        cv2.imwrite(output_path, image_resized)
        
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
    
    def get_intrinsics(self, camera: str, target_resolution = "1920x1080") -> Intrinsics:
        cam_key = self._normalize_camera(camera)
        target_res = target_resolution or f"{self.width}x{self.height}"
        
        intrinsics = self._load_json(cam_key, target_res)
        if intrinsics and self.force_recalib is False:
            self._cache[cam_key] = intrinsics
            return intrinsics
        
        if target_res == f"{self.width}x{self.height}" or self.force_recalib:
            cache_directory = self._prepare_image_set(cam_key, target_res)
            intrinsics = self._compute_from_folder(cam_key, cache_directory)
            self._save_json(cam_key, intrinsics)
            self._cache[cam_key] = intrinsics
            return intrinsics
  
        base_intrinsics = self.get_intrinsics(cam_key, f"{self.width}x{self.height}")
        scaled_intrinsics = self._scale_intrinsics(base_intrinsics, target_res)
        self._save_json(cam_key, scaled_intrinsics)
        return scaled_intrinsics
    
    def undistort(self, img: np.ndarray, camera: str, target_resolution = None) -> np.ndarray:
        intrinsics = self.get_intrinsics(camera, target_resolution)
        K = intrinsics.K()
        dist = np.array(intrinsics.dist, dtype=np.float64)
        new_K, _ = cv2.getOptimalNewCameraMatrix(K,
                                                 dist,
                                                 (intrinsics.width, intrinsics.height),
                                                 1.0, 
                                                 (intrinsics.width, intrinsics.height)) 
        return cv2.undistort(img, K, dist, None, new_K)      
            
    def _prepare_image_set(self, cam_key: str, target_resolution: str):
        tw, th = self._parse_resolution(target_resolution)
        target_aspect_ratio = self._aspect_ratio(tw, th)
        original_directory = os.path.join(self.base_directory, CAMERA_FOLDERS[cam_key][0])
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
        exts = {".jpg", ".jpeg", ".png"}
        
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
                    
                iw, ih = image.shape[:2]
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
                
    def _compute_from_folder(self, cam_key: str, folder: Optional[str] = None) -> Intrinsics:
        if folder is None:
            folder = os.path.join(self.base_directory, CAMERA_FOLDERS[cam_key][0])
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
        object_points = np.zeros((rows*cols,3), np.float64)
        object_points[:, :2] = np.mgrid[0:cols, 0:rows].T.reshape(-1,2)
        object_points *= float(self.sq_size_meters)
        
        objpoints: List[np.ndarray] = []
        imgpoints: List[np.ndarray] = []
        
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

            # refine to subpixel (cornerSubPix expects float32)
            corners = cv2.cornerSubPix(gray, corners, (11, 11), (-1, -1), criteria)
            # store as float64 for calibrateCamera (it accepts both; we keep consistency)
            objpoints.append(object_points.copy())
            imgpoints.append(np.asarray(corners, dtype=np.float64))

        if len(objpoints) < 10:
            raise RuntimeError(f"Only {len(objpoints)} valid detections found in {folder}; need >= 10.")

        image_size = (self.width, self.height)
        flags = 0
        if self.use_rational_model:
            flags |= cv2.CALIB_RATIONAL_MODEL

        rms, K, dist, rvecs, tvecs = cv2.calibrateCamera(objpoints, imgpoints, image_size, None, None, flags=flags)
        print(f"[calib] {cam_key} used {len(objpoints)} imgs, RMS={rms:.4f}")

        intrinsics = Intrinsics(
            fx=float(K[0, 0]), fy=float(K[1, 1]),
            cx=float(K[0, 2]), cy=float(K[1, 2]),
            dist=dist.ravel().astype(float).tolist(),
            width=self.width, height=self.height,
            rms=float(rms), resolution=f"{self.width}x{self.height}",
            created_at=time.time(),
        )
        return intrinsics
    
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
        if key not in CAMERA_FOLDERS:
            raise KeyError(f"Unknown camera key '{cam}'. Expected one of {list(CAMERA_FOLDERS)}")
        return key
    
    def _json_path(self, cam_key: str, resolution: Optional[str] = None) -> str:
        res = resolution or f"{self.width}x{self.height}"
        folder_name = CAMERA_FOLDERS[cam_key][1]
        out_dir = os.path.join(self.base_directory, folder_name)
        os.makedirs(out_dir, exist_ok=True)
        return os.path.join(out_dir, f"intrinsics_{cam_key}_{res}.json")

    
    def _save_json(self, cam_key: str, intrinsics: Intrinsics):
        p = self._json_path(cam_key, intrinsics.resolution)
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(asdict(intrinsics), f, indent=2)
                
    def _load_json(self, cam_key: str, resolution: str = None):
        p = self._json_path(cam_key, resolution)
        if not os.path.exists(p):
            return None
        with open(p, "r", encoding="utf-8") as file:
            data = json.load(file)
        return Intrinsics(**data)
    
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