import numpy as np
from typing import Iterable, List, Tuple, Optional, Dict
from ball_type import BallType

LABEL_MAP = {
    BallType.EIGHT.value:   ("e", "8"),
    BallType.CUE.value:      ("c",   "/"),
    BallType.STRIPE.value:  ("st",    "u"),
    BallType.SOLID.value:    ("so",    "u"),
    BallType.UNKNOWN.value:  ("na",    "u"),
}   

# helpers
def _f32(x: float) -> float: return float(np.float32(x))

def _round_or_none(value, decimals: int):
    if value is None:
        return None
    return round(float(value), decimals)

def _fmt_float(value: float, decimals: int) -> str: 
    return f"{_f32(value):.{decimals}f}"

def _fmt2(x, y, decimals: int = 4):
    return f"{_fmt_float(x, decimals)},{_fmt_float(y, decimals)}"

def _fmt_num_or_backslash(v, decimals: Optional[int] = None):
    if v is None:
        return "\\"
    if decimals is None:
        return str(v)
    return _fmt_float(float(v), decimals)

def line_pockets(pockets_xy, decimals: int = 7):
    if pockets_xy is None:
        return ""
    return "p " + ";".join(_fmt2(x, y, decimals) for (x, y) in pockets_xy)

def line_configuration_name(configuration_name: str):
    return "E " + configuration_name

def line_cue_stick(cue_data, pos_decimals=4, dir_decimals=4, conf_decimals=2):
    line_x, line_y = cue_data["line_point_m"]
    dir_x, dir_y = cue_data["direction_m"]
    hit_x, hit_y = cue_data["hit_point_m"]
    confidence = float(cue_data["confidence"])

    return (
        f"s "
        f"{line_x:.{pos_decimals}f},{line_y:.{pos_decimals}f};"
        f"{dir_x:.{dir_decimals}f},{dir_y:.{dir_decimals}f};"
        f"{hit_x:.{pos_decimals}f},{hit_y:.{pos_decimals}f};"
        f"{confidence:.{conf_decimals}f}"
    )

def group_entries_by_type(entries):
    groups = {}
    for entry in entries:
        groups.setdefault(entry["type"], []).append((float(entry["x"]), float(entry["y"])))
    for key in groups.keys():
        groups[key].sort(key=lambda p: (p[0], p[1]))
    return groups

def normalize_detection_entries(
    entries_px: List[Dict],
    pos_decimals: int = 4,
    conf_decimals: int = 3,
    keep_velocity: bool = False,
    vel_decimals: int = 3
) -> List[Dict]:
    normalized = []

    for ball in entries_px or []:
        t = ball.get("type")
        x = _round_or_none(ball.get("x", 0.0), pos_decimals)
        y = _round_or_none(ball.get("y", 0.0), pos_decimals)

        if t == BallType.EIGHT.value:
            num = LABEL_MAP[BallType.EIGHT.value][1]
        elif t == BallType.CUE.value:
            num = LABEL_MAP[BallType.CUE.value][1]
        else:
            num = ball.get("number", LABEL_MAP[BallType.UNKNOWN.value][1])

        item = {
            "type": t,
            "x": x,
            "y": y,
            "number": num,
            "conf": _round_or_none(ball.get("conf", None), conf_decimals),
            "vx": None,
            "vy": None,
        }

        if keep_velocity:
            item["vx"] = _round_or_none(ball.get("vx", None), vel_decimals)
            item["vy"] = _round_or_none(ball.get("vy", None), vel_decimals)

        normalized.append(item)

    return normalized

def build_detection_signature(
    entries_px: List[Dict],
    pos_decimals: int = 4,
    conf_decimals: int = 3
) -> Tuple:
    normalized = normalize_detection_entries(
        entries_px=entries_px,
        pos_decimals=pos_decimals,
        conf_decimals=conf_decimals,
        keep_velocity=False
    )

    signature = []
    for ball in normalized:
        signature.append((
            ball.get("type"),
            ball.get("x"),
            ball.get("y"),
            ball.get("number"),
            ball.get("conf"),
        ))
    return tuple(signature)



