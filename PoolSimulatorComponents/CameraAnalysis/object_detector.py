from ultralytics import YOLO
import cv2
import numpy as np
import torch
# from sort import sort

class ObjectDetector:
    
    YOLO_MODEL_NAME = "yolov8n"
    CONFIDENCE = .25
    IOU = .45
    MAX_DET = 64
   
    def __init__(self):
        self.cuda_available, self.cuda_version, self.vram = self.get_gpu_info()
        if not self.cuda_available:
            self.device = "cpu"
        else:
            self.device = "cuda"
            
        self.yolo = None
        self._yolo_conf = float(self.CONFIDENCE)
        self._iou = float(self.IOU)
        self._max_det = int(self.MAX_DET)
            
        # self.model = yolo(weights_path)
        # self.model.to(self.device)
        self._corner_ema = None
        self._pocket_ema = None
        self._corner_alpha = .2
        self._pocket_alpha = .25
    
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
        # Always output the corners in order TL, TR, BL, BR. 
        # The remaining 2 are a the center of the longer table axis.
        
        corner_data = np.asarray(corner_data, dtype=np.float32)
        sum_of_points = corner_data.sum(axis=1)
        diff = np.diff(corner_data, axis=1).reshape(-1)
        top_left = corner_data[np.argmin(sum_of_points)]
        bottom_right = corner_data[np.argmax(sum_of_points)]
        top_right = corner_data[np.argmin(diff)]
        bottom_left = corner_data[np.argmax(diff)]
        
        return np.array([top_left, top_right, bottom_left, bottom_right], dtype=np.float32)
    
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
    
    def gate_and_smooth_corners(self, corners, expected_aspect_ratio = None):
        good = self._aspect_ok(corners, expected_aspect_ratio)
        if not good and self._corner_ema is not None:
            return self._corner_ema.copy()
        self._corner_ema = self._exp_moving_avg(self._corner_ema, corners, self._corner_alpha)
        return self._corner_ema.copy()
    
    def smooth_pockets(self, pockets_xy):
        self._pocket_ema = self._exp_moving_avg(self._pocket_ema, pockets_xy, self._pocket_alpha)
        return self._pocket_ema.copy()
    
    def _denoise_mask(self, mask, kernel_one = 3, kernel_two = 5):
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((kernel_one,kernel_one), np.uint8), iterations=1)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((kernel_two,kernel_two), np.uint8), iterations=2)

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
        TLm = np.array([0.0,         table_width_mm], np.float32)
        TRm = np.array([table_length_mm, table_width_mm], np.float32)
        BRm = np.array([table_length_mm, 0.0], np.float32)
        BLm = np.array([0.0,         0.0], np.float32)
        src = np.array([TLm, TRm, BRm, BLm], np.float32)
        dst = np.array([TL,  TR,  BL,  BR ], np.float32)
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
            return "unknown"
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        white_pixels = np.sum(gray > white_treshold)
        black_pixels = np.sum(gray < eightball_treshold)
        total_pixels = roi.shape[0] * roi.shape[1]
        white_ratio = white_pixels / total_pixels
        if black_pixels / total_pixels > 0.5:
            return "8-ball"
        if white_ratio > 0.8:
            return "cue"
        if white_ratio > stripe_white_ratio:
            return "striped"
        return "solid"
        

    def classify_balls_yolo():
        pass
        
    