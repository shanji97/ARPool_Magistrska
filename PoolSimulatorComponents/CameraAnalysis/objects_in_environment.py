from dataclasses import dataclass, asdict
from typing import Dict, Tuple, List, Optional
import os
import json

@dataclass
class TableSpec:
    name: str
    playfield_mm: Tuple[int, int]
    overall_mm: Optional[Tuple[int, int]] = None
    notes: str = ""
    
@dataclass
class PocketSpec:
    corner_pocket_mm: int
    side_pocket_mm: int     # Side pockets are typically wider than corne ones
    corner_jaw_radius: Optional[int] = None 
    side_jaw_radius: Optional[int] = None 
    # Meaning: Radius of curvature of the jaws (the cushion cut-outs that form the pocket).
    # Why it matters: A larger radius makes the pocket “accept” more angled shots, while a sharper (small radius) cut is unforgiving.
    # Kept optional so you can later refine geometry if you want to model rebounds realistically.
    
@dataclass
class BallSpec:
    diameter_mm: float = 57.15
    
@dataclass
class CameraSpec:
    height_from_floor_mm: float
    
@dataclass
class EnvironmentConfig:
    table: TableSpec
    pockets: PocketSpec
    ball_spec: BallSpec
    camera: CameraSpec
    
    def table_uv_to_mm(self, u: float, v: float) -> Tuple[float, float]:
        """
        Convert normalized playfield coordinates (u,v) in [0..1] to mm.
        Convention:
          - (0,0) = near-left cushion nose corner
          - u increases along table length (long rail direction)
          - v increases along table width (short rail direction)
        """
        L, W = self.table.playfield_mm
        return (u * L, v * W)
    
    def pocket_uv_positions(self) -> Dict[str, Tuple[float, float]]:
        """
        Returns idealized pocket centers in normalized table coordinates.
        Corners: (0,0), (1,0), (0,1), (1,1) ; Middles: (0.5,0) and (0.5,1)
        """
        return {
            "corner_BL": (0.0, 0.0),
            "corner_BR": (1.0, 0.0),
            "corner_TL": (0.0, 1.0),
            "corner_TR": (1.0, 1.0),
            "side_B": (0.5, 0.0),
            "side_T": (0.5, 1.0)
        }

# Playfield (nose-to-nose) + overall (full cabinet) sizes
PRESET_TABLES: List[TableSpec] = [
    TableSpec("7ft (bar box)",
            playfield_mm=(1930, 965),
            overall_mm=(2133, 1120),  # ~7′×3.7′ cabinet
            notes="Common 7ft bar table"),
    TableSpec("8ft (home)",
            playfield_mm=(2235, 1118),
            overall_mm=(2438, 1219),  # overall sizes (my measurements)
            notes="Typical 8ft home table"),
    TableSpec("8.5ft (pro-8)",
            playfield_mm=(2340, 1170),
            overall_mm=None,
            notes="Pro-8, no standard cabinet ref"),
    TableSpec("9ft (tournament)",
            playfield_mm=(2540, 1270),
            overall_mm=(2743, 1372),  #  9′×4.5′
            notes="WPA tournament size"),
    TableSpec("10ft (snooker)",
            playfield_mm=(2845, 1422),
            overall_mm=(3048, 1524),  # 10′×5′
            notes="10ft snooker/billiards"),
]

PRESET_POCKETS = [
    ("Pool (typical relaxed)", PocketSpec(120, 135)),
    ("Pool (tighter)",         PocketSpec(110, 125)),
    ("Chinese 8-ball (tight)", PocketSpec(105, 120)),
]

ENVIRONMENT_JSON = "./Configuration/last_environment.json"

DEFAULT_BALLS = BallSpec(diameter_mm=57.15)

def _ensure_dir(path: str):
    directory = os.path.dirname(path)
    if directory and not os.path.exists(directory):
        os.makedirs(directory, exist_ok=True)
        
def save_environment(environment: EnvironmentConfig, path: str) -> None:
    _ensure_dir(path)
    payload = {
        "table": asdict(environment.table),
        "pockets": asdict(environment.pockets),
        "ball_spec": asdict(environment.ball_spec),
        "camera": asdict(environment.camera)
    }
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
    
def load_last_environment(path: str) -> Optional[EnvironmentConfig]:
    # Sharable data between project modules (for testing and other purposes..., but also to make the setup faster)
    if not os.path.exists(path):
        return None

    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    table = TableSpec(**data["table"])
    pockets = PocketSpec(**data["pockets"])
    balls = BallSpec(**data["ball_spec"])
    camera = CameraSpec(**data["camera"])
    return EnvironmentConfig(table, pockets, balls, camera)