def _serialize_all_balls(
    entries_px: List[Dict],
    discard_diamonds: bool = True,
    pos_decimals: int = 7,
    conf_decimals: int = 7,
    vel_decimals: int = 7
) -> List[str]:
    eight_parts, cue_parts, st_parts, so_parts, edge_d_parts = [], [], [], [], []

    for ball in entries_px or []:
        t = ball.get("type")
        x = float(ball.get("x", 0.0))
        y = float(ball.get("y", 0.0))

        if t == BallType.EIGHT.value:
            num = LABEL_MAP[BallType.EIGHT.value][1]
        elif t == BallType.CUE.value:
            num = LABEL_MAP[BallType.CUE.value][1]
        else:
            num = ball.get("number", LABEL_MAP[BallType.UNKNOWN.value][1])

        conf = _fmt_num_or_backslash(ball.get("conf", None), conf_decimals)
        vx = _fmt_num_or_backslash(ball.get("vx", None), vel_decimals)
        vy = _fmt_num_or_backslash(ball.get("vy", None), vel_decimals)

        token = f"{_fmt_float(x, pos_decimals)},{_fmt_float(y, pos_decimals)},{num},{conf},{vx},{vy}"

        if t == BallType.EIGHT.value:
            eight_parts.append(token)
        elif t == BallType.CUE.value:
            cue_parts.append(token)
        elif t == BallType.STRIPE.value:
            st_parts.append(token)
        elif t == BallType.SOLID.value:
            so_parts.append(token)
        elif t == BallType.UNKNOWN.value:
            if not discard_diamonds:
                edge_d_parts.append(token)

    zero_xy = f"{_fmt_float(0.0, pos_decimals)},{_fmt_float(0.0, pos_decimals)}"
    eight_line = f"{BallType.EIGHT.value} " + (
        eight_parts[0] if eight_parts else f"{zero_xy},{LABEL_MAP[BallType.EIGHT.value][1]},\\,\\,\\"
    )
    cue_line = f"{BallType.CUE.value} " + (
        cue_parts[0] if cue_parts else f"{zero_xy},{LABEL_MAP[BallType.CUE.value][1]},\\,\\,\\"
    )
    st_line = f"{BallType.STRIPE.value} " + "; ".join(st_parts)
    so_line = f"{BallType.SOLID.value} " + "; ".join(so_parts)
    edge_d_line = ("d " + "; ".join(edge_d_parts)) if (not discard_diamonds and edge_d_parts) else ""

    return [eight_line, cue_line, st_line, so_line, edge_d_line]


def line_diamonds(diamond_entries: List[Dict], discard_diamonds: bool = True, pos_decimals: int = 7, conf_decimals: int = 4) -> str:
    if discard_diamonds:
        return ""
    parts = []
    for item in diamond_entries or []:
        x = float(item["x"])
        y = float(item["y"])
        idx = int(item["index"])
        conf = float(item.get("conf", 0.0))
        parts.append(f"{_fmt_float(x, pos_decimals)},{_fmt_float(y, pos_decimals)},{idx},{_fmt_float(conf, conf_decimals)}")
    return "d " + "; ".join(parts)

def p2p_classification_to_balltype(ball_id: int) -> str:
    if ball_id == 0:
        return BallType.STRIPE.value
    elif ball_id == 1: 
        return BallType.SOLID.value
    elif ball_id == 2: 
        return BallType.CUE.value
    elif ball_id == 3: 
        return BallType.EIGHT.value
    return BallType.UNKNOWN.value

def build_conf_transfer_block(
    pockets=None,
    table_LW_m=None,
    ball_diameter_m=0.05715,
    camera_height_m=2.5,
    detection_entries: List[Dict] = None,
    discard_diamonds: bool = True,
    pos_decimals: int = 7,
    conf_decimals: int = 7,
    vel_decimals: int = 7
):
    ball_lines = _serialize_all_balls(
        detection_entries,
        discard_diamonds=discard_diamonds,
        pos_decimals=pos_decimals,
        conf_decimals=conf_decimals,
        vel_decimals=vel_decimals
    )
    lines = [line_pockets(pockets, decimals=pos_decimals)]
    for f in ball_lines:
        if f:
            lines.append(f)
    return "\n".join(lines) + "\n"