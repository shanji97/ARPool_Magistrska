from __future__ import annotations
from dataclasses import dataclass, asdict
from typing import Dict, Tuple, List, Optional, ClassVar
import os
import json

@dataclass
class TableSpec:
    name: str
    playfield_mm: Tuple[float, float, float]
    overall_mm: Optional[Tuple[int, int]] = None
    notes: str = ""
    cloth_profile: Optional[str] = None
    cloth_lower_hsv: Optional[Tuple[int, int, int]] = None
    cloth_upper_hsv: Optional[Tuple[int, int, int]] = None
    
    def pocket_mm_positions(self, corner_inset_mm: float, side_inset_mm: float):
        L, W, H = self.playfield_mm
        
        # Corners in mm (origin BL; +X length, +Y width)
        TL = (0.0, W)
        TR = (L, W)
        BL = (0.0, 0.0)
        BR = (L, 0.0)
        # Apply insets along both axes
        P_TL = (TL[0] + corner_inset_mm, TL[1] - corner_inset_mm)
        P_TR = (TR[0] - corner_inset_mm, TR[1] - corner_inset_mm)
        P_BL = (BL[0] + corner_inset_mm, BL[1] + corner_inset_mm)
        P_BR = (BR[0] - corner_inset_mm, BR[1] + corner_inset_mm)
        
        # Side pockets @ 1/2 of the longer rail, inset inward from bottom/top.
        P_ML = (0.5 * L, side_inset_mm)           # bottom (y≈0) inward +Y
        P_MR = (0.5 * L, W - side_inset_mm)       # top    (y≈W) inward -Y
        
        return [P_TL, P_TR,  
                P_ML, P_MR,
                P_BL, P_BR]
    
@dataclass
class PocketSpec:
    corner_pocket_diameter_mm: int   # Corner pocket inset is basically radius - so diameter / 2
    side_pocket_diameter_mm: int     # Side pockets are typically wider than corne ones
    corner_jaw_diameter_mm: Optional[int] = None 
    side_jaw_diameters_mm: Optional[int] = None 
    # Meaning: Radius of curvature of the jaws (the cushion cut-outs that form the pocket).
    # Why it matters: A larger radius makes the pocket “accept” more angled shots, while a sharper (small radius) cut is unforgiving.
    # Kept optional so you can later refine geometry if you want to model rebounds realistically.

    def derive_insets(self):
          # Use jaw curvature radius if available (CORRECT)
        if self.corner_jaw_diameter_mm is not None:
            corner_inset = self.corner_jaw_diameter_mm * 0.5
        else:
            # fallback for legacy configs
            corner_inset = self.corner_pocket_diameter_mm * 0.5
        
        if self.side_jaw_diameters_mm is not None:
            side_inset = self.side_jaw_diameters_mm * 0.5
        else:
            side_inset = self.side_pocket_diameter_mm * 0.5
        
        return float(corner_inset), float(side_inset)
    
@dataclass
class BallSpec:
    diameter_m: float = .05715
    ball_circumference_m: float = 0.068
    
    
@dataclass
class CameraSpec:
    height_from_floor_m: float
    
