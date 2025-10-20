import numpy as np
from typing import Iterable, List, Tuple, Optional, Dict
from ball_type import BallType

LABEL_MAP = {
    BallType.EIGHT.value:   ("e", "8"),
    BallType.CUE.value:      ("c",   "/"),
    "striped":  ("st",    "u"),
    "solid":    ("so",    "u"),
    BallType.UNKNOWN.value:  ("na",    "u"),
}   

# helpers
def _f32(x: float) -> float: return float(np.float32(x))

def _fmt2(x, y): return f"{_f32(x):.7f},{_f32(y):.7f}"

def _fmt_num_or_backslash(v):
    return "\\" if v is None else str(v)

def line_pockets(pockets_xy):
    return "p " + ";".join(_fmt2(x, y) for (x, y) in pockets_xy)

def line_table(LWH_m, ball_diameter_m=0.05715, camera_height_m=None):
    L, W, H = LWH_m
    s = f"L={_f32(L):.7f}; W={_f32(W):.7f}; H={_f32(H):.7f}; B={_f32(ball_diameter_m):.7f}"
    if camera_height_m is not None:
        s += f"; C={_f32(camera_height_m):.7f}"
    return "t " + s

def _serialize_all_balls(entries_px: List[Dict]) -> List[str]:
    # four independent lists (fixes unpack bug)
    eight_parts, cue_parts, st_parts, so_parts = [], [], [], []

    for ball in entries_px or []:
        t   = ball.get("type")
        x   = float(round(ball.get("x", 0.0)))
        y   = float(round(ball.get("y", 0.0)))

        # assign number based on type if not provided
        if t == BallType.EIGHT.value:
            num = LABEL_MAP[BallType.EIGHT.value][1]
        elif t == BallType.CUE.value:
            num = LABEL_MAP[BallType.CUE.value][1]
        else:
            num = ball.get("number",  LABEL_MAP[BallType.UNKNOWN.value][1])

        conf = _fmt_num_or_backslash(ball.get("confidence", None))
        vx   = _fmt_num_or_backslash(ball.get("vx", None))
        vy   =  _fmt_num_or_backslash(ball.get("vy", None))

        token = f"{_f32(x):.7f},{_f32(y):.7f},{num},{conf},{vx},{vy}"

        if t == BallType.EIGHT.value:
            eight_parts.append(token)
        elif t == BallType.CUE.value:
            cue_parts.append(token)
        elif t == BallType.STRIPE.value:
            st_parts.append(token)
        elif t == BallType.SOLID.value:
            so_parts.append(token)

    # correct default cue backslash
    eight_line = f"{BallType.EIGHT.value} " + (eight_parts[0] if eight_parts else f"0.0000000,0.0000000,{LABEL_MAP[BallType.EIGHT.value][1]},\\,\\,\\")
    cue_line   = f"{BallType.CUE.value} "   + (cue_parts[0]   if cue_parts   else f"0.0000000,0.0000000,{LABEL_MAP[BallType.CUE.value][1]},\\,\\,\\")
    st_line    = f"{BallType.STRIPE.value} " + "; ".join(st_parts)
    so_line    = f"{BallType.SOLID.value} "  + "; ".join(so_parts)
    return [eight_line, cue_line, st_line, so_line]
        
def build_transfer_block(
    pockets,
    table_LW_m,
    ball_diameter_m=0.05715,
    camera_height_m=2.5,
    detection_entries: List[Dict] = None):
    ball_lines = _serialize_all_balls(detection_entries)
    lines = [line_pockets(pockets)]
    for f in (ball_lines):
        if f: lines.append(f)
    lines.append(line_table(table_LW_m, ball_diameter_m, camera_height_m))
    return "\n".join(lines) + "\n\n"