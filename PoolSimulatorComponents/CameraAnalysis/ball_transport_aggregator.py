from formatters import (
    normalize_detection_entries,
    build_detection_signature,
)

class BallTransportAggregator:
    def __init__(
        self,
        batch_size_frames: int = 3,
        pos_decimals: int = 4,
        conf_decimals: int = 3,
        vel_decimals: int = 3,
        reset_max_position_delta_m: float = 0.08,
        force_send_interval_sec: float = 0.25
    ):
        self.batch_size_frames = max(1, int(batch_size_frames))
        self.pos_decimals = pos_decimals
        self.conf_decimals = conf_decimals
        self.vel_decimals = vel_decimals
        self.reset_max_position_delta_m = float(reset_max_position_delta_m)
        self.force_send_interval_sec = float(force_send_interval_sec)

        self._buffer = []
        self._last_sent_signature = None
        self._last_send_time = 0.0

    def reset(self):
        self._buffer.clear()

    def _type_sequence(self, entries):
        return tuple(item.get("type") for item in (entries or []))

    def _should_reset_for_structure_change(self, entries):
        if not self._buffer:
            return False

        prev = self._buffer[-1]

        if len(prev) != len(entries):
            return True

        if self._type_sequence(prev) != self._type_sequence(entries):
            return True

        for prev_item, curr_item in zip(prev, entries):
            dx = float(curr_item["x"]) - float(prev_item["x"])
            dy = float(curr_item["y"]) - float(prev_item["y"])
            dist = (dx * dx + dy * dy) ** 0.5
            if dist > self.reset_max_position_delta_m:
                return True

        return False

    def _average_entries(self):
        if not self._buffer:
            return []

        count = len(self._buffer)
        first = self._buffer[0]
        averaged = []

        for i in range(len(first)):
            xs = []
            ys = []
            confs = []

            for frame_entries in self._buffer:
                xs.append(float(frame_entries[i]["x"]))
                ys.append(float(frame_entries[i]["y"]))
                conf_value = frame_entries[i].get("conf", None)
                if conf_value is not None:
                    confs.append(float(conf_value))

            averaged.append({
                "type": first[i]["type"],
                "number": first[i].get("number", None),
                "x": sum(xs) / count,
                "y": sum(ys) / count,
                "conf": (sum(confs) / len(confs)) if confs else None,
                "vx": None,
                "vy": None,
            })

        return averaged

    def push(self, entries, now_sec: float):
        if not entries:
            self.reset()
            return None
        
        structure_changed = self._should_reset_for_structure_change(entries)
        if structure_changed:
            self.reset()
            self._buffer.append(entries)
            return None

        self._buffer.append(entries)
        
        if len(self._buffer) < self.batch_size_frames:
            if (now_sec - self._last_send_time) < self.force_send_interval_sec:
                return None

        averaged_entries = self._average_entries()
        normalized_entries = normalize_detection_entries(
            entries_px=averaged_entries,
            pos_decimals=self.pos_decimals,
            conf_decimals=self.conf_decimals,
            keep_velocity=False,
            vel_decimals=self.vel_decimals
        )

        signature = build_detection_signature(
            entries_px=normalized_entries,
            pos_decimals=self.pos_decimals,
            conf_decimals=self.conf_decimals
        )

        should_force_send = (now_sec - self._last_send_time) >= self.force_send_interval_sec
        batch_ready = len(self._buffer) >= self.batch_size_frames

        if not batch_ready and not should_force_send:
            return None

        self.reset()

        if signature == self._last_sent_signature:
            return None

        self._last_sent_signature = signature
        self._last_send_time = now_sec
        return normalized_entries