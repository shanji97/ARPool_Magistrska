# ARPool Python Module – Full Developer Guide

This document describes all Python components of the **ARPool_Magistrska** project. It covers installation, file responsibilities, data formats, running instructions, troubleshooting, and planned future work.

---

## 1. Installation and Setup

- **Python:** version **3.12**
- **Dependencies:** install with
  ```bash
  pip install -r PoolSimulatorComponents/CameraAnalysis/requirements.txt
  ```
  Typical packages include:
  - `opencv-python`
  - `numpy`
  - `ultralytics` (YOLO)
  - `PyYAML`
  - `requests`

- **External Tools:**
  - **ADB (Android Debug Bridge):** Required for USB port forwarding. Install Android Platform Tools, enable Developer Mode + USB debugging on Quest.
  - **DroidCam:** App + PC client to use phone camera as input.

- **ADB Setup:**
  ```bash
  adb devices
  adb forward tcp:5005 tcp:5005
  ```
  Unity app on Quest listens on port 5005.

---

## 2. File Overview

### `detection.py`
- **Role:** Main entry point. Captures frames, runs detection, formats results, and streams via USB.
- **Features:**
  - Uses `cv2.VideoCapture` to connect to DroidCam feed.
  - Invokes `ObjectDetector` for table/pocket/ball detection.
  - Calls `formatters.build_transfer_block` to generate payload.
  - Sends payload using `UsbTcpClient` from `connection.py`.
  - Provides keyboard controls for camera (torch, focus, lens switch).
  - Displays debug window with overlays and logs performance.

### `object_detector.py`
- **Role:** Implements detection logic.
- **Features:**
  - Modes: classical (threshold, Hough circles), YOLO, or BOTH.
  - YOLO (via Ultralytics) detects balls and classifies (cue, solids, stripes, eight).
  - Table detection: finds edges, corners, and computes homography.
  - Pocket detection: identifies six pockets from table geometry.
  - Ball detection: outputs center positions and optional ball numbers.
  - Stabilization: caches pocket positions until mask changes.

### `formatters.py`
- **Role:** Converts detection results into text payload.
- **Functions:**
  - `line_pockets(pockets)` → `p x,y;...`
  - `line_cue(cue)` → `c x,y,z`
  - `line_eight(eight)` → `e x,y,z`
  - `line_solids(list)` → `so x,y,z,n;...`
  - `line_stripes(list)` → `st x,y,z,n;...`
  - `line_table((L,W),y)` → `ts L,W,y`
  - `build_transfer_block(...)` → Assembles full block with `\n\n` terminator.

### `connection.py`
- **Role:** Handles TCP communication.
- **Class:** `UsbTcpClient`
  - Connects to `127.0.0.1:5005` (forwarded to Quest).
  - Methods: `connect()`, `send(data)`, `close()`.
  - Auto-reconnects if Quest app restarts.
- **Usage:** Called each frame by `detection.py`.

### `droid_cam_controller.py`
- **Role:** Communicates with DroidCam phone API.
- **Features:**
  - Torch toggle, autofocus/manual focus, zoom, exposure, lens switching.
  - Uses `requests` to send HTTP PUT/GET to DroidCam server (`http://<phone_ip>:4747/...`).
  - Maintains current camera index for cycling lenses.
- **Integration:** Controlled via keybindings in `detection.py`.

### `calibration.py` (if present)
- **Role:** Runs chessboard/charuco calibration routines.
- **Features:**
  - Loads calibration images from `Images/Calibration/...`.
  - Uses OpenCV calibration functions to compute intrinsics/extrinsics.
  - Saves calibration data (RMS error, matrices) to file for reuse.
- **Purpose:** Enables mapping pixel coords → metric coords for true 3D.

### `stats_logger.py` (if present)
- **Role:** Provides CSV/Parquet logging of detections.
- **Features:**
  - Saves frame-by-frame pockets, balls, detection confidences.
  - Optionally logs camera metadata (lens, torch state).
- **Usage:** Helps analyze jitter, false detections.

---

## 3. Data Format Specification

Each payload is a text block with lines, terminated by `\n\n`.

- **Pockets (p):**
  ```
  p x1,y1; x2,y2; x3,y3; x4,y4; x5,y5; x6,y6
  ```
  Order: TL, TR, ML, MR, BL, BR. Coordinates in meters.

- **Cue (c):**
  ```
  c x,y,z
  ```

- **Eight (e):**
  ```
  e x,y,z
  ```

- **Solids (so):**
  ```
  so x,y,z,n; x,y,z,n; ...
  ```

- **Stripes (st):**
  ```
  st x,y,z,n; ...
  ```

- **Table summary (ts):**
  ```
  ts length,width,y
  ```

---

## 4. Running the Pipeline

1. Start DroidCam app on phone + connect to PC client.
2. Plug Quest via USB, enable debugging, run:
   ```bash
   adb forward tcp:5005 tcp:5005
   ```
3. Start Unity app on Quest.
4. Launch Python detection:
   ```bash
   python PoolSimulatorComponents/CameraAnalysis/detection.py
   ```
5. Observe:
   - PC shows detection overlay window.
   - Unity shows pockets/balls in 3D.

**Keyboard shortcuts in detection.py:**
- `q` / Esc → quit
- `t` → torch toggle
- `f` → manual focus
- `z` → zoom
- `0-3` → switch phone camera lens

---

## 5. Troubleshooting

- **No Unity updates:** Check ADB forwarding with `adb forward --list`. Ensure Unity app is running.
- **Quest not detected:** Ensure Developer Mode, correct USB cable, `adb devices` lists Quest.
- **Camera not opening:** Adjust `cv2.VideoCapture` index or URL for DroidCam.
- **Laggy:** Lower resolution (720p), ensure GPU YOLO (CUDA), adjust detection mode.
- **Jitter:** Use lock mode in Unity, improve lighting, stabilize camera mount.

---

## 6. Future Work

- **MQTT integration:** For wireless updates + lock synchronization.
- **Persistent lock:** Unity lock command propagates back to Python.
- **3D calibration:** Use calibration profiles for accurate z-coordinates.
- **Ball ID tracking:** Add SORT/DeepSORT or YOLOv8 tracking to maintain ball identities.
- **Packaging:** Provide executable build (PyInstaller) for easier deployment.

---