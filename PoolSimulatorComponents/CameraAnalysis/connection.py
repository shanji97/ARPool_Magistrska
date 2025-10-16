from __future__ import annotations
import socket
from typing import Optional

class UsbTcpSender:
    
    RETRY_BACKOFF_SEC = 1.0
    
    def __init__(self,
                 host: str = "127.0.0.1",
                 port: int = 5005,
                 auto_reconnect: bool = True,
                 connect_timeout_s: float = 2.0,
                 send_timeout_s: float = 2.0,
                 retry_initial_delay_s: float = 0.5,
                 retry_max_delay_s: float = 5.0) -> None:
        self.host = host
        self.port = int(port)
        self.auto_reconnect = auto_reconnect
        self.connect_timeout_s = connect_timeout_s
        self.send_timeout_s = send_timeout_s
        self.retry_initial_delay_s = retry_initial_delay_s
        self.retry_max_delay_s = retry_max_delay_s
        self._sock: Optional[socket.socket] = None
            
    def _close_quiet(self):
        if self._sock:
            try: self._sock.close()
            except: pass
        self._sock = None

    def connect(self) -> bool:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(self.connect_timeout_s)
            s.connect((self.host, self.port))
            s.settimeout(self.send_timeout_s)
            self._sock = s
            print(f"[USB] Connected {self.host}:{self.port}")
            return True
        except Exception as e:
            print(f"[USB] Connect failed: {e}")
            self._close_quiet()
            return False

    def send(self, data: str) -> bool:
        if not data.endswith("\n"): data += "\n"
        if self._sock is None and not self.connect():
            return False
        try:
            self._sock.sendall(data.encode("utf-8"))
            return True
        except Exception as e:
            print(f"[USB] Send failed: {e}")
            self._close_quiet()
            return False

    def close(self): self._close_quiet()