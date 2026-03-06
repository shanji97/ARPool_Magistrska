"""
A4 PDF with QR codes where the QR symbol itself (incl. quiet zone) is EXACTLY 10cm x 10cm.
The cut outline is exactly the same 10cm x 10cm square.

Python 3.12
pip install qrcode[pil] reportlab pillow
"""

from __future__ import annotations
from dataclasses import dataclass
from typing import List, Tuple

import qrcode
from qrcode.constants import ERROR_CORRECT_H
from PIL import Image

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas
from reportlab.lib.units import mm
from reportlab.lib.utils import ImageReader


# -----------------------------
# USER SETTINGS
# -----------------------------

LAYOUT = "2UP"  # "2UP" (recommended) or "4UP"
OUTPUT_PDF = "qr_10cm_exact_A4.pdf"

@dataclass(frozen=True)
class MarkerSpec:
    payload: str
    label: str

# Change payloads if needed
MARKERS: List[MarkerSpec] = [
    MarkerSpec(payload="ARPOOL_MARKER_01", label="M1"),
    MarkerSpec(payload="ARPOOL_MARKER_02", label="M2"),
    MarkerSpec(payload="ARPOOL_MARKER_03", label="M3"),
    MarkerSpec(payload="ARPOOL_MARKER_04", label="M4"),
]

# QR physical size (this is the QR symbol size INCLUDING quiet zone)
QR_SIZE_MM = 100.0  # 10 cm exactly

# Print quality
DPI = 600

# Cut / crop marks
CUT_LINE_MM = 0.30
CROP_MARK_LEN_MM = 4.0
CROP_MARK_GAP_MM = 0.8

PAGE_W, PAGE_H = A4


# -----------------------------
# Helpers
# -----------------------------

def mm_to_px(mm_value: float, dpi: int) -> int:
    return int(round((mm_value / 25.4) * dpi))

def make_qr_exact_10cm_png(payload: str) -> Image.Image:
    """
    Generates a QR code image (black/white) and scales it to EXACTLY QR_SIZE_MM at DPI.
    The size includes the quiet zone ('border' modules).
    """
    qr = qrcode.QRCode(
        version=None,                 # auto fit
        error_correction=ERROR_CORRECT_H,
        box_size=1,                   # generate minimal; we will scale precisely
        border=4,                     # quiet zone in modules (recommended)
    )
    qr.add_data(payload)
    qr.make(fit=True)

    img = qr.make_image(fill_color="black", back_color="white").convert("RGB")

    target_px = mm_to_px(QR_SIZE_MM, DPI)
    # NEAREST keeps edges crisp; no blur.
    img = img.resize((target_px, target_px), resample=Image.NEAREST)
    return img

def draw_crop_marks(c: canvas.Canvas, x: float, y: float, size: float) -> None:
    """Draw crop marks around a square cut box at (x,y) with side 'size'."""
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

def chunk(markers: List[Tuple[MarkerSpec, Image.Image]], n: int) -> List[List[Tuple[MarkerSpec, Image.Image]]]:
    return [markers[i:i+n] for i in range(0, len(markers), n)]


# -----------------------------
# PDF layout
# -----------------------------

def place_page(c: canvas.Canvas, page_markers: List[Tuple[MarkerSpec, Image.Image]]) -> None:
    size_pt = QR_SIZE_MM * mm  # cut box == QR size

    c.setLineWidth(CUT_LINE_MM * mm)

    if LAYOUT.upper() == "2UP":
        # Safer margins (avoids printer clipping of crop marks)
        margin_pt = 12 * mm

        x = (PAGE_W - size_pt) / 2.0
        y_top = PAGE_H - margin_pt - size_pt
        y_bottom = margin_pt

        positions = [(x, y_top)]
        if len(page_markers) > 1:
            positions.append((x, y_bottom))

    else:
        # 4UP (tight). If your printer clips, use 2UP.
        margin_pt = 6 * mm
        gutter_pt = 6 * mm

        col_x = [margin_pt, margin_pt + size_pt + gutter_pt]
        row_y_top = PAGE_H - margin_pt - size_pt
        row_y = [row_y_top, row_y_top - size_pt - gutter_pt]

        positions = []
        for i in range(len(page_markers)):
            col = i % 2
            row = i // 2
            positions.append((col_x[col], row_y[row]))

    for (spec, img), (x, y) in zip(page_markers, positions):
        c.drawImage(ImageReader(img), x, y, width=size_pt, height=size_pt)

        # Cut outline is EXACTLY 10x10cm (same as QR)
        c.rect(x, y, size_pt, size_pt, stroke=1, fill=0)

        # Crop marks outside the cut outline
        draw_crop_marks(c, x, y, size_pt)

        # Tiny label (inside the QR area, bottom-left). Remove if you want a "pure" QR.
        c.setFont("Helvetica", 8)
        c.drawString(x + 2 * mm, y + 2 * mm, spec.label)

    c.setFont("Helvetica", 8)
    c.drawString(10 * mm, 6 * mm, "Print at 100% / Actual size (NO Fit to page). QR square must measure 100 mm.")


def main() -> None:
    built = [(spec, make_qr_exact_10cm_png(spec.payload)) for spec in MARKERS]

    per_page = 2 if LAYOUT.upper() == "2UP" else 4
    pages = chunk(built, per_page)

    c = canvas.Canvas(OUTPUT_PDF, pagesize=A4)
    for p in pages:
        place_page(c, p)
        c.showPage()
    c.save()

    print(f"Created: {OUTPUT_PDF} (layout={LAYOUT}, qr_size={QR_SIZE_MM}mm @ {DPI}dpi)")
    print("IMPORTANT: print with scaling OFF (100% / Actual size).")

if __name__ == "__main__":
    main()