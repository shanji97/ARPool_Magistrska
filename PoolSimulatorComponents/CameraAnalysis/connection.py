from __future__ import annotations

import socket
import threading
from typing import Optional


class UsbTcpSender:
    """
    Persistent TCP sender for Unity/Quest receiver.

    This implementation is safe for:
    - first connect
    - remote disconnects
    - reconnect-on-next-send
    - long-lived open connection during the whole detection session

    IMPORTANT:
    The previous implementation deadlocked because send() acquired a Lock and
    then called connect(), which tried to acquire the same Lock again.
    This version avoids that by using a private _connect_unlocked() method.
    """

    RETRY_BACKOFF_SEC = 1.0

    def __init__(
        self,
        host: str = "127.0.0.1",
        port: str = "5005",
        auto_reconnect: bool = True,
        connect_timeout_s: float = 30.0,
        send_timeout_s: float = 30.0,
        retry_initial_delay_s: float = 0.5,
        retry_max_delay_s: float = 5.0,
    ) -> None:
        self.host = host
        self.port = int(port)
        self.auto_reconnect = auto_reconnect
        self.connect_timeout_s = connect_timeout_s
        self.send_timeout_s = send_timeout_s
        self.retry_initial_delay_s = retry_initial_delay_s
        self.retry_max_delay_s = retry_max_delay_s

        self._sock: Optional[socket.socket] = None
        self._lock = threading.Lock()

    def _create_socket(self) -> socket.socket:
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        s.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
        s.settimeout(self.connect_timeout_s)
        return s

    def _close_quiet_unlocked(self) -> None:
        if self._sock is not None:
            try:
                self._sock.close()
            except Exception:
                pass
        self._sock = None

    def _connect_unlocked(self) -> bool:
        self._close_quiet_unlocked()

        try:
            s = self._create_socket()
            s.connect((self.host, self.port))
            s.settimeout(self.send_timeout_s)
            self._sock = s
            print(f"[USB] Connected {self.host}:{self.port}")
            return True
        except Exception as e:
            print(f"[USB] Connect failed: {e}")
            self._close_quiet_unlocked()
            return False

    def connect(self) -> bool:
        with self._lock:
            return self._connect_unlocked()

    def send(self, data: str) -> bool:
        if not data.endswith("\n"):
            data += "\n"

        payload = data.encode("utf-8")
        max_attempts = 2 if self.auto_reconnect else 1

        with self._lock:
            for attempt in range(max_attempts):
                if self._sock is None:
                    if not self._connect_unlocked():
                        continue

                try:
                    self._sock.sendall(payload)
                    return True
                except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError, OSError, socket.timeout) as e:
                    print(f"[USB] Send failed: {e}")
                    self._close_quiet_unlocked()

                    if attempt + 1 >= max_attempts:
                        return False

            return False

    def close(self) -> None:
        with self._lock:
            self._close_quiet_unlocked()