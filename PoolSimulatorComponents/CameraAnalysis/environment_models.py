from __future__ import annotations

from dataclasses import asdict, dataclass, field
from math import cos, radians, sin
from typing import Any, ClassVar, Dict, Iterable, List, Optional, Tuple

from environment_input import float_tuple, int_tuple


Vector2Float = Tuple[float, float]
Vector3Float = Tuple[float, float, float]


@dataclass
class TableSpec:
    name: str
    playfield_mm: Vector3Float
    overall_mm: Optional[Vector2Float] = None
    overall_height_mm: Optional[float] = None
    rail_top_height_mm: Optional[float] = None
    marker_plane_height_mm: Optional[float] = None
    notes: str = ""

    def playfield_length_mm(self) -> float:
        return float(self.playfield_mm[0])

    def playfield_width_mm(self) -> float:
        return float(self.playfield_mm[1])

    def playfield_height_mm(self) -> float:
        return float(self.playfield_mm[2])

    def effective_marker_plane_height_mm(self) -> float:
        if self.marker_plane_height_mm is not None:
            return float(self.marker_plane_height_mm)
        if self.rail_top_height_mm is not None:
            return float(self.rail_top_height_mm)
        return self.playfield_height_mm()

    def ball_center_height_mm(self, ball_diameter_m: float) -> float:
        return self.playfield_height_mm() + (float(ball_diameter_m) * 1000.0 * 0.5)

    def rail_center_offsets_mm(self) -> Vector2Float:
        if self.overall_mm is None:
            return (0.0, 0.0)
        length_offset = (float(self.overall_mm[0]) - self.playfield_length_mm()) * 0.25
        width_offset = (float(self.overall_mm[1]) - self.playfield_width_mm()) * 0.25
        return (float(length_offset), float(width_offset))

    def pocket_mm_positions(self, corner_inset_mm: float, side_inset_mm: float) -> List[Vector2Float]:
        length = self.playfield_length_mm()
        width = self.playfield_width_mm()
        return [
            (corner_inset_mm, width - corner_inset_mm),
            (length - corner_inset_mm, width - corner_inset_mm),
            (0.5 * length, side_inset_mm),
            (0.5 * length, width - side_inset_mm),
            (corner_inset_mm, corner_inset_mm),
            (length - corner_inset_mm, corner_inset_mm),
        ]

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "TableSpec":
        playfield = float_tuple(data.get("playfield_mm"), 3, (2445.0, 1225.0, 810.0))
        overall = float_tuple(data.get("overall_mm"), 2, None)
        return TableSpec(
            name=str(data.get("name", "Custom")),
            playfield_mm=playfield,  # type: ignore[arg-type]
            overall_mm=overall,  # type: ignore[arg-type]
            overall_height_mm=float(data["overall_height_mm"]) if data.get("overall_height_mm") is not None else None,
            rail_top_height_mm=float(data["rail_top_height_mm"]) if data.get("rail_top_height_mm") is not None else None,
            marker_plane_height_mm=float(data["marker_plane_height_mm"]) if data.get("marker_plane_height_mm") is not None else None,
            notes=str(data.get("notes", "")),
        )


@dataclass
class PocketSpec:
    corner_pocket_diameter_mm: int
    side_pocket_diameter_mm: int
    corner_jaw_diameter_mm: Optional[int] = None
    side_jaw_diameters_mm: Optional[int] = None

    def derive_insets(self) -> Vector2Float:
        corner_inset = self.corner_jaw_diameter_mm * 0.5 if self.corner_jaw_diameter_mm is not None else self.corner_pocket_diameter_mm * 0.5
        side_inset = self.side_jaw_diameters_mm * 0.5 if self.side_jaw_diameters_mm is not None else self.side_pocket_diameter_mm * 0.5
        return float(corner_inset), float(side_inset)

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "PocketSpec":
        return PocketSpec(
            corner_pocket_diameter_mm=int(data.get("corner_pocket_diameter_mm", 125)),
            side_pocket_diameter_mm=int(data.get("side_pocket_diameter_mm", 105)),
            corner_jaw_diameter_mm=int(data["corner_jaw_diameter_mm"]) if data.get("corner_jaw_diameter_mm") is not None else None,
            side_jaw_diameters_mm=int(data["side_jaw_diameters_mm"]) if data.get("side_jaw_diameters_mm") is not None else None,
        )


@dataclass
class BallSpec:
    diameter_m: float = 0.05715
    ball_circumference_m: float = 0.1795

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "BallSpec":
        return BallSpec(
            diameter_m=float(data.get("diameter_m", 0.05715)),
            ball_circumference_m=float(data.get("ball_circumference_m", 0.1795)),
        )


@dataclass
class CameraSpec:
    height_from_floor_m: float

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "CameraSpec":
        return CameraSpec(height_from_floor_m=float(data.get("height_from_floor_m", 2.735)))