@dataclass
class EnvironmentConfig:
    table: TableSpec
    pockets: PocketSpec
    ball_spec: BallSpec
    camera: CameraSpec
    
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
    PRESET_TABLES: ClassVar[List[TableSpec]] = [
    TableSpec("7ft (bar box)",
        playfield_mm=(1930, 965, 785),
        overall_mm=(2133, 1120),
        notes="Common 7ft bar table"),
    TableSpec("8ft (home)",
        playfield_mm=(2235, 1118, 785),
        overall_mm=(2438, 1219),
        notes="Typical 8ft home table"),
    TableSpec("8.5ft (pro-8)",
        playfield_mm=(2340, 1170, 785),
        overall_mm=(2543, 1272),
        notes="Pro-8, no standard cabinet ref"),
    TableSpec("9ft (tournament)",
        playfield_mm=(2540, 1270, 785),
        overall_mm=(2743, 1372),
        notes="WPA tournament size"),
    TableSpec("10ft (snooker)",
        playfield_mm=(2845, 1422, 785),
        overall_mm=(3048, 1524),
        notes="10ft snooker/billiards"),
    ]

    PRESET_POCKETS: ClassVar[List[tuple[str, PocketSpec]]] = [
    ("Pool (typical relaxed)", PocketSpec(120, 135, corner_jaw_diameter_mm=36, side_jaw_diameters_mm=40)),
    ("Pool (tighter)",         PocketSpec(110, 125, corner_jaw_diameter_mm=24, side_jaw_diameters_mm=28)),
    ("Chinese 8-ball (tight)", PocketSpec(105, 120, corner_jaw_diameter_mm=20, side_jaw_diameters_mm=24)),
    ]

    PRESET_CLOTHS: ClassVar[Dict[str, Tuple[Tuple[int,int,int], Tuple[int,int,int]]]] = {
    "Green cloth":       ((35, 40, 40),  (85, 255, 255)),
    "Blue (tournament)": ((95, 60, 40),  (135,255,255)),
    "Snooker green":     ((40, 60, 40),  (80, 255, 255)),
    "Grey cloth":        ((0,  0, 40),   (180, 60, 220)),
    }

    # HSV formats -> OpenCV has hue value from 1° to 180° 
    # https://stackoverflow.com/questions/16685707/why-is-the-range-of-hue-0-180-in-opencv
    # https://docs.wpilib.org/en/stable/docs/software/vision-processing/wpilibpi/image-thresholding.html


    # Build an ordered list for UI selection
    CLOTH_OPTIONS = [
        ("Green cloth",       *PRESET_CLOTHS["Green cloth"]),
        ("Blue (tournament)", *PRESET_CLOTHS["Blue (tournament)"]),
        ("Snooker green",     *PRESET_CLOTHS["Snooker green"]),
        ("Grey cloth",        *PRESET_CLOTHS["Grey cloth"]),
    ]

    ENVIRONMENT_JSON_PATH = "../Configuration/"

    __loaded_json_configuration_name: str = ""

    DEFAULT_BALLS = BallSpec(.05715,.068)

    SCHEMA_VERSION = 2 # Bump every time when a change is made (add/rename/delete). Reset to 1 for final shippment.

    def _tuple_or_none(self,x):
        return tuple(x) if isinstance(x, (list, tuple)) else None

    def table_from_json_data(self, table_data) -> TableSpec:
        return TableSpec( 
            name=table_data.get("name", "Custom"),
            playfield_mm=self._tuple_or_none(table_data.get("playfield_mm")),
            overall_mm=self._tuple_or_none(table_data.get("overall_mm")),
            notes=table_data.get("notes", ""),
            cloth_profile=table_data.get("cloth_profile", ""),  # Added v2
            cloth_lower_hsv=self._tuple_or_none(table_data.get("cloth_lower_hsv")),  # Added v2
            cloth_upper_hsv=self._tuple_or_none(table_data.get("cloth_upper_hsv"))   # Added v2
        )

    def _ensure_dir(path: str):
        directory = os.path.dirname(path)
        if directory and not os.path.exists(directory):
            os.makedirs(directory, exist_ok=True)
        
    def save_environment(self, environment_config: EnvironmentConfig, path: str) -> None:
        self._ensure_dir(path)
        payload = {
            "_schema_version": self.SCHEMA_VERSION,
            "table": asdict(environment_config.table),
            "pockets": asdict(environment_config.pockets),
            "ball_spec": asdict(environment_config.ball_spec),
            "camera": asdict(environment_config.camera)
        }
        with open(path, "w", encoding="utf-8") as f:
            json.dump(payload, f, indent=2)
        return environment_config
    
    def load_last_environment(self, path: str) -> Optional[EnvironmentConfig]:
        # Sharable data between project modules (for testing and other purposes..., but also to make the setup faster)
        if not os.path.exists(path):
            return None

        data = None
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
   
        schema = data.get("_schema_version", 1)
        # Table
        table = self.table_from_json_data(data.get("table", {}))
        
        # Original environments objects
        pockets = PocketSpec(**data["pockets"])
        ball    = BallSpec(**data["ball_spec"])
        camera  = CameraSpec(**data["camera"])
        
        env = EnvironmentConfig(table, pockets, ball, camera)
        # Auto upgrade schema
        if schema < self.SCHEMA_VERSION:
            try:
                self.save_environment(env, path)
            except Exception:
                pass
            
        return env

    def _print_table_menu(self):
        print("\n Select TABLE size:")
        for index, specification in enumerate(self.PRESET_TABLES, start = 1):
            play_field = f"{specification.playfield_mm[0]}×{specification.playfield_mm[1]} mm"
            ov = f" / overall {specification.overall_mm[0]}×{specification.overall_mm[1]} mm" if specification.overall_mm else ""
            print(f" {index}. {specification.name} - playfield {play_field}{ov} ({specification.notes})")
        print(" c. Custom (enter manually)")

    def _print_cloth_menu(self):
        print("\nSelect TABLE CLOTH color profile:")
        for idx, (name, lower, upper) in enumerate(self.CLOTH_OPTIONS, start=1):
            print(f"  {idx}. {name} — lower {lower}, upper {upper}")
        print("  c. Custom (enter HSV ranges manually)")

    def _print_pocket_menu(self):
        print("\nSelect POCKET profile:")
        for idx, (name, p) in enumerate(self.PRESET_POCKETS, start=1):
            cj = f"{p.corner_jaw_diameter_mm} mm" if p.corner_jaw_diameter_mm is not None else "—"
            sj = f"{p.side_jaw_diameters_mm} mm" if p.side_jaw_diameters_mm is not None else "—"
            print(f"  {idx}. {name} — mouth: corner {p.corner_pocket_diameter_mm} mm / side {p.side_pocket_diameter_mm} mm; "
                f"jaw radius: corner {cj} / side {sj}")
        print("  c. Custom (enter manually)")

    def _read_choice(self, valid_choices: List[str]) -> str:
        while True:
            choice = input("> ").strip().lower()
            if choice in valid_choices:
                return choice
            print(f"Please choose one of {valid_choices}.")

    def _read_int(self, prompt: str, min_v: int, max_v: int, default: Optional[int]=None) -> int:
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
        
    def _read_optional_int(self, prompt: str, min_v: int, max_v: int, default: Optional[int] = None) -> Optional[int]:
        """
        Like _read_int but allows empty input to keep 'None' (no jaw radius modeled).
        If a default is provided, hitting Enter returns that default instead of None.
        """
        while True:
            raw = input(
                f"{prompt} [{min_v}..{max_v}]"
                + (f" (default {default})" if default is not None else " (Enter for none)")
                + ": "
            ).strip()
            if raw == "":
                return default  # may be None
            try:
                val = int(float(raw))
                if min_v <= val <= max_v:
                    return val
            except ValueError:
                pass
            print("Invalid value.")

    def _read_float(self, prompt: str, min_v: float, max_v: float, default: Optional[float]=None) -> float:
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

    def set_up_table(
        self,
        preset_index: int | None = None,
        custom_playfield_mm: tuple[int, int] | None = None,
        custom_overall_mm: tuple[int, int] | None = None
    ) -> TableSpec:
        from dataclasses import replace

        # Fast paths (non-interactive)
        if preset_index is not None:
            # return a COPY, never mutate the preset entry
            return replace(self.PRESET_TABLES[preset_index - 1])

        if custom_playfield_mm is not None:
            return TableSpec(
                name="Custom",
                playfield_mm=custom_playfield_mm,
                overall_mm=custom_overall_mm,
                notes="User-defined",
            )
            
        # Interactive way
        self._print_table_menu()
        choice = self._read_choice([str(i) for i in range(1, len(self.PRESET_TABLES)+1)] + ["c"])
        if choice == "c":
            user_defined_str = "User-defined"
            Lpf = self._read_int("Playfield length (mm)", 1500, 3200, 2540)
            Wpf = self._read_int("Playfield width (mm)", 700, 1800, 1270)
            Hpf = self._read_float("Playfield height from floor (mm)", 600, 1500, 785)
            use_overall = input("Do you know overall cabinet size? (y/n): ").strip().lower()
            overall = None
            
            if use_overall == "y":
                Lov = self._read_int("Overall length (mm)", 2000, 3300, None)
                Wov = self._read_int("Overall width (mm)", 1000, 1800, None)
                overall = (Lov, Wov)
                return TableSpec("Custom", (Lpf, Wpf, Hpf), overall, user_defined_str)
            return TableSpec("Custom", (Lpf, Wpf, Hpf), overall, user_defined_str)
        else:
            return replace(self.PRESET_TABLES[int(choice) - 1])
    
    def set_up_hsv(self, table: TableSpec) -> TableSpec:
        self._print_cloth_menu()
        choice = self._read_choice([str(i) for i in range(1, len(self.CLOTH_OPTIONS)+1)] + ["c"])
        if choice == "c":
            print("Enter custom HSV lower bound (H,S,V):")
            h_low = self._read_int("Hue min", 0, 179)
            s_low = self._read_int("Sat min", 0, 255)
            v_low = self._read_int("Val min", 0, 255)
            print("Enter custom HSV upper bound (H,S,V):")
            h_up  = self._read_int("Hue max", 0, 179)
            s_up  = self._read_int("Sat max", 0, 255)
            v_up  = self._read_int("Val max", 0, 255)
            table.cloth_profile   = "Custom"
            table.cloth_lower_hsv = (h_low, s_low, v_low)
            table.cloth_upper_hsv = (h_up,  s_up,  v_up)
            return table
        
        name, lower, upper = self.CLOTH_OPTIONS[int(choice)-1]
        table.cloth_profile   = name
        table.cloth_lower_hsv = lower
        table.cloth_upper_hsv = upper
        return table

    def set_up_pockets(self):
        self._print_pocket_menu()
        choice = self._read_choice([str(i) for i in range(1, len(self.PRESET_POCKETS)+1)] + ["c"])
        corner_jaw_text = "Corner jaw diameter (mm)"
        side_jaw_text = "Side jaw diameter (mm)"
        if choice == "c":
            corner = self._read_int("Corner pocket mouth (mm)", 95, 160, 120)
            side   = self._read_int("Side pocket mouth (mm)",   105, 180, 135)
            corner_jaw = self._read_optional_int(corner_jaw_text, 10, 80, 36)
            side_jaw   = self._read_optional_int(side_jaw_text,   10, 80, 40)
            return PocketSpec(
                corner_pocket_diameter_mm=corner,
                side_pocket_diameter_mm=side,
                corner_jaw_diameter_mm=corner_jaw,
                side_jaw_diameters_mm=side_jaw,
            )
        _, preset =self.PRESET_POCKETS[int(choice) - 1]
        
        override = input("Override jaw radii? (y/N): ").strip().lower() == "y"
        if override:
            return PocketSpec(
                corner_pocket_diameter_mm = preset.corner_pocket_diameter_mm,
                side_pocket_diameter_mm = preset.side_pocket_diameter_mm,
                corner_jaw_diameter_mm = self._read_optional_int(
                    corner_jaw_text,
                    10, 
                    80,
                    default=preset.corner_jaw_diameter_mm
                ),
                side_jaw_diameters_mm = self._read_optional_int(
                    side_jaw_text,
                    10, 
                    80,
                    default=preset.side_jaw_diameters_mm
                ),
            )
        
        return preset
    
    def set_up_camera_height_mm(self):
        print("\nEnter camera height from FLOOR (m), typical 2–3 m:") # The camera sensor is assumed to be on the XY center of the table. Only Z is in question.
        return self._read_float("Camera height (m)", 1.5, 4.0, 2.5)

    def get_debug_env_config(self, config_name: str) -> EnvironmentConfig:
        return self.get_environment_config(False, True, config_name, True)
    
    def get_json_name_for_unity(self):
        if self.__loaded_json_configuration_name is None or self.__loaded_json_configuration_name == "":
            self.get_environment_config()
        return self.__loaded_json_configuration_name

    def get_environment_config(
                            self,
                            interactive: bool = True,
                            use_last_known: bool = True,
                            config_name: str = "last_environment.json",
                            debug: bool = False) -> EnvironmentConfig:
        
        if  (config_name is None or len(config_name) == 0 ) and debug:
            print('No debug config specified. Using last known enviroment.')
            return self.get_environment_config()
        
        cache_path = f"{self.ENVIRONMENT_JSON_PATH}{config_name}"
        self.__loaded_json_configuration_name = config_name
        
        if use_last_known:
            env = self.load_last_environment(cache_path)
            if debug:
                return env
            if env is not None:
                needs_cloth = (
                    env.table.cloth_profile in (None, "") or
                    env.table.cloth_lower_hsv is None or
                    env.table.cloth_upper_hsv is None
                )
                if interactive and needs_cloth:
                    env.table = self.set_up_hsv(env.table)
                    return self.save_environment(env, cache_path)
        if env is not None:
            return env
        table = None
        pockets = None
        camera_height_mm = None
        
        if interactive:
            table = self.set_up_table()
            table = self.set_up_hsv(table)
            pockets = self.set_up_pockets()
            camera_height_mm = self.set_up_camera_height_mm()
        else:
            # non-interactive sane defaults (COPY preset, don't mutate global list)
            from dataclasses import replace
            base = replace(self.PRESET_TABLES[3])  # 9ft tournament as default size copy
            low, up = self.PRESET_CLOTHS["Grey cloth"]  # explicit profile by name
            base.cloth_profile, base.cloth_lower_hsv, base.cloth_upper_hsv = "Grey cloth", low, up
            table = base
            pockets = self.PRESET_POCKETS[0][1]
            camera_height_mm = 2500
            
        env = EnvironmentConfig(table,
                                pockets,
                                self.DEFAULT_BALLS,
                                CameraSpec(camera_height_mm))
        return self.save_environment(env, cache_path)