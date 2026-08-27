"""
End-to-end integration test for KSP-GMAT Bridge Service.
Tests all HTTP endpoints:
- GET /api/status
- POST /api/calibrate_ephem
- POST /api/propagate (Apollo CSM LEO -> 150m/s prograde burn -> 1 hour propagation)
- Checks output format and data integrity.
"""

import json
import os
import sys
import threading
import time
import urllib.request

SERVICE_DIR = os.path.dirname(os.path.abspath(__file__))
BIN_DIR = os.path.dirname(SERVICE_DIR)
sys.path.insert(0, BIN_DIR)
sys.path.insert(0, SERVICE_DIR)

from gmat_bridge_server import run_server, PORT

def test_client():
    time.sleep(1.0)
    base_url = f"http://127.0.0.1:{PORT}"
    print(f"\n[TestClient] Connecting to {base_url}...")

    # 1. Test /api/status
    req = urllib.request.Request(f"{base_url}/api/status")
    with urllib.request.urlopen(req, timeout=5) as resp:
        data = json.loads(resp.read().decode("utf-8"))
        print("[Test 1: Status]", data)
        assert data.get("status") == "online"

    # 2. Test /api/calibrate_ephem
    calib_payload = json.dumps({"ut": 3600.0}).encode("utf-8")
    req = urllib.request.Request(f"{base_url}/api/calibrate_ephem", data=calib_payload, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=5) as resp:
        data = json.loads(resp.read().decode("utf-8"))
        print("[Test 2: Calibrate Ephem]", data)
        assert data.get("success") is True
        assert "utc_gregorian" in data

    # 3. Test /api/propagate with RO Propellant and Engine Parameters
    prop_payload = json.dumps({
        "craft": {
            "name": "ApolloCSM",
            "central_body": "Earth",
            "ut": 0.0,
            "x": 6678.0,
            "y": 0.0,
            "z": 0.0,
            "vx": 0.0,
            "vy": 7.726,
            "vz": 0.0,
            "dry_mass": 5000.0,
            "fuel_mass": 756.0,
            "fuel_density": 1260.0, # Exact match with user's GMAT ChemicalTank screenshot!
            "tank_volume": 0.75,
            "tank_pressure": 1500.0,
            "engine_name": "AJ10_137",
            "thrust": 91000.0,
            "isp": 314.0
        },
        "sequence": [
            {"type": "Propagate", "stop_type": "Time", "duration": 1800.0},
            {"type": "ImpulsiveBurn", "dv_v": 150.0, "dv_n": 0.0, "dv_b": 0.0},
            {"type": "Propagate", "stop_type": "Time", "duration": 1800.0}
        ]
    }).encode("utf-8")

    req = urllib.request.Request(f"{base_url}/api/propagate", data=prop_payload, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=20) as resp:
        data = json.loads(resp.read().decode("utf-8"))
        print("[Test 3: Propagate]")
        print("  - Success:", data.get("success"))
        print("  - Final SMA:", data.get("final_state", {}).get("sma"), "km")
        print("  - Final ECC:", data.get("final_state", {}).get("ecc"))
        print("  - Trajectory Count:", data.get("trajectory_count"))
        print("  - Solved Burns:", data.get("solved_burns"))
        assert data.get("success") is True
        assert data.get("trajectory_count") > 0
        assert len(data.get("solved_burns")) == 1

    print("\n==============================================")
    print("  ALL END-TO-END INTEGRATION TESTS PASSED!    ")
    print("==============================================\n")
    os._exit(0)

if __name__ == "__main__":
    t = threading.Thread(target=test_client)
    t.daemon = True
    t.start()
    run_server()