def _print_table_menu():
    print("\n Select TABLE size:")
    for index, specification in enumerate(PRESET_TABLES, start = 1):
        play_field = f"{specification.playfield_mm[0]}×{specification.playfield_mm[1]} mm"
        ov = f" / overall {specification.overall_mm[0]}×{specification.overall_mm[1]} mm" if specification.overall_mm else ""
        print(f" {index}. {specification.name} - playfield {play_field}{ov} ({specification.notes})")
    print(" c. Custom (enter manually)")

def _print_pocket_menu():
    print("\nSelect POCKET profile:")
    for idx, (name, p) in enumerate(PRESET_POCKETS, start=1):
        print(f"  {idx}. {name} — corner {p.corner_pocket_mm} mm / side {p.side_pocket_mm} mm")
    print("  c. Custom (enter manually)")

def _read_choice(valid_choices: List[str]) -> str:
    while True:
        choice = input("> ").strip().lower()
        if choice in valid_choices:
            return choice
        print(f"Please choose one of {valid_choices}.")

def _read_int(prompt: str, min_v: int, max_v: int, default: Optional[int]=None) -> int:
    while True:
        raw = input(f"{prompt} [{min_v}..{max_v}]{' (default '+str(default)+')' if default else ''}: ").strip()
        if not raw and default is not None:
            return default
        try: 
            val = int(float(raw)) 
            if min_v <= val <= max_v:
                return val
        except ValueError:
            pass
        print("Invalid value")

def _read_float(prompt: str, min_v: float, max_v: float, default: Optional[float]=None) -> float:
    while True:
        raw = input(f"{prompt} [{min_v}..{max_v}]{' (default '+str(default)+')' if default else ''}: ").strip()
        if not raw and default is not None:
            return default
        try:
            val = float(raw)
            if min_v <= val <= max_v:
                return val
        except ValueError:
            pass
        print("Invalid value.")

def set_up_table():
    _print_table_menu()
    choice = _read_choice([str(i) for i in range(1, len(PRESET_TABLES)+1)] + ["c"])
    if choice == "c":
        Lpf = _read_int("Playfield length (mm)", 1500, 3200, 2540)
        Wpf = _read_int("Playfield width (mm)", 700, 1800, 1270)
        use_overall = input("Do you know overall cabinet size? (y/n): ").strip().lower()
        overall = None
        
        if use_overall == "y":
            Lov = _read_int("Overall length (mm)", 2000, 3300, None)
            Wov = _read_int("Overall width (mm)", 1000, 1800, None)
            overall = (Lov, Wov)
            return TableSpec("Custom", (Lpf, Wpf), overall, "User-defined")
    else:
        return PRESET_TABLES[int(choice) - 1]

def set_up_pockets():
    _print_pocket_menu()
    choice = _read_choice([str(i) for i in range(1, len(PRESET_POCKETS)+1)] + ["c"])
    if choice == "c":
        corner = _read_int("Corner pocket mouth (mm)", 95, 160, 120)
        side   = _read_int("Side pocket mouth (mm)", 105, 180, 135)
        return PocketSpec(corner_pocket_mm=corner, side_pocket_mm=side)
    else:
        return PRESET_POCKETS[int(choice)-1][1]
    
def set_up_camera_height_mm():
    print("\nEnter camera height from FLOOR (m), typical 2–3 m:") # The camera sensor is assumed to be on the XY center of the table. Only Z is in question.
    return _read_float("Camera height (m)", 1.5, 4.0, 2.5) * 1000

def get_environment_config(interactive: bool = True,
                           use_last_known: bool = True,
                           cache_path: str = ENVIRONMENT_JSON) -> EnvironmentConfig:
    
    if use_last_known:
        last_known_environment = load_last_environment(cache_path)
        if last_known_environment is not None:
            return last_known_environment
        
    table = None
    pockets = None
    camera_height_mm = None
    if interactive:
       table = set_up_table()
       pockets = set_up_pockets()
       camera_height_mm = set_up_camera_height_mm()
    else:
        table = PRESET_TABLES[3] # 9ft Tournament table
        pockets =  PRESET_POCKETS[0][1]
        camera_height_mm = 2500 # 2.5m
    env = EnvironmentConfig(   table,
                                pockets,
                                DEFAULT_BALLS,
                                CameraSpec(camera_height_mm))
    save_environment(env, cache_path)
    
    return env