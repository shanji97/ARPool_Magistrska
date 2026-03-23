import cv2
import numpy as np
import torch
import os
import sys
from ball_type import BallType

class ObjectDetector:

    YOLO_MODEL_NAME = "yolov5"
    CONFIDENCE = .25
    IOU = .45
    MAX_DET = 64

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
        self._corner_alpha = .2
        self._pocket_alpha = .25
        self.debug = debug
        self.local_repo = ""
        self.load_yolo()

    def dispose(self):
        try:
            if getattr(self, "_local_repo_added_to_syspath", False) and self.local_repo in sys.path:
                sys.path.remove(self.local_repo)  # UPDATED
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
        
        weights_path = os.path.abspath(os.path.join(".", "pix2pockets", "detection_model_weight","detection_model.pt"))
        
        if not os.path.isfile(weights_path):
            raise FileNotFoundError(f"YOLOv5 weights not found: {weights_path}")
        
        local_repo = os.path.abspath(os.path.join(".", "pix2pockets", "yolov5"))
        if not os.path.isdir(local_repo):
            raise FileNotFoundError(                
                f"Local YOLOv5 repo not found at: {local_repo}\n"
                f"Clone it once (offline safe afterwards):\n"
                f"  git clone https://github.com/ultralytics/yolov5.git pix2pockets/yolov5")
        if local_repo not in sys.path:
            self.local_repo = local_repo
            sys.path.insert(0, local_repo) # Remove after use from path
            self._local_repo_added_to_syspath = True
        else:
            self.local_repo = local_repo
            self._local_repo_added_to_syspath = False
        
        model = None
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
        except:
            pass
        self._yolov5_model = model
        return self._yolov5_model
        
    def detect_balls_yolov5(self,frame_bgr, img_size: int = 640):
        """Runs Pix2Pockets YOLOv5 and returns raw detections with boxes.

        Returns a list of dicts:
            {
              'x1','y1','x2','y2',   # bbox corners in pixels
              'cx','cy',            # bbox center in pixels
              'cls',                # Pix2Pockets class id (0..3)
              'confidence'          # float
            }
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
                results = model(frame_rgb, size = int(img_size))
        except Exception as e:
            print("[yolov5] inference failed: ", e)
            return []
        
        preds = getattr(results, "xyxy", [None])[0]
        if preds is None or len(preds) == 0:
            return []
        preds = preds.detach().float().cpu().numpy()
        out = []
        for x1, y1, x2, y2, conf, cls in preds:
            cx = float((x1 + x2) * 0.5)
            cy = float((y1 + y2) * 0.5)
            out.append({
                "x1": float(x1),
                "y1": float(y1),
                "x2": float(x2),
                "y2": float(y2),
                "cx": cx,
                "cy": cy,
                "cls": int(cls),
                "confidence": float(conf),
            })
        return out       
    
    def classify_balls_pix2pockets(self, frame):
        dets = self.detect_balls_yolov5(frame_bgr=frame, img_size=640)  # UPDATED: central implementation
        return [(d["cx"], d["cy"], d["cls"], d["confidence"]) for d in dets]

    def get_gpu_info(self):
        cuda_version = "N/A"
        vram = 0
        try:
            cuda_available = torch.cuda.is_available()
            if cuda_available:
                device = torch.device('cuda:0')
                vram = int((torch.cuda.get_device_properties(device).total_memory) / (1024 * 1024))
                if hasattr(torch.version, "cuda") and torch.version.cuda:
                    cuda_version = str(torch.version.cuda)

            return (cuda_available, cuda_version, vram)
        except Exception as e:
            print(f"Error fetching GPU info: {e}")
            return False, cuda_version, vram
        
    @staticmethod
    def _normalize_vector_2d(vx, vy):
        length = float(np.hypot(vx, vy))
        if length <= 1e-6:
            return (.0,.0)
        return (float(vx/length), float(vy/length))
    
    @staticmethod
    def _axis_angle_deg(vx,vy): 
        angle = float(np.degrees(np.arctan2(vy,vx)))
        while angle < 0.0:
            angle += 180
        while angle > 180:
            angle -= 180
        return angle
    
    @staticmethod
    def _axis_angle_delta_deg(a_deg, b_deg):
        delta = abs(float(a_deg) - float(b_deg)) % 180
        return min(delta, 180 - delta)
            
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
        debug=False
    ):
        """
        Detect a cue line near the cue ball from a single overhead frame.

        This version is intentionally more permissive than the previous one.
        It does not require a Hough segment endpoint to be close to the cue ball.
        Instead, it accepts segments whose infinite line passes close to the cue ball.

        Returns:
            {
                "line_point_px": (x, y),
                "direction_px": (dx, dy),
                "direction_probe_px": (x, y),
                "hit_point_px": (x, y),
                "confidence": float,
                "candidate_count": int,
                "debug_mask": np.ndarray | None,
                "debug_edges": np.ndarray | None,
            }
            or None
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
        cv2.circle(
            cue_mask,
            (int(round(cx)), int(round(cy))),
            int(round(ball_radius_px * 1.05)),
            0,
            -1
        )

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
            maxLineGap=int(max_line_gap)
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
                circle_radius_px=float(ball_radius_px * float(line_circle_gate_scale))
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

            candidates.append({
                "far_pt": (float(far_pt[0]), float(far_pt[1])),
                "hit_point": (float(hit_point[0]), float(hit_point[1])),
                "dir": (float(seg_dir_x), float(seg_dir_y)),
                "angle": float(axis_angle),
                "length": float(length),
                "line_dist": float(center_line_distance),
                "score": float(score),
            })

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
            item for item in candidates
            if self._axis_angle_delta_deg(item["angle"], dominant_angle) <= float(angle_tolerance_deg)
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
            float(avg_far_y + avg_dir_y * probe_distance_px)
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

    def _aspect_ok(self, corners, expected_aspect_ratio=None, tolerance=.4):
        if expected_aspect_ratio is None:
            return True
        c = self._order_corners(np.asarray(corners, np.float32))

        top = np.linalg.norm(c[1] - c[0])  # TR - TL
        bottom = np.linalg.norm(c[3] - c[2])  # BR - BL
        left = np.linalg.norm(c[2] - c[0])  # BL - TL
        right = np.linalg.norm(c[3] - c[1])  # BR - TR

        width = 0.5 * (top + bottom)
        height = 0.5 * (left + right)
        minimum_dimnesion = 1e-6
        if height <= minimum_dimnesion or width <= minimum_dimnesion:
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
        interations_for_kernel = [1, 2]
        operations_in_iteration = [cv2.MORPH_OPEN, cv2.MORPH_CLOSE]
        for morph in zip(operations_in_iteration, kernels, interations_for_kernel):
            mask = cv2.morphologyEx(mask, morph[0], np.ones((morph[1], morph[1]), np.uint8), iterations=morph[2])
        return cv2.GaussianBlur(mask, (kernel_two, kernel_two), 0)

    def detect_table(self, frame, hsv_bounds):
        hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
        mask = self._denoise_mask(cv2.inRange(hsv, hsv_bounds[0], hsv_bounds[1]), 3, 5)
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        if not contours:
            return (None, None, None)

        table_contour = max(contours, key=cv2.contourArea)
        x, y, w, h = cv2.boundingRect(table_contour)

        countour_perimeter = cv2.arcLength(table_contour, True)
        deviation_for_episilon = 0.02
        approximation = cv2.approxPolyDP(table_contour, deviation_for_episilon * countour_perimeter, True)
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

        return ((x, y, w, h), mask, corners)

    # -----------------------------
    # Markerless inner-cushion rectangle detection
    # -----------------------------
    @staticmethod
    def _line_angle_deg(x1, y1, x2, y2):
        return float(np.degrees(np.arctan2((y2 - y1), (x2 - x1))))

    @staticmethod
    def _intersect_lines(lineA, lineB):
        # line: (x1,y1,x2,y2)
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
        debug=False
    ):
        """
        Returns ordered TL,TR,BL,BR for inner cushion rectangle.
        Uses Hough lines inside ROI around cloth table area.
        Falls back to approx_table_corners_px if it cannot robustly compute.
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
            maxLineGap=int(max_line_gap)
        )

        if lines is None or len(lines) < 4:
            return approx_table_corners_px, edges

        # Convert lines into full-image coordinates
        candidates = []
        for L in lines.reshape(-1, 4):
            x1l, y1l, x2l, y2l = L
            x1l += rx0
            x2l += rx0
            y1l += ry0
            y2l += ry0
            ang = ObjectDetector._line_angle_deg(x1l, y1l, x2l, y2l)
            length = float(np.hypot((x2l - x1l), (y2l - y1l)))
            candidates.append((x1l, y1l, x2l, y2l, ang, length))

        # Cluster into near-horizontal and near-vertical
        horizontals = []
        verticals = []
        for x1l, y1l, x2l, y2l, ang, length in candidates:
            a = abs(ang)
            if a > 90:
                a = 180 - a
            if a <= 20:  # ~horizontal
                horizontals.append((x1l, y1l, x2l, y2l, length))
            elif a >= 70:  # ~vertical
                verticals.append((x1l, y1l, x2l, y2l, length))

        if len(horizontals) < 2 or len(verticals) < 2:
            return approx_table_corners_px, edges

        # Choose top/bottom horizontals by average y
        horizontals_sorted = sorted(horizontals, key=lambda t: 0.5 * (t[1] + t[3]))
        verticals_sorted = sorted(verticals, key=lambda t: 0.5 * (t[0] + t[2]))

        # Robust pick: take best-length among top-k and bottom-k
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

        # Intersections
        TL = ObjectDetector._intersect_lines(top_line[:4], left_line[:4])
        TR = ObjectDetector._intersect_lines(top_line[:4], right_line[:4])
        BL = ObjectDetector._intersect_lines(bot_line[:4], left_line[:4])
        BR = ObjectDetector._intersect_lines(bot_line[:4], right_line[:4])

        if TL is None or TR is None or BL is None or BR is None:
            return approx_table_corners_px, edges

        inner = np.array([TL, TR, BL, BR], dtype=np.float32)
        # Re-order using existing ordering logic to avoid flips
        # Note: _order_corners returns [TL,TR,BL,BR]
        inner_ordered = self._order_corners(inner)

        return inner_ordered, edges

    # -----------------------------
    # Homography helpers
    # -----------------------------
    @staticmethod
    def homography_mm_to_px(corners_px, table_length_mm, table_width_mm):
        TL, TR, BL, BR = corners_px.astype(np.float32)
        TLm = np.array([0.0, table_width_mm], np.float32)
        TRm = np.array([table_length_mm, table_width_mm], np.float32)
        BRm = np.array([table_length_mm, 0.0], np.float32)
        BLm = np.array([0.0, 0.0], np.float32)
        src = np.array([TLm, TRm, BRm, BLm], np.float32)
        dst = np.array([TL, TR, BR, BL], np.float32)  # fixed order
        H, _ = cv2.findHomography(src, dst, method=cv2.RANSAC)
        return H

    @staticmethod
    def homography_px_to_plane(corners_px, plane_length, plane_width):
        """
        Homography from image px -> canonical plane (plane_length x plane_width units).
        corners_px expected ordered TL,TR,BL,BR.
        """
        TL, TR, BL, BR = corners_px.astype(np.float32)
        dst = np.array([
            [0.0, plane_width],        # TL
            [plane_length, plane_width],  # TR
            [0.0, 0.0],                # BL
            [plane_length, 0.0]        # BR
        ], dtype=np.float32)
        src = np.array([TL, TR, BL, BR], dtype=np.float32)
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
        Hinverse = np.linalg.inv(H)
        out = []
        for (xpx, ypx) in points_px:
            v = np.array([float(xpx), float(ypx), 1.0], np.float32)
            q = Hinverse @ v
            mmx, mmy = (q[0] / q[2]), (q[1] / q[2])
            out.append((mmx / 1000.0, mmy / 1000.0))
        return out


    @staticmethod
    def warp_m_to_px(H, points_m):
        """Project points from table-plane meters (m) to image pixels (px).

        Args:
            H: 3x3 homography that maps table-plane millimeters (mm) -> image pixels (px).
            points_m: Iterable of (x_m, y_m) tuples in meters.

        Returns:
            List of (x_px, y_px) tuples as floats. If H is None, returns (None, None) for each point.
        """
        if H is None:
            return [(None, None) for _ in points_m]
        out = []
        for (xm, ym) in points_m:
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
    # Markerless pocket detection in rectified plane
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
        return (cx, cy)

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
        debug=False
    ):
        """
        Returns pockets_px ordered [TL,TR,BM,TM,BL,BR] in original image coordinates.

        Steps:
        1) Warp to canonical plane sized (L_mm x W_mm) => 1px == 1mm
        2) Build dark-region mask in plane
        3) Search 6 ROIs for centroids
        4) Map back to original using inverse homography
        """

        if corners_px_inner is None:
            return None, None, None

        # Warp image -> plane (1px/mm)
        Lp = int(round(float(playfield_L_mm)))
        Wp = int(round(float(playfield_W_mm)))
        if Lp <= 0 or Wp <= 0:
            return None, None, None

        H_img_to_plane = self.homography_px_to_plane(corners_px_inner, Lp, Wp)
        if H_img_to_plane is None:
            return None, None, None
        H_plane_to_img = np.linalg.inv(H_img_to_plane)

        plane = cv2.warpPerspective(frame_bgr, H_img_to_plane, (Lp, Wp), flags=cv2.INTER_LINEAR)

        # Dark pocket openings: low V, often low-ish S (but depends)
        hsv = cv2.cvtColor(plane, cv2.COLOR_BGR2HSV)
        Hc, Sc, Vc = cv2.split(hsv)

        dark = (Vc < int(v_thresh)).astype(np.uint8) * 255

        # reduce false positives from very saturated cloth noise
        if sat_max is not None:
            sat_ok = (Sc < int(sat_max)).astype(np.uint8) * 255
            dark = cv2.bitwise_and(dark, sat_ok)

        # Morph cleanup
        dark = cv2.morphologyEx(dark, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8), iterations=1)
        dark = cv2.morphologyEx(dark, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8), iterations=2)

        # Define ROIs in plane coords
        def crop_roi(x0, y0, x1, y1):
            x0i = max(0, int(x0)); y0i = max(0, int(y0))
            x1i = min(Lp, int(x1)); y1i = min(Wp, int(y1))
            roi_mask = np.zeros_like(dark)
            roi_mask[y0i:y1i, x0i:x1i] = dark[y0i:y1i, x0i:x1i]
            sub = dark[y0i:y1i, x0i:x1i]
            return (x0i, y0i, x1i, y1i, sub, roi_mask)

        cw = roi_frac_corner
        # Corner boxes
        TL_roi = crop_roi(0, Wp * (1 - cw), Lp * cw, Wp)
        TR_roi = crop_roi(Lp * (1 - cw), Wp * (1 - cw), Lp, Wp)
        BL_roi = crop_roi(0, 0, Lp * cw, Wp * cw)
        BR_roi = crop_roi(Lp * (1 - cw), 0, Lp, Wp * cw)

        # Side boxes (middle pockets)
        sw = roi_frac_side_w
        sh = roi_frac_side_h
        TM_roi = crop_roi(Lp * (0.5 - sw / 2.0), Wp * (1 - sh), Lp * (0.5 + sw / 2.0), Wp)
        BM_roi = crop_roi(Lp * (0.5 - sw / 2.0), 0, Lp * (0.5 + sw / 2.0), Wp * sh)

        # Find centroids within each ROI
        def centroid_in_roi(roi):
            x0, y0, x1, y1, sub, _ = roi
            c = self._find_dark_centroid(sub, min_area=min_area_px)
            if c is None:
                return None
            cx, cy = c
            return (float(x0) + float(cx), float(y0) + float(cy))

        TLp = centroid_in_roi(TL_roi)
        TRp = centroid_in_roi(TR_roi)
        BLp = centroid_in_roi(BL_roi)
        BRp = centroid_in_roi(BR_roi)
        TMp = centroid_in_roi(TM_roi)
        BMp = centroid_in_roi(BM_roi)

        pockets_plane = {
            "TL": TLp,
            "TR": TRp,
            "TM": TMp,
            "BM": BMp,
            "BL": BLp,
            "BR": BRp,
        }

        # If something fails, keep None (caller can fallback) – but usually you’ll get all 6.
        def plane_to_img(pt):
            if pt is None:
                return None
            x, y = pt
            v = np.array([x, y, 1.0], np.float32)
            q = H_plane_to_img @ v
            return (float(q[0] / q[2]), float(q[1] / q[2]))

        pockets_px = {
            k: plane_to_img(v) for k, v in pockets_plane.items()
        }

        # Output order must match your labels: ["TL","TR","BM","TM","BL","BR"]
        ordered = [
            pockets_px["TL"],
            pockets_px["TR"],
            pockets_px["BM"],
            pockets_px["TM"],
            pockets_px["BL"],
            pockets_px["BR"],
        ]

        dbg = {
            "plane": plane if debug else None,
            "dark": dark if debug else None,
            "pockets_plane": pockets_plane,
            "H_img_to_plane": H_img_to_plane,
        } if debug else None

        return ordered, pockets_plane, dbg

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
            maxRadius=max_ball_radius
        )
        if circles is not None:
            return np.uint16(np.around(circles[0]))
        return []

    @staticmethod
    def classify_balls(frame, circle, white_treshold, eightball_treshold, stripe_white_ratio):
        x, y, r = int(circle[0]), int(circle[1]), int(circle[2])
        roi = frame[y - r:y + r, x - r:x + r]
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
    # Balls (YOLO)
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
            device=self.device
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
            entries.append({
                "type": t,
                "x": xm,
                "y": ym,
                "number": self.label_map[BallType.UNKNOWN.value][1] if t in (BallType.SOLID.value, BallType.STRIPE.value)
                else (self.label_map[BallType.CUE.value][1]
                      if t == BallType.CUE.value
                      else self.label_map[BallType.EIGHT.value][1]),
                "confidence": float(conf),
                "vx": None,
                "vy": None,
            })

        if self.debug:
            print(entries)
        return entries

    def classify_balls_yolo():
        pass
    
    
    @staticmethod
    def is_pocketed(x_m: float, y_m:float, pockets_xy_m, pocketed_dist_m ):
        if x_m is None or y_m is None:
            return True
        for(pxm, pym) in pockets_xy_m:
            if pym is None or pxm is None:
                continue
            dx = float(x_m) - float(pxm)
            dy = float(y_m) - float(pym)
            if(((dx * dx)+ (dy * dy))**0.5  <= pocketed_dist_m):
                return True
        return False    
        
    @staticmethod
    def is_in_playfield_bounds_xy(xm: float, ym: float, table_LW_m, ball_diamter_m:float) -> bool:
        if xm is None or ym is None:
            return False
        L = float(table_LW_m[0])
        W = float(table_LW_m[1])
        r = float(ball_diamter_m) * .5
        return (0.0 + r) <= float(xm) <= (L - r) and (0.0 + r) <= float(ym) <= (W - r)

    @staticmethod
    def is_in_table_bounds(x_m: float, y_m: float, pockets_xy_m, ball_diameter_m = 0.05715) -> bool:
        # Axis-aligned bounds from pocket locations (good enough for now).
        xs = [p[0] for p in pockets_xy_m if (p is not None and p[0] is not None)]
        ys = [p[1] for p in pockets_xy_m if (p is not None and p[1] is not None)]
        if not xs or not ys:
            return True
        margin = float(ball_diameter_m) * 0.6
        return (min(xs) - margin) <= float(x_m) <= (max(xs) + margin) and (min(ys) - margin) <= float(y_m) <= (max(ys) + margin)
    
    @staticmethod
    def nearest_yolo_type(cx: float, cy: float, yolo_centers, match_distance_px:float):
        best = None
        best_d2 = (match_distance_px*match_distance_px)
        for (x,y, t ,conf) in yolo_centers:
            dx = x - cx
            dy = y - cy
            d2 = (dx * dx) + (dy * dy)
            if d2 <= best_d2:
                best_d2 = d2
                best = (t, conf)
        return best
    
    
    