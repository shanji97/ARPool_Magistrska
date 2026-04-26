import cv2
import numpy as np
import torch
import os
import sys
from ball_type import BallType


class ObjectDetector:
    YOLO_MODEL_NAME = "yolov5"
    CONFIDENCE = 0.25
    IOU = 0.45
    MAX_DET = 64

    POCKET_KEY_ORDER = ("TL", "TR", "BM", "TM", "BL", "BR")

    def __init__(self, label_map, debug: bool = False):
        self.cuda_available, self.cuda_version, self.vram = self.get_gpu_info()
        self.device = "cuda:0" if self.cuda_available else "cpu"
        self.yolo = None
        self._yolo_conf = float(self.CONFIDENCE)
        self._iou = float(self.IOU)
        self._max_det = int(self.MAX_DET)
        self.label_map = label_map
        self._yolov5_model = None
        self._corner_ema = None
        self._pocket_ema = None
        self._last_stable_pockets = None
        self._pocket_stable_frames = 0
        self._corner_alpha = 0.2
        self._pocket_alpha = 0.25

        # Separate temporal state for the video pocket pipeline.
        self._video_pocket_plane_ema = None
        self._video_pocket_alpha = 0.22

        self.debug = debug
        self.local_repo = ""
        self.load_yolo()

    def dispose(self):
        try:
            if getattr(self, "_local_repo_added_to_syspath", False) and self.local_repo in sys.path:
                sys.path.remove(self.local_repo)
        except Exception:
            pass

    def load_yolo(self):
        self._get_yolov5_model()

    def _ensure_yolo(self):
        if getattr(self, "_yolov5_model", None) is None:
            self.load_yolo()

    def _get_yolov5_model(self):
        if getattr(self, "_yolov5_model", None) is not None:
            return self._yolov5_model

        weights_path = os.path.abspath(os.path.join(".", "pix2pockets", "detection_model_weight", "detection_model.pt"))

        if not os.path.isfile(weights_path):
            raise FileNotFoundError(f"YOLOv5 weights not found: {weights_path}")

        local_repo = os.path.abspath(os.path.join(".", "pix2pockets", "yolov5"))
        if not os.path.isdir(local_repo):
            raise FileNotFoundError(
                f"Local YOLOv5 repo not found at: {local_repo}\n"
                f"Clone it once (offline safe afterwards):\n"
                f"  git clone https://github.com/ultralytics/yolov5.git pix2pockets/yolov5"
            )

        if local_repo not in sys.path:
            self.local_repo = local_repo
            sys.path.insert(0, local_repo)
            self._local_repo_added_to_syspath = True
        else:
            self.local_repo = local_repo
            self._local_repo_added_to_syspath = False

        try:
            model = torch.hub.load(local_repo, "custom", path=weights_path, source="local")
        except Exception as e:
            raise RuntimeError(
                f"[YOLOv5] torch.hub.load failed.\n"
                f"repo: {local_repo}\n"
                f"weights: {weights_path}\n"
                f"error: {e}"
            )

        model.to(self.device)
        model.eval()
        model.conf = float(self._yolo_conf)
        model.iou = float(self._iou)
        model.max_det = int(self._max_det)

        try:
            if self.device.startswith("cuda"):
                model.half()
        except Exception:
            pass

        self._yolov5_model = model
        return self._yolov5_model

    def detect_balls_yolov5(self, frame_bgr, img_size: int = 640):
        """
        Runs Pix2Pockets YOLOv5 and returns raw detections with boxes.

        Returns:
            [
                {
                    "x1": float,
                    "y1": float,
                    "x2": float,
                    "y2": float,
                    "cx": float,
                    "cy": float,
                    "cls": int,
                    "confidence": float,
                }
            ]
        """
        if frame_bgr is None:
            return []

        self._ensure_yolo()
        model = getattr(self, "_yolov5_model", None)
        if model is None:
            return []

        frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)

        try:
            with torch.no_grad():
                results = model(frame_rgb, size=int(img_size))
        except Exception as e:
            print("[yolov5] inference failed:", e)
            return []

        preds = getattr(results, "xyxy", [None])[0]
        if preds is None or len(preds) == 0:
            return []

        preds = preds.detach().float().cpu().numpy()
        out = []

        for x1, y1, x2, y2, conf, cls in preds:
            cx = float((x1 + x2) * 0.5)
            cy = float((y1 + y2) * 0.5)
            out.append(
                {
                    "x1": float(x1),
                    "y1": float(y1),
                    "x2": float(x2),
                    "y2": float(y2),
                    "cx": cx,
                    "cy": cy,
                    "cls": int(cls),
                    "confidence": float(conf),
                }
            )

        return out

    def classify_balls_pix2pockets(self, frame):
        dets = self.detect_balls_yolov5(frame_bgr=frame, img_size=640)
        return [(d["cx"], d["cy"], d["cls"], d["confidence"]) for d in dets]

    def get_gpu_info(self):
        cuda_version = "N/A"
        vram = 0
        try:
            cuda_available = torch.cuda.is_available()
            if cuda_available:
                device = torch.device("cuda:0")
                vram = int(torch.cuda.get_device_properties(device).total_memory / (1024 * 1024))
                if hasattr(torch.version, "cuda") and torch.version.cuda:
                    cuda_version = str(torch.version.cuda)

            return cuda_available, cuda_version, vram
        except Exception as e:
            print(f"Error fetching GPU info: {e}")
            return False, cuda_version, vram

    @staticmethod
    def _normalize_vector_2d(vx, vy):
        length = float(np.hypot(vx, vy))
        if length <= 1e-6:
            return 0.0, 0.0
        return float(vx / length), float(vy / length)

    @staticmethod
    def _axis_angle_deg(vx, vy):
        angle = float(np.degrees(np.arctan2(vy, vx)))
        while angle < 0.0:
            angle += 180.0
        while angle > 180.0:
            angle -= 180.0
        return angle

    @staticmethod
    def _axis_angle_delta_deg(a_deg, b_deg):
        delta = abs(float(a_deg) - float(b_deg)) % 180.0
        return min(delta, 180.0 - delta)

    @staticmethod
    def _distance_point_to_segment(px, py, x1, y1, x2, y2):
        vx = float(x2 - x1)
        vy = float(y2 - y1)
        denom = vx * vx + vy * vy
        if denom <= 1e-6:
            return float(np.hypot(px - x1, py - y1))

        t = ((float(px) - float(x1)) * vx + (float(py) - float(y1)) * vy) / denom
        t = max(0.0, min(1.0, t))
        qx = float(x1) + t * vx
        qy = float(y1) + t * vy
        return float(np.hypot(float(px) - qx, float(py) - qy))

    @staticmethod
    def _distance_point_to_infinite_line(px, py, x1, y1, x2, y2):
        vx = float(x2 - x1)
        vy = float(y2 - y1)
        denom = float(np.hypot(vx, vy))
        if denom <= 1e-6:
            return float("inf")
        return float(abs((float(py) - float(y1)) * vx - (float(px) - float(x1)) * vy) / denom)

    @staticmethod
    def _line_circle_intersections(line_point_px, line_dir_px, circle_center_px, circle_radius_px):
        px, py = map(float, line_point_px)
        dx, dy = ObjectDetector._normalize_vector_2d(*line_dir_px)
        if abs(dx) <= 1e-6 and abs(dy) <= 1e-6:
            return []

        cx, cy = map(float, circle_center_px)
        r = float(circle_radius_px)

        ox = px - cx
        oy = py - cy

        b = 2.0 * (dx * ox + dy * oy)
        c = ox * ox + oy * oy - r * r
        disc = b * b - 4.0 * c

        if disc < 0.0:
            return []

        root = float(np.sqrt(max(0.0, disc)))
        t1 = (-b - root) * 0.5
        t2 = (-b + root) * 0.5

        return [
            (px + t1 * dx, py + t1 * dy),
            (px + t2 * dx, py + t2 * dy),
        ]

    def detect_cue_stick(
        self,
        frame_bgr,
        cue_ball_px,
        cue_ball_radius_px,
        table_polygon_px=None,
        roi_radius_scale=14.0,
        min_line_length_px=None,
        max_center_line_distance_scale=1.75,
        line_circle_gate_scale=1.35,
        canny1=35,
        canny2=120,
        hough_threshold=18,
        max_line_gap=32,
        angle_tolerance_deg=9.0,
        debug=False,
    ):
        """
        Detect a cue line near the cue ball from a single overhead frame.
        """
        if frame_bgr is None or cue_ball_px is None:
            return None

        cx, cy = map(float, cue_ball_px)
        ball_radius_px = max(float(cue_ball_radius_px), 4.0)

        h, w = frame_bgr.shape[:2]
        roi_radius_px = int(max(ball_radius_px * float(roi_radius_scale), ball_radius_px * 7.0))
        min_line_length_px = int(
            max(12.0, ball_radius_px * 1.8) if min_line_length_px is None else max(8.0, float(min_line_length_px))
        )

        x0 = max(0, int(round(cx - roi_radius_px)))
        y0 = max(0, int(round(cy - roi_radius_px)))
        x1 = min(w, int(round(cx + roi_radius_px)))
        y1 = min(h, int(round(cy + roi_radius_px)))
        if x1 <= x0 or y1 <= y0:
            return None

        gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (5, 5), 0)

        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        gray_eq = clahe.apply(gray)

        edges_a = cv2.Canny(gray, int(canny1), int(canny2))
        edges_b = cv2.Canny(gray_eq, int(max(10, canny1 * 0.7)), int(max(20, canny2 * 0.8)))
        edges = cv2.bitwise_or(edges_a, edges_b)
        edges = cv2.dilate(edges, np.ones((3, 3), np.uint8), iterations=1)

        cue_mask = np.zeros((h, w), dtype=np.uint8)
        cv2.circle(cue_mask, (int(round(cx)), int(round(cy))), int(roi_radius_px), 255, -1)
        cv2.circle(cue_mask, (int(round(cx)), int(round(cy))), int(round(ball_radius_px * 1.05)), 0, -1)

        if table_polygon_px is not None:
            table_mask = np.zeros((h, w), dtype=np.uint8)
            polygon = np.asarray(table_polygon_px, dtype=np.int32).reshape(-1, 1, 2)
            cv2.fillPoly(table_mask, [polygon], 255)
            cue_mask = cv2.bitwise_and(cue_mask, table_mask)

        masked_edges = cv2.bitwise_and(edges, cue_mask)
        roi_edges = masked_edges[y0:y1, x0:x1]
        if roi_edges.size == 0:
            if debug:
                print("[cue] Empty ROI edge image.")
            return None

        lines = cv2.HoughLinesP(
            roi_edges,
            rho=1,
            theta=np.pi / 180.0,
            threshold=int(hough_threshold),
            minLineLength=int(min_line_length_px),
            maxLineGap=int(max_line_gap),
        )

        if lines is None or len(lines) == 0:
            if debug:
                print(
                    f"[cue] No Hough lines. "
                    f"r={ball_radius_px:.1f}px roi={roi_radius_px}px minLen={min_line_length_px}px "
                    f"canny=({canny1},{canny2}) hough={hough_threshold} gap={max_line_gap}"
                )
            return None

        rejected_too_short = 0
        rejected_too_far_from_axis = 0
        rejected_no_hit = 0
        candidates = []

        for raw in lines.reshape(-1, 4):
            lx1, ly1, lx2, ly2 = raw
            lx1 += x0
            lx2 += x0
            ly1 += y0
            ly2 += y0

            length = float(np.hypot(lx2 - lx1, ly2 - ly1))
            if length < float(min_line_length_px):
                rejected_too_short += 1
                continue

            center_line_distance = self._distance_point_to_infinite_line(cx, cy, lx1, ly1, lx2, ly2)
            if center_line_distance > float(ball_radius_px * float(max_center_line_distance_scale)):
                rejected_too_far_from_axis += 1
                continue

            d1 = float(np.hypot(lx1 - cx, ly1 - cy))
            d2 = float(np.hypot(lx2 - cx, ly2 - cy))
            far_pt = (float(lx1), float(ly1)) if d1 >= d2 else (float(lx2), float(ly2))

            seg_dir_x = float(lx2 - lx1)
            seg_dir_y = float(ly2 - ly1)
            seg_dir_x, seg_dir_y = self._normalize_vector_2d(seg_dir_x, seg_dir_y)
            if abs(seg_dir_x) <= 1e-6 and abs(seg_dir_y) <= 1e-6:
                continue

            to_center_x = float(cx - far_pt[0])
            to_center_y = float(cy - far_pt[1])

            if (seg_dir_x * to_center_x + seg_dir_y * to_center_y) < 0.0:
                seg_dir_x = -seg_dir_x
                seg_dir_y = -seg_dir_y

            intersections = self._line_circle_intersections(
                line_point_px=far_pt,
                line_dir_px=(seg_dir_x, seg_dir_y),
                circle_center_px=(cx, cy),
                circle_radius_px=float(ball_radius_px * float(line_circle_gate_scale)),
            )

            if not intersections:
                rejected_no_hit += 1
                continue

            forward_hits = []
            for px_hit, py_hit in intersections:
                proj = ((float(px_hit) - far_pt[0]) * seg_dir_x) + ((float(py_hit) - far_pt[1]) * seg_dir_y)
                if proj >= 0.0:
                    forward_hits.append((float(px_hit), float(py_hit), float(proj)))

            if not forward_hits:
                rejected_no_hit += 1
                continue

            hit_point = min(forward_hits, key=lambda item: item[2])
            axis_angle = self._axis_angle_deg(seg_dir_x, seg_dir_y)
            score = float(length) - (12.0 * center_line_distance)

            candidates.append(
                {
                    "far_pt": (float(far_pt[0]), float(far_pt[1])),
                    "hit_point": (float(hit_point[0]), float(hit_point[1])),
                    "dir": (float(seg_dir_x), float(seg_dir_y)),
                    "angle": float(axis_angle),
                    "length": float(length),
                    "line_dist": float(center_line_distance),
                    "score": float(score),
                }
            )

        if not candidates:
            if debug:
                print(
                    f"[cue] All cue candidates rejected. "
                    f"rawLines={len(lines.reshape(-1, 4))} "
                    f"tooShort={rejected_too_short} "
                    f"tooFarAxis={rejected_too_far_from_axis} "
                    f"noHit={rejected_no_hit}"
                )
            return None

        dominant = max(candidates, key=lambda item: item["score"])
        dominant_angle = float(dominant["angle"])

        filtered = [
            item for item in candidates if self._axis_angle_delta_deg(item["angle"], dominant_angle) <= float(angle_tolerance_deg)
        ]

        if not filtered:
            if debug:
                print(f"[cue] No angle-consistent candidates after clustering. candidates={len(candidates)}")
            return None

        weight_sum = float(sum(max(1.0, item["score"]) for item in filtered))
        if weight_sum <= 1e-6:
            return None

        avg_far_x = sum(item["far_pt"][0] * max(1.0, item["score"]) for item in filtered) / weight_sum
        avg_far_y = sum(item["far_pt"][1] * max(1.0, item["score"]) for item in filtered) / weight_sum
        avg_hit_x = sum(item["hit_point"][0] * max(1.0, item["score"]) for item in filtered) / weight_sum
        avg_hit_y = sum(item["hit_point"][1] * max(1.0, item["score"]) for item in filtered) / weight_sum
        avg_dir_x = sum(item["dir"][0] * max(1.0, item["score"]) for item in filtered)
        avg_dir_y = sum(item["dir"][1] * max(1.0, item["score"]) for item in filtered)
        avg_dir_x, avg_dir_y = self._normalize_vector_2d(avg_dir_x, avg_dir_y)

        if abs(avg_dir_x) <= 1e-6 and abs(avg_dir_y) <= 1e-6:
            return None

        probe_distance_px = float(max(ball_radius_px * 3.0, 20.0))
        direction_probe_px = (
            float(avg_far_x + avg_dir_x * probe_distance_px),
            float(avg_far_y + avg_dir_y * probe_distance_px),
        )

        avg_length = float(sum(item["length"] for item in filtered) / len(filtered))
        avg_line_dist = float(sum(item["line_dist"] for item in filtered) / len(filtered))

        confidence = 0.25
        confidence += min(0.25, 0.08 * float(len(filtered)))
        confidence += min(0.30, avg_length / max(1.0, float(roi_radius_px)))
        confidence += min(0.20, 1.0 - (avg_line_dist / max(1.0, float(ball_radius_px * max_center_line_distance_scale))))
        confidence = float(max(0.0, min(1.0, confidence)))

        if debug:
            print(
                f"[cue] detected candidates={len(candidates)} filtered={len(filtered)} "
                f"avgLen={avg_length:.1f}px avgLineDist={avg_line_dist:.2f}px conf={confidence:.2f}"
            )

        return {
            "line_point_px": (float(avg_far_x), float(avg_far_y)),
            "direction_px": (float(avg_dir_x), float(avg_dir_y)),
            "direction_probe_px": (float(direction_probe_px[0]), float(direction_probe_px[1])),
            "hit_point_px": (float(avg_hit_x), float(avg_hit_y)),
            "confidence": confidence,
            "candidate_count": int(len(filtered)),
            "debug_mask": cue_mask if debug else None,
            "debug_edges": masked_edges if debug else None,
        }

    # -----------------------------
    # Corner ordering + smoothing
    # -----------------------------
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

    def _aspect_ok(self, corners, expected_aspect_ratio=None, tolerance=0.4):
        if expected_aspect_ratio is None:
            return True

        c = self._order_corners(np.asarray(corners, np.float32))
        top = np.linalg.norm(c[1] - c[0])
        bottom = np.linalg.norm(c[3] - c[2])
        left = np.linalg.norm(c[2] - c[0])
        right = np.linalg.norm(c[3] - c[1])

        width = 0.5 * (top + bottom)
        height = 0.5 * (left + right)
        minimum_dimension = 1e-6
        if height <= minimum_dimension or width <= minimum_dimension:
            return False

        aspect_ratio_obs = width / height
        return (expected_aspect_ratio * (1 - tolerance)) <= aspect_ratio_obs <= (expected_aspect_ratio * (1 + tolerance))

    def gate_and_smooth_corners(self, corners, expected_aspect_ratio=2.0):
        good = self._aspect_ok(corners, expected_aspect_ratio)
        if not good and self._corner_ema is not None:
            return self._corner_ema.copy()
        self._corner_ema = self._exp_moving_avg(self._corner_ema, corners, self._corner_alpha)
        return self._corner_ema.copy()

    # -----------------------------
    # Pocket smoothing + stability
    # -----------------------------
    def smooth_pockets(self, pockets_xy):
        self._pocket_ema = self._exp_moving_avg(self._pocket_ema, pockets_xy, self._pocket_alpha)
        return self._pocket_ema.copy()

    def reset_pocket_tracking(self):
        self._pocket_ema = None
        self._last_stable_pockets = None
        self._pocket_stable_frames = 0

        # Separate reset for the video-only pocket pipeline.
        self._video_pocket_plane_ema = None

    def stabilize_pockets(self, pockets_xy, max_delta_px=1.5, required_stable_frames=8):
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

    # -----------------------------
    # Table detection (cloth mask)
    # -----------------------------
    def _denoise_mask(self, mask, kernel_one=3, kernel_two=5):
        kernels = [kernel_one, kernel_two]
        iterations_for_kernel = [1, 2]
        operations_in_iteration = [cv2.MORPH_OPEN, cv2.MORPH_CLOSE]
        for morph in zip(operations_in_iteration, kernels, iterations_for_kernel):
            mask = cv2.morphologyEx(mask, morph[0], np.ones((morph[1], morph[1]), np.uint8), iterations=morph[2])
        return cv2.GaussianBlur(mask, (kernel_two, kernel_two), 0)

    def detect_table(self, frame, hsv_bounds):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = self._denoise_mask(cv2.inRange(hsv, hsv_bounds[0], hsv_bounds[1]), 3, 5)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        if not contours:
            return None, None, None

        table_contour = max(contours, key=cv2.contourArea)
        x, y, w, h = cv2.boundingRect(table_contour)

        contour_perimeter = cv2.arcLength(table_contour, True)
        epsilon_ratio = 0.02
        approximation = cv2.approxPolyDP(table_contour, epsilon_ratio * contour_perimeter, True)
        corners = None

        if len(approximation) >= 4:
            if len(approximation) == 4:
                corners = self._order_corners(approximation.reshape(-1, 2))
            else:
                rectangle = cv2.minAreaRect(table_contour)
                bounding_box = cv2.boxPoints(rectangle)
                corners = self._order_corners(bounding_box)
        else:
            box = np.array([[x, y], [x + w, y], [x + w, y + h], [x, y + h]], dtype=np.float32)
            corners = self._order_corners(box)

        return (x, y, w, h), mask, corners

    # -----------------------------
    # Markerless inner-cushion rectangle detection
    # -----------------------------
    @staticmethod
    def _line_angle_deg(x1, y1, x2, y2):
        return float(np.degrees(np.arctan2((y2 - y1), (x2 - x1))))

    @staticmethod
    def _intersect_lines(lineA, lineB):
        x1, y1, x2, y2 = lineA
        x3, y3, x4, y4 = lineB

        denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
        if abs(denom) < 1e-6:
            return None

        px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denom
        py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denom
        return np.array([px, py], dtype=np.float32)

    def detect_inner_cushion_corners(
        self,
        frame_bgr,
        approx_table_corners_px,
        roi_expand=0.06,
        canny1=60,
        canny2=160,
        hough_thresh=120,
        min_line_len_frac=0.35,
        max_line_gap=30,
        debug=False,
    ):
        """
        Returns ordered TL, TR, BL, BR for inner cushion rectangle.
        """
        if approx_table_corners_px is None:
            return None, None

        pts = np.asarray(approx_table_corners_px, dtype=np.float32)
        xs = pts[:, 0]
        ys = pts[:, 1]
        x0, y0, x1, y1 = int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())

        w = frame_bgr.shape[1]
        h = frame_bgr.shape[0]
        pad_x = int((x1 - x0) * float(roi_expand))
        pad_y = int((y1 - y0) * float(roi_expand))

        rx0 = max(0, x0 - pad_x)
        ry0 = max(0, y0 - pad_y)
        rx1 = min(w - 1, x1 + pad_x)
        ry1 = min(h - 1, y1 + pad_y)

        roi = frame_bgr[ry0:ry1, rx0:rx1]
        if roi.size == 0:
            return approx_table_corners_px, None

        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (5, 5), 0)
        edges = cv2.Canny(gray, canny1, canny2)

        min_len = int(max(20, (rx1 - rx0) * float(min_line_len_frac)))
        lines = cv2.HoughLinesP(
            edges,
            rho=1,
            theta=np.pi / 180.0,
            threshold=int(hough_thresh),
            minLineLength=min_len,
            maxLineGap=int(max_line_gap),
        )

        if lines is None or len(lines) < 4:
            return approx_table_corners_px, edges

        candidates = []
        for line in lines.reshape(-1, 4):
            x1l, y1l, x2l, y2l = line
            x1l += rx0
            x2l += rx0
            y1l += ry0
            y2l += ry0
            ang = ObjectDetector._line_angle_deg(x1l, y1l, x2l, y2l)
            length = float(np.hypot((x2l - x1l), (y2l - y1l)))
            candidates.append((x1l, y1l, x2l, y2l, ang, length))

        horizontals = []
        verticals = []
        for x1l, y1l, x2l, y2l, ang, length in candidates:
            a = abs(ang)
            if a > 90:
                a = 180 - a
            if a <= 20:
                horizontals.append((x1l, y1l, x2l, y2l, length))
            elif a >= 70:
                verticals.append((x1l, y1l, x2l, y2l, length))

        if len(horizontals) < 2 or len(verticals) < 2:
            return approx_table_corners_px, edges

        horizontals_sorted = sorted(horizontals, key=lambda t: 0.5 * (t[1] + t[3]))
        verticals_sorted = sorted(verticals, key=lambda t: 0.5 * (t[0] + t[2]))

        k = min(6, len(horizontals_sorted))
        top_k = horizontals_sorted[:k]
        bot_k = horizontals_sorted[-k:]
        top_line = max(top_k, key=lambda t: t[4])
        bot_line = max(bot_k, key=lambda t: t[4])

        k = min(6, len(verticals_sorted))
        left_k = verticals_sorted[:k]
        right_k = verticals_sorted[-k:]
        left_line = max(left_k, key=lambda t: t[4])
        right_line = max(right_k, key=lambda t: t[4])

        tl = ObjectDetector._intersect_lines(top_line[:4], left_line[:4])
        tr = ObjectDetector._intersect_lines(top_line[:4], right_line[:4])
        bl = ObjectDetector._intersect_lines(bot_line[:4], left_line[:4])
        br = ObjectDetector._intersect_lines(bot_line[:4], right_line[:4])

        if tl is None or tr is None or bl is None or br is None:
            return approx_table_corners_px, edges

        inner = np.array([tl, tr, bl, br], dtype=np.float32)
        inner_ordered = self._order_corners(inner)
        return inner_ordered, edges

    # -----------------------------
    # Homography helpers
    # -----------------------------
    @staticmethod
    def homography_mm_to_px(corners_px, table_length_mm, table_width_mm):
        tl, tr, bl, br = corners_px.astype(np.float32)
        tlm = np.array([0.0, table_width_mm], np.float32)
        trm = np.array([table_length_mm, table_width_mm], np.float32)
        brm = np.array([table_length_mm, 0.0], np.float32)
        blm = np.array([0.0, 0.0], np.float32)
        src = np.array([tlm, trm, brm, blm], np.float32)
        dst = np.array([tl, tr, br, bl], np.float32)
        H, _ = cv2.findHomography(src, dst, method=cv2.RANSAC)
        return H

    @staticmethod
    def homography_px_to_plane(corners_px, plane_length, plane_width):
        """
        Homography from image px -> canonical plane.
        corners_px expected ordered TL, TR, BL, BR.
        """
        tl, tr, bl, br = corners_px.astype(np.float32)
        dst = np.array(
            [
                [0.0, plane_width],
                [plane_length, plane_width],
                [0.0, 0.0],
                [plane_length, 0.0],
            ],
            dtype=np.float32,
        )
        src = np.array([tl, tr, bl, br], dtype=np.float32)
        H, _ = cv2.findHomography(src, dst, method=cv2.RANSAC)
        return H

    @staticmethod
    def warp_mm_points_to_px(H, points_mm):
        out = []
        for x, y in points_mm:
            v = np.array([x, y, 1.0], np.float32)
            q = H @ v
            out.append((float(q[0] / q[2]), float(q[1] / q[2])))
        return out

    @staticmethod
    def warp_px_to_m(H, points_px):
        if H is None:
            return [(None, None) for _ in points_px]

        H_inverse = np.linalg.inv(H)
        out = []
        for xpx, ypx in points_px:
            v = np.array([float(xpx), float(ypx), 1.0], np.float32)
            q = H_inverse @ v
            mmx, mmy = q[0] / q[2], q[1] / q[2]
            out.append((mmx / 1000.0, mmy / 1000.0))
        return out

    @staticmethod
    def warp_m_to_px(H, points_m):
        if H is None:
            return [(None, None) for _ in points_m]

        out = []
        for xm, ym in points_m:
            if xm is None or ym is None:
                out.append((None, None))
                continue

            mmx = float(xm) * 1000.0
            mmy = float(ym) * 1000.0
            v = np.array([mmx, mmy, 1.0], np.float32)
            q = H @ v
            out.append((float(q[0] / q[2]), float(q[1] / q[2])))
        return out

    # -----------------------------
    # Markerless pocket detection in rectified plane (existing static pipeline)
    # -----------------------------
    @staticmethod
    def _find_dark_centroid(mask, min_area=150):
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None

        c = max(contours, key=cv2.contourArea)
        area = float(cv2.contourArea(c))
        if area < float(min_area):
            return None

        M = cv2.moments(c)
        if abs(M["m00"]) < 1e-6:
            return None

        cx = float(M["m10"] / M["m00"])
        cy = float(M["m01"] / M["m00"])
        return cx, cy

    def detect_pockets_markerless(
        self,
        frame_bgr,
        corners_px_inner,
        playfield_L_mm,
        playfield_W_mm,
        v_thresh=70,
        sat_max=180,
        roi_frac_corner=0.18,
        roi_frac_side_w=0.22,
        roi_frac_side_h=0.16,
        min_area_px=180,
        debug=False,
    ):
        """
        Existing static-image pocket detector.
        Output order:
            [TL, TR, BM, TM, BL, BR]
        """
        if corners_px_inner is None:
            return None, None, None

        Lp = int(round(float(playfield_L_mm)))
        Wp = int(round(float(playfield_W_mm)))
        if Lp <= 0 or Wp <= 0:
            return None, None, None

        H_img_to_plane = self.homography_px_to_plane(corners_px_inner, Lp, Wp)
        if H_img_to_plane is None:
            return None, None, None

        H_plane_to_img = np.linalg.inv(H_img_to_plane)
        plane = cv2.warpPerspective(frame_bgr, H_img_to_plane, (Lp, Wp), flags=cv2.INTER_LINEAR)

        hsv = cv2.cvtColor(plane, cv2.COLOR_BGR2HSV)
        _, Sc, Vc = cv2.split(hsv)

        dark = (Vc < int(v_thresh)).astype(np.uint8) * 255
        if sat_max is not None:
            sat_ok = (Sc < int(sat_max)).astype(np.uint8) * 255
            dark = cv2.bitwise_and(dark, sat_ok)

        dark = cv2.morphologyEx(dark, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8), iterations=1)
        dark = cv2.morphologyEx(dark, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8), iterations=2)

        def crop_roi(x0, y0, x1, y1):
            x0i = max(0, int(x0))
            y0i = max(0, int(y0))
            x1i = min(Lp, int(x1))
            y1i = min(Wp, int(y1))
            roi_mask = np.zeros_like(dark)
            roi_mask[y0i:y1i, x0i:x1i] = dark[y0i:y1i, x0i:x1i]
            sub = dark[y0i:y1i, x0i:x1i]
            return x0i, y0i, x1i, y1i, sub, roi_mask

        cw = roi_frac_corner
        tl_roi = crop_roi(0, Wp * (1 - cw), Lp * cw, Wp)
        tr_roi = crop_roi(Lp * (1 - cw), Wp * (1 - cw), Lp, Wp)
        bl_roi = crop_roi(0, 0, Lp * cw, Wp * cw)
        br_roi = crop_roi(Lp * (1 - cw), 0, Lp, Wp * cw)

        sw = roi_frac_side_w
        sh = roi_frac_side_h
        tm_roi = crop_roi(Lp * (0.5 - sw / 2.0), Wp * (1 - sh), Lp * (0.5 + sw / 2.0), Wp)
        bm_roi = crop_roi(Lp * (0.5 - sw / 2.0), 0, Lp * (0.5 + sw / 2.0), Wp * sh)

        def centroid_in_roi(roi):
            x0, y0, _, _, sub, _ = roi
            c = self._find_dark_centroid(sub, min_area=min_area_px)
            if c is None:
                return None
            cx, cy = c
            return float(x0) + float(cx), float(y0) + float(cy)

        TLp = centroid_in_roi(tl_roi)
        TRp = centroid_in_roi(tr_roi)
        BLp = centroid_in_roi(bl_roi)
        BRp = centroid_in_roi(br_roi)
        TMp = centroid_in_roi(tm_roi)
        BMp = centroid_in_roi(bm_roi)

        pockets_plane = {
            "TL": TLp,
            "TR": TRp,
            "TM": TMp,
            "BM": BMp,
            "BL": BLp,
            "BR": BRp,
        }

        def plane_to_img(pt):
            if pt is None:
                return None
            x, y = pt
            v = np.array([x, y, 1.0], np.float32)
            q = H_plane_to_img @ v
            return float(q[0] / q[2]), float(q[1] / q[2])

        pockets_px = {k: plane_to_img(v) for k, v in pockets_plane.items()}

        ordered = [
            pockets_px["TL"],
            pockets_px["TR"],
            pockets_px["BM"],
            pockets_px["TM"],
            pockets_px["BL"],
            pockets_px["BR"],
        ]

        dbg = (
            {
                "plane": plane if debug else None,
                "dark": dark if debug else None,
                "pockets_plane": pockets_plane,
                "H_img_to_plane": H_img_to_plane,
            }
            if debug
            else None
        )

        return ordered, pockets_plane, dbg

    # -----------------------------
    # Separate video pocket pipeline
    # -----------------------------
    @staticmethod
    def _safe_percentile(values, q, fallback):
        if values is None:
            return float(fallback)
        arr = np.asarray(values)
        if arr.size == 0:
            return float(fallback)
        return float(np.percentile(arr, q))

    @staticmethod
    def _gray_world_white_balance(frame_bgr):
        if frame_bgr is None or frame_bgr.size == 0:
            return frame_bgr

        b, g, r = cv2.split(frame_bgr.astype(np.float32))
        mb = float(np.mean(b)) + 1e-6
        mg = float(np.mean(g)) + 1e-6
        mr = float(np.mean(r)) + 1e-6
        mean_gray = (mb + mg + mr) / 3.0

        b *= mean_gray / mb
        g *= mean_gray / mg
        r *= mean_gray / mr

        balanced = cv2.merge([b, g, r])
        return np.clip(balanced, 0.0, 255.0).astype(np.uint8)

    @classmethod
    def _ordered_pocket_map_from_list(cls, ordered_points):
        if ordered_points is None or len(ordered_points) != 6:
            return None

        def _to_tuple(point):
            if point is None:
                return None
            return float(point[0]), float(point[1])

        return {
            "TL": _to_tuple(ordered_points[0]),
            "TR": _to_tuple(ordered_points[1]),
            "BM": _to_tuple(ordered_points[2]),
            "TM": _to_tuple(ordered_points[3]),
            "BL": _to_tuple(ordered_points[4]),
            "BR": _to_tuple(ordered_points[5]),
        }

    @classmethod
    def _build_expected_pocket_map(cls, expected_pockets_mm, playfield_L_mm, playfield_W_mm):
        if expected_pockets_mm is not None and len(expected_pockets_mm) == 6:
            return {
                "TL": tuple(map(float, expected_pockets_mm[0])),
                "TR": tuple(map(float, expected_pockets_mm[1])),
                "BM": tuple(map(float, expected_pockets_mm[2])),
                "TM": tuple(map(float, expected_pockets_mm[3])),
                "BL": tuple(map(float, expected_pockets_mm[4])),
                "BR": tuple(map(float, expected_pockets_mm[5])),
            }

        L = float(playfield_L_mm)
        W = float(playfield_W_mm)
        return {
            "TL": (0.0, W),
            "TR": (L, W),
            "BM": (0.5 * L, 0.0),
            "TM": (0.5 * L, W),
            "BL": (0.0, 0.0),
            "BR": (L, 0.0),
        }

    @staticmethod
    def _mean_valid(values, fallback):
        valid = [float(v) for v in values if v is not None]
        return float(sum(valid) / len(valid)) if valid else float(fallback)

    def _smooth_video_pockets_plane(self, ordered_points):
        ordered_points = np.asarray(ordered_points, dtype=np.float32)
        self._video_pocket_plane_ema = self._exp_moving_avg(
            self._video_pocket_plane_ema,
            ordered_points,
            self._video_pocket_alpha,
        )
        return self._video_pocket_plane_ema.copy()

    def _pick_best_video_pocket_candidate(
        self,
        gray_eq,
        value_channel,
        blackhat,
        roi_rect,
        expected_point_xy,
        min_area_px=180,
    ):
        x0, y0, x1, y1 = roi_rect
        if x1 <= x0 or y1 <= y0:
            return None, None

        gray_roi = gray_eq[y0:y1, x0:x1]
        v_roi = value_channel[y0:y1, x0:x1]
        bh_roi = blackhat[y0:y1, x0:x1]

        if gray_roi.size == 0 or v_roi.size == 0 or bh_roi.size == 0:
            return None, None

        gray_thr = self._safe_percentile(gray_roi, 18, 70.0)
        value_thr = self._safe_percentile(v_roi, 18, 75.0)
        blackhat_thr = max(8.0, self._safe_percentile(bh_roi, 78, 18.0))

        mask_gray = (gray_roi <= gray_thr).astype(np.uint8) * 255
        mask_value = (v_roi <= value_thr).astype(np.uint8) * 255
        mask_blackhat = (bh_roi >= blackhat_thr).astype(np.uint8) * 255

        candidate_mask = cv2.bitwise_or(cv2.bitwise_and(mask_gray, mask_value), mask_blackhat)
        candidate_mask = cv2.morphologyEx(candidate_mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8), iterations=1)
        candidate_mask = cv2.morphologyEx(candidate_mask, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8), iterations=2)

        contours, _ = cv2.findContours(candidate_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return None, candidate_mask

        roi_w = max(1, x1 - x0)
        roi_h = max(1, y1 - y0)
        max_area = float(roi_w * roi_h) * 0.80

        exp_x, exp_y = map(float, expected_point_xy)
        best_point = None
        best_score = -1e9

        for contour in contours:
            area = float(cv2.contourArea(contour))
            if area < float(min_area_px) or area > max_area:
                continue

            moments = cv2.moments(contour)
            if abs(moments["m00"]) < 1e-6:
                continue

            cx_local = float(moments["m10"] / moments["m00"])
            cy_local = float(moments["m01"] / moments["m00"])
            cx = float(x0) + cx_local
            cy = float(y0) + cy_local

            distance = float(np.hypot(cx - exp_x, cy - exp_y))
            distance_norm = distance / max(1.0, 0.5 * (roi_w + roi_h))

            contour_mask = np.zeros_like(candidate_mask)
            cv2.drawContours(contour_mask, [contour], -1, 255, -1)

            mean_gray = float(cv2.mean(gray_roi, mask=contour_mask)[0])
            mean_value = float(cv2.mean(v_roi, mask=contour_mask)[0])
            mean_blackhat = float(cv2.mean(bh_roi, mask=contour_mask)[0])

            darkness_score = 1.0 - (((mean_gray + mean_value) * 0.5) / 255.0)
            blackhat_score = mean_blackhat / 255.0
            area_score = min(1.0, area / max(1.0, float(min_area_px)))

            bx, by, bw, bh = cv2.boundingRect(contour)
            edge_touch = 1.0 if bx <= 2 or by <= 2 or (bx + bw) >= (roi_w - 2) or (by + bh) >= (roi_h - 2) else 0.0

            score = (
                (2.40 * darkness_score)
                + (1.60 * blackhat_score)
                + (0.25 * area_score)
                + (0.20 * edge_touch)
                - (1.50 * distance_norm)
            )

            if distance_norm > 1.10:
                continue

            if score > best_score:
                best_score = score
                best_point = (float(cx), float(cy))

        return best_point, candidate_mask

    def _align_video_pockets_plane(
        self,
        measured_map,
        expected_map,
        measured_weight=0.70,
        max_axis_offset_mm=140.0,
    ):
        expected_left_x = 0.5 * (expected_map["TL"][0] + expected_map["BL"][0])
        expected_right_x = 0.5 * (expected_map["TR"][0] + expected_map["BR"][0])
        expected_mid_x = 0.5 * (expected_map["TM"][0] + expected_map["BM"][0])

        # Important:
        # In this plane coordinate system, top pockets have larger Y than bottom pockets.
        expected_top_y = self._mean_valid(
            [expected_map["TL"][1], expected_map["TR"][1], expected_map["TM"][1]],
            expected_map["TM"][1],
        )
        expected_bottom_y = self._mean_valid(
            [expected_map["BL"][1], expected_map["BR"][1], expected_map["BM"][1]],
            expected_map["BM"][1],
        )

        def blended_axis(keys, axis_index, expected_value):
            values = [
                float(measured_map[key][axis_index])
                for key in keys
                if measured_map.get(key) is not None
            ]
            if not values:
                return float(expected_value)

            measured_mean = float(sum(values) / len(values))
            delta = measured_mean - float(expected_value)
            delta = max(-float(max_axis_offset_mm), min(float(max_axis_offset_mm), delta))
            return float(expected_value) + (float(measured_weight) * delta)

        left_x = blended_axis(("TL", "BL"), 0, expected_left_x)
        right_x = blended_axis(("TR", "BR"), 0, expected_right_x)
        mid_x = blended_axis(("TM", "BM"), 0, expected_mid_x)
        top_y = blended_axis(("TL", "TR", "TM"), 1, expected_top_y)
        bottom_y = blended_axis(("BL", "BR", "BM"), 1, expected_bottom_y)

        expected_width = max(1.0, expected_right_x - expected_left_x)
        expected_height = max(1.0, expected_top_y - expected_bottom_y)

        if (right_x - left_x) < (0.65 * expected_width):
            left_x = float(expected_left_x)
            right_x = float(expected_right_x)

        if (top_y - bottom_y) < (0.55 * expected_height):
            top_y = float(expected_top_y)
            bottom_y = float(expected_bottom_y)

        if left_x >= right_x:
            left_x = float(expected_left_x)
            right_x = float(expected_right_x)

        if top_y <= bottom_y:
            top_y = float(expected_top_y)
            bottom_y = float(expected_bottom_y)

        mid_x = max(float(left_x) + 1e-3, min(float(mid_x), float(right_x) - 1e-3))

        return {
            "TL": (float(left_x), float(top_y)),
            "TR": (float(right_x), float(top_y)),
            "BM": (float(mid_x), float(bottom_y)),
            "TM": (float(mid_x), float(top_y)),
            "BL": (float(left_x), float(bottom_y)),
            "BR": (float(right_x), float(bottom_y)),
        }

    def detect_pockets_video_markerless(
        self,
        frame_bgr,
        corners_px_inner,
        playfield_L_mm,
        playfield_W_mm,
        expected_pockets_mm=None,
        v_thresh=70,
        sat_max=180,
        roi_frac_corner=0.18,
        roi_frac_side_w=0.22,
        roi_frac_side_h=0.16,
        min_area_px=180,
        debug=False,
    ):
        """
        Separate pocket detector for live / recorded video.

        Differences from the static pipeline:
        - uses illumination normalization
        - uses expected pocket positions from table geometry
        - detects pocket candidates per expected ROI
        - aligns pockets in canonical plane
        - applies separate temporal smoothing only for video mode

        Output order stays exactly:
            [TL, TR, BM, TM, BL, BR]
        """
        if corners_px_inner is None or frame_bgr is None:
            return None, None, None

        Lp = int(round(float(playfield_L_mm)))
        Wp = int(round(float(playfield_W_mm)))
        if Lp <= 0 or Wp <= 0:
            return None, None, None

        expected_map = self._build_expected_pocket_map(expected_pockets_mm, playfield_L_mm, playfield_W_mm)

        H_img_to_plane = self.homography_px_to_plane(corners_px_inner, Lp, Wp)
        if H_img_to_plane is None:
            return None, None, None

        H_plane_to_img = np.linalg.inv(H_img_to_plane)
        plane = cv2.warpPerspective(frame_bgr, H_img_to_plane, (Lp, Wp), flags=cv2.INTER_LINEAR)

        plane_balanced = self._gray_world_white_balance(plane)
        hsv = cv2.cvtColor(plane_balanced, cv2.COLOR_BGR2HSV)
        _, sat_channel, value_channel = cv2.split(hsv)

        gray = cv2.cvtColor(plane_balanced, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (5, 5), 0)
        gray_eq = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8)).apply(gray)

        blackhat_kernel_size = max(21, int(round(min(Lp, Wp) * 0.04)))
        if (blackhat_kernel_size % 2) == 0:
            blackhat_kernel_size += 1
        blackhat_kernel = cv2.getStructuringElement(
            cv2.MORPH_ELLIPSE,
            (blackhat_kernel_size, blackhat_kernel_size),
        )
        blackhat = cv2.morphologyEx(gray_eq, cv2.MORPH_BLACKHAT, blackhat_kernel)

        # Optional weak S-channel gate to suppress saturated cloth noise.
        if sat_max is not None:
            sat_mask = (sat_channel < int(sat_max)).astype(np.uint8) * 255
            blackhat = cv2.bitwise_and(blackhat, sat_mask)

        measured_map = {key: None for key in self.POCKET_KEY_ORDER}
        candidate_mask_full = np.zeros((Wp, Lp), dtype=np.uint8)

        corner_half_w = max(70.0, float(playfield_L_mm) * float(roi_frac_corner) * 0.70)
        corner_half_h = max(70.0, float(playfield_W_mm) * float(roi_frac_corner) * 0.95)
        side_half_w = max(90.0, float(playfield_L_mm) * float(roi_frac_side_w) * 0.50)
        side_half_h = max(60.0, float(playfield_W_mm) * float(roi_frac_side_h) * 0.90)

        for key in self.POCKET_KEY_ORDER:
            expected_point = expected_map[key]
            ex, ey = map(float, expected_point)

            if key in ("TM", "BM"):
                half_w = side_half_w
                half_h = side_half_h
            else:
                half_w = corner_half_w
                half_h = corner_half_h

            x0 = max(0, int(round(ex - half_w)))
            y0 = max(0, int(round(ey - half_h)))
            x1 = min(Lp, int(round(ex + half_w)))
            y1 = min(Wp, int(round(ey + half_h)))

            point, candidate_mask = self._pick_best_video_pocket_candidate(
                gray_eq=gray_eq,
                value_channel=value_channel,
                blackhat=blackhat,
                roi_rect=(x0, y0, x1, y1),
                expected_point_xy=(ex, ey),
                min_area_px=min_area_px,
            )

            if point is not None:
                measured_map[key] = (float(point[0]), float(point[1]))

            if candidate_mask is not None:
                candidate_mask_full[y0:y1, x0:x1] = cv2.bitwise_or(
                    candidate_mask_full[y0:y1, x0:x1],
                    candidate_mask,
                )

        aligned_map = self._align_video_pockets_plane(
            measured_map=measured_map,
            expected_map=expected_map,
            measured_weight=0.70,
            max_axis_offset_mm=140.0,
        )

        aligned_ordered_plane = [
            aligned_map["TL"],
            aligned_map["TR"],
            aligned_map["BM"],
            aligned_map["TM"],
            aligned_map["BL"],
            aligned_map["BR"],
        ]

        smoothed_ordered_plane = self._smooth_video_pockets_plane(aligned_ordered_plane)
        smoothed_map = self._ordered_pocket_map_from_list(smoothed_ordered_plane)

        def plane_to_img(pt):
            if pt is None:
                return None
            x, y = pt
            v = np.array([float(x), float(y), 1.0], np.float32)
            q = H_plane_to_img @ v
            return float(q[0] / q[2]), float(q[1] / q[2])

        ordered_px = [
            plane_to_img(smoothed_map["TL"]),
            plane_to_img(smoothed_map["TR"]),
            plane_to_img(smoothed_map["BM"]),
            plane_to_img(smoothed_map["TM"]),
            plane_to_img(smoothed_map["BL"]),
            plane_to_img(smoothed_map["BR"]),
        ]

        dbg = (
            {
                "plane": plane if debug else None,
                "dark": candidate_mask_full if debug else None,
                "plane_balanced": plane_balanced if debug else None,
                "gray_eq": gray_eq if debug else None,
                "blackhat": blackhat if debug else None,
                "pockets_plane_raw": measured_map,
                "pockets_plane_aligned": aligned_map,
                "pockets_plane_smoothed": smoothed_map,
                "H_img_to_plane": H_img_to_plane,
                "v_thresh": v_thresh,
                "sat_max": sat_max,
            }
            if debug
            else None
        )

        return ordered_px, smoothed_map, dbg

    # -----------------------------
    # Balls (classic)
    # -----------------------------
    @staticmethod
    def detect_balls(frame, table_mask, min_ball_radius, max_ball_radius, gaussian_kernel=(9, 9)):
        masked = cv2.bitwise_and(frame, frame, mask=table_mask)
        gray = cv2.cvtColor(masked, cv2.COLOR_BGR2GRAY)
        blurred = cv2.GaussianBlur(gray, gaussian_kernel, 2)
        circles = cv2.HoughCircles(
            blurred,
            cv2.HOUGH_GRADIENT,
            dp=1.2,
            minDist=20,
            param1=50,
            param2=30,
            minRadius=min_ball_radius,
            maxRadius=max_ball_radius,
        )
        if circles is not None:
            return np.uint16(np.around(circles[0]))
        return []

    @staticmethod
    def classify_balls(frame, circle, white_treshold, eightball_treshold, stripe_white_ratio):
        x, y, r = int(circle[0]), int(circle[1]), int(circle[2])
        roi = frame[y - r : y + r, x - r : x + r]
        if roi.size == 0:
            return BallType.UNKNOWN.value

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
            return BallType.STRIPE.value
        return BallType.SOLID.value

    @staticmethod
    def circle_to_entry(frame, circle, circle_center_m, white_treshold, eight_treshold, stripe_wb_ratio):
        t = ObjectDetector.classify_balls(frame, circle, white_treshold, eight_treshold, stripe_wb_ratio)
        x, y = circle_center_m
        return {
            "type": t,
            "x": x,
            "y": y,
            "confidence": None,
            "vx": None,
            "vy": None,
        }

    # -----------------------------
    # Balls (YOLO legacy path)
    # -----------------------------
    def detect_balls_yolo(self, frame_bgr):
        self._ensure_yolo()
        if self.yolo is None:
            return []

        results = self.yolo.predict(
            source=frame_bgr,
            conf=self._yolo_conf,
            iou=self._iou,
            max_det=self._max_det,
            verbose=False,
            device=self.device,
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
            if c not in id_to_type:
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

            entries.append(
                {
                    "type": t,
                    "x": xm,
                    "y": ym,
                    "number": self.label_map[BallType.UNKNOWN.value][1]
                    if t in (BallType.SOLID.value, BallType.STRIPE.value)
                    else (
                        self.label_map[BallType.CUE.value][1]
                        if t == BallType.CUE.value
                        else self.label_map[BallType.EIGHT.value][1]
                    ),
                    "confidence": float(conf),
                    "vx": None,
                    "vy": None,
                }
            )

        if self.debug:
            print(entries)

        return entries

    def classify_balls_yolo(self):
        pass

    @staticmethod
    def is_pocketed(x_m: float, y_m: float, pockets_xy_m, pocketed_dist_m):
        if x_m is None or y_m is None:
            return True

        for pxm, pym in pockets_xy_m:
            if pym is None or pxm is None:
                continue
            dx = float(x_m) - float(pxm)
            dy = float(y_m) - float(pym)
            if ((dx * dx) + (dy * dy)) ** 0.5 <= pocketed_dist_m:
                return True

        return False

    @staticmethod
    def is_in_playfield_bounds_xy(xm: float, ym: float, table_LW_m, ball_diamter_m: float) -> bool:
        if xm is None or ym is None:
            return False

        L = float(table_LW_m[0])
        W = float(table_LW_m[1])
        r = float(ball_diamter_m) * 0.5
        return (0.0 + r) <= float(xm) <= (L - r) and (0.0 + r) <= float(ym) <= (W - r)

    @staticmethod
    def is_in_table_bounds(x_m: float, y_m: float, pockets_xy_m, ball_diameter_m=0.05715) -> bool:
        xs = [p[0] for p in pockets_xy_m if (p is not None and p[0] is not None)]
        ys = [p[1] for p in pockets_xy_m if (p is not None and p[1] is not None)]
        if not xs or not ys:
            return True

        margin = float(ball_diameter_m) * 0.6
        return (min(xs) - margin) <= float(x_m) <= (max(xs) + margin) and (min(ys) - margin) <= float(y_m) <= (max(ys) + margin)

    @staticmethod
    def nearest_yolo_type(cx: float, cy: float, yolo_centers, match_distance_px: float):
        best = None
        best_d2 = match_distance_px * match_distance_px

        for x, y, t, conf in yolo_centers:
            dx = x - cx
            dy = y - cy
            d2 = (dx * dx) + (dy * dy)
            if d2 <= best_d2:
                best_d2 = d2
                best = (t, conf)

        return best
    
    @staticmethod
    def _midpoint_2d(point_a, point_b):
        point_a = np.asarray(point_a, dtype=np.float32)
        point_b = np.asarray(point_b, dtype=np.float32)
        return np.array([
            0.5 * (float(point_a[0]) + float(point_b[0])),
            0.5 * (float(point_a[1]) + float(point_b[1]))
        ], dtype=np.float32)

    @staticmethod
    def _line_from_points(point_a, point_b):
        point_a = np.asarray(point_a, dtype=np.float32)
        point_b = np.asarray(point_b, dtype=np.float32)
        return (
            float(point_a[0]), float(point_a[1]),
            float(point_b[0]), float(point_b[1])
        )

    def _refine_pocket_center_local_dark_hsv(
        self,
        frame_bgr,
        seed_point_px,
        search_radius_px=42,
        max_value_threshold=105,
        max_saturation_threshold=220,
        min_area_px=40
    ):
        """
        Local HSV-based pocket refinement around a geometric seed.
        Returns the dark-region centroid nearest/best matching the seed.
        Falls back to the seed if nothing reliable is found.
        """
        if frame_bgr is None or seed_point_px is None:
            return seed_point_px

        frame_height, frame_width = frame_bgr.shape[:2]
        seed_x = int(round(float(seed_point_px[0])))
        seed_y = int(round(float(seed_point_px[1])))
        radius = int(max(8, search_radius_px))

        x0 = max(0, seed_x - radius)
        y0 = max(0, seed_y - radius)
        x1 = min(frame_width, seed_x + radius + 1)
        y1 = min(frame_height, seed_y + radius + 1)

        if x1 <= x0 or y1 <= y0:
            return seed_point_px

        roi = frame_bgr[y0:y1, x0:x1]
        hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
        _, saturation, value = cv2.split(hsv)

        percentile_threshold = int(np.percentile(value, 25)) + 15
        value_threshold = int(min(max_value_threshold, max(40, percentile_threshold)))

        dark_mask = (value < value_threshold).astype(np.uint8) * 255
        saturation_mask = (saturation < int(max_saturation_threshold)).astype(np.uint8) * 255
        candidate_mask = cv2.bitwise_and(dark_mask, saturation_mask)

        roi_center_x = float(seed_x - x0)
        roi_center_y = float(seed_y - y0)

        local_circle_mask = np.zeros_like(candidate_mask)
        cv2.circle(
            local_circle_mask,
            (int(round(roi_center_x)), int(round(roi_center_y))),
            int(radius * 0.9),
            255,
            -1
        )

        candidate_mask = cv2.bitwise_and(candidate_mask, local_circle_mask)
        candidate_mask = cv2.morphologyEx(candidate_mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8), iterations=1)
        candidate_mask = cv2.morphologyEx(candidate_mask, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8), iterations=2)

        contours, _ = cv2.findContours(candidate_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return seed_point_px

        best_center = None
        best_score = -float("inf")

        for contour in contours:
            area = float(cv2.contourArea(contour))
            if area < float(min_area_px):
                continue

            moments = cv2.moments(contour)
            if abs(moments["m00"]) <= 1e-6:
                continue

            cx = float(moments["m10"] / moments["m00"])
            cy = float(moments["m01"] / moments["m00"])

            distance_to_seed = float(np.hypot(cx - roi_center_x, cy - roi_center_y))
            score = area - (2.25 * distance_to_seed)

            if score > best_score:
                best_score = score
                best_center = (float(x0) + cx, float(y0) + cy)

        return best_center if best_center is not None else seed_point_px

    def _refine_pocket_center_local_dark_gray(
        self,
        frame_bgr,
        seed_point_px,
        search_radius_px=42,
        max_gray_threshold=110,
        min_area_px=40
    ):
        """
        Local grayscale-based pocket refinement around a geometric seed.

        Pipeline:
        - grayscale conversion
        - Gaussian blur
        - CLAHE equalization
        - blackhat to emphasize dark openings on lighter surroundings
        - Canny edges for structural support
        - contour scoring by area + edge support - seed distance

        This is intentionally video-only and should not affect the static-image pipeline.
        """
        if frame_bgr is None or seed_point_px is None:
            return seed_point_px

        frame_height, frame_width = frame_bgr.shape[:2]
        seed_x = int(round(float(seed_point_px[0])))
        seed_y = int(round(float(seed_point_px[1])))
        radius = int(max(8, search_radius_px))

        x0 = max(0, seed_x - radius)
        y0 = max(0, seed_y - radius)
        x1 = min(frame_width, seed_x + radius + 1)
        y1 = min(frame_height, seed_y + radius + 1)

        if x1 <= x0 or y1 <= y0:
            return seed_point_px

        roi = frame_bgr[y0:y1, x0:x1]
        gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
        gray = cv2.GaussianBlur(gray, (5, 5), 0)

        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        gray_eq = clahe.apply(gray)

        blackhat_kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (15, 15))
        blackhat = cv2.morphologyEx(gray_eq, cv2.MORPH_BLACKHAT, blackhat_kernel)

        percentile_gray = int(np.percentile(gray_eq, 30)) + 12
        gray_threshold = int(min(max_gray_threshold, max(35, percentile_gray)))

        gray_dark_mask = (gray_eq < gray_threshold).astype(np.uint8) * 255
        _, blackhat_mask = cv2.threshold(blackhat, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)

        edges = cv2.Canny(gray_eq, 45, 135)
        edges_dilated = cv2.dilate(edges, np.ones((3, 3), np.uint8), iterations=1)

        candidate_mask = cv2.bitwise_or(gray_dark_mask, blackhat_mask)

        roi_center_x = float(seed_x - x0)
        roi_center_y = float(seed_y - y0)

        local_circle_mask = np.zeros_like(candidate_mask)
        cv2.circle(
            local_circle_mask,
            (int(round(roi_center_x)), int(round(roi_center_y))),
            int(radius * 0.9),
            255,
            -1
        )

        candidate_mask = cv2.bitwise_and(candidate_mask, local_circle_mask)
        candidate_mask = cv2.morphologyEx(candidate_mask, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8), iterations=1)
        candidate_mask = cv2.morphologyEx(candidate_mask, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8), iterations=2)

        contours, _ = cv2.findContours(candidate_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            return seed_point_px

        best_center = None
        best_score = -float("inf")

        for contour in contours:
            area = float(cv2.contourArea(contour))
            if area < float(min_area_px):
                continue

            moments = cv2.moments(contour)
            if abs(moments["m00"]) <= 1e-6:
                continue

            cx = float(moments["m10"] / moments["m00"])
            cy = float(moments["m01"] / moments["m00"])

            contour_mask = np.zeros_like(candidate_mask)
            cv2.drawContours(contour_mask, [contour], -1, 255, -1)

            edge_support = float(cv2.countNonZero(cv2.bitwise_and(edges_dilated, contour_mask)))
            distance_to_seed = float(np.hypot(cx - roi_center_x, cy - roi_center_y))

            score = area + (0.35 * edge_support) - (2.5 * distance_to_seed)

            if score > best_score:
                best_score = score
                best_center = (float(x0) + cx, float(y0) + cy)

        return best_center if best_center is not None else seed_point_px

    def _detect_pockets_video_markerless(
        self,
        frame_bgr,
        outer_corners_px,
        inner_corners_px,
        use_grayscale=False,
        refine_with_local_search=True,
        dark_search_radius_px=42,
        debug=False
    ):
        """
        Video-only pocket detector.

        Pocket order is preserved exactly as required:
            [TL, TR, BM, TM, BL, BR]

        Strategy:
        1) derive geometric pocket seeds from outer and inner rail geometry
        2) refine locally near each seed using either:
        - HSV dark-region pipeline
        - grayscale dark/edge pipeline

        The grayscale branch is only used here for video pocket detection.
        """
        if frame_bgr is None or outer_corners_px is None or inner_corners_px is None:
            return None, None

        outer = self._order_corners(np.asarray(outer_corners_px, dtype=np.float32))
        inner = self._order_corners(np.asarray(inner_corners_px, dtype=np.float32))

        outer_tl, outer_tr, outer_bl, outer_br = outer
        inner_tl, inner_tr, inner_bl, inner_br = inner

        top_mid_left = self._midpoint_2d(outer_tl, inner_tl)
        top_mid_right = self._midpoint_2d(outer_tr, inner_tr)
        bottom_mid_left = self._midpoint_2d(outer_bl, inner_bl)
        bottom_mid_right = self._midpoint_2d(outer_br, inner_br)

        top_mid_line = self._line_from_points(top_mid_left, top_mid_right)
        bottom_mid_line = self._line_from_points(bottom_mid_left, bottom_mid_right)
        left_mid_line = self._line_from_points(top_mid_left, bottom_mid_left)
        right_mid_line = self._line_from_points(top_mid_right, bottom_mid_right)

        tl_seed = self._intersect_lines(top_mid_line, left_mid_line)
        tr_seed = self._intersect_lines(top_mid_line, right_mid_line)
        bl_seed = self._intersect_lines(bottom_mid_line, left_mid_line)
        br_seed = self._intersect_lines(bottom_mid_line, right_mid_line)

        if tl_seed is None or tr_seed is None or bl_seed is None or br_seed is None:
            return None, None

        tm_seed = self._midpoint_2d(top_mid_left, top_mid_right)
        bm_seed = self._midpoint_2d(bottom_mid_left, bottom_mid_right)

        pocket_seeds = {
            "TL": tuple(map(float, tl_seed)),
            "TR": tuple(map(float, tr_seed)),
            "BM": tuple(map(float, bm_seed)),
            "TM": tuple(map(float, tm_seed)),
            "BL": tuple(map(float, bl_seed)),
            "BR": tuple(map(float, br_seed)),
        }

        if not refine_with_local_search:
            pocket_final = pocket_seeds.copy()
        else:
            refiner = self._refine_pocket_center_local_dark_gray if use_grayscale else self._refine_pocket_center_local_dark_hsv

            pocket_final = {
                "TL": refiner(frame_bgr, pocket_seeds["TL"], search_radius_px=dark_search_radius_px),
                "TR": refiner(frame_bgr, pocket_seeds["TR"], search_radius_px=dark_search_radius_px),
                "BM": refiner(frame_bgr, pocket_seeds["BM"], search_radius_px=dark_search_radius_px),
                "TM": refiner(frame_bgr, pocket_seeds["TM"], search_radius_px=dark_search_radius_px),
                "BL": refiner(frame_bgr, pocket_seeds["BL"], search_radius_px=dark_search_radius_px),
                "BR": refiner(frame_bgr, pocket_seeds["BR"], search_radius_px=dark_search_radius_px),
            }

        ordered = [
            pocket_final["TL"],
            pocket_final["TR"],
            pocket_final["BM"],
            pocket_final["TM"],
            pocket_final["BL"],
            pocket_final["BR"],
        ]

        dbg = None
        if debug:
            dbg = {
                "pipeline": "grayscale" if use_grayscale else "hsv",
                "outer_corners": outer.copy(),
                "inner_corners": inner.copy(),
                "top_mid_line": top_mid_line,
                "bottom_mid_line": bottom_mid_line,
                "left_mid_line": left_mid_line,
                "right_mid_line": right_mid_line,
                "pocket_seeds": pocket_seeds,
                "pocket_final": pocket_final,
            }

        return ordered, dbg