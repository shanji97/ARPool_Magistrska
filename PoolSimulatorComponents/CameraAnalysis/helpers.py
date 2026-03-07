import json
from pathlib import Path
import time

from connection import UsbTcpSender
from formatters import(
    line_configuration_name
)

CONFIG_PATH = Path("../Configuration")
CONFIG_PATH.mkdir(parents=True, exist_ok=True)

def _purge_cache():
    import subprocess
    import os
    try:
        print("Trying to clean python package cache with 'python -m pip cache purge' to remove GiB worth of cached packets.")
        result = subprocess.run(["python", "-m", "pip", "cache", "purge"], check=True)
        print(result.stdout)
        os.system("cls")
    except subprocess.CalledProcessError as e:
        print(f"Error clearing pip cache: {e}. Try cleaning it manually.")
        
def install_dependecies_for_other_projects(sub_folders):
    import subprocess
    import os
    installed_text = "installed.txt"
    print("Installing dependencies for other projects.....")
    for folder in sub_folders:
        if os.path.exists(os.path.join(folder, installed_text)):
            continue
        req_file = os.path.join(folder,"requirements.txt")
        if not subprocess.run(["pip", "install", "-r", req_file], check=True):
            print(f"Failed to install other project dependencies which are neccessary for this project. Requirements txt: {req_file}.")
        else:
            with open(os.path.join(folder, installed_text), "w") as file:
                file.write("Dependecies successfully installed.")      
    _purge_cache()

def _persist_connection_data(ip: str, port: str, device: str):
    import json
    path = CONFIG_PATH / f"{device}_network_data.json"
    data = {}
    if path.exists():
        try:
             with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception:
            pass
    data[device] = {"ip": ip, "port": port}
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    print(f"[INFO] Persisted connection '{device}' -> {ip}:{port}")
    
def _load_connection_data(device: str):
    path = CONFIG_PATH / f"{device}_network_data.json"
    if not path.exists():
        return None
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        return data.get(device)
    except Exception as e:
        print(f"[WARN] Failed to load connection data: {e}")
        return None
    
def _validate_ip(ip: str):
    import re
    pattern = r"^\d{1,3}(\.\d{1,3}){3}$"
    return re.match(pattern, ip) is not None

def setup_connection(connect_to_quest: bool = False, is_editor_build: bool = False ) -> tuple[str, str]: 
    key: str = "quest_3_primary" if connect_to_quest else "droid_cam"
    cached = _load_connection_data(key)
    if cached:
        print(f"[INFO] Using cached {key}: {cached['ip']}:{cached['port']}")
        return cached["ip"], cached["port"]
    
    ip: str = input(
        "Enter Quest 3 IP address (e.g., 192.168.0.40): "
        if connect_to_quest else
        "Enter DroidCam IP address (e.g., 192.168.0.40): ").strip()
        
    while not _validate_ip(ip):
        print("Invalid IP format. Try again.")
        if connect_to_quest:
            ip = input("IP: ").strip()
        else:
            ip = input("Enter DroidCam IP address: ").strip()
    port:str = (
        input("Enter Quest 3 port [default=5005]: ").strip() or "5005"
        if connect_to_quest else
        input("Enter DroidCam port [default=4747]: ").strip() or "4747"
    )
    _persist_connection_data(ip, port, key)
    
    if connect_to_quest and is_editor_build:
        ip = "127.0.0.1"
    
    return ip, port

def open_ports(usb_quest_port: int = 5005, is_editor_build: bool = False):
    if not is_editor_build:
        print("No ports to be forwarded, the app is not running in the editor.")
        return
    import subprocess
    try:
        port_int = int(usb_quest_port) if usb_quest_port is not None else 5005
    except Exception:
        port_int = 5005

    # Clamp to valid TCP port range
    if port_int <= 0 or port_int > 2 ** 16 - 1:
        port_int = 5005

    port = str(port_int)
    result = subprocess.run(["adb", "forward", f"tcp:{port}", f"tcp:{port}"], check=False)
    if result.returncode != 0:
        print("Failed to run command mannualy. Ensure the Quest 3 is connected via the USB cable and try again using the MQDH.")
        print("You have 2 seconds to do this manually.")
        time.sleep(2)
        return False
    return True

def send_config_name_to_quest(config_name: str, quest_ip: str, port:str = "5005"):
    open_ports(5005)
    sender = UsbTcpSender(quest_ip, port)
    sender.send(line_configuration_name(config_name))