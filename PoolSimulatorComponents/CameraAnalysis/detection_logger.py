from __future__ import annotations
import csv
import time
from datetime import datetime
import cv2

from detection_mode import DetectionMode
from objects_in_environment import EnvironmentConfig

DETECTION_MODE = DetectionMode.Both

def prepare_log_file():
    global _detector
    if _detector is None:
        print("Ball detector not instantiated properly")
        return
    
    filename = f"debug_{datetime.now().strftime('%Y%m%d_%H%M%S')}.csv"
    file = open(filename, 'w', newline='')
    writer = csv.writer(file)
    cuda_available, cuda_version, vram  = _detector.get_gpu_info()
    
    header = [
        "timestamp", "cloth_H", "cloth_S", "cloth_V",
        "table_width_px", "table_height_px", "table_width_mm", "table_length_mm",
        "pocket1_x", "pocket1_y", "pocket2_x", "pocket2_y",
    ]

    if DETECTION_MODE in (DetectionMode.Tresholding, DetectionMode.Both):
        for i in range(1, 17):
            header.extend([f"ball{i}_x", f"ball{i}_y", f"ball{i}_type"])

    if DETECTION_MODE in (DetectionMode.YOLO, DetectionMode.Both):
        for i in range(1, 17):
            header.extend([f"yolo_ball{i}_x", f"yolo_ball{i}_y", f"yolo_ball{i}_type"])

    header.extend([
        "resolution", "performance_mode", "detection_mode",
        "cuda_available", "cuda_version", "vram_MB", "proc_time_ms"
    ])
    
    writer.writerow(header)
    return file, writer, cuda_available, cuda_version, vram

def log_csv_row(writer, 
                frame,
                table_mask,
                pockets,
                start_time,
                table_bbox,
                classical_results,
                yolo_results,
                resolution_str: str = "1920x1080",
                cuda_available = "True",
                cuda_version = "12.8",
                vram_mb_int =  0,
                enviromentInfo: EnvironmentConfig = None
                ):
    vram_mb = str(vram_mb_int)
    hsv_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    mean_h, mean_s, mean_v, _ = cv2.mean(hsv_frame, mask=table_mask)
    now = datetime.now().strftime('%Y-%m-%d %H:%M:%S.%f')[:-3]

    row = [
        now,
        int(mean_h), int(mean_s), int(mean_v)
    ]

    (length, width) = enviromentInfo.table.playfield_mm
    if table_bbox:
        _, _, w, h = table_bbox
        row += [w, h, width, length]
    else:
        row += [None, None, width, length]

    for pt in pockets:
        if pt is None or pt == (None, None):
            row += [None, None]
        else:
            row += [pt[0], pt[1]]
            
    def append_ball_results(results):
        for i in range(16):
            if i < len(results):
                x, y, label = results[i]
                row.extend([x, y, label])
            else:
                row.extend([None, None, None])

    if DETECTION_MODE in (DetectionMode.Tresholding, DetectionMode.Both):
        append_ball_results(classical_results)
    if DETECTION_MODE in (DetectionMode.YOLO, DetectionMode.Both):
        append_ball_results(yolo_results)

    elapsed_ms = round((time.perf_counter() - start_time) * 1000.0, 2)
    row += [
        resolution_str, "PERFORMANCE_MODE", DETECTION_MODE.name,
        cuda_available, cuda_version, vram_mb, elapsed_ms
    ]
    writer.writerow(row)
