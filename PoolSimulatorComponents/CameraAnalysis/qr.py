"""
Generate large QR markers for computer vision calibration.

Each page contains ONE QR code:
QR size = EXACTLY the specified QR_SIZE_MM (height) * QR_SIZE_MM (width) dimensions

Edges are marked with a cut outline and crop marks.

Designed for A4 printing at 100% scale.

Python 3.12
pip install qrcode[pil] reportlab pillow
"""

from dataclasses import dataclass
from typing import List

import qrcode
from qrcode.constants import ERROR_CORRECT_H
from PIL import Image

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from reportlab.lib.units import mm
from reportlab.lib.utils import ImageReader


# -----------------------------
# CONFIGURATION
# -----------------------------

QR_SIZE_MM = 165
DPI = 600

OUTPUT_FILE = f"qr_markers_{QR_SIZE_MM}mm_A4.pdf"

NUMBER_OF_MARKERS = 12


@dataclass(frozen=True)
class MarkerSpec:
    payload: str
    label: str


MARKERS: List[MarkerSpec] = [
    MarkerSpec(payload=f"ARPOOL_MARKER_{i:02}", label=f"M{i:02}")
    for i in range(1, NUMBER_OF_MARKERS + 1)
]


# Crop marks
CROP_MARK_LEN_MM = 5.0
CROP_MARK_GAP_MM = 1.0
CUT_LINE_MM = 0.35


# -----------------------------
# UTILS
# -----------------------------

def mm_to_px(mm_value: float, dpi: int) -> int:
    return int(round((mm_value / 25.4) * dpi))


def make_qr(payload: str) -> Image.Image:
    qr = qrcode.QRCode(
        version=None,
        error_correction=ERROR_CORRECT_H,
        box_size=1,
        border=4,
    )

    qr.add_data(payload)
    qr.make(fit=True)

    img = qr.make_image(fill_color="black", back_color="white").convert("RGB")

    target_px = mm_to_px(QR_SIZE_MM, DPI)

    img = img.resize((target_px, target_px), Image.NEAREST)

    return img


def draw_crop_marks(c: canvas.Canvas, x: float, y: float, size: float):

    L = CROP_MARK_LEN_MM * mm
    G = CROP_MARK_GAP_MM * mm

    # bottom-left
    c.line(x - G - L, y, x - G, y)
    c.line(x, y - G - L, x, y - G)

    # bottom-right
    c.line(x + size + G, y, x + size + G + L, y)
    c.line(x + size, y - G - L, x + size, y - G)

    # top-left
    c.line(x - G - L, y + size, x - G, y + size)
    c.line(x, y + size + G, x, y + size + G + L)

    # top-right
    c.line(x + size + G, y + size, x + size + G + L, y + size)
    c.line(x + size, y + size + G, x + size, y + size + G + L)


# -----------------------------
# PDF GENERATION
# -----------------------------

def generate_pdf():

    PAGE_W, PAGE_H = A4
    qr_size_pt = QR_SIZE_MM * mm

    c = canvas.Canvas(OUTPUT_FILE, pagesize=A4)

    for marker in MARKERS:

        img = make_qr(marker.payload)

        x = (PAGE_W - qr_size_pt) / 2
        y = (PAGE_H - qr_size_pt) / 2

        c.drawImage(
            ImageReader(img),
            x,
            y,
            width=qr_size_pt,
            height=qr_size_pt
        )

        # cut outline
        c.setLineWidth(CUT_LINE_MM * mm)
        c.rect(x, y, qr_size_pt, qr_size_pt)

        # crop marks
        draw_crop_marks(c, x, y, qr_size_pt)

        # label
        c.setFont("Helvetica", 10)
        c.drawString(x + 5 * mm, y + 5 * mm, marker.label)

        c.showPage()

    c.save()

    print("PDF generated:", OUTPUT_FILE)
    print("IMPORTANT: Print at 100% scale (disable 'Fit to page').")
    print(f"The QR square must measure exactly {QR_SIZE_MM} mm.")


if __name__ == "__main__":
    generate_pdf()