"""
Generate large ArUco markers for computer vision calibration.

Each page contains ONE ArUco marker:
- black marker square = exactly MARKER_SIZE_MM x MARKER_SIZE_MM
- white cutout margin = exactly WHITE_MARGIN_MM on each side
- cutout square = MARKER_SIZE_MM + 2 * WHITE_MARGIN_MM

Designed for A4 printing at 100% scale.
Print with scaling disabled: use "Actual size" / "100%", not "Fit to page".

Python 3.12
pip install opencv-contrib-python reportlab pillow numpy
"""

from dataclasses import dataclass
from typing import List

import cv2
import numpy as np
from PIL import Image

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from reportlab.lib.units import mm
from reportlab.lib.utils import ImageReader


# -----------------------------
# CONFIGURATION
# -----------------------------

# OpenCV ArUco dictionary.
# DICT_4X4_50 is recommended for this project because the marker cells are larger
# than 5x5/6x6 dictionaries at the same physical marker size.
ARUCO_DICTIONARY_NAME = "DICT_4X4_50"

# Marker IDs to generate. This creates 10 markers: 0..9.
MARKER_IDS = list(range(10))

# Physical dimensions.
# MARKER_SIZE_MM is the black ArUco square itself.
# WHITE_MARGIN_MM is the white paper around the black marker on each side.
MARKER_SIZE_MM = 130.0
WHITE_MARGIN_MM = 30.0
CUTOUT_SIZE_MM = MARKER_SIZE_MM + (2.0 * WHITE_MARGIN_MM)

# Rasterization DPI used before embedding in the PDF.
# The final physical size is controlled by ReportLab points, not by image DPI.
DPI = 600

OUTPUT_FILE = (
    f"aruco_markers_{ARUCO_DICTIONARY_NAME}_{int(MARKER_SIZE_MM)}mm_"
    f"margin{int(WHITE_MARGIN_MM)}mm_A4.pdf"
)

# Keep the ID label outside the cut outline by default.
# If set to True, a small ID is printed inside the white margin of the cutout.
# Keeping this False is safer for marker detection.
PRINT_ID_INSIDE_CUTOUT = False

# Crop/cut styling.
CROP_MARK_LEN_MM = 5.0
CROP_MARK_GAP_MM = 1.0
CUT_LINE_MM = 0.35


@dataclass(frozen=True)
class MarkerSpec:
    marker_id: int
    label: str


MARKERS: List[MarkerSpec] = [
    MarkerSpec(marker_id=marker_id, label=f"ARUCO ID {marker_id:02}")
    for marker_id in MARKER_IDS
]


# -----------------------------
# UTILS
# -----------------------------

def mm_to_px(mm_value: float, dpi: int) -> int:
    return int(round((float(mm_value) / 25.4) * int(dpi)))


def get_aruco_dictionary(dictionary_name: str):
    if not hasattr(cv2, "aruco"):
        raise RuntimeError(
            "cv2.aruco is not available. Install opencv-contrib-python, not plain opencv-python."
        )

    if not hasattr(cv2.aruco, dictionary_name):
        raise ValueError(f"Unknown ArUco dictionary: {dictionary_name}")

    dictionary_id = getattr(cv2.aruco, dictionary_name)
    return cv2.aruco.getPredefinedDictionary(dictionary_id)


def make_aruco_marker(marker_id: int, dictionary_name: str, marker_size_mm: float, dpi: int) -> Image.Image:
    dictionary = get_aruco_dictionary(dictionary_name)
    marker_px = mm_to_px(marker_size_mm, dpi)

    if hasattr(cv2.aruco, "generateImageMarker"):
        marker = cv2.aruco.generateImageMarker(dictionary, int(marker_id), int(marker_px))
    elif hasattr(cv2.aruco, "drawMarker"):
        marker = np.zeros((int(marker_px), int(marker_px)), dtype=np.uint8)
        cv2.aruco.drawMarker(dictionary, int(marker_id), int(marker_px), marker, 1)
    else:
        raise RuntimeError("This OpenCV build has no supported ArUco marker generation function.")

    return Image.fromarray(marker).convert("RGB")


