#!/usr/bin/env python3
"""Serve the small low-priority fixtures on loopback with proper HTTP byte ranges."""
import argparse
import hashlib
import re
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--port", type=int, default=8765)
args = parser.parse_args()
root = (Path(__file__).parent / "fixtures" / "low").resolve()


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=str(root), **kw)

    def send_head(self):
        path = Path(self.translate_path(self.path)).resolve()
        if not path.is_relative_to(root) or not path.is_file():
            self.send_error(404)
            return None
        if path.stat().st_size > 16 * 1024 * 1024:
            self.send_error(413, "This server is for small examples only")
            return None
        data = path.read_bytes()
        size = len(data)
        etag = '"' + hashlib.sha256(data).hexdigest() + '"'
        requested = self.headers.get("Range")
        if self.headers.get("If-Range") not in (None, etag):
            requested = None
        start, end = 0, size - 1
        if requested:
            match = re.fullmatch(r"bytes=(\d+)-(\d*)", requested)
            if not match:
                self.send_error(416, "One explicit byte range is required")
                return None
            start = int(match[1])
            end = min(int(match[2]) if match[2] else size - 1, size - 1)
            if start > end:
                self.send_response(416)
                self.send_header("Content-Range", f"bytes */{size}")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return None
        self.send_response(206 if requested else 200)
        self.send_header("Content-Type", self.guess_type(str(path)))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("ETag", etag)
        self.send_header("Content-Length", str(end - start + 1))
        if requested:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.end_headers()
        import io
        return io.BytesIO(data[start:end + 1])


print(f"Serving synthetic fixtures at http://127.0.0.1:{args.port}/", flush=True)
try:
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
except KeyboardInterrupt:
    pass
