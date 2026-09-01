import os
import glob
import json
import sys

target_dir = r"C:\Users\43701\Documents\Downloads\GMAT-NASA-main"
if not os.path.exists(target_dir):
    print(f"Directory not found: {target_dir}")
    sys.exit(1)

script_files = []
for root, dirs, files in os.walk(target_dir):
    for f in files:
        if f.endswith(".script"):
            script_files.append(os.path.join(root, f))

print(f"Found {len(script_files)} script files in {target_dir}:")
for sf in script_files[:20]:
    print(" -", os.path.relpath(sf, target_dir))
if len(script_files) > 20:
    print(f" ... and {len(script_files) - 20} more.")
