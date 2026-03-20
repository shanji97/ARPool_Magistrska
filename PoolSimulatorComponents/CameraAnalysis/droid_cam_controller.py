import requests
import os
import json
import socket
from enum import Enum
from typing import Tuple, Optional

from calibration import Calibrator

class FocusMode(Enum):
    Auto = 0
    Continuous = 1
    Manual = 2
    
class Camera(Enum):
    Front = 0
    Main = 1
    Telephoto = 2
    Ultrawide = 3
    
class WhiteBalance(Enum):
    Candle = 2000
    Tungsten = 2850
    Fluorescent = 4000
    Daylight = 5500
    Cloudy = 6500
    Shade = 7500
    Custom_1 = 4100 # My bedroom

class DroidCamController:
    
    CONF_PATH = "./Configuration"
    TORCH_STATE_JSON = "torch_state.json"
    TORCH_STATE_CONF = f"{CONF_PATH}/{TORCH_STATE_JSON}"
    
    CAMERA_MAP = {
    0: {"name": "Front", "torch_supported": False, "folder_alias": "front", 
        "lens_correction_on": False, "lens_correction_manual_control":True, "os": "ios_18.7.2"},  
    1: {"name": "Main", "torch_supported": True, "folder_alias": "main", 
        "lens_correction_on": True, "lens_correction_manual_control": False, "os": "ios_18.7.2"},
    2: {"name": "Telephoto", "torch_supported": True, "folder_alias": "tp", 
        "lens_correction_on": True, "lens_correction_manual_control":False, "os": "ios_18.7.2"},
    3: {"name": "Ultrawide", "torch_supported": True, "folder_alias": "uw_wth_lens_dist", 
        "lens_correction_on": False, "lens_correction_manual_control":True, "os": "ios_18.7.2"},
    }

    ZOOM_RANGE = (1.0, 6.0)
    EV_RANGE = (-8.0, 8.0)
    WB_RANGE = (2000, 8000)
    MF_RANGE = (0.0, 1.0)

    def __init__(self, ip: str, port: str = "4747"):
        self.ip = ip
        self.port = port
        self.base_url = f'http://{ip}:{port}'
        self.current_camera = Camera.Main.value
        self.info = None
        self.manual_focus_value = 0.5 
        self.torch_state = False
        self._load_torch_state()
        self.apply_default_settings()
        
    def __init__(self, connection: Tuple[str, str]):
        self.ip, self.port = connection
        self.__init__(self.ip,self.port)
        
    def get_stream_url(self, resolution: str):
        return f'{self.base_url}/video?{resolution}'   
        
    def _load_torch_state(self):
        torch_state_defaults = {"1": False, "2": False, "3": False}
        if os.path.exists(self.TORCH_STATE_CONF):
            with open(self.TORCH_STATE_CONF, "r") as file:
                try:
                    data = json.load(file)
                    if isinstance(data, dict):
                        self.torch_state = data.get("torch_state", torch_state_defaults)
                        if isinstance(self.torch_state, bool):
                           
                            self.torch_state = torch_state_defaults
                    else:
                        self.torch_state = torch_state_defaults
                except Exception as e:
                    print(f"Failed to load torch state config: {e}")
                    self.torch_state = torch_state_defaults
        else:
            self.torch_state = torch_state_defaults
            
    def _save_torch_state(self):
        os.makedirs(self.CONF_PATH, exist_ok=True)
        with open(self.TORCH_STATE_CONF, "w") as file:
            json.dump(self.torch_state, file)
            
    def _put(self, endpoint):
        url = f"{self.base_url}{endpoint}"
        try:
            r = requests.put(url, timeout=2)
            r.raise_for_status()
        except Exception as e:
            print(f"Failed PUT {url}: {e}")
    
    def get_camera_info(self):
        url = f"{self.base_url}/v1/camera/info"
        try:
            r = requests.get(url, timeout = 2)
            r.raise_for_status()
            self.info = r.json()
            return self.info
        except Exception as e:
            print(f"Failed to get camera info: {e}")
            return {}
    
    def select_camera(self, camera_id):
        if camera_id not in self.CAMERA_MAP:
            print(f"Camera ID {camera_id} not recognized.")
            return
        self._put(f"/v1/camera/active/{camera_id}")
        if self.current_camera != camera_id:
            self.current_camera = camera_id
            print(f"Switched to {self.CAMERA_MAP[camera_id]['name']}")
            self._sync_torch_state() 
    
    def _sync_torch_state(self):
        cam_id = str(self.current_camera)
        if self.CAMERA_MAP[self.current_camera]['torch_supported']:
            desired_state = self.torch_state.get(cam_id, False)
            info = self.get_camera_info()
            actual_state = info.get("led_on", 0) == 1
            print(f"Torch Sync for Camera {cam_id}: Desired={'ON' if desired_state else 'OFF'} | Actual={'ON' if actual_state else 'OFF'}")
            if desired_state != actual_state:
                self._put("/v1/camera/torch_toggle")
                self.torch_state[cam_id] = actual_state
                self._save_torch_state()
        else:
            print("Torch not available on current camera.")

    def toggle_torch(self):
        camera_id = str(self.current_camera)
        if self.CAMERA_MAP[self.current_camera]['torch_supported']:
            self._put("/v1/camera/torch_toggle")
            current_state = self.torch_state.get(camera_id, False)
            self.torch_state[camera_id] = not current_state
            self._save_torch_state()
            print(f"Torch manually toggled to: {'ON' if self.torch_state[camera_id] else 'OFF'} for Camera {camera_id}")
        else:
            print("Torch toggle ignored: not supported on current camera.")
    
    def reset_all_torch_states(self):
        self.torch_state = {"1" : False, "2" : False, "3" : False}
        self._save_torch_state()
        print("All torch states reset to OFF.")
    
    def set_zoom(self, level):
        level = max(self.ZOOM_RANGE[0], min(self.ZOOM_RANGE[1], level))
        self._put(f"/v3/camera/zoom/{level}")

    def set_exposure(self, value):
        value = max(self.EV_RANGE[0], min(self.EV_RANGE[1], value))
        self._put(f"/v3/camera/ev/{value}")

    def set_white_balance(self, kelvin):
        kelvin = max(self.WB_RANGE[0], min(self.WB_RANGE[1], kelvin))
        self._put(f"/v3/camera/wb/{kelvin}")

    def set_manual_focus(self, value):
        value = max(self.MF_RANGE[0], min(self.MF_RANGE[1], value))
        self._put(f"/v3/camera/mf/{value}")
    
    def set_manual_focus_value(self, value):
        value = max(self.MF_RANGE[0], min(self.MF_RANGE[1], value))
        self.manual_focus_value = value  # Save for future sync
        info = self.get_camera_info()
        if info.get("focusMode") == FocusMode.Manual.value:
            actual_focus = info.get("mfValue", 0)
            if abs(value - actual_focus) > 0.01:
                print(f"Setting Manual Focus to {value}")
                self.set_manual_focus(value)
    
    def set_focus_mode(self, mode: int):
        if mode not in (FocusMode.Auto.value, FocusMode.Continuous.value, FocusMode.Manual.value):
            print(f"Invalid focus mode: {mode}")
            return
        current_info = self.get_camera_info()
        current_mode = current_info.get("focusMode", -1)

        if mode != current_mode:
            print(f"Switching focus mode to {mode}")
            self._put(f"/v1/camera/autofocus_mode/{mode}")
        if mode == FocusMode.Manual.value:
             actual_focus = current_info.get("mfValue", -1)
             minimum_sync_error = 0.01
             if abs(self.manual_focus_value - actual_focus) > minimum_sync_error:
                print(f"Syncing Manual Focus to {self.manual_focus_value}")
                self.set_manual_focus(self.manual_focus_value)

    def set_wb_mode(self, mode):
        self._put(f"/v1/camera/wb_mode/{mode}")

    def sync_all_locks(self, exposure_lock = True, wb_lock = True):
        info = self.get_camera_info()
        current_exposure_lock = info.get("exposure_lock", 0) == 1
        current_wb_lock = current_wb_lock = info.get("wbLock", 0) == 1
        
        if exposure_lock != current_exposure_lock:
            print(f"{'Locking' if exposure_lock else 'Unlocking'} Exposure Lock")
            self._put("/v1/camera/el_toggle")

        if wb_lock != current_wb_lock:
            print(f"{'Locking' if wb_lock else 'Unlocking'} White Balance Lock")
            self._put("/v1/camera/wbl_toggle")

    def apply_default_settings(self):
        self.select_camera(Camera.Main.value)
        self.set_zoom(1.0)
        self.set_exposure(0)
        self.set_white_balance(WhiteBalance.Custom_1.value)
        self.set_focus_mode(FocusMode.Auto.value)
        print("Default settings applied.")
        self.sync_all_locks()
        self._sync_torch_state()
            
    def is_host_reachable(self, timeout = 2):
        try:
            with socket.create_connection((self.ip, int(self.port)), timeout):
                return True
        except(socket.timeout, socket.error):
            return False
    
    def send_camera_command(self, command: str, *args, suppres_info: bool = False ,calibrator: Optional[Calibrator] = None):
    
        is_changing_camera = False
        reset_pocket_globals = False
        
        if command == "toggle_torch":
            self.toggle_torch()
        elif command == "reset_torch":
            self.reset_all_torch_states()
        elif command == "set_focus_mode":
            if args:
                self._controller.set_focus_mode(args[0])
        elif command == "set_manual_focus_value":
            if args:
                self.set_manual_focus_value(args[0])
        elif command == "set_zoom":
            if args:
                self.set_zoom(args[0])
        elif command == "set_exposure":
            if args:
                self.set_exposure(args[0])
        elif command == "set_white_balance":
            if args:
                self.set_white_balance(args[0])
        elif command == "sync_all_locks":
            self.sync_all_locks()
        elif command == "apply_defaults":
            self.apply_default_settings()
        elif command == "select_camera":
            if args and calibrator is not None:
                is_changing_camera = True
                self.select_camera(args[0])   
                calibrator._load_intrinsics_for_camera(args[1])
                reset_pocket_globals = True
                reset_pocket_globals()
        elif command == "get_stream_url":
                return self.get_stream_url(args[0])
        elif command == "dump_camera_info":
                info = self.get_camera_info()
                if info and not suppres_info:
                    print(json.dumps(info, indent=2))
                    return info, is_changing_camera, reset_pocket_globals
                if info is None:
                    print("Failed to get camera info.")
                    return None, info, is_changing_camera, reset_pocket_globals
        else:
            print(f"Unknown command: {command}")
            return None, is_changing_camera, reset_pocket_globals
