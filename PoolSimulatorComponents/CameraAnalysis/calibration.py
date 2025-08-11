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
                 resolution = "1920x1080" ,
                 sq_size_meters = 0.020,
                 checkerboard_inner_corners = (13 , 9), 
                 caliscope = False,
                 force_recalib = False,
                 fish_eye_for_uw =  False,
                 device = "i16pm",
                 use_rational_model = False,
                 base_directory = "CameraAnalysis/Images/Calibration/in_ex"):
        self.width, self.height = self._parse_resolution(resolution)
        self.base_directory = os.path.join(base_directory, device, f"{self.height}p")
        self.inner_corners = checkerboard_inner_corners
        self.sq_size_meters = sq_size_meters
        self.use_fisheye_uw =  fish_eye_for_uw
        self.force_recalib = force_recalib
        self.use_rational_model = use_rational_model
        self.caliscope = caliscope
        self._cache: Dict[str, Intrinsics] = {}
    
    def get_intrinsics(self, camera, target_resolution = None):
        cam_key = self._normalize_camera(camera)
        target_res = target_resolution or f"{self.width}x{self.height}"
        
        intrinsics = self._load_json(cam_key, target_res)
        if intrinsics and self.force_recalib is False:
            self._cache[cam_key] = intrinsics
            return intrinsics
        
        if target_res == f"{self.width}x{self.height}" or self.force_recalib is True:
            intrinsics = self._compute_from_folder(cam_key)
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
            

    def _compute_from_folder(self, cam_key: str) -> Intrinsics:
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
    def _parse_resolution(resolution: str):
        try:
            w, h = resolution.lower().split("x")
            return int(w), int(h)
        except Exception:
            raise ValueError(f"Invalid resolution string: {resolution}.")