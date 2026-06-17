import numpy as np
from typing import List, Tuple, Optional, Dict

from ball_type import BallType


LABEL_MAP = {
    BallType.EIGHT.value: ("e", "8"),
    BallType.CUE.value: ("c", "/"),
    BallType.STRIPE.value: ("st", "u"),
    BallType.SOLID.value: ("so", "u"),
    BallType.UNKNOWN.value: ("u", "u"),
}

BALL_LINE_TOKEN = "b"


def _f32(x: float) -> float:
    return float(np.float32(x))


def _round_or_none(value, decimals: int):
    if value is None:
        return None
    return round(float(value), decimals)


def _fmt_float(value: float, decimals: int) -> str:
    return f"{_f32(value):.{decimals}f}"


def _fmt2(x, y, decimals: int = 4):
    return f"{_fmt_float(x, decimals)},{_fmt_float(y, decimals)}"


def _fmt_num_or_backslash(value, decimals: Optional[int] = None):
    if value is None:
        return "\\"

    if decimals is None:
        return str(value)

    return _fmt_float(float(value), decimals)


def line_pockets(pockets_xy, decimals: int = 7):
    if pockets_xy is None:
        return ""

    return "p " + ";".join(_fmt2(x, y, decimals) for (x, y) in pockets_xy)


def line_configuration_name(configuration_name: str):
    return "E " + configuration_name


def normalize_detection_entries(
    entries_px: List[Dict],
    pos_decimals: int = 4,
    conf_decimals: int = 3,
    keep_velocity: bool = False,
    vel_decimals: int = 3,
) -> List[Dict]:
    normalized = []

    for ball in entries_px or []:
        ball_type = ball.get("type")
        x = _round_or_none(ball.get("x", 0.0), pos_decimals)
        y = _round_or_none(ball.get("y", 0.0), pos_decimals)

        if ball_type == BallType.EIGHT.value:
            number = LABEL_MAP[BallType.EIGHT.value][1]
        elif ball_type == BallType.CUE.value:
            number = LABEL_MAP[BallType.CUE.value][1]
        else:
            number = ball.get("number", LABEL_MAP[BallType.UNKNOWN.value][1])

        item = {
            "type": ball_type,
            "x": x,
            "y": y,
            "number": number,
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
    conf_decimals: int = 3,
) -> Tuple:
    normalized = normalize_detection_entries(
        entries_px=entries_px,
        pos_decimals=pos_decimals,
        conf_decimals=conf_decimals,
        keep_velocity=False,
    )

    signature = []

    for ball in normalized:
        signature.append(
            (
                ball.get("type"),
                ball.get("x"),
                ball.get("y"),
                ball.get("number"),
                ball.get("conf"),
            )
        )

    return tuple(signature)


def _ordered_detection_entries(entries_px: List[Dict]) -> List[Dict]:
    type_order = {
        BallType.CUE.value: 0,
        BallType.EIGHT.value: 1,
        BallType.SOLID.value: 2,
        BallType.STRIPE.value: 3,
        BallType.UNKNOWN.value: 4,
    }

    return sorted(
        entries_px or [],
        key=lambda ball: (
            type_order.get(ball.get("type"), 99),
            float(ball.get("x", 0.0)),
            float(ball.get("y", 0.0)),
            -float(ball.get("conf", 0.0) or 0.0),
        ),
    )


def line_balls(
    entries_px: List[Dict],
    discard_unknowns: bool = True,
    pos_decimals: int = 7,
    conf_decimals: int = 7,
    include_velocity: bool = False,
    vel_decimals: int = 7,
) -> str:
    parts = []

    for ball in _ordered_detection_entries(entries_px):
        ball_type = ball.get("type")

        if ball_type == BallType.UNKNOWN.value and discard_unknowns:
            continue

        if ball_type not in LABEL_MAP:
            ball_type = BallType.UNKNOWN.value

        x = float(ball.get("x", 0.0))
        y = float(ball.get("y", 0.0))
        conf = _fmt_num_or_backslash(ball.get("conf", None), conf_decimals)

        token = (
            f"{_fmt_float(x, pos_decimals)},"
            f"{_fmt_float(y, pos_decimals)},"
            f"{ball_type},"
            f"{conf}"
        )

        if include_velocity:
            vx = _fmt_num_or_backslash(ball.get("vx", None), vel_decimals)
            vy = _fmt_num_or_backslash(ball.get("vy", None), vel_decimals)
            token = f"{token},{vx},{vy}"

        parts.append(token)

    return f"{BALL_LINE_TOKEN} " + "; ".join(parts) if parts else ""


