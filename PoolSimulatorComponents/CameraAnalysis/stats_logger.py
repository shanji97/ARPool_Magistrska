from __future__ import annotations
import os, json
from typing import Dict, Any, List, Optional
from datetime import datetime

import pandas as pd

class StatsLogger:
    def __init__(self, base_directory: str, resolution: str,device = "i16pm", debug: bool = True, overwrite: bool = True):
        self.base_directory = base_directory
        self.resolution = resolution
        self.device = device
        self.debug = debug
        self.overwrite = overwrite
        self.rows: List[Dict[str, Any]] = []
        self.ndjson_path: Optional[str] = None
        self.parquet_path: Optional[str] = None
        self.csv_path: Optional[str] = None
        self.statistics_directory_name = "statistics"
        self._end = False
        
    def _ensure_directory(self, camera: str, pattern: str) -> str:
    
        statistics_directory = os.path.join(
            self.base_directory, self.device, self.resolution, self.statistics_directory_name, camera, (pattern or "_root")
        )
        os.makedirs(statistics_directory, exist_ok=True)
        return statistics_directory
    
    def begin(self, camera: str, pattern: str):
        if not self.debug:
            return 
        self._end = True
        statistics_directory = self._ensure_directory(camera, pattern)
        self.csv_path = os.path.join(statistics_directory, "per_image.csv")
        self.parquet_path = os.path.join(statistics_directory, "per_image.parquet")
        self.ndjson_path = os.path.join(statistics_directory, "per_image.ndjson")
        if self.overwrite:
            for path in [self.csv_path, self.parquet_path, self.ndjson_path]:
                if os.path.exists(path):
                    os.remove(path)
        self.rows = []
    
    def log_row(self, row: Dict[str, Any]):
        if not self.debug:
            return
        self.rows.append(row)
        
    def append_ndjson(self, row: Dict[str, Any]):
        if not self.debug or not self.ndjson_path:
            return
        with open(self.ndjson_path, "a", encoding="utf-8") as file:
            file.write(json.dumps(row, ensure_ascii=False))
            file.write("\n")
    
    def flush(self):
        if not self.debug or not self.rows:
            return
        df = pd.DataFrame(self.rows)
        preferred_columns = [
        "image_filename", "pattern", "camera", "width", "height",
        "corners_expected", "corners_found", "found_ok",
        "rms_px", "blur_lapl_var", "coverage_frac", "tilt_deg", "quality_class", "notes"
        ]
        
        cols = [c for c in preferred_columns if c in df.columns] + [c for c in df.columns if c not in preferred_columns]
        df = df[cols]
        if self.parquet_path:
            df.to_parquet(self.parquet_path, index=False)
            print("Flushed data to parquet file.")
        if self.csv_path:
            df.to_csv(self.csv_path, index=False) 
            print("Flushed data to csv file.")
            
    def mark_for_end(self):
        self._end = True
                
    def end(self):
        print("Wrapping things up.")
        if not self._end:
            print("Forgot set the end flag in code.")
        else:
            self._end = False
            self.flush()