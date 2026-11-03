"""Tiny throttled HTTP server with Range support for WDM QA.
/f/<name>?size=<bytes>&kbps=<per-connection KB/s>  -> deterministic content (byte i = i % 251)
/bad -> 500, /gone -> 404
"""
import re, sys, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs


class H(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _serve(self, head):
        u = urlparse(self.path)
        q = parse_qs(u.query)
        if u.path == "/page":
            names = ["Annual-Report-2025.pdf", "dataset-sample.zip", "lecture-01-introduction.mp4", "lecture-02-segments.mp4", "podcast-episode-12.mp3", "installer-x64.msi", "cover-photo.jpg", "slides-week-3.pptx"]
            html = "<html><head><title>Course files</title></head><body>" + "".join(f'<a href="/course/{n}">{n}</a>' for i, n in enumerate(names)) + "</body></html>"
            data = html.encode()
            self.send_response(200); self.send_header("Content-Type", "text/html"); self.send_header("Content-Length", str(len(data))); self.end_headers()
            if not head: self.wfile.write(data)
            return
        if u.path == "/bad":
            self.send_error(500); return
        if u.path == "/gone":
            self.send_error(404); return
        if u.path.startswith("/course/"):
            q = {"size": [str((sum(map(ord, u.path)) % 9 + 3) * 7340032)], "kbps": ["700"]}
        size = int(q.get("size", ["10485760"])[0])
        kbps = int(q.get("kbps", ["0"])[0])
        start, end = 0, size - 1
        m = re.match(r"bytes=(\d+)-(\d*)", self.headers.get("Range", ""))
        code = 200
        if m:
            start = int(m.group(1))
            if m.group(2):
                end = min(int(m.group(2)), size - 1)
            code = 206
        length = end - start + 1
        self.send_response(code)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(length))
        if code == 206:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.send_header("ETag", '"qa-fixed"')
        self.end_headers()
        if head:
            return
        pos = start
        chunk = 64 * 1024
        try:
            while pos <= end:
                n = min(chunk, end - pos + 1)
                self.wfile.write(bytes((pos + i) % 251 for i in range(n)) if n < 1024 else (bytes(range(251)) * (n // 251 + 2))[(pos % 251):(pos % 251) + n])
                pos += n
                if kbps:
                    time.sleep(n / (kbps * 1024))
        except (BrokenPipeError, ConnectionResetError):
            pass

    def do_GET(self):
        self._serve(False)

    def do_HEAD(self):
        self._serve(True)


ThreadingHTTPServer(("127.0.0.1", int(sys.argv[1]) if len(sys.argv) > 1 else 8765), H).serve_forever()
