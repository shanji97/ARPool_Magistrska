import numpy as np
from typing import Iterable, List, Tuple, Optional

Float2 = Tuple[float, float]
Float3 = Tuple[float, float, float]
Ball3  = Tuple[float, float, float, int]  # (x,y,z,number)

def _f32(x: float) -> float: return float(np.float32(x))
def _fmt2(x, y): return f"{_f32(x):.7f},{_f32(y):.7f}"
def _fmt3(x, y, z): return f"{_f32(x):.7f},{_f32(y):.7f},{_f32(z):.7f}"
def _fmt_ball3(x, y, z, n: int = -1): return f"{_f32(x):.7f},{_f32(y):.7f},{_f32(z):.7f},{int(n)}"

def line_pockets(pockets_xy: Iterable[Float2]) -> str:
    return "p " + ";".join(_fmt2(x,y) for (x,y) in pockets_xy)

def line_cue_ball(cue: Optional[Float3]): 
    return None if cue is None else "c " + _fmt3(*cue)

def line_eight(eight: Optional[Float3]): 
    return None if eight is None else "e " + _fmt3(*eight)

def line_solids(solids: Iterable[Ball3]):
    solids = list(solids); 
    return None if not solids else "so " + ";".join(_fmt_ball3(*b) for b in solids)

def line_stripes(stripes: Iterable[Ball3]):
    stripes = list(stripes)
    return None if not stripes else "st " + ";".join(_fmt_ball3(*b) for b in stripes)

def line_table(LW_m: Tuple[float,float], y: float) -> str:
    return "ts " + _fmt3(LW_m[0], LW_m[1], y)

def build_transfer_block(pockets: List[Float2],
                         table_LW_m: Tuple[float,float],
                         table_y: float,
                         cue=None, eight=None,
                         solids=None, stripes=None) -> str:
    lines = [line_pockets(pockets)]
    for f in (line_cue_ball(cue), line_eight(eight),
              line_solids(solids or []), line_stripes(stripes or [])):
        if f: lines.append(f)
    lines.append(line_table(table_LW_m, table_y))
    return "\n".join(lines) + "\n\n"