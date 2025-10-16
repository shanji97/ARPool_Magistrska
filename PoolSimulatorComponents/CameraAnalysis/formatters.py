import numpy as np
from typing import Iterable, List, Tuple, Optional

Float2 = Tuple[float, float]
Float3 = Tuple[float, float, float]
Ball3  = Tuple[float, float, float, int]  # (x,y,z,number)

# helpers
def _f32(x: float) -> float: return float(np.float32(x))
def _fmt2(x, y): return f"{_f32(x):.7f},{_f32(y):.7f}"
def _fmt3(x, y, z): return f"{_f32(x):.7f},{_f32(y):.7f},{_f32(z):.7f}"
def _fmt_ball3(x, y, z, n: int = -1): return f"{_f32(x):.7f},{_f32(y):.7f},{_f32(z):.7f},{int(n)}"

def line_pockets(pockets_xy):
    return "p " + ";".join(_fmt2(x, y) for (x, y) in pockets_xy)

def line_cue_ball(cue):   return None if cue   is None else "c "  + _fmt3(*cue)
def line_eight(eight):    return None if eight is None else "e "  + _fmt3(*eight)

def line_solids(solids):
    solids = list(solids or [])
    return None if not solids else "so " + ";".join(_fmt_ball3(*b) for b in solids)

def line_stripes(stripes):
    stripes = list(stripes or [])
    return None if not stripes else "st " + ";".join(_fmt_ball3(*b) for b in stripes)

def line_table(LWH_m, ball_diameter_m=0.05715, camera_height_m=None):
    L, W, H = LWH_m
    s = f"L={_f32(L):.7f}; W={_f32(W):.7f}; H={_f32(H):.7f}; B={_f32(ball_diameter_m):.7f}"
    if camera_height_m is not None:
        s += f"; C={_f32(camera_height_m):.7f}"
    return "t " + s

def build_transfer_block(
    pockets,
    table_LW_m,
    ball_diameter_m=0.05715,
    camera_height_m=None,
    cue=None, eight=None,
    solids=None, stripes=None,
):
    lines = [line_pockets(pockets)]
    for f in (line_cue_ball(cue),
              line_eight(eight),
              line_solids(solids),
              line_stripes(stripes)):
        if f: lines.append(f)
    lines.append(line_table(table_LW_m, ball_diameter_m, camera_height_m))
    return "\n".join(lines) + "\n\n"