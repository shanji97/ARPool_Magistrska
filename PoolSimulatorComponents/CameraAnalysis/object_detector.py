from ultralytics import yolo
import cv2
import numpy as np
import torch
# from sort import sort

class ObjectDetector:
   
    def __init__(self, weights_path:str):
        self.cuda_available, self.cuda_version, self.vram = ObjectDetector.get_gpu_info()
        if not self.cuda_available:
            self.device = "cpu"
        else:
            self.device = "cuda"
        # self.model = yolo(weights_path)
        # self.model.to(self.device)
    
    @staticmethod
    def get_gpu_info():
        try:
            cuda_version = "N/A"
            cuda_available = torch.cuda.is_available()
            if cuda_available:
                device = torch.device('cuda:0')
                vram = int((torch.cuda.get_device_properties(device).total_memory) / (1024*1024))
                if hasattr(torch.version,"cuda") and torch.version.cuda:
                    cuda_version = str(torch.version.cuda)
                    
            return (cuda_available, cuda_version, vram)
        except Exception as e:
            print(f"Error fetching GPU info: {e}")
            return (False, "Error", 0)
        
    @staticmethod
    def detect_pockets(table_bbox, pocket_radius_pixels: int = 30):
        x,y,w,h = table_bbox
        top_left = (x + pocket_radius_pixels,y + pocket_radius_pixels)
        bottom_right = (x + w - pocket_radius_pixels, y + h - pocket_radius_pixels)
        return [top_left, bottom_right]
    
    @staticmethod
    def detect_table(frame, lower_hsv, upper_hsv):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = cv2.inRange(hsv, lower_hsv, upper_hsv)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None, None
        table_contour = max(contours, key=cv2.contourArea)
        x, y, w, h = cv2.boundingRect(table_contour)
        return (x, y, w, h), mask
        
    @staticmethod
    def detect_balls(frame, table_mask, min_ball_radius, max_ball_radius,gaussian_kernel = (9,9),):
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
        
    