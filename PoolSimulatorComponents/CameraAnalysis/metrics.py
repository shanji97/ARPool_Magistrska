import cv2
import numpy as np
from enum import Enum


class Quality(Enum):
    Good = "GOOD"
    Average = "AVERAGE"
    Poor = "POOR"

def compute_blur_laplacian(grayscale_image: np.ndarray) -> float:
    return cv2.Laplacian(grayscale_image, cv2.CV_64F).var()

def compute_coverage_fraction(points_xy: np.ndarray, image_width: int, image_height: int) -> float:
    minimum_points = 4
    if points_xy is None or len(points_xy) < minimum_points:
        return 0.0
    points = points_xy.astype(np.int32)
    hull = cv2.convexHull(points)
    area = cv2.contourArea(hull)
    return float(area) / (image_width * image_height)

def classify_quality(rms_px: float, coverage_fraction: float, blur_variance: float, blur_threshold: float = 120) -> str:
    upper_rms_px_for_good = 0.5
    upper_coverage_for_good = 0.35
    
    upper_rms_px_for_average = 1.0
    upper_coverage_for_average = 0.2
    
    blur_variance_treshold_coefficient = 0.5
    
    if rms_px <= upper_rms_px_for_good and coverage_fraction >= upper_coverage_for_good and blur_variance >= blur_threshold:
        return Quality.Good.value
    if rms_px <= upper_rms_px_for_average and  coverage_fraction >= upper_coverage_for_average and blur_variance >= blur_threshold * blur_variance_treshold_coefficient:
        return Quality.Average.value
    return Quality.Poor.value