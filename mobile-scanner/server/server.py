#!/usr/bin/env python3
"""
手機條碼掃描器 — 電腦端伺服器

接收手機掃到的條碼，記錄下來並即時顯示。
不會碰你的鍵盤，游標在哪都不受影響。

只用 Python 標準函式庫，不裝任何套件就能跑。

    python server.py            # 內網 HTTP（給手機 App 用）
    python server.py --https    # 內網 HTTPS（給手機瀏覽器用）

收到的條碼會印在這個視窗，也可以開 /monitor 看即時清單與匯出 CSV。
"""

import argparse
import json
import os
import queue
import socket
import ssl
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
CERT_DIR = os.path.join(BASE_DIR, "cert")

# ---------------------------------------------------------------- 掃描紀錄


class ScanBus:
    """收到的掃描結果：存一份紀錄，同時推播給所有監看頁面。"""

    def __init__(self, limit=500):
        self.limit = limit
        self.items = []
        self.subscribers = set()
        self.lock = threading.Lock()

    def publish(self, item):
        with self.lock:
            self.items.append(item)
            if len(self.items) > self.limit:
                self.items = self.items[-self.limit :]
            subs = list(self.subscribers)
        for q in subs:
            try:
                q.put_nowait(item)
            except queue.Full:
                pass

    def subscribe(self):
        q = queue.Queue(maxsize=100)
        with self.lock:
            self.subscribers.add(q)
        return q

    def unsubscribe(self, q):
        with self.lock:
            self.subscribers.discard(q)

    def recent(self, n=50):
        with self.lock:
            return self.items[-n:]


# ---------------------------------------------------------------- HTTP


