import time

from connection import UsbTcpSender
from formatters import build_conf_transfer_block


def synth_test():
    from ball_type import BallType
    usb_sender = UsbTcpSender()
    usb_sender.connect()

    pockets_xy_m = [
        (0.0320000, 1.2400000),
        (2.5080001, 1.2400000),
        (1.2700000, 0.0600000),
        (1.2700000, 1.2100000),
        (0.0320000, 0.0320000),
        (2.5080001, 0.0320000),
    ]
    

    # Synthetic balls — mix of solids, stripes, cue, eight
    entries = [
        # EIGHT
        {"type": BallType.EIGHT.value, "x": 1.2500000, "y": 0.6350000, "number": 8, "confidence": 0.97, "vx": 0.0,  "vy": 0.0},
        # CUE
        {"type": BallType.CUE.value,   "x": 1.2700000, "y": 0.4000000, "number": "/", "confidence": 0.92, "vx": 0.15, "vy": -0.10},

        # STRIPES (9–15)
        {"type": BallType.STRIPE.value,"x": 0.3000000, "y": 0.5000000, "number": 9,  "confidence": 0.88, "vx": 0.20, "vy": -0.05},
        {"type": BallType.STRIPE.value,"x": 0.4500000, "y": 0.5200000, "number": 10, "confidence": None, "vx": None, "vy": None},
        {"type": BallType.STRIPE.value,"x": 0.6000000, "y": 0.5400000, "number": 11, "confidence": None, "vx": -0.10,"vy": 0.00},
        {"type": BallType.STRIPE.value,"x": 0.7500000, "y": 0.5600000, "number": 12, "confidence": 0.66, "vx": 0.00, "vy": 0.00},
        {"type": BallType.STRIPE.value,"x": 0.9000000, "y": 0.5800000, "number": 13, "confidence": 0.80, "vx": 0.05, "vy": 0.02},
        {"type": BallType.STRIPE.value,"x": 1.0500000, "y": 0.6000000, "number": 14, "confidence": 0.74, "vx": -0.02,"vy": 0.03},
        {"type": BallType.STRIPE.value,"x": 1.2000000, "y": 0.6200000, "number": 15, "confidence": 0.60, "vx": None, "vy": 0.00},

        # SOLIDS (1–7)
        {"type": BallType.SOLID.value, "x": 0.3500000, "y": 0.3000000, "number": 1, "confidence": 0.95, "vx": 0.10, "vy": 0.00},
        {"type": BallType.SOLID.value, "x": 0.5000000, "y": 0.3200000, "number": 2, "confidence": 0.93, "vx": -0.12,"vy": 0.04},
        {"type": BallType.SOLID.value, "x": 0.6500000, "y": 0.3400000, "number": 3, "confidence": None, "vx": -0.05,"vy": None},
        {"type": BallType.SOLID.value, "x": 0.8000000, "y": 0.3600000, "number": 4, "confidence": 0.85, "vx": 0.00, "vy": 0.00},
        {"type": BallType.SOLID.value, "x": 0.9500000, "y": 0.3800000, "number": 5, "confidence": 0.70, "vx": None, "vy": None},
        {"type": BallType.SOLID.value, "x": 1.1000000, "y": 0.4000000, "number": 6, "confidence": 0.78, "vx": 0.03, "vy": -0.01},
        {"type": BallType.SOLID.value, "x": 1.2500000, "y": 0.4200000, "number": 7, "confidence": 0.82, "vx": 0.01, "vy": 0.02},
        
        # Unknown sample
        # {"type": BallType.SOLID.value, "x": 1.2500000, "y": 0.4200000, "number": "/"" "confidence": 0.82, "vx": 0.01, "vy": 0.02},
        # {"type": BallType.STRIPE.value, "x": 1.2500000, "y": 0.4200000, "number": "/"" "confidence": 0.82, "vx": 0.01, "vy": 0.02},
    ]

    payload = build_conf_transfer_block(
        pockets=pockets_xy_m,
        table_LW_m=(2.5400000, 1.2700000, 0.7850000),
        ball_diameter_m=0.0571500,
        camera_height_m=2.5,
        detection_entries=entries
    )

    while True:
        usb_sender.send(payload)
        time.sleep(0.1)