# -*- coding: utf-8 -*-
"""
GMAT CLI One-Shot Runner for Direct KSP Invocation.
Matches bfc58e9 with export and run_raw_script support.
"""

import sys
import os
import json
import traceback

SERVICE_DIR = os.path.dirname(os.path.abspath(__file__))
BIN_DIR = os.path.dirname(SERVICE_DIR)
if SERVICE_DIR not in sys.path:
    sys.path.insert(0, SERVICE_DIR)
if BIN_DIR not in sys.path:
    sys.path.insert(0, BIN_DIR)

import gmat_engine
import principia_comparator

def main():
    import argparse
    parser = argparse.ArgumentParser(description="GMAT CLI One-Shot Runner")
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    def_out_dir = os.path.abspath(os.path.join(curr_dir, "..", "..", "output"))
    parser.add_argument("--action", default="solve", choices=["solve", "sync", "compare", "render", "import", "export", "run_raw_script", "metadata", "prop_metadata", "solver_metadata"], help="Action to execute")
    parser.add_argument("--input", default=os.path.join(def_out_dir, "mission_input.json"), help="Path to input JSON")
    parser.add_argument("--output", default=os.path.join(def_out_dir, "mission_result.json"), help="Path to output JSON")
    args = parser.parse_args()

    is_meta_action = args.action in ("metadata", "prop_metadata", "solver_metadata")
    if not is_meta_action and not os.path.exists(args.input):
        err = {"success": False, "error": f"Input file not found: {args.input}"}
        os.makedirs(os.path.dirname(args.output), exist_ok=True)
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(err, f, ensure_ascii=False, indent=2)
        print(f"[GmatCLI] Error: {args.input} not found", file=sys.stderr)
        sys.exit(1)

    try:
        req_data = {}
        if os.path.exists(args.input):
            with open(args.input, "r", encoding="utf-8-sig") as f:
                req_data = json.load(f)

        if args.action == "prop_metadata":
            res = gmat_engine.get_propagator_api_metadata()
        elif args.action == "solver_metadata":
            res = gmat_engine.get_solver_api_metadata()
        elif args.action == "metadata":
            p_meta = gmat_engine.get_propagator_api_metadata()
            s_meta = gmat_engine.get_solver_api_metadata()
            res = {
                "success": True,
                "propagator_metadata": p_meta,
                "solver_metadata": s_meta
            }
        elif args.action == "sync":
            vessel_state = req_data.get("vessel_state", {})
            nodes = req_data.get("nodes", [])
            principia_comparator.update_principia_state(vessel_state, nodes)
            comp = principia_comparator.get_latest_comparison()
            res = {"success": True, "comparison": comp}

        elif args.action == "compare":
            comp = principia_comparator.get_latest_comparison()
            res = {"success": True, "comparison": comp}

        elif args.action == "import":
            from script_parser import parse_gmat_script
            script_path = req_data.get("script_path", "")
            vessel_name = req_data.get("vessel_name", None)
            ut = req_data.get("ut", None)
            epoch_utc = None
            if ut is not None:
                try:
                    from ephemeris_calibrator import ksp_ut_to_jd, jd_to_utc_gregorian
                    jd = ksp_ut_to_jd(float(ut))
                    epoch_utc = jd_to_utc_gregorian(jd)
                except Exception:
                    pass
            res = parse_gmat_script(script_path, override_epoch_utc=epoch_utc, override_craft_name=vessel_name)

        elif args.action == "export":
            craft = req_data.get("craft_data", {}) or req_data.get("craft", {})
            sequence = req_data.get("mission_sequence", []) or req_data.get("sequence", [])
            propagator_configs = req_data.get("propagator_configs", {})
            custom_coords = req_data.get("custom_coordinate_systems") or []
            export_path = req_data.get("export_path")
            solver_configs = req_data.get("solver_configs")
            res = gmat_engine.export_mission_script(craft, sequence, export_path=export_path, propagator_configs=propagator_configs, custom_coordinate_systems=custom_coords, solver_configs=solver_configs)

        elif args.action == "run_raw_script":
            script_path = req_data.get("script_path", "")
            res = gmat_engine.run_raw_script(script_path)

        elif args.action == "render":
            view_mode = req_data.get("view_mode", "dual")
            central_body = req_data.get("central_body", "Earth")
            body_radius = req_data.get("body_radius", None)
            output_dir = os.path.dirname(args.output)
            traj_file = os.path.join(output_dir, "mission_traj.txt")
            img_file = os.path.join(output_dir, "gmat_orbit_render.png")
            from orbit_renderer import render_gmat_trajectory
            ok, msg = render_gmat_trajectory(traj_file, img_file, view_mode=view_mode, central_body=central_body, body_radius=body_radius)
            res = {"success": ok, "image_path": img_file, "message": msg}

        else: # solve
            craft = req_data.get("craft", {}) or req_data.get("craft_data", {})
            sequence = req_data.get("sequence", []) or req_data.get("mission_sequence", [])
            propagator_configs = req_data.get("propagator_configs", {})
            if "ut" in craft and "epoch_utc" not in craft:
                try:
                    from ephemeris_calibrator import ksp_ut_to_jd, jd_to_utc_gregorian
                    ut = float(craft["ut"])
                    jd = ksp_ut_to_jd(ut)
                    craft["epoch_utc"] = jd_to_utc_gregorian(jd)
                except Exception as ex:
                    print(f"[GmatCLI] Ephemeris calibration warning: {ex}")
            custom_coords = req_data.get("custom_coordinate_systems") or []
            res = gmat_engine.run_mission_sequence(craft, sequence, propagator_configs=propagator_configs, custom_coordinate_systems=custom_coords)
            if res.get("success"):
                principia_comparator.update_gmat_result(res)

        os.makedirs(os.path.dirname(args.output), exist_ok=True)
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(res, f, ensure_ascii=False, indent=2)

        print(f"[GmatCLI] Action '{args.action}' completed successfully.")
        sys.exit(0)
    except Exception as e:
        err_res = {"success": False, "error": str(e), "traceback": traceback.format_exc()}
        os.makedirs(os.path.dirname(args.output), exist_ok=True)
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(err_res, f, ensure_ascii=False, indent=2)
        print(f"[GmatCLI] Error during '{args.action}': {e}", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
