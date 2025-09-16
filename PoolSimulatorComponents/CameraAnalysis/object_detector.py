# from ultralytics import yolo
import cv2
import numpy as np
import torch
# from sort import sort

class ObjectDetector:
   
    def __init__(self):
        self.cuda_available, self.cuda_version, self.vram = self.get_gpu_info()
        if not self.cuda_available:
            self.device = "cpu"
        else:
            self.device = "cuda"
        # self.model = yolo(weights_path)
        # self.model.to(self.device)
    
    
    def get_gpu_info(self):
        try:
            cuda_version = "N/A"
            vram = 0
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
    
    def detect_table(self,
                    frame,
                    hsv_bounds):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, hsv_bounds[0], hsv_bounds[1])
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
        
    