def _serialize_all_balls_legacy(
    entries_px: List[Dict],
    discard_diamonds: bool = True,
    pos_decimals: int = 7,
    conf_decimals: int = 7,
    vel_decimals: int = 7,
) -> List[str]:
    eight_parts, cue_parts, stripe_parts, solid_parts, unknown_parts = [], [], [], [], []

    for ball in entries_px or []:
        ball_type = ball.get("type")
        x = float(ball.get("x", 0.0))
        y = float(ball.get("y", 0.0))

        if ball_type == BallType.EIGHT.value:
            number = LABEL_MAP[BallType.EIGHT.value][1]
        elif ball_type == BallType.CUE.value:
            number = LABEL_MAP[BallType.CUE.value][1]
        else:
            number = ball.get("number", LABEL_MAP[BallType.UNKNOWN.value][1])

        conf = _fmt_num_or_backslash(ball.get("conf", None), conf_decimals)
        vx = _fmt_num_or_backslash(ball.get("vx", None), vel_decimals)
        vy = _fmt_num_or_backslash(ball.get("vy", None), vel_decimals)

        token = (
            f"{_fmt_float(x, pos_decimals)},"
            f"{_fmt_float(y, pos_decimals)},"
            f"{number},"
            f"{conf},"
            f"{vx},"
            f"{vy}"
        )

        if ball_type == BallType.EIGHT.value:
            eight_parts.append(token)
        elif ball_type == BallType.CUE.value:
            cue_parts.append(token)
        elif ball_type == BallType.STRIPE.value:
            stripe_parts.append(token)
        elif ball_type == BallType.SOLID.value:
            solid_parts.append(token)
        elif ball_type == BallType.UNKNOWN.value and not discard_diamonds:
            unknown_parts.append(token)

    zero_xy = f"{_fmt_float(0.0, pos_decimals)},{_fmt_float(0.0, pos_decimals)}"

    eight_line = f"{BallType.EIGHT.value} " + (
        eight_parts[0] if eight_parts else f"{zero_xy},{LABEL_MAP[BallType.EIGHT.value][1]},\\,\\,\\"
    )

    cue_line = f"{BallType.CUE.value} " + (
        cue_parts[0] if cue_parts else f"{zero_xy},{LABEL_MAP[BallType.CUE.value][1]},\\,\\,\\"
    )

    stripe_line = f"{BallType.STRIPE.value} " + "; ".join(stripe_parts)
    solid_line = f"{BallType.SOLID.value} " + "; ".join(solid_parts)
    unknown_line = ("u " + "; ".join(unknown_parts)) if (not discard_diamonds and unknown_parts) else ""

    return [eight_line, cue_line, stripe_line, solid_line, unknown_line]


def line_diamonds(
    diamond_entries: List[Dict],
    discard_diamonds: bool = True,
    pos_decimals: int = 7,
    conf_decimals: int = 4,
) -> str:
    if discard_diamonds:
        return ""

    parts = []

    for item in diamond_entries or []:
        x = float(item["x"])
        y = float(item["y"])
        idx = int(item["index"])
        conf = float(item.get("conf", 0.0))

        parts.append(
            f"{_fmt_float(x, pos_decimals)},"
            f"{_fmt_float(y, pos_decimals)},"
            f"{idx},"
            f"{_fmt_float(conf, conf_decimals)}"
        )

    return "d " + "; ".join(parts)


def p2p_classification_to_balltype(ball_id: int) -> str:
    if ball_id == 0:
        return BallType.STRIPE.value

    if ball_id == 1:
        return BallType.SOLID.value

    if ball_id == 2:
        return BallType.CUE.value

    if ball_id == 3:
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
    vel_decimals: int = 7,
    use_aggregate_ball_line: bool = True,
):
    lines = []

    pocket_line = line_pockets(pockets, decimals=pos_decimals)
    if pocket_line:
        lines.append(pocket_line)

    if use_aggregate_ball_line:
        ball_line = line_balls(
            detection_entries,
            discard_unknowns=discard_diamonds,
            pos_decimals=pos_decimals,
            conf_decimals=conf_decimals,
            include_velocity=False,
            vel_decimals=vel_decimals,
        )

        if ball_line:
            lines.append(ball_line)
    else:
        for line in _serialize_all_balls_legacy(
            detection_entries,
            discard_diamonds=discard_diamonds,
            pos_decimals=pos_decimals,
            conf_decimals=conf_decimals,
            vel_decimals=vel_decimals,
        ):
            if line:
                lines.append(line)

    return "\n".join(lines) + "\n"

def build_bootstrap_payloads(primary_ip: str, secondary_quest_ip: str,  configuration_name: Optional[str],):
    payloads = []

    if configuration_name:
        payloads.append(_line_configuration_name(configuration_name))

    if primary_ip and secondary_quest_ip:
        payloads.append(_line_quest_peers([
            {"ip": primary_ip, "role": "p"},
            {"ip": secondary_quest_ip, "role": "s"},
        ]))

    return payloads