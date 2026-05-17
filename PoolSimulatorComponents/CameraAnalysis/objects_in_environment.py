from __future__ import annotations

from dataclasses import replace
from pathlib import Path
from typing import Optional

from environment_input import (
    ensure_directory,
    load_json,
    read_bool,
    read_choice,
    read_float,
    read_int,
    read_optional_float,
    read_optional_int,
    save_json,
)
from environment_models import (
    BallSpec,
    CameraSpec,
    EnvironmentConfig,
    FiducialConfig,
    FiducialMarkerSpec,
    FiducialSetSpec,
    PocketSpec,
    TableSpec,
)


class EnvironmentConfigRepository:
    ENVIRONMENT_JSON_PATH = Path("../Configuration")

    DEFAULT_BALLS = BallSpec(0.05715, 0.1795)
    DEFAULT_SCHEMA_VERSION = EnvironmentConfig.SCHEMA_VERSION

    PRESET_TABLES = [
        TableSpec("7ft (bar box)", (1930.0, 965.0, 785.0), (2133.0, 1120.0), None, 820.0, 820.0, "Common 7ft bar table"),
        TableSpec("8ft (home)", (2235.0, 1118.0, 785.0), (2438.0, 1219.0), None, 820.0, 820.0, "Typical 8ft home table"),
        TableSpec("8.5ft (pro-8)", (2340.0, 1170.0, 785.0), (2543.0, 1272.0), None, 820.0, 820.0, "Pro-8, no standard cabinet ref"),
        TableSpec("9ft (tournament)", (2540.0, 1270.0, 785.0), (2743.0, 1372.0), None, 820.0, 820.0, "WPA tournament size"),
        TableSpec("10ft (snooker)", (2845.0, 1422.0, 785.0), (3048.0, 1524.0), None, 820.0, 820.0, "10ft snooker/billiards"),
    ]

    PRESET_POCKETS = [
        ("Pool (typical relaxed)", PocketSpec(120, 135, corner_jaw_diameter_mm=36, side_jaw_diameters_mm=40)),
        ("Pool (tighter)", PocketSpec(110, 125, corner_jaw_diameter_mm=24, side_jaw_diameters_mm=28)),
        ("Chinese 8-ball (tight)", PocketSpec(105, 120, corner_jaw_diameter_mm=20, side_jaw_diameters_mm=24)),
    ]

    def __init__(self, environment_json_path: str | Path | None = None):
        self.environment_json_path = ensure_directory(environment_json_path or self.ENVIRONMENT_JSON_PATH)
        self._loaded_json_configuration_name = ""

    def get_json_name_for_unity(self) -> str:
        return self._loaded_json_configuration_name

    def _path(self, config_name: str) -> Path:
        return self.environment_json_path / config_name

    def save_environment(self, environment_config: EnvironmentConfig, config_name_or_path: str | Path) -> EnvironmentConfig:
        path = Path(config_name_or_path)
        if not path.is_absolute() and path.parent == Path("."):
            path = self._path(str(config_name_or_path))
        save_json(path, environment_config.to_json_data())
        self._loaded_json_configuration_name = path.name
        return environment_config

    def load_environment(self, config_name: str = "last_environment.json") -> Optional[EnvironmentConfig]:
        path = self._path(config_name)
        if not path.exists():
            return None

        data = load_json(path)
        environment = EnvironmentConfig.from_json_data(data)
        self._loaded_json_configuration_name = config_name

        schema = int(data.get("_schema_version", 1))
        if schema < EnvironmentConfig.SCHEMA_VERSION:
            self.save_environment(environment, config_name)

        return environment

    def list_environment_configs(self) -> list[dict[str, object]]:
        if not self.environment_json_path.exists():
            return []

        environments: list[dict[str, object]] = []
        for path in sorted(self.environment_json_path.glob("*.json"), key=lambda item: item.name.lower()):
            try:
                data = load_json(path)
                if not self._is_environment_payload(data):
                    continue
                table = data.get("table", {}) or {}
                environments.append(
                    {
                        "file_name": path.name,
                        "table_name": table.get("name", "Unknown"),
                        "playfield_mm": tuple(table.get("playfield_mm", ())),
                        "fiducial_sets": tuple((data.get("fiducials", {}) or {}).get("sets", {}).keys()),
                    }
                )
            except Exception:
                continue
        return environments

    def choose_environment_config_interactive(self, default_config_name: Optional[str] = None) -> Optional[str]:
        environments = self.list_environment_configs()
        if not environments:
            print("\nNo valid environment configuration files were found. Falling back to interactive setup.")
            return None

        print("\nSelect ENVIRONMENT configuration to load:")
        for index, environment in enumerate(environments, start=1):
            playfield = environment.get("playfield_mm", ())
            playfield_text = (
                f"{int(playfield[0])}x{int(playfield[1])}x{int(playfield[2])} mm"
                if isinstance(playfield, tuple) and len(playfield) == 3 else "unknown playfield"
            )
            fiducial_sets = ",".join(environment.get("fiducial_sets", ())) or "none"
            default_text = " [default]" if environment["file_name"] == default_config_name else ""
            print(f"  {index}. {environment['file_name']} | {environment['table_name']} | {playfield_text} | fiducials: {fiducial_sets}{default_text}")

        print("  n. Create or overwrite current config interactively")
        if default_config_name:
            print("  Enter. Load default")

        valid_indices = [str(index) for index in range(1, len(environments) + 1)]
        while True:
            choice = input("> ").strip().lower()
            if choice == "" and default_config_name and any(environment["file_name"] == default_config_name for environment in environments):
                return default_config_name
            if choice == "n":
                return None
            if choice in valid_indices:
                return str(environments[int(choice) - 1]["file_name"])
            print("Invalid choice.")

    def get_environment_config(
        self,
        interactive: bool = True,
        use_last_known: bool = True,
        config_name: str = "last_environment.json",
        debug: bool = False,
    ) -> EnvironmentConfig:
        if interactive:
            chosen = self.choose_environment_config_interactive(config_name if use_last_known else None)
            if chosen is not None:
                config_name = chosen
                use_last_known = True

        if use_last_known:
            environment = self.load_environment(config_name)
            if environment is not None:
                return environment

        environment = self.create_environment_interactive() if interactive else self.default_environment()
        return self.save_environment(environment, config_name)

    def default_environment(self) -> EnvironmentConfig:
        table = TableSpec(
            name="Custom",
            playfield_mm=(2445.0, 1225.0, 810.0),
            overall_mm=(2730.0, 1510.0),
            overall_height_mm=845.0,
            rail_top_height_mm=845.0,
            marker_plane_height_mm=845.0,
            notes="User-defined",
        )
        return EnvironmentConfig(
            table=table,
            pockets=PocketSpec(125, 105, corner_jaw_diameter_mm=114, side_jaw_diameters_mm=100),
            ball_spec=self.DEFAULT_BALLS,
            camera=CameraSpec(2.735),
            fiducials=self.build_default_fiducials(table),
        )

    def create_environment_interactive(self) -> EnvironmentConfig:
        table = self.set_up_table()
        pockets = self.set_up_pockets()
        camera_height_mm = self.set_up_camera_height_mm()
        return EnvironmentConfig(
            table=table,
            pockets=pockets,
            ball_spec=self.DEFAULT_BALLS,
            camera=CameraSpec(camera_height_mm / 1000.0),
            fiducials=self.build_default_fiducials(table),
        )

    def set_up_table(self) -> TableSpec:
        print("\nSelect TABLE size:")
        for index, specification in enumerate(self.PRESET_TABLES, start=1):
            playfield = f"{int(specification.playfield_mm[0])}x{int(specification.playfield_mm[1])} mm"
            overall = f" / overall {int(specification.overall_mm[0])}x{int(specification.overall_mm[1])} mm" if specification.overall_mm else ""
            print(f" {index}. {specification.name} - playfield {playfield}{overall} ({specification.notes})")
        print(" c. Custom")

        valid_choices = [str(index) for index in range(1, len(self.PRESET_TABLES) + 1)] + ["c"]
        choice = read_choice(">", valid_choices)
        if choice != "c":
            table = replace(self.PRESET_TABLES[int(choice) - 1])
            table.rail_top_height_mm = read_optional_float("Rail/top-edge height from floor (mm)", 600.0, 1500.0, table.playfield_height_mm() + 35.0)
            table.marker_plane_height_mm = read_optional_float("Marker plane height from floor (mm)", 600.0, 1500.0, table.rail_top_height_mm)
            table.overall_height_mm = table.rail_top_height_mm
            return table

        length = read_float("Playfield length (mm)", 1500.0, 3200.0, 2445.0)
        width = read_float("Playfield width (mm)", 700.0, 1800.0, 1225.0)
        height = read_float("Playfield height from floor (mm)", 600.0, 1500.0, 810.0)
        overall = None
        if read_bool("Do you know overall cabinet length/width?", True):
            overall_length = read_float("Overall length (mm)", 1800.0, 3500.0, 2730.0)
            overall_width = read_float("Overall width (mm)", 900.0, 2000.0, 1510.0)
            overall = (overall_length, overall_width)
        rail_top_height = read_optional_float("Rail/top-edge height from floor (mm)", 600.0, 1500.0, height + 35.0)
        marker_plane_height = read_optional_float("Marker plane height from floor (mm)", 600.0, 1500.0, rail_top_height)
        return TableSpec(
            name="Custom",
            playfield_mm=(length, width, height),
            overall_mm=overall,
            overall_height_mm=rail_top_height,
            rail_top_height_mm=rail_top_height,
            marker_plane_height_mm=marker_plane_height,
            notes="User-defined",
        )

    def set_up_pockets(self) -> PocketSpec:
        print("\nSelect POCKET profile:")
        for index, (name, pocket) in enumerate(self.PRESET_POCKETS, start=1):
            print(f"  {index}. {name} - corner {pocket.corner_pocket_diameter_mm} mm / side {pocket.side_pocket_diameter_mm} mm")
        print("  c. Custom")
        valid_choices = [str(index) for index in range(1, len(self.PRESET_POCKETS) + 1)] + ["c"]
        choice = read_choice(">", valid_choices)
        if choice != "c":
            return replace(self.PRESET_POCKETS[int(choice) - 1][1])
        return PocketSpec(
            corner_pocket_diameter_mm=read_int("Corner pocket mouth (mm)", 95, 160, 125),
            side_pocket_diameter_mm=read_int("Side pocket mouth (mm)", 105, 180, 105),
            corner_jaw_diameter_mm=read_optional_int("Corner jaw diameter (mm)", 10, 200, 114),
            side_jaw_diameters_mm=read_optional_int("Side jaw diameter (mm)", 10, 200, 100),
        )

    def set_up_camera_height_mm(self) -> float:
        print("\nEnter camera height from FLOOR (mm), typical 2-3 m:")
        return read_float("Camera height (mm)", 1000.0, 4000.0, 2735.0)

    def build_default_fiducials(self, table: TableSpec) -> FiducialConfig:
        length = table.playfield_length_mm()
        width = table.playfield_width_mm()
        rail_x, rail_y = table.rail_center_offsets_mm()
        marker_z = table.effective_marker_plane_height_mm()

        # Conservative table-edge positions. These avoid pocket openings and keep enough spread for pose estimation.
        aruco_positions = [
            (0, (350.0, -rail_y, marker_z), "bottom-left long rail"),
            (1, (length - 350.0, -rail_y, marker_z), "bottom-right long rail"),
            (2, (350.0, width + rail_y, marker_z), "top-left long rail"),
            (3, (length - 350.0, width + rail_y, marker_z), "top-right long rail"),
            (4, (-rail_x, 250.0, marker_z), "left-lower short rail"),
            (5, (-rail_x, width - 250.0, marker_z), "left-upper short rail"),
            (6, (length + rail_x, 250.0, marker_z), "right-lower short rail"),
            (7, (length + rail_x, width - 250.0, marker_z), "right-upper short rail"),
        ]

        qr_positions = [
            (0, (650.0, -rail_y, marker_z), "bottom-left long rail QR"),
            (1, (length - 650.0, -rail_y, marker_z), "bottom-right long rail QR"),
            (2, (650.0, width + rail_y, marker_z), "top-left long rail QR"),
            (3, (length - 650.0, width + rail_y, marker_z), "top-right long rail QR"),
            (4, (-rail_x, 500.0, marker_z), "left-lower short rail QR"),
            (5, (-rail_x, width - 500.0, marker_z), "left-upper short rail QR"),
            (6, (length + rail_x, 500.0, marker_z), "right-lower short rail QR"),
            (7, (length + rail_x, width - 500.0, marker_z), "right-upper short rail QR"),
        ]

        aruco_set = FiducialSetSpec(
            marker_type="aruco",
            dictionary_name="DICT_4X4_50",
            marker_size_mm=130.0,
            white_margin_mm=30.0,
            cutout_size_mm=190.0,
            corner_order="TL_TR_BR_BL",
            markers=[
                FiducialMarkerSpec(id=marker_id, center_mm=center, rotation_deg=0.0, label=label)
                for marker_id, center, label in aruco_positions
            ],
        )

        qr_set = FiducialSetSpec(
            marker_type="qr",
            payload_prefix="ARPOOL_QR_",
            marker_size_mm=130.0,
            white_margin_mm=30.0,
            cutout_size_mm=190.0,
            corner_order="TL_TR_BR_BL",
            markers=[
                FiducialMarkerSpec(id=marker_id, center_mm=center, rotation_deg=0.0, label=label, payload=f"ARPOOL_QR_{marker_id:02}")
                for marker_id, center, label in qr_positions
            ],
        )

        return FiducialConfig(
            coordinate_system="origin_inner_bottom_left_x_length_y_width_z_floor_mm",
            sets={"aruco": aruco_set, "qr": qr_set},
        )

    @staticmethod
    def _is_environment_payload(data: dict) -> bool:
        if not isinstance(data, dict):
            return False
        required = {"table", "pockets", "ball_spec", "camera"}
        return required.issubset(data.keys())
