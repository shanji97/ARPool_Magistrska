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
        
        
def build_issue_83_middle_lower_pocket_block() -> str:
    """
    Raw USB payload for ISSUE-83.
    One stripe is intentionally very close to the lower-middle pocket and should be
    suppressed or marked ambiguous by Unity near-pocket logic.

    Lower-middle pocket in this payload:
        (1.2700000, 0.0600000)

    Stripe expected to trigger ISSUE-83 behavior:
        (1.2302539, -0.0113007)
    """
    return (
        "E predator_9ft_virtual_debug.json\n"
        "p 0.0320000,1.2400000;2.5080001,1.2400000;1.2700000,0.0600000;1.2700000,1.2100000;0.0320000,0.0320000;2.5080001,0.0320000\n"
        "e 0.6196690,0.5729381,8,0.91796875,\\,\\\n"
        "c 0.1438348,0.5885691,/,0.935546875,\\,\\\n"
        "st 2.1871898,1.1307166,u,0.94091796875,\\,\\; 1.6080190,0.4053252,u,0.93994140625,\\,\\; 1.8339624,1.0690025,u,0.92431640625,\\,\\; 2.1732988,0.4029377,u,0.92333984375,\\,\\; 0.4337689,0.7016096,u,0.91845703125,\\,\\; 1.0316985,0.4764176,u,0.9111328125,\\,\\; 1.2302539,-0.0113007,u,0.8681640625,\\,\\\n"
        "so 0.2275915,0.5222517,u,0.93505859375,\\,\\; 0.2587466,1.1564102,u,0.93115234375,\\,\\; 0.5787677,0.2162453,u,0.92431640625,\\,\\; 1.9773390,0.2994787,u,0.92431640625,\\,\\; 1.6321940,0.5848715,u,0.9228515625,\\,\\; 1.3385810,0.4352357,u,0.9208984375,\\,\\\n"
        "t L=2.5400000; W=1.2700000; H=0.7850000; B=0.0571500; C=2.5000000\n"
    )


def issue_83_middle_lower_pocket_test(repeat_delay_s: float = 0.1) -> None:
    """
    Repeatedly sends the dedicated ISSUE-83 regression payload over the USB TCP sender.

    Use this after:
    1. Unity app is running on Quest
    2. Environment and pockets are already loaded / confirmed
    3. UsbSocketReceiver is already listening
    """
    usb_sender = UsbTcpSender()
    usb_sender.connect()

    payload = build_issue_83_middle_lower_pocket_block()

    print("[USB TEST] Connected.")
    print("[USB TEST] Sending ISSUE-83 payload repeatedly.")
    print("[USB TEST] Expected Unity behavior: one stripe near lower-middle pocket is suppressed or shown only as debug state.")

    try:
        while True:
            usb_sender.send(payload)
            time.sleep(repeat_delay_s)
    except KeyboardInterrupt:
        print("[USB TEST] Stopped by user.")