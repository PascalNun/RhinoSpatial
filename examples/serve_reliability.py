#!/usr/bin/env python3
"""Loopback-only WMS/WCS/Overpass fixtures for development cancellation checks."""
import argparse
import json
import re
import struct
import time
import urllib.parse
import zlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def png():
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 2, 2, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"\0" + b"\xff\0\0\xff" * 2 + b"\0" + b"\0\xff\0\xff" * 2))
            + chunk(b"IEND", b""))


class Handler(BaseHTTPRequestHandler):
    def reply(self, payload, content_type, scenario="normal"):
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(1024 ** 3 if scenario == "oversized" else len(payload)))
        self.end_headers()
        try:
            if scenario == "slow":
                self.wfile.write(payload[:16])
                self.wfile.flush()
                time.sleep(self.server.delay)
                self.wfile.write(payload[16:])
            elif scenario == "truncated":
                self.wfile.write(payload[:-16])
            else:
                self.wfile.write(payload)
        except (BrokenPipeError, ConnectionResetError):
            pass  # Expected when the component cancels a transfer.

    def do_GET(self):
        url = urllib.parse.urlsplit(self.path)
        parts = url.path.strip("/").split("/")
        if len(parts) != 2 or parts[0].split("-", 1)[0] not in {"normal", "slow", "truncated", "oversized", "retry", "multipart"}:
            self.send_error(404)
            return
        scenario, service = parts[0].split("-", 1)[0], parts[1]
        query = {key.upper(): value[-1] for key, value in urllib.parse.parse_qs(url.query).items()}
        if query.get("SERVICE", service).upper() == "WMTS":
            self.send_error(404)
            return
        operation = query.get("REQUEST", "GetCapabilities").lower()
        endpoint = f"http://127.0.0.1:{self.server.server_port}{url.path}"
        if service == "wms" and operation == "getcapabilities":
            text = f'''<WMS_Capabilities version="1.3.0" xmlns:xlink="http://www.w3.org/1999/xlink"><Capability>
              <Request><GetMap><DCPType><HTTP><Get><OnlineResource xlink:href="{endpoint}"/></Get></HTTP></DCPType></GetMap></Request>
              <Layer><Title>Fixture</Title><CRS>EPSG:25833</CRS><Layer><Name>site</Name><Title>Test image</Title></Layer></Layer>
              </Capability></WMS_Capabilities>'''
            self.reply(text.encode(), "text/xml")
        elif service == "wms" and operation == "getmap":
            if scenario == "retry" and int(query.get("WIDTH", "1024")) >= 1024:
                self.reply(b"<ServiceExceptionReport><ServiceException>image size too large</ServiceException></ServiceExceptionReport>", "text/xml")
            else:
                self.reply(png(), "image/png", scenario)
        elif service == "wcs" and operation == "getcapabilities":
            self.reply(b'<Capabilities version="2.0.1"><Contents><CoverageSummary><CoverageId>dem</CoverageId></CoverageSummary></Contents></Capabilities>', "text/xml")
        elif service == "wcs" and operation == "describecoverage":
            self.reply(b'''<CoverageDescriptions><CoverageDescription><CoverageId>dem</CoverageId>
              <Envelope srsName="EPSG:25833" axisLabels="x y"><lowerCorner>392000 5819872</lowerCorner><upperCorner>392128 5820000</upperCorner></Envelope>
              </CoverageDescription></CoverageDescriptions>''', "text/xml")
        elif service == "wcs" and operation == "getcoverage":
            data = (Path(__file__).resolve().parent / "fixtures" / "low" / "elevation-mask.cog.tif").read_bytes()
            if scenario == "multipart":
                data = b"--fixture\r\nContent-Type: application/gml+xml\r\n\r\n<metadata/>\r\n--fixture\r\nContent-Type: image/tiff\r\n\r\n" + data + b"\r\n--fixture--\r\n"
                self.reply(data, "multipart/related; boundary=fixture")
            else:
                self.reply(data, "image/tiff", scenario)
        else:
            self.send_error(404)

    def do_POST(self):
        path = urllib.parse.urlsplit(self.path).path.strip("/").split("/")
        if len(path) != 2 or path[1] != "osm" or path[0].split("-", 1)[0] not in {"normal", "slow", "truncated", "oversized"}:
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", "0"))
        if length < 0 or length > 65536:
            self.send_error(413)
            return
        query = urllib.parse.parse_qs(self.rfile.read(length).decode()).get("data", [""])[0]
        match = re.search(r"\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\)", query)
        south, west, north, east = map(float, match.groups()) if match else (52.5, 13.4, 52.501, 13.401)
        data = {"elements": [{"type": "way", "id": 1, "tags": {"highway": "residential"}, "geometry": [
            {"lon": west + (east - west) * .25, "lat": south + (north - south) * .25},
            {"lon": west + (east - west) * .75, "lat": south + (north - south) * .75}]}]}
        self.reply(json.dumps(data).encode(), "application/json", path[0].split("-", 1)[0])

    def log_message(self, format_string, *args):
        # Do not log source URLs or user queries.
        print("Fixture request completed", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8769)
    parser.add_argument("--delay", type=float, default=8)
    args = parser.parse_args()
    if not 0 <= args.delay <= 120:
        parser.error("--delay must be between 0 and 120 seconds")
    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    server.delay = args.delay
    print(f"Reliability fixtures at http://127.0.0.1:{server.server_port}; Ctrl+C stops the server", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
