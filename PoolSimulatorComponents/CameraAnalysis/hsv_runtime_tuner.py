from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Optional

import cv2
import numpy as np


class HsvRuntimeTuner:
    WINDOW_NAME = "debug-hsv-tuner"
    MASK_WINDOW_NAME = "debug-hsv-mask"

    def __init__(
        self,
        enabled: bool,
        initial_ranges,
        config_path: Optional[str] = None,
    ):
        self.enabled = bool(enabled)
        self.config_path = Path(config_path) if config_path else None
        self._extra_ranges = []

        normalized = self._normalize_ranges(initial_ranges)
        first_lower, first_upper = normalized[0] if normalized else ((60, 100, 60), (90, 255, 255))

        self._extra_ranges = normalized[1:] if len(normalized) > 1 else []

        self._current_lower = tuple(first_lower)
        self._current_upper = tuple(first_upper)
        self._last_print_time = 0.0

        if self.enabled:
            self._create_window(first_lower, first_upper)
            print("[hsv] Runtime HSV tuner enabled.")
            print("[hsv] Use the trackbars to change the active range.")
            print("[hsv] Press 'h' to print the current range.")
            print("[hsv] Press 's' to save/replace the first range in the selected JSON config.")
            print("[hsv] Press 'a' to append the current range to the selected JSON config.")

    @staticmethod
    def _clamp_hsv(values):
        h, s, v = values
        return (
            int(max(0, min(179, h))),
            int(max(0, min(255, s))),
            int(max(0, min(255, v))),
        )

    @classmethod
    def _normalize_ranges(cls, ranges):
        if ranges is None:
            return []

        if (
            isinstance(ranges, (list, tuple))
            and len(ranges) == 2
            and isinstance(ranges[0], (list, tuple))
            and isinstance(ranges[1], (list, tuple))
            and len(ranges[0]) == 3
            and len(ranges[1]) == 3
        ):
            return [(cls._clamp_hsv(ranges[0]), cls._clamp_hsv(ranges[1]))]

        normalized = []

        for item in ranges:
            if isinstance(item, dict):
                if not item.get("enabled", True):
                    continue
                lower = item.get("lower")
                upper = item.get("upper")
            elif isinstance(item, (list, tuple)) and len(item) == 2:
                lower, upper = item
            else:
                continue

            if lower is None or upper is None or len(lower) != 3 or len(upper) != 3:
                continue

            normalized.append((cls._clamp_hsv(lower), cls._clamp_hsv(upper)))

        return normalized

    @staticmethod
    def _noop(_):
        pass

    def _create_window(self, lower, upper):
        cv2.namedWindow(self.WINDOW_NAME, cv2.WINDOW_NORMAL)

        cv2.createTrackbar("H min", self.WINDOW_NAME, int(lower[0]), 179, self._noop)
        cv2.createTrackbar("S min", self.WINDOW_NAME, int(lower[1]), 255, self._noop)
        cv2.createTrackbar("V min", self.WINDOW_NAME, int(lower[2]), 255, self._noop)

        cv2.createTrackbar("H max", self.WINDOW_NAME, int(upper[0]), 179, self._noop)
        cv2.createTrackbar("S max", self.WINDOW_NAME, int(upper[1]), 255, self._noop)
        cv2.createTrackbar("V max", self.WINDOW_NAME, int(upper[2]), 255, self._noop)

        cv2.createTrackbar("Show mask", self.WINDOW_NAME, 1, 1, self._noop)

    def _read_trackbars(self):
        lower = (
            cv2.getTrackbarPos("H min", self.WINDOW_NAME),
            cv2.getTrackbarPos("S min", self.WINDOW_NAME),
            cv2.getTrackbarPos("V min", self.WINDOW_NAME),
        )

        upper = (
            cv2.getTrackbarPos("H max", self.WINDOW_NAME),
            cv2.getTrackbarPos("S max", self.WINDOW_NAME),
            cv2.getTrackbarPos("V max", self.WINDOW_NAME),
        )

        self._current_lower = self._clamp_hsv(lower)
        self._current_upper = self._clamp_hsv(upper)

    @staticmethod
    def _hsv_range_mask(hsv_frame, lower, upper):
        lower_array = np.array(lower, dtype=np.uint8)
        upper_array = np.array(upper, dtype=np.uint8)

        if int(lower_array[0]) <= int(upper_array[0]):
            return cv2.inRange(hsv_frame, lower_array, upper_array)

        high_mask = cv2.inRange(
            hsv_frame,
            np.array([lower_array[0], lower_array[1], lower_array[2]], dtype=np.uint8),
            np.array([179, upper_array[1], upper_array[2]], dtype=np.uint8),
        )

        low_mask = cv2.inRange(
            hsv_frame,
            np.array([0, lower_array[1], lower_array[2]], dtype=np.uint8),
            np.array([upper_array[0], upper_array[1], upper_array[2]], dtype=np.uint8),
        )

        return cv2.bitwise_or(high_mask, low_mask)

    def update_and_get_ranges(self, frame_bgr=None):
        if not self.enabled:
            return [(self._current_lower, self._current_upper)] + list(self._extra_ranges)

        self._read_trackbars()

        ranges = [(self._current_lower, self._current_upper)] + list(self._extra_ranges)

        show_mask = cv2.getTrackbarPos("Show mask", self.WINDOW_NAME) == 1
        if show_mask and frame_bgr is not None and getattr(frame_bgr, "size", 0) > 0:
            hsv = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2HSV)
            combined = np.zeros(hsv.shape[:2], dtype=np.uint8)

            for lower, upper in ranges:
                combined = cv2.bitwise_or(combined, self._hsv_range_mask(hsv, lower, upper))

            cv2.imshow(self.MASK_WINDOW_NAME, combined)

        return ranges

    def current_range_dict(self, name: Optional[str] = None):
        return {
            "name": name or f"runtime_hsv_{int(time.time())}",
            "lower": list(self._current_lower),
            "upper": list(self._current_upper),
            "enabled": True,
        }

    def print_current_range(self):
        print(json.dumps(self.current_range_dict("runtime_preview"), indent=2))

    def save_current_range_to_config(self, append: bool = False):
        if self.config_path is None:
            print("[hsv] No config path was provided. Nothing was saved.")
            return

        if not self.config_path.exists():
            print(f"[hsv] Config path does not exist: {self.config_path}")
            return

        with self.config_path.open("r", encoding="utf-8") as file:
            data = json.load(file)

        table = data.setdefault("table", {})
        current = self.current_range_dict()

        existing_ranges = table.get("cloth_hsv_ranges", [])
        if not isinstance(existing_ranges, list):
            existing_ranges = []

        if append:
            existing_ranges.append(current)
            self._extra_ranges.append((tuple(current["lower"]), tuple(current["upper"])))
        else:
            existing_ranges = [current] + existing_ranges[1:]

        table["cloth_hsv_ranges"] = existing_ranges
        table["cloth_lower_hsv"] = current["lower"]
        table["cloth_upper_hsv"] = current["upper"]
        table["cloth_profile"] = "Runtime tuned HSV"

        data["_schema_version"] = max(int(data.get("_schema_version", 1)), 3)

        with self.config_path.open("w", encoding="utf-8") as file:
            json.dump(data, file, indent=2)

        action = "appended to" if append else "saved to"
        print(f"[hsv] Current HSV range {action}: {self.config_path}")

    def handle_key(self, key: int):
        if not self.enabled:
            return

        if key == ord("h"):
            now = time.time()
            if now - self._last_print_time > 0.25:
                self._last_print_time = now
                self.print_current_range()

        if key == ord("s"):
            self.save_current_range_to_config(append=False)

        if key == ord("a"):
            self.save_current_range_to_config(append=True)