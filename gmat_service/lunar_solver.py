import math
import numpy as np
from datetime import datetime

# Gravitational parameters (km^3 / s^2)
MU_EARTH = 398600.4418
MU_MOON = 4902.800066

def get_moon_state_approx(ut_seconds, epoch_utc_str=None):
    """
    Computes Earth-Moon vector in ICRF / J2000 coordinates.
    Moon semi-major axis: ~384400 km
    Orbital inclination to equator: ~18.3 to 28.6 deg (mean ~23.4 deg)
    Orbital period: 27.321661 days (sidereal month)
    """
    # Sidereal period in seconds
    T_moon = 27.321661 * 86400.0
    omega_moon = 2.0 * math.pi / T_moon
    
    # Reference epoch: J2000 = 2000-01-01 12:00:00 TT (ut ~ 0 or offset)
    # Mean inclination to Earth equator ~ 28.5 deg (varies between 18.3 and 28.6)
    inc = math.radians(28.5)
    
    # Current orbital phase of Moon
    theta = (omega_moon * ut_seconds) % (2.0 * math.pi)
    
    r_dist = 384400.0 # mean distance km
    v_speed = math.sqrt(MU_EARTH / r_dist) # ~1.022 km/s
    
    # Moon position in Earth-centered ICRF
    x = r_dist * math.cos(theta)
    y = r_dist * math.sin(theta) * math.cos(inc)
    z = r_dist * math.sin(theta) * math.sin(inc)
    
    vx = -v_speed * math.sin(theta)
    vy =  v_speed * math.cos(theta) * math.cos(inc)
    vz =  v_speed * math.cos(theta) * math.sin(inc)
    
    return np.array([x, y, z]), np.array([vx, vy, vz])

def solve_cislunar_targeting(r0, v0, ut_current, target_perilune_km=3229.0):
    """
    Solves optimal TLI departure time, delta-v vector (VNB),
    and arrival capture burn for an active vessel in LEO to reach Moon NRHO.
    
    r0: Vessel position [x, y, z] in km relative to Earth
    v0: Vessel velocity [vx, vy, vz] in km/s relative to Earth
    ut_current: current simulation UT in seconds
    """
    r0 = np.array(r0, dtype=float)
    v0 = np.array(v0, dtype=float)
    
    r0_mag = np.linalg.norm(r0)
    v0_mag = np.linalg.norm(v0)
    
    # Vessel orbital period
    sma_0 = 1.0 / (2.0 / r0_mag - (v0_mag**2) / MU_EARTH)
    period_0 = 2.0 * math.pi * math.sqrt(abs(sma_0)**3 / MU_EARTH)
    
    # Nominal transfer time of flight ~ 4.3 to 4.8 days
    # Half-period of transfer ellipse
    r_moon_mean = 384400.0
    sma_transfer = (r0_mag + r_moon_mean) / 2.0
    tof_transfer = math.pi * math.sqrt(sma_transfer**3 / MU_EARTH) # ~ 4.35 days
    
    # Angular velocity of Moon and Vessel
    T_moon = 27.321661 * 86400.0
    omega_moon = 2.0 * math.pi / T_moon
    omega_vessel = 2.0 * math.pi / period_0
    
    # During transfer, Moon sweeps angle
    delta_theta_moon = omega_moon * tof_transfer
    # Required phase angle at TLI: Moon should be (pi - delta_theta_moon) ahead
    phi_req = math.pi - delta_theta_moon # ~ 125 degrees
    
    # Search over 1 orbital period of vessel (0 to period_0) for optimal departure
    best_t_coast = 0.0
    best_dv_vnb = None
    min_miss = float('inf')
    
    # Unit vectors of vessel's orbital plane
    h_vec = np.cross(r0, v0)
    h_hat = h_vec / np.linalg.norm(h_vec)
    
    # Let's search best coast time in steps of 30 seconds
    num_steps = max(10, int(period_0 / 30.0))
    dt_arr = np.linspace(0, period_0, num_steps)
    
    # Target Moon state at arrival
    for dt_c in dt_arr:
        t_dep = ut_current + dt_c
        t_arr = t_dep + tof_transfer
        
        # Propagate vessel unperturbed along its circular/elliptic orbit for dt_c
        angle_c = omega_vessel * dt_c
        # Rotate r0, v0 in orbital plane by angle_c
        u_r = r0 / r0_mag
        u_v = np.cross(h_hat, u_r)
        
        r_dep = r0_mag * (math.cos(angle_c) * u_r + math.sin(angle_c) * u_v)
        v_dep = v0_mag * (-math.sin(angle_c) * u_r + math.cos(angle_c) * u_v)
        
        # Moon position at arrival
        r_moon_arr, v_moon_arr = get_moon_state_approx(t_arr)
        
        # Angle between r_dep and r_moon_arr
        cos_sep = np.dot(r_dep, r_moon_arr) / (np.linalg.norm(r_dep) * np.linalg.norm(r_moon_arr))
        cos_sep = max(-1.0, min(1.0, cos_sep))
        sep_angle = math.acos(cos_sep)
        
        # We want sep_angle ~ phi_req
        diff = abs(sep_angle - phi_req)
        if diff < min_miss:
            min_miss = diff
            best_t_coast = dt_c
            
    # Now compute precise TLI speed required:
    # vis-viva at departure
    v_depart_req = math.sqrt(MU_EARTH * (2.0 / r0_mag - 1.0 / sma_transfer))
    dv_prograde = (v_depart_req - v0_mag) * 1000.0 # m/s
    
    # Out of plane alignment (Normal dv)
    # Angle between vessel plane and Moon at arrival
    r_moon_arr, v_moon_arr = get_moon_state_approx(ut_current + best_t_coast + tof_transfer)
    moon_norm = np.cross(r_moon_arr, v_moon_arr)
    moon_norm = moon_norm / np.linalg.norm(moon_norm)
    
    plane_inc_diff = math.asin(max(-1.0, min(1.0, np.dot(h_hat, r_moon_arr) / np.linalg.norm(r_moon_arr))))
    dv_normal = (v_depart_req * math.sin(plane_inc_diff)) * 1000.0 * 0.15 # initial component, rest fine tuned at TCM
    
    # Arrival relative speed at Moon perilune
    v_arr_inf = abs(v_depart_req * (r0_mag / r_moon_mean) - np.linalg.norm(v_moon_arr))
    v_perilune = math.sqrt(v_arr_inf**2 + 2.0 * MU_MOON / target_perilune_km)
    
    # Target NRHO speed at perilune = 1.6674 km/s
    v_target_nrho = 1.6674 # km/s
    dv_nrhoi = -(v_perilune - v_target_nrho) * 1000.0 # m/s (retrograde braking)
    
    return {
        "coast_seconds": best_t_coast,
        "tof_days": tof_transfer / 86400.0,
        "tli_dv_v": dv_prograde,
        "tli_dv_n": dv_normal,
        "tli_dv_b": 0.0,
        "tcm_dv_v": 5.0,
        "tcm_dv_n": plane_inc_diff * 50.0,
        "tcm_dv_b": -1.0,
        "nrhoi_dv_v": dv_nrhoi,
        "nrhoi_dv_n": 0.0,
        "nrhoi_dv_b": 0.0
    }

if __name__ == "__main__":
    # Test with typical LEO 200km orbit
    r_leo = [6578.14, 0.0, 0.0]
    v_leo = [0.0, 7.784, 0.0]
    sol = solve_cislunar_targeting(r_leo, v_leo, 100000.0)
    print("Solved Cislunar Targeting:")
    for k, v in sol.items():
        print(f"  {k}: {v}")
