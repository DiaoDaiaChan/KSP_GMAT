"""
Standardized Trajectory Renderer for GMAT and KSP.
Renders publication-quality aerospace trajectory plots for ANY celestial body
without hardcoded system assumptions.
"""

import os
import math
import numpy as np
import matplotlib
matplotlib.use('Agg') # Headless rendering
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D

BODY_RADII = {
    "Sun": 696342.0,
    "Mercury": 2439.7,
    "Venus": 6051.8,
    "Earth": 6378.137,
    "Luna": 1737.4,
    "Moon": 1737.4,
    "Mars": 3396.19,
    "Jupiter": 71492.0,
    "Saturn": 60268.0,
    "Uranus": 25559.0,
    "Neptune": 24764.0
}

BODY_COLORS = {
    "Sun": "#ffd700",
    "Mercury": "#a0a0a0",
    "Venus": "#e3bb76",
    "Earth": "#388bfd",
    "Luna": "#d0d7de",
    "Moon": "#d0d7de",
    "Mars": "#f85149",
    "Jupiter": "#d29922",
    "Saturn": "#e3b341",
    "Uranus": "#79c0ff",
    "Neptune": "#58a6ff"
}

def render_gmat_trajectory(traj_file_path, output_png_path, view_mode="dual", central_body="Earth", body_radius=None):
    """
    Renders publication-quality dark-mode aerospace trajectory plots from GMAT output.
    Supports any central body and any mission profile.
    view_mode:
      - 'dual': 2 subplots (Left: Orbital plane 2D; Right: 3D Isometric view)
      - 'planar': 2D Orbital plane full view
      - 'target': Zoomed view near destination / final orbit phase
    """
    if not os.path.exists(traj_file_path):
        return False, "Trajectory file not found"

    times, xs, ys, zs = [], [], [], []
    with open(traj_file_path, 'r', errors='ignore') as f:
        for line in f:
            parts = line.split()
            if len(parts) >= 4:
                try:
                    times.append(float(parts[0]))
                    xs.append(float(parts[1]))
                    ys.append(float(parts[2]))
                    zs.append(float(parts[3]))
                except ValueError:
                    continue

    if len(xs) < 2:
        return False, "Insufficient trajectory points"

    xs = np.array(xs)
    ys = np.array(ys)
    zs = np.array(zs)

    body_name = str(central_body).capitalize() if central_body else "Earth"
    if body_radius is None or body_radius <= 0:
        r_body = BODY_RADII.get(body_name, 6378.137)
    else:
        r_body = float(body_radius)

    b_color = BODY_COLORS.get(body_name, "#388bfd")

    # Style configuration
    plt.style.use('dark_background')
    fig = plt.figure(figsize=(10, 5), dpi=120)
    fig.patch.set_facecolor('#0d1117')

    step = max(1, len(xs) // 1000)

    if view_mode in ("dual", "default"):
        # Left Subplot: 2D Orbital Plane (XY Plane)
        ax1 = fig.add_subplot(1, 2, 1)
        ax1.set_facecolor('#161b22')
        ax1.grid(True, color='#30363d', linestyle='--', linewidth=0.5, alpha=0.7)

        # Plot Central Body at origin
        ax1.plot(0, 0, marker='o', markersize=9, color=b_color, label=body_name)
        body_circle = plt.Circle((0, 0), r_body, color=b_color, fill=False, linestyle=':', linewidth=1.2)
        ax1.add_patch(body_circle)

        # Trajectory curve
        ax1.plot(xs[::step], ys[::step], color='#58a6ff', linewidth=1.5, label='Trajectory', alpha=0.9)
        # Mark initial point
        ax1.plot(xs[0], ys[0], marker='^', markersize=6, color='#3fb950', label='Initial State')
        # Mark final point
        ax1.plot(xs[-1], ys[-1], marker='s', markersize=6, color='#f0883e', label='Final State')

        ax1.set_title(f"{body_name} Orbital Plane (ICRF X-Y)", color='#e6edf3', fontsize=11, fontweight='bold')
        ax1.set_xlabel("X (km)", color='#8b949e', fontsize=9)
        ax1.set_ylabel("Y (km)", color='#8b949e', fontsize=9)
        ax1.legend(loc='upper right', fontsize=8, facecolor='#21262d', edgecolor='#30363d')
        ax1.tick_params(colors='#8b949e', labelsize=8)

        # Right Subplot: 3D Isometric View
        ax2 = fig.add_subplot(1, 2, 2, projection='3d')
        ax2.set_facecolor('#161b22')
        ax2.xaxis.pane.fill = False
        ax2.yaxis.pane.fill = False
        ax2.zaxis.pane.fill = False
        ax2.xaxis.pane.set_edgecolor('#30363d')
        ax2.yaxis.pane.set_edgecolor('#30363d')
        ax2.zaxis.pane.set_edgecolor('#30363d')
        ax2.grid(True, color='#30363d', linestyle=':', alpha=0.5)

        # Central Body in 3D
        ax2.scatter([0], [0], [0], color=b_color, s=60, label=body_name)

        # 3D trajectory curve
        ax2.plot(xs[::step], ys[::step], zs[::step], color='#7ee787', linewidth=1.5, alpha=0.85)
        ax2.scatter([xs[0]], [ys[0]], [zs[0]], color='#3fb950', s=30, marker='^')
        ax2.scatter([xs[-1]], [ys[-1]], [zs[-1]], color='#f0883e', s=30, marker='s')

        ax2.set_title(f"3D Space Trajectory ({body_name} Centered)", color='#e6edf3', fontsize=11, fontweight='bold')
        ax2.set_xlabel("X (km)", color='#8b949e', fontsize=8)
        ax2.set_ylabel("Y (km)", color='#8b949e', fontsize=8)
        ax2.set_zlabel("Z (km)", color='#8b949e', fontsize=8)
        ax2.tick_params(colors='#8b949e', labelsize=7)

    elif view_mode in ("planar", "cislunar"):
        # Single Large 2D Planar View
        ax1 = fig.add_subplot(1, 1, 1)
        ax1.set_facecolor('#161b22')
        ax1.grid(True, color='#30363d', linestyle='--', linewidth=0.5, alpha=0.7)

        ax1.plot(0, 0, marker='o', markersize=10, color=b_color, label=body_name)
        body_circle = plt.Circle((0, 0), r_body, color=b_color, fill=False, linestyle=':', linewidth=1.2)
        ax1.add_patch(body_circle)

        ax1.plot(xs[::step], ys[::step], color='#58a6ff', linewidth=1.8, label='Trajectory', alpha=0.9)
        ax1.plot(xs[0], ys[0], marker='^', markersize=7, color='#3fb950', label='Initial State')
        ax1.plot(xs[-1], ys[-1], marker='s', markersize=7, color='#f0883e', label='Final State')

        ax1.set_title(f"{body_name} Trajectory Full Overview (ICRF X-Y)", color='#e6edf3', fontsize=12, fontweight='bold')
        ax1.set_xlabel("X (km)", color='#8b949e', fontsize=9)
        ax1.set_ylabel("Y (km)", color='#8b949e', fontsize=9)
        ax1.legend(loc='upper right', fontsize=9, facecolor='#21262d', edgecolor='#30363d')
        ax1.tick_params(colors='#8b949e', labelsize=8)

    else: # target / zoom mode
        # Zoomed into final 30% of trajectory
        ax1 = fig.add_subplot(1, 1, 1)
        ax1.set_facecolor('#161b22')
        ax1.grid(True, color='#30363d', linestyle='--', linewidth=0.5, alpha=0.7)

        n_pts = len(xs)
        start_idx = max(0, int(n_pts * 0.7))
        zoom_xs = xs[start_idx:]
        zoom_ys = ys[start_idx:]

        ax1.plot(zoom_xs[::max(1, len(zoom_xs)//500)], zoom_ys[::max(1, len(zoom_xs)//500)], color='#58a6ff', linewidth=1.8, label='Approach Trajectory')
        ax1.plot(zoom_xs[-1], zoom_ys[-1], marker='s', markersize=8, color='#f0883e', label='Target / Final State')

        # If central body is near zoom area, plot it
        if abs(zoom_xs[-1]) < r_body * 5 and abs(zoom_ys[-1]) < r_body * 5:
            ax1.plot(0, 0, marker='o', markersize=8, color=b_color, label=body_name)
            body_circle = plt.Circle((0, 0), r_body, color=b_color, fill=False, linestyle=':')
            ax1.add_patch(body_circle)

        ax1.set_title(f"Target Terminal Orbit Close-up", color='#e6edf3', fontsize=12, fontweight='bold')
        ax1.set_xlabel("X (km)", color='#8b949e', fontsize=9)
        ax1.set_ylabel("Y (km)", color='#8b949e', fontsize=9)
        ax1.legend(loc='upper right', fontsize=9, facecolor='#21262d', edgecolor='#30363d')
        ax1.tick_params(colors='#8b949e', labelsize=8)

    plt.tight_layout()
    os.makedirs(os.path.dirname(output_png_path), exist_ok=True)
    plt.savefig(output_png_path, dpi=120, facecolor=fig.get_facecolor(), edgecolor='none')
    plt.close(fig)
    return True, output_png_path

if __name__ == "__main__":
    curr_dir = os.path.dirname(os.path.abspath(__file__))
    def_out_dir = os.path.abspath(os.path.join(curr_dir, "..", "..", "output"))
    t_file = os.path.join(def_out_dir, "mission_traj.txt")
    o_file = os.path.join(def_out_dir, "gmat_orbit_render.png")
    ok, msg = render_gmat_trajectory(t_file, o_file, central_body="Earth")
    print("Rendered:", ok, msg)