class Handler(BaseHTTPRequestHandler):
    server_version = "PhoneScanner/1.0"
    protocol_version = "HTTP/1.1"

    # ---- 工具

    def _send(self, code, body=b"", ctype="text/plain; charset=utf-8", extra=None):
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        for k, v in (extra or {}).items():
            self.send_header(k, v)
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _json(self, obj, code=200):
        self._send(code, json.dumps(obj, ensure_ascii=False).encode("utf-8"),
                   "application/json; charset=utf-8")

    def _file(self, name, ctype):
        path = os.path.join(BASE_DIR, name)
        if not os.path.exists(path):
            self._send(404, f"找不到 {name}".encode("utf-8"))
            return
        with open(path, "rb") as fh:
            self._send(200, fh.read(), ctype)

    def _log(self, mark, text):
        who = self.client_address[0]
        print(f"  {mark} [{time.strftime('%H:%M:%S')}] {who:<15} {text}")

    def log_message(self, fmt, *args):
        pass  # 自己控制輸出，不要 access log 洗版

    def handle_one_request(self):
        # 對方（瀏覽器分頁、手機 App）隨時可能直接斷線，
        # 那是正常現象，不是錯誤，不需要印一整串堆疊。
        try:
            super().handle_one_request()
        except (ConnectionResetError, ConnectionAbortedError,
                BrokenPipeError, TimeoutError, OSError):
            self.close_connection = True

    # ---- 路由

    def do_GET(self):
        path = self.path.split("?")[0]
        if path in ("/", "/index.html"):
            self._log("→", "開啟掃描頁")
            self._file("scanner.html", "text/html; charset=utf-8")
        elif path == "/monitor":
            self._log("→", "開啟監看頁")
            self._file("monitor.html", "text/html; charset=utf-8")
        elif path == "/vendor/zxing.js":
            self._file(os.path.join("vendor", "zxing.js"), "application/javascript")
        elif path == "/ping":
            self._log("→", "測試連線")
            self._json({"ok": True, "output": "none"})
        elif path == "/history":
            self._json({"items": self.server.bus.recent()})
        elif path == "/events":
            self._log("→", "監看頁連上，開始推播")
            self._events()
            self._log("←", "監看頁離線")
        else:
            self._log("?", f"不存在的路徑 {path}")
            self._send(404, b"not found")

    def do_POST(self):
        if self.path.split("?")[0] != "/scan":
            self._send(404, b"not found")
            return
        try:
            raw = self._read_body()
            payload = json.loads(raw or b"{}")
        except Exception as exc:
            self._log("✘", f"收到的資料讀不懂：{exc}")
            self._json({"ok": False, "error": f"資料格式不正確：{exc}"}, 400)
            return

        code = (payload.get("code") or "").strip()
        if not code:
            self._log("✘", "請求裡沒有條碼內容")
            self._json({"ok": False, "error": "沒有收到條碼內容"}, 400)
            return

        fmt = payload.get("format") or "unknown"
        newline = bool(payload.get("newline"))  # 這筆是不是一組的最後一個
        ok, detail = True, "空行" if newline else "已記錄"
        item = {
            "code": code,
            "format": fmt,
            "time": time.strftime("%H:%M:%S"),
            "ok": ok,
            "detail": detail,
            "newline": newline,
        }
        self.server.bus.publish(item)

        self._log("✔" if ok else "✘", f"{code}   ({fmt} / {detail})")
        self._json({"ok": ok, "detail": detail})

    def _read_body(self):
        """讀取請求內容。

        有些客戶端（例如 Dart 的 HttpClient）不送 Content-Length，
        改用 chunked 分塊傳輸，這裡兩種都要接得住。
        """
        encoding = (self.headers.get("Transfer-Encoding") or "").lower()
        if "chunked" in encoding:
            data = b""
            while True:
                line = self.rfile.readline().strip()
                if not line:
                    break
                try:
                    size = int(line.split(b";")[0], 16)
                except ValueError:
                    break
                if size == 0:
                    self.rfile.readline()  # 結尾的空行
                    break
                data += self.rfile.read(size)
                self.rfile.read(2)  # 每塊後面的 CRLF
            return data

        length = int(self.headers.get("Content-Length", 0) or 0)
        return self.rfile.read(length) if length else b""

    def _events(self):
        q = self.server.bus.subscribe()
        self.send_response(200)
        self.send_header("Content-Type", "text/event-stream; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.send_header("Connection", "keep-alive")
        self.end_headers()
        try:
            while True:
                try:
                    item = q.get(timeout=15)
                    data = json.dumps(item, ensure_ascii=False)
                    self.wfile.write(f"data: {data}\n\n".encode("utf-8"))
                except queue.Empty:
                    self.wfile.write(b": keep-alive\n\n")
                self.wfile.flush()
        except Exception:
            pass
        finally:
            self.server.bus.unsubscribe(q)


class QuietServer(ThreadingHTTPServer):
    """瀏覽器關分頁 / 重新整理時會直接斷線，那不是錯誤，不用印一整串堆疊。"""

    def handle_error(self, request, client_address):
        exc = sys.exc_info()[1]
        if isinstance(exc, (ConnectionResetError, ConnectionAbortedError,
                            BrokenPipeError, TimeoutError)):
            return
        super().handle_error(request, client_address)


# ---------------------------------------------------------------- 啟動輔助


def lan_ips():
    ips = []
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
        ips.append(s.getsockname()[0])
        s.close()
    except Exception:
        pass
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if not ip.startswith("127.") and ip not in ips:
                ips.append(ip)
    except Exception:
        pass
    return ips or ["127.0.0.1"]


def ensure_cert(hosts):
    """用 openssl 產一張自簽憑證（含 IP SAN），已存在就沿用。"""
    os.makedirs(CERT_DIR, exist_ok=True)
    key = os.path.join(CERT_DIR, "server.key")
    crt = os.path.join(CERT_DIR, "server.crt")
    if os.path.exists(key) and os.path.exists(crt):
        return key, crt

    san = ",".join(["DNS:localhost", "IP:127.0.0.1"] + [f"IP:{h}" for h in hosts])
    cmd = [
        "openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes",
        "-keyout", key, "-out", crt, "-days", "825",
        "-subj", "/CN=phone-scanner",
        "-addext", f"subjectAltName={san}",
    ]
    try:
        subprocess.run(cmd, check=True, capture_output=True)
    except FileNotFoundError:
        sys.exit("  ! 系統沒有 openssl，無法產生憑證。請改用 USB 模式（見 README）。")
    except subprocess.CalledProcessError as exc:
        sys.exit(f"  ! 憑證產生失敗：{exc.stderr.decode('utf-8', 'ignore')[:300]}")
    return key, crt


def print_qr(url):
    try:
        import qrcode  # 有裝才用，沒裝就跳過
    except Exception:
        return
    qr = qrcode.QRCode(border=1)
    qr.add_data(url)
    qr.make(fit=True)
    qr.print_ascii(invert=True)


def main():
    ap = argparse.ArgumentParser(description="把手機變成條碼掃描槍")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--https", action="store_true", help="啟用自簽憑證 HTTPS（Wi-Fi 模式必需）")
    args = ap.parse_args()

    bus = ScanBus()

    httpd = QuietServer((args.host, args.port), Handler)
    httpd.daemon_threads = True
    httpd.bus = bus

    ips = lan_ips()
    scheme = "http"
    if args.https:
        key, crt = ensure_cert(ips)
        ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        ctx.load_cert_chain(crt, key)
        httpd.socket = ctx.wrap_socket(httpd.socket, server_side=True)
        scheme = "https"

    primary = f"{scheme}://{ips[0]}:{args.port}"
    print()
    print("  手機條碼掃描器已啟動")
    print("  只記錄條碼，不會模擬鍵盤輸入")
    print("  " + "-" * 52)
    if args.https:
        print(f"  Wi-Fi 模式  手機開：{primary}")
        for ip in ips[1:]:
            print(f"              或：{scheme}://{ip}:{args.port}")
        print("              第一次會出現憑證警告，選「繼續前往」")
        print_qr(primary)
    else:
        print(f"  手機 App    連線位址：{ips[0]}:{args.port}")
        if len(ips) > 1:
            print(f"              (其他網卡：{'、'.join(ips[1:])}，通常用不到)")
    print(f"  電腦監看頁  {scheme}://localhost:{args.port}/monitor")
    print("  " + "-" * 52)
    print("  Ctrl+C 結束\n")

    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n  已停止")
        httpd.shutdown()


if __name__ == "__main__":
    main()