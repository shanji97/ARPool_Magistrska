# 📷 Camera Pool Table Analysis Instructions

## 1. 🐍 Install Python 3.13 and Requirements

Install Python and required packages:

```bash
pip install -r requirements.txt
```

> ⚠️ Make sure the filename is `requirements.txt`, not `requitements.txt` or something else!

---

## 2. 📱 Set Up DroidCam

### ✅ A. Install DroidCam

- **PC Client**:  
  Download and install the **DroidCam Client** for [Windows 10/11 or Linux](https://droidcam.app/).  
  *Note: The OBS plugin is **not** required.*

- **Mobile App**:  
  Install **DroidCam Webcam & OBS Camera** for:
  - [Android](https://play.google.com/store/apps/details?id=com.dev47apps.obsdroidcam&pli=1)  
  - [iOS](https://apps.apple.com/si/app/droidcam-webcam-obs-camera/id1510258102)  

> 💡 You can purchase the PRO version or just watch ads (😆).

---

### ✅ B. Configure the Phone App

1. **Note the IP address and port** of the device (usually port `4747`).
2. **Allow local network access** so the PC can connect to the camera.
3. Set the following **camera settings**:
   - Use the **back camera** by default.
   - Set **maximum FPS**.
   - Set **target bitrate to high**.
4. Disable any **unnecessary features** to reduce latency.

---

### ✅ C. Configure the DroidCam Client on PC

1. Open the DroidCam Client on your PC.
2. In **source properties**, configure:
   - **Resolution**: `1920x1080`
   - **Video Format**: `AVC/H.264`
3. **If your device is detected**:
   - Select it from the dropdown list.
4. **If not detected**:
   - Manually enter the IP and port from your phone.
5. Enable:
   - **Audio input**
   - **AVC/H.264 hardware acceleration**
6. Click **"Activate"** to start the stream.

> 🎉 You should now see your phone camera as a webcam source on your PC!

---

## ⚠️ Final Notes

- Do **not** touch camera privacy settings or remove devices in **Device Manager**.
- Your camera feed should now appear in your camera app and any compatible desktop software.
- You may see **multiple camera sources** if you previously had virtual cameras—choose the one marked as DroidCam.

---

## 🖼️ Screenshots

![Phone App Settings](/Images/Screenshots/settings.PNG)

![Permissions on Phone](/Images/Screenshots/permissions.PNG)

![PC Client Settings](/Images/Screenshots/client.png)
