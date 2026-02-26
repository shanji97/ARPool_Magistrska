from enum import Enum

class DetectionMode(Enum):
    Tresholding = 1
    YOLO = 2
    Both = 3
    