@dataclass
class FiducialMarkerSpec:
    id: int
    center_mm: Vector3Float
    rotation_deg: float = 0.0
    enabled: bool = True
    label: str = ""
    payload: Optional[str] = None

    def corner_points_mm(self, marker_size_mm: float) -> List[Vector3Float]:
        """
        Returns physical marker/code corners in table coordinates.

        Corner order is top-left, top-right, bottom-right, bottom-left when the marker is viewed
        in its own local coordinate system. This is the order expected by OpenCV ArUco-style
        corner correspondences and is also convenient for QR quadrangle points.
        """
        half = float(marker_size_mm) * 0.5
        angle = radians(float(self.rotation_deg))
        ux = (cos(angle), sin(angle))
        uy = (-sin(angle), cos(angle))
        cx, cy, cz = (float(self.center_mm[0]), float(self.center_mm[1]), float(self.center_mm[2]))

        def point(local_x: float, local_y: float) -> Vector3Float:
            return (
                float(cx + local_x * ux[0] + local_y * uy[0]),
                float(cy + local_x * ux[1] + local_y * uy[1]),
                float(cz),
            )

        return [
            point(-half, half),
            point(half, half),
            point(half, -half),
            point(-half, -half),
        ]

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "FiducialMarkerSpec":
        center = float_tuple(data.get("center_mm"), 3, (0.0, 0.0, 0.0))
        return FiducialMarkerSpec(
            id=int(data.get("id", 0)),
            center_mm=center,  # type: ignore[arg-type]
            rotation_deg=float(data.get("rotation_deg", 0.0)),
            enabled=bool(data.get("enabled", True)),
            label=str(data.get("label", "")),
            payload=str(data["payload"]) if data.get("payload") is not None else None,
        )


@dataclass
class FiducialSetSpec:
    marker_type: str
    marker_size_mm: float
    white_margin_mm: float
    cutout_size_mm: float
    markers: List[FiducialMarkerSpec] = field(default_factory=list)
    dictionary_name: Optional[str] = None
    payload_prefix: Optional[str] = None
    corner_order: str = "TL_TR_BR_BL"

    def enabled_markers(self) -> List[FiducialMarkerSpec]:
        return [marker for marker in self.markers if marker.enabled]

    def marker_by_id(self) -> Dict[int, FiducialMarkerSpec]:
        return {marker.id: marker for marker in self.enabled_markers()}

    def object_points_by_id(self) -> Dict[int, List[Vector3Float]]:
        return {
            marker.id: marker.corner_points_mm(self.marker_size_mm)
            for marker in self.enabled_markers()
        }

    @staticmethod
    def from_json_data(marker_type: str, data: dict[str, Any]) -> "FiducialSetSpec":
        return FiducialSetSpec(
            marker_type=str(data.get("marker_type", marker_type)),
            marker_size_mm=float(data.get("marker_size_mm", data.get("code_size_mm", 130.0))),
            white_margin_mm=float(data.get("white_margin_mm", 30.0)),
            cutout_size_mm=float(data.get("cutout_size_mm", 190.0)),
            dictionary_name=str(data["dictionary_name"]) if data.get("dictionary_name") is not None else None,
            payload_prefix=str(data["payload_prefix"]) if data.get("payload_prefix") is not None else None,
            corner_order=str(data.get("corner_order", "TL_TR_BR_BL")),
            markers=[FiducialMarkerSpec.from_json_data(item) for item in data.get("markers", [])],
        )


@dataclass
class FiducialConfig:
    coordinate_system: str
    sets: Dict[str, FiducialSetSpec] = field(default_factory=dict)

    def get_set(self, marker_type: str) -> Optional[FiducialSetSpec]:
        return self.sets.get(marker_type)

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "FiducialConfig":
        raw_sets = data.get("sets", {}) or {}
        return FiducialConfig(
            coordinate_system=str(data.get("coordinate_system", "origin_inner_bottom_left_x_length_y_width_z_floor_mm")),
            sets={
                str(marker_type): FiducialSetSpec.from_json_data(str(marker_type), set_data)
                for marker_type, set_data in raw_sets.items()
            },
        )


@dataclass
class EnvironmentConfig:
    table: TableSpec
    pockets: PocketSpec
    ball_spec: BallSpec
    camera: CameraSpec
    fiducials: FiducialConfig = field(default_factory=lambda: FiducialConfig("origin_inner_bottom_left_x_length_y_width_z_floor_mm", {}))

    SCHEMA_VERSION: ClassVar[int] = 4

    def get_fiducial_set(self, marker_type: str) -> Optional[FiducialSetSpec]:
        return self.fiducials.get_set(marker_type)

    def pocket_uv_positions(self) -> Dict[str, Vector2Float]:
        return {
            "corner_BL": (0.0, 0.0),
            "corner_BR": (1.0, 0.0),
            "corner_TL": (0.0, 1.0),
            "corner_TR": (1.0, 1.0),
            "side_B": (0.5, 0.0),
            "side_T": (0.5, 1.0),
        }

    def to_json_data(self) -> dict[str, Any]:
        return {
            "_schema_version": self.SCHEMA_VERSION,
            "table": asdict(self.table),
            "pockets": asdict(self.pockets),
            "ball_spec": asdict(self.ball_spec),
            "camera": asdict(self.camera),
            "fiducials": asdict(self.fiducials),
        }

    @staticmethod
    def from_json_data(data: dict[str, Any]) -> "EnvironmentConfig":
        return EnvironmentConfig(
            table=TableSpec.from_json_data(data.get("table", {})),
            pockets=PocketSpec.from_json_data(data.get("pockets", {})),
            ball_spec=BallSpec.from_json_data(data.get("ball_spec", {})),
            camera=CameraSpec.from_json_data(data.get("camera", {})),
            fiducials=FiducialConfig.from_json_data(data.get("fiducials", {}) or {}),
        )