def draw_crop_marks(c: canvas.Canvas, x: float, y: float, size: float) -> None:
    length = CROP_MARK_LEN_MM * mm
    gap = CROP_MARK_GAP_MM * mm

    # bottom-left
    c.line(x - gap - length, y, x - gap, y)
    c.line(x, y - gap - length, x, y - gap)

    # bottom-right
    c.line(x + size + gap, y, x + size + gap + length, y)
    c.line(x + size, y - gap - length, x + size, y - gap)

    # top-left
    c.line(x - gap - length, y + size, x - gap, y + size)
    c.line(x, y + size + gap, x, y + size + gap + length)

    # top-right
    c.line(x + size + gap, y + size, x + size + gap + length, y + size)
    c.line(x + size, y + size + gap, x + size, y + size + gap + length)


# -----------------------------
# PDF GENERATION
# -----------------------------

def generate_pdf() -> None:
    page_width, page_height = A4
    marker_size_pt = MARKER_SIZE_MM * mm
    cutout_size_pt = CUTOUT_SIZE_MM * mm
    margin_pt = WHITE_MARGIN_MM * mm

    if cutout_size_pt > page_width or cutout_size_pt > page_height:
        raise ValueError(
            f"Cutout size {CUTOUT_SIZE_MM:.1f} mm does not fit on A4. "
            f"Reduce MARKER_SIZE_MM or WHITE_MARGIN_MM."
        )

    pdf = canvas.Canvas(OUTPUT_FILE, pagesize=A4)

    for marker in MARKERS:
        image = make_aruco_marker(
            marker_id=marker.marker_id,
            dictionary_name=ARUCO_DICTIONARY_NAME,
            marker_size_mm=MARKER_SIZE_MM,
            dpi=DPI,
        )

        cutout_x = (page_width - cutout_size_pt) / 2.0
        cutout_y = (page_height - cutout_size_pt) / 2.0
        marker_x = cutout_x + margin_pt
        marker_y = cutout_y + margin_pt

        # White cutout area. This is intentionally explicit so printed pages are clean.
        pdf.setFillColorRGB(1, 1, 1)
        pdf.rect(cutout_x, cutout_y, cutout_size_pt, cutout_size_pt, fill=1, stroke=0)

        # ArUco marker. This black square must measure MARKER_SIZE_MM after printing.
        pdf.drawImage(
            ImageReader(image),
            marker_x,
            marker_y,
            width=marker_size_pt,
            height=marker_size_pt,
        )

        # Cut outline around the full marker + white margin cutout.
        pdf.setStrokeColorRGB(0, 0, 0)
        pdf.setLineWidth(CUT_LINE_MM * mm)
        pdf.rect(cutout_x, cutout_y, cutout_size_pt, cutout_size_pt, fill=0, stroke=1)

        # Crop marks around the full cutout.
        draw_crop_marks(pdf, cutout_x, cutout_y, cutout_size_pt)

        # Page label outside the cutout. Cut this away or write the ID on the back after cutting.
        pdf.setFont("Helvetica", 10)
        label_y = max(10 * mm, cutout_y - 14 * mm)
        pdf.drawString(cutout_x, label_y, f"{marker.label} | {ARUCO_DICTIONARY_NAME}")
        pdf.drawString(
            cutout_x,
            label_y - 5 * mm,
            f"black square: {MARKER_SIZE_MM:.0f} mm | white margin: {WHITE_MARGIN_MM:.0f} mm | cutout: {CUTOUT_SIZE_MM:.0f} mm",
        )
        pdf.drawString(cutout_x, label_y - 10 * mm, "Print at 100% scale. Do not use Fit to page.")

        if PRINT_ID_INSIDE_CUTOUT:
            pdf.setFont("Helvetica", 8)
            pdf.drawString(cutout_x + 5 * mm, cutout_y + 5 * mm, f"ID {marker.marker_id:02}")

        pdf.showPage()

    pdf.save()

    print("PDF generated:", OUTPUT_FILE)
    print("IMPORTANT: Print at 100% scale. Disable Fit to page.")
    print(f"Black ArUco square must measure exactly {MARKER_SIZE_MM:.1f} mm.")
    print(f"Full cutout square must measure exactly {CUTOUT_SIZE_MM:.1f} mm.")
    print(f"Dictionary: {ARUCO_DICTIONARY_NAME}")
    print(f"Marker IDs: {MARKER_IDS}")


if __name__ == "__main__":
    generate_pdf()
