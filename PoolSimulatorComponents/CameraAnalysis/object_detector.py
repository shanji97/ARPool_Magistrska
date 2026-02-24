from ultralytics import YOLO
import cv2
import numpy as np
import torch
from ball_type import BallType

class ObjectDetector:
    
    YOLO_MODEL_NAME = "yolov8n"
    CONFIDENCE = .25
    IOU = .45
    MAX_DET = 64
   
    def __init__(self, label_map, debug:bool = False):
        self.cuda_available, self.cuda_version, self.vram = self.get_gpu_info()
        self.device = "cuda" if self.cuda_available else "cpu"
        self.yolo = None
        self._yolo_conf = float(self.CONFIDENCE)
        self._iou = float(self.IOU)
        self._max_det = int(self.MAX_DET)
        self.label_map = label_map
        self._corner_ema = None
        self._pocket_ema = None
        self._last_stable_pockets = None
        self._pocket_stable_frames = 0
        self._corner_alpha = .2
        self._pocket_alpha = .25
        self.debug = debug
    
    def load_yolo(self):
        if YOLO is None:
            print("[YOLO] ultralytics not installed; YOLO path disabled.")
        else:
            try:
                weights = self.YOLO_MODEL_NAME
                self.yolo = YOLO(weights)
                print(f"[YOLO] Loaded '{weights}' on {self.device}")
            except Exception as e:
                print(f"[YOLO] Failed to load model: {e}")
                self._yolo = None
                
    def _ensure_yolo(self):
        if self.yolo is None:
            self.load_yolo()

    @staticmethod
    def get_gpu_info():
        cuda_version = "N/A"
        vram = 0
        try:
            cuda_available = torch.cuda.is_available()
            if cuda_available:
                device = torch.device('cuda:0')
                vram = int((torch.cuda.get_device_properties(device).total_memory) / (1024*1024))
                if hasattr(torch.version,"cuda") and torch.version.cuda:
                    cuda_version = str(torch.version.cuda)
                    
            return (cuda_available, cuda_version, vram)
        except Exception as e:
            print(f"Error fetching GPU info: {e}")
            return (False, cuda_version, vram)
    
    def _order_corners(self, corner_data):
        corner_data = np.asarray(corner_data, dtype=np.float32)
        s = corner_data.sum(axis=1)
        d = np.diff(corner_data, axis=1).reshape(-1)
        tl = corner_data[np.argmin(s)]
        br = corner_data[np.argmax(s)]
        tr = corner_data[np.argmin(d)]
        bl = corner_data[np.argmax(d)]
        return np.array([tl, tr, bl, br], dtype=np.float32)
        
    def _exp_moving_avg(self, previous, x, alpha):
        x = np.asarray(x, np.float32)
        return x if previous is None else (alpha * x + (1.0 - alpha) * previous)
    
    def _aspect_ok(self, corners, expected_aspect_ratio = None, tolerance = .4):
        if expected_aspect_ratio is None: return True
        c = self._order_corners(np.asarray(corners, np.float32))
        w = np.linalg.norm(c[1] - c[0]) + np.linalg.norm(c[3] - c[2]) # TR-TL + BR-BL 
        h = np.linalg.norm(c[2] - c[0]) + np.linalg.norm(c[3] - c[1]) # BL-TL + BR-TR
        # Opposite-edge averages → robust width/height
        top    = np.linalg.norm(c[1] - c[0])  # TR - TL
        bottom = np.linalg.norm(c[3] - c[2])  # BR - BL
        left   = np.linalg.norm(c[2] - c[0])  # BL - TL
        right  = np.linalg.norm(c[3] - c[1])  # BR - TR

        width  = 0.5 * (top + bottom)
        height = 0.5 * (left + right)
        minimum_dimnesion = 1e-6
        if height <= minimum_dimnesion or width <= minimum_dimnesion:
            return False
        aspect_ratio_obs = width / height
        return (expected_aspect_ratio * (1 - tolerance)) <= aspect_ratio_obs <= (expected_aspect_ratio * (1 + tolerance))
    
    def gate_and_smooth_corners(self, corners, expected_aspect_ratio = 2.0):
        good = self._aspect_ok(corners, expected_aspect_ratio)
        if not good and self._corner_ema is not None:
            return self._corner_ema.copy()
        self._corner_ema = self._exp_moving_avg(self._corner_ema, corners, self._corner_alpha)
        return self._corner_ema.copy()
    
    def smooth_pockets(self, pockets_xy):
        self._pocket_ema = self._exp_moving_avg(self._pocket_ema, pockets_xy, self._pocket_alpha)
        return self._pocket_ema.copy()

    def reset_pocket_tracking(self):
        self._pocket_ema = None
        self._last_stable_pockets = None
        self._pocket_stable_frames = 0

    def stabilize_pockets(self, pockets_xy, max_delta_px = 1.5, required_stable_frames = 8):
        smoothed = np.asarray(self.smooth_pockets(pockets_xy), dtype=np.float32)
        if self._last_stable_pockets is None:
            self._last_stable_pockets = smoothed.copy()
            self._pocket_stable_frames = 1
            return smoothed.copy(), False, float("inf")

        deltas = np.linalg.norm(smoothed - self._last_stable_pockets, axis=1)
        max_delta = float(np.max(deltas)) if deltas.size else 0.0

        if max_delta <= float(max_delta_px):
            self._pocket_stable_frames += 1
        else:
            self._pocket_stable_frames = 0

        self._last_stable_pockets = smoothed.copy()
        is_stable = self._pocket_stable_frames >= int(required_stable_frames)
        return smoothed.copy(), is_stable, max_delta
    
    def _denoise_mask(self, mask, kernel_one = 3, kernel_two = 5):
        kernels = [kernel_one, kernel_two]
        interations_for_kernel = [1, 2]
        operations_in_iteration = [cv2.MORPH_OPEN, cv2.MORPH_CLOSE]
        for morph in zip(operations_in_iteration, kernels, interations_for_kernel):
            mask = cv2.morphologyEx(mask, morph[0], np.ones((morph[1], morph[1]), np.uint8), iterations=morph[2])
        return cv2.GaussianBlur(mask, (kernel_two, kernel_two),0)
    
    def detect_table(self,
                    frame,
                    hsv_bounds):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = self._denoise_mask(
            cv2.inRange(hsv, hsv_bounds[0], hsv_bounds[1]),
            3, 5
            )
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        
        if not contours:
            return (None, None, None)
        
        table_contour = max(contours, key=cv2.contourArea)
        x, y, w, h = cv2.boundingRect(table_contour)
        
        countour_perimeter = cv2.arcLength(table_contour, True)
        deviation_for_episilon = 0.02 # Accurate corners, less noise than 1%
        approximation = cv2.approxPolyDP(table_contour,
                                         deviation_for_episilon * countour_perimeter,
                                         True)
        corners = None
        
        if len(approximation) >= 4:
            if len(approximation) == 4:
                corners = self._order_corners(approximation.reshape(-1, 2))
            else:
                rectangle = cv2.minAreaRect(table_contour)
                bounding_box = cv2.boxPoints(rectangle)
                corners = self._order_corners(bounding_box)
        else:
            box = np.array([[x, y], [x+w, y], [x+w, y+h], [x, y+h]], dtype=np.float32)
            corners = self._order_corners(box)       
        return ((x, y, w, h), mask, corners)
    
    @staticmethod      
    def homography_mm_to_px(corners_px, table_length_mm, table_width_mm):
        TL, TR, BL, BR = corners_px.astype(np.float32)
        TLm = np.array([0.0,              table_width_mm], np.float32)
        TRm = np.array([table_length_mm,  table_width_mm], np.float32)
        BRm = np.array([table_length_mm,  0.0],           np.float32)
        BLm = np.array([0.0,              0.0],           np.float32)
        src = np.array([TLm, TRm, BRm, BLm], np.float32)
        dst = np.array([TL,  TR,  BR,  BL], np.float32)   # <-- fixed order
        H, _ = cv2.findHomography(src, dst, method=cv2.RANSAC)
        return H

    @staticmethod
    def warp_mm_points_to_px(H, points_mm):
        out = []
        for x,y in points_mm:
            v = np.array([x,y, 1.0], np.float32)
            q = H @ v # Matrix multiplication
            out.append((float(q[0]/q[2]), float(q[1]/q[2])))
        return out
    
    @staticmethod
    def warp_px_to_m(H, points_px):
        if H is None:
            return [(None, None) for _ in points_px]
        Hinverse = np.linalg.inv(H)
        out = []
        for (xpx, ypx) in points_px:
            v = np.array([float(xpx), float(ypx), 1.0], np.float32)
            q = Hinverse @ v
            mmx, mmy = (q[0] / q[2]), (q[1] / q[2])
            out.append((mmx / 1000.0, mmy / 1000.0))
        return out
        
    def apply_smoothing(self, points_to_smooth):
        pts = np.asarray(points_to_smooth, np.float32)

    @staticmethod
    def detect_balls(frame, table_mask, min_ball_radius, max_ball_radius,gaussian_kernel = (9,9)):
        masked = cv2.bitwise_and(frame, frame, mask=table_mask)
        gray = cv2.cvtColor(masked, cv2.COLOR_BGR2GRAY)
        blurred = cv2.GaussianBlur(gray, gaussian_kernel, 2)
        circles = cv2.HoughCircles(blurred, 
                                cv2.HOUGH_GRADIENT,
                                dp = 1.2,
                                minDist=20,
                                param1= 50,
                                param2=30,
                                minRadius=min_ball_radius ,
                                maxRadius=max_ball_radius )
        if circles is not None:
            return np.uint16(np.around(circles[0]))
        return []
    
    @staticmethod
    def classify_balls(frame, circle, white_treshold, eightball_treshold, stripe_white_ratio):
        x, y, r = int(circle[0]), int(circle[1]), int(circle[2])
        roi = frame[y - r:y + r, x - r:x + r]
        if roi.size == 0:
            return  BallType.UNKNOWN.value
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        white_pixels = np.sum(gray > white_treshold)
        black_pixels = np.sum(gray < eightball_treshold)
        total_pixels = roi.shape[0] * roi.shape[1]
        white_ratio = white_pixels / total_pixels
        if black_pixels / total_pixels > 0.5:
            return BallType.EIGHT.value
        if white_ratio > 0.8:
            return BallType.CUE.value
        if white_ratio > stripe_white_ratio:
            return  BallType.STRIPE.value
        return BallType.SOLID.value
    
    @staticmethod
    def circle_to_entry(frame, circle, circle_center_m, white_treshold, eight_treshold, stripe_wb_ratio):
        t = ObjectDetector.classify_balls(frame, circle, white_treshold, eight_treshold, stripe_wb_ratio) # Only white balls with color stripe for now.
        x, y, = circle_center_m
        return {
            "type": t,
            "x": x,
            "y": y,
            "confidence" : None,
            "vx": None,
            "vy": None,
        }
        
    def detect_balls_yolo(self, frame_bgr):
        self._ensure_yolo()
        if self.yolo is None:
            return []
        results = self.yolo.predict(
            
            source = frame_bgr,
            conf = self._yolo_conf,
            iou = self._iou,
            max_det = self._max_det,
            verbose = False,
            device = self.device
        )
        
        if not results:
            return []
        out = []
        r0 = results[0]
        boxes = r0.boxes
        if boxes is None or boxes.xyxy is None:
            return out
        
        xyxy = boxes.cpu().numpy()
        cls = boxes.cls.cpu().numpy().astype(int)
        conf = boxes.conf.cpu().numpy()
        
        id_to_type = {
        0: BallType.CUE.value,
        1: BallType.EIGHT.value,
        2: BallType.SOLID.value,
        3: BallType.STRIPE.value,
        }
        
        for (x1, y1, x2, y2), c, p in zip(xyxy, cls, conf):
            if  c not in id_to_type:
                continue
            cx = 0.5 * (x1 + x2)
            cy = 0.5 * (y1 + y2)
            out.append((float(cx), float(cy), id_to_type[int(c)], float(p)))
        return out
    
    def yolo_to_entries(self, detections_px, H_inv_m_from_px):
        if not detections_px:
            return []
        pts_px = [(d[0], d[1]) for d in detections_px]
        centers_m = H_inv_m_from_px(pts_px)
        
        entries = []
        for (xm, ym), (_, _, t, conf) in zip(centers_m, detections_px):
            if xm is None or ym is None:
                continue
            entries.append({
                "type": t,            # 'c','e','st','so'
                "x": xm,
                "y": ym,
                "number": self.label_map[BallType.UNKNOWN.value][1] if t in (BallType.SOLID.value, BallType.STRIPE.value) 
                else (self.label_map[BallType.CUE.value][1] 
                      if t==BallType.CUE.value 
                      else self.label_map[BallType.EIGHT.value][1]),
                "confidence": float(conf),
                "vx": None,
                "vy": None,
            })
            
        if self.debug:
            print(entries)
        return entries
        
    
    def classify_balls_yolo():
        pass
