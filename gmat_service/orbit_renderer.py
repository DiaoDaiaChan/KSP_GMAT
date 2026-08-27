import os
import math
import numpy as np
import matplotlib
matplotlib.use('Agg') # Headless rendering
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D

def render_gmat_trajectory(traj_file_path, output_png_path, view_mode="dual", moon_pos_arr=None):
    """
    Renders publication-quality dark-mode aerospace trajectory plots from GMAT output.
    view_mode:
      - 'dual': 2 subplots (Left: Earth-Moon Cislunar Transfer; Right: Lunar NRHO Zoom)
      - 'cislunar': Full cislunar transfer view
      - 'nrho': Zoomed view near Moon
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

    times = np.array(times)
    xs = np.array(xs)
    ys = np.array(ys)
    zs = np.array(zs)

    # Style configuration
    plt.style.use('dark_background')
    fig = plt.figure(figsize=(10, 5), dpi=120)
    fig.patch.set_facecolor('#0d1117')

    if view_mode == "dual":
        # Left Subplot: Earth-Moon Global Transfer (XY Plane)
        ax1 = fig.add_subplot(1, 2, 1)
        ax1.set_facecolor('#161b22')
        ax1.grid(True, color='#30363d', linestyle='--', linewidth=0.5, alpha=0.7)

        # Plot Earth at origin
        ax1.plot(0, 0, marker='o', markersize=9, color='#388bfd', label='Earth')
        earth_circle = plt.Circle((0, 0), 6378.14, color='#1f6feb', fill=False, linestyle=':')
        ax1.add_patch(earth_circle)

        # Plot trajectory curve
        # Subsample if too large
        step = max(1, len(xs) // 1000)
        ax1.plot(xs[::step], ys[::step], color='#58a6ff', linewidth=1.5, label='Trajectory', alpha=0.9)
        # Mark initial point (LEO)
        ax1.plot(xs[0], ys[0], marker='^', markersize=6, color='#3fb950', label='LEO Start')
        # Mark final point
        ax1.plot(xs[-1], ys[-1], marker='s', markersize=6, color='#f0883e', label='NRHO Arrival')

        ax1.set_title("Earth-Moon Transfer (ICRF X-Y)", color='#e6edf3', fontsize=11, fontweight='bold')
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

        # Plot Earth in 3D
        ax2.scatter([0], [0], [0], color='#388bfd', s=60, label='Earth')

        # 3D trajectory curve
        ax2.plot(xs[::step], ys[::step], zs[::step], color='#7ee787', linewidth=1.5, alpha=0.85)
        ax2.scatter([xs[0]], [ys[0]], [zs[0]], color='#3fb950', s=30, marker='^')
        ax2.scatter([xs[-1]], [ys[-1]], [zs[-1]], color='#f0883e', s=30, marker='s')

        ax2.set_title("3D Cislunar & NRHO Flight Path", color='#e6edf3', fontsize=11, fontweight='bold')
        ax2.set_xlabel("X (km)", color='#8b949e', fontsize=8)
        ax2.set_ylabel("Y (km)", color='#8b949e', fontsize=8)
        ax2.set_zlabel("Z (km)", color='#8b949e', fontsize=8)
        ax2.tick_params(colors='#8b949e', labelsize=7)

    plt.tight_layout()
    os.makedirs(os.path.dirname(output_png_path), exist_ok=True)
    plt.savefig(output_png_path, dpi=120, facecolor=fig.get_facecolor(), edgecolor='none')
    plt.close(fig)
    return True, output_png_path

if __name__ == "__main__":
    t_file = r"d:\nocomp\gmat-win-R2026a\output\mission_traj.txt"
    o_file = r"d:\nocomp\gmat-win-R2026a\output\gmat_orbit_render.png"
    ok, msg = render_gmat_trajectory(t_file, o_file)
    print("Rendered:", ok, msg)
