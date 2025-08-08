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
        
        
        
       
    