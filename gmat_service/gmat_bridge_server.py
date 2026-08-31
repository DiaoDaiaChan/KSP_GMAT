"""
Lightweight HTTP REST API Server for KSP-GMAT Bridge.
Bridges KSP Unity mod with GMAT R2026a Python API (gmatpy).
"""

import json
import os
import subprocess
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# Add GMAT bin and service dirs to path
BIN_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if BIN_DIR not in sys.path:
    sys.path.insert(0, BIN_DIR)
SERVICE_DIR = os.path.dirname(os.path.abspath(__file__))
if SERVICE_DIR not in sys.path:
    sys.path.insert(0, SERVICE_DIR)

from gmat_paths import resolve_gmat_root
from ephemeris_calibrator import ksp_ut_to_jd, jd_to_gmat_mjd, jd_to_utc_gregorian, map_body_name
BIN_DIR = resolve_gmat_root(SERVICE_DIR)
from gmat_engine import run_mission_sequence, init_gmat

PORT = 8055

class GMATBridgeHandler(BaseHTTPRequestHandler):
    def _send_json(self, data, status=200):
        body = json.dumps(data, indent=2, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.end_headers()
        self.wfile.write(body)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.end_headers()

    def do_GET(self):
        if self.path == "/api/status":
            try:
                init_gmat()
                self._send_json({
                    "status": "online",
                    "engine": "GMAT R2026a",
                    "python_version": sys.version,
                    "bin_dir": BIN_DIR
                })
            except Exception as e:
                self._send_json({"status": "error", "error": str(e)}, 500)

        elif self.path == "/api/templates":
            templates = [
                {
                    "id": "lunar_nrho_1988",
                    "title": "🌙 地球到月球NRHO任务 (1988)",
                    "file": os.path.join(BIN_DIR, "Mission_EarthToLunarNRHO_1988.script"),
                    "desc": "200km LEO -> TLI (+3133m/s) -> TCM -> NRHOI (-780m/s) -> 21天NRHO Halo轨道"
                },
                {
                    "id": "gto_transfer",
                    "title": "🛰️ 地球同步转移轨道 (GTO Targeter)",
                    "file": "",
                    "desc": "LEO -> 自动微分校正求解远心点 35786 km GTO 转移"
                }
            ]
        elif self.path in ("/debug", "/dashboard"):
            dashboard_file = os.path.join(SERVICE_DIR, "debug_dashboard.html")
            if os.path.exists(dashboard_file):
                with open(dashboard_file, "r", encoding="utf-8") as f:
                    content = f.read().encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(content)))
                self.end_headers()
                self.wfile.write(content)
            else:
                self._send_json({"error": "Dashboard not found"}, 404)

        elif self.path == "/api/principia/compare":
            try:
                from principia_comparator import get_latest_comparison
                self._send_json(get_latest_comparison())
            except Exception as e:
                self._send_json({"status": "error", "error": str(e)}, 500)

        else:
            self._send_json({"error": "Not Found"}, 404)

    def do_POST(self):
        content_length = int(self.headers.get("Content-Length", 0))
        req_body = self.rfile.read(content_length).decode("utf-8") if content_length > 0 else "{}"
        try:
            req_data = json.loads(req_body)
        except Exception:
            req_data = {}

        if self.path == "/api/calibrate_ephem":
            ut = float(req_data.get("ut", 0.0))
            game_epoch_jd = float(req_data.get("game_epoch_jd", 2433647.5))
            jd = ksp_ut_to_jd(ut, game_epoch_jd)
            mjd = jd_to_gmat_mjd(jd)
            utc_str = jd_to_utc_gregorian(jd)

            self._send_json({
                "success": True,
                "ut": ut,
                "jd": jd,
                "mjd": mjd,
                "utc_gregorian": utc_str,
                "sol_epoch_jd": game_epoch_jd,
                "status": "Calibrated"
            })

        elif self.path == "/api/propagate":
            craft = req_data.get("craft", {})
            sequence = req_data.get("sequence", [])
            propagator_configs = req_data.get("propagator_configs", {})
            raw_script = req_data.get("raw_script")

            # Automatically compute epoch if UT is provided
            if "ut" in craft and "epoch_utc" not in craft:
                ut = float(craft["ut"])
                game_epoch_jd = float(craft.get("game_epoch_jd", 2433647.5))
                jd = ksp_ut_to_jd(ut, game_epoch_jd)
                craft["epoch_utc"] = jd_to_utc_gregorian(jd)

            try:
                custom_coords = req_data.get("custom_coordinate_systems") or []
                result = run_mission_sequence(craft, sequence, propagator_configs=propagator_configs, custom_coordinate_systems=custom_coords, raw_script=raw_script)
                if result.get("success"):
                    try:
                        from principia_comparator import update_gmat_result
                        update_gmat_result(result)
                    except Exception:
                        pass
                self._send_json(result)
            except Exception as e:
                import traceback
                traceback.print_exc()
                # Mission failures are application results, not transport failures.
                self._send_json({"success": False, "error": str(e), "traceback": traceback.format_exc()}, 200)

        elif self.path == "/api/principia/sync":
            vessel_state = req_data.get("vessel_state", {})
            nodes = req_data.get("nodes", [])
            try:
                from principia_comparator import update_principia_state, get_latest_comparison
                update_principia_state(vessel_state, nodes)
                self._send_json({"success": True, "comparison": get_latest_comparison()})
            except Exception as e:
                self._send_json({"success": False, "error": str(e)}, 500)

        elif self.path == "/api/render_trajectory":
            view_mode = req_data.get("view_mode", "dual")
            central_body = req_data.get("central_body", "Earth")
            body_radius = req_data.get("body_radius", None)
            gmat_root = os.path.dirname(BIN_DIR)
            output_dir = os.path.join(gmat_root, "output")
            traj_file = os.path.join(output_dir, "mission_traj.txt")
            img_file = os.path.join(output_dir, "gmat_orbit_render.png")
            try:
                from orbit_renderer import render_gmat_trajectory
                ok, msg = render_gmat_trajectory(traj_file, img_file, view_mode=view_mode, central_body=central_body, body_radius=body_radius)
                self._send_json({"success": ok, "image_path": img_file, "message": msg})
            except Exception as e:
                self._send_json({"success": False, "error": str(e)}, 500)

        elif self.path == "/api/open_gmat_gui":
            script_path = req_data.get("script_path", "")
            gmat_exe = os.path.join(BIN_DIR, "GMAT.exe")
            if os.path.exists(gmat_exe):
                args = [gmat_exe]
                if script_path and os.path.exists(script_path):
                    args.append(script_path)
                subprocess.Popen(args, cwd=BIN_DIR)
                self._send_json({"success": True, "message": "GMAT GUI launched"})
            else:
                self._send_json({"success": False, "error": "GMAT.exe not found"}, 404)

        else:
            self._send_json({"error": "Not Found"}, 404)

    def log_message(self, format, *args):
        # Concise logging
        print(f"[GMATBridgeServer] {self.address_string()} - {format % args}")

def run_server():
    server_address = ("127.0.0.1", PORT)
    httpd = ThreadingHTTPServer(server_address, GMATBridgeHandler)
    print(f"==================================================")
    print(f"  KSP-GMAT Bridge Service running on http://127.0.0.1:{PORT}")
    print(f"==================================================")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down GMAT Bridge Service.")
        httpd.server_close()

if __name__ == "__main__":
    run_server()
