from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Iterable, Optional, Sequence, TypeVar

T = TypeVar("T")


def ensure_directory(path: str | Path) -> Path:
    target = Path(path)
    target.mkdir(parents=True, exist_ok=True)
    return target


def load_json(path: str | Path) -> dict[str, Any]:
    target = Path(path)
    with target.open("r", encoding="utf-8") as file:
        return json.load(file)


def save_json(path: str | Path, payload: dict[str, Any]) -> None:
    target = Path(path)
    if target.parent:
        target.parent.mkdir(parents=True, exist_ok=True)
    with target.open("w", encoding="utf-8") as file:
        json.dump(payload, file, indent=2, ensure_ascii=False)


def tuple_or_none(value: Any) -> Optional[tuple[Any, ...]]:
    return tuple(value) if isinstance(value, (list, tuple)) else None


def float_tuple(value: Any, expected_length: int, default: Optional[Sequence[float]] = None) -> Optional[tuple[float, ...]]:
    if not isinstance(value, (list, tuple)) or len(value) != int(expected_length):
        return tuple(float(item) for item in default) if default is not None else None
    return tuple(float(item) for item in value)


def int_tuple(value: Any, expected_length: int, default: Optional[Sequence[int]] = None) -> Optional[tuple[int, ...]]:
    if not isinstance(value, (list, tuple)) or len(value) != int(expected_length):
        return tuple(int(item) for item in default) if default is not None else None
    return tuple(int(float(item)) for item in value)


def read_choice(prompt: str, valid_choices: Iterable[str], default: Optional[str] = None) -> str:
    valid = {str(choice).strip().lower() for choice in valid_choices}
    if default is not None and str(default).strip().lower() not in valid:
        raise ValueError(f"Default choice '{default}' is not in valid choices: {sorted(valid)}")

    while True:
        suffix = f" [default {default}]" if default is not None else ""
        raw = input(f"{prompt}{suffix}: ").strip().lower()
        if raw == "" and default is not None:
            return str(default).strip().lower()
        if raw in valid:
            return raw
        print(f"Invalid choice. Choose one of: {sorted(valid)}")


def read_bool(prompt: str, default: bool = False) -> bool:
    default_text = "Y/n" if default else "y/N"
    while True:
        raw = input(f"{prompt} ({default_text}): ").strip().lower()
        if raw == "":
            return bool(default)
        if raw in {"y", "yes", "true", "1"}:
            return True
        if raw in {"n", "no", "false", "0"}:
            return False
        print("Invalid value. Enter yes or no.")


def read_int(prompt: str, min_value: int, max_value: int, default: Optional[int] = None) -> int:
    while True:
        default_text = f" (default {default})" if default is not None else ""
        raw = input(f"{prompt} [{min_value}..{max_value}]{default_text}: ").strip()
        if raw == "" and default is not None:
            return int(default)
        try:
            value = int(float(raw))
            if int(min_value) <= value <= int(max_value):
                return value
        except ValueError:
            pass
        print("Invalid integer value.")


def read_optional_int(prompt: str, min_value: int, max_value: int, default: Optional[int] = None) -> Optional[int]:
    while True:
        default_text = f" (default {default})" if default is not None else " (Enter for none)"
        raw = input(f"{prompt} [{min_value}..{max_value}]{default_text}: ").strip()
        if raw == "":
            return default
        try:
            value = int(float(raw))
            if int(min_value) <= value <= int(max_value):
                return value
        except ValueError:
            pass
        print("Invalid optional integer value.")


def read_float(prompt: str, min_value: float, max_value: float, default: Optional[float] = None) -> float:
    while True:
        default_text = f" (default {default})" if default is not None else ""
        raw = input(f"{prompt} [{min_value}..{max_value}]{default_text}: ").strip()
        if raw == "" and default is not None:
            return float(default)
        try:
            value = float(raw)
            if float(min_value) <= value <= float(max_value):
                return value
        except ValueError:
            pass
        print("Invalid floating-point value.")


def read_optional_float(prompt: str, min_value: float, max_value: float, default: Optional[float] = None) -> Optional[float]:
    while True:
        default_text = f" (default {default})" if default is not None else " (Enter for none)"
        raw = input(f"{prompt} [{min_value}..{max_value}]{default_text}: ").strip()
        if raw == "":
            return default
        try:
            value = float(raw)
            if float(min_value) <= value <= float(max_value):
                return value
        except ValueError:
            pass
        print("Invalid optional floating-point value.")
