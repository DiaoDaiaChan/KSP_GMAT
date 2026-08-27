"""
Ephemeris and Time Calibration module for KSP-Principia and GMAT.
Maps KSP/Principia time to GMAT Julian / Gregorian epochs and aligns coordinate frames.
"""

import math
from datetime import datetime, timedelta

# Sol-Configs / RSS default game epoch: 1951-01-01 00:00:00 TT (JD 2433647.5)
SOL_GAME_EPOCH_JD = 2433647.5
GMAT_MJD_OFFSET = 2430000.0 # GMAT Modified Julian Date base (Day 0 = Jan 5, 1941)

BODY_NAME_MAP = {
    "sun": "Sun",
    "earth": "Earth",
    "kerbin": "Earth",
    "moon": "Luna",
    "luna": "Luna",
    "mun": "Luna",
    "mars": "Mars",
    "duna": "Mars",
    "venus": "Venus",
    "eve": "Venus",
    "mercury": "Mercury",
    "moho": "Mercury",
    "jupiter": "Jupiter",
    "jool": "Jupiter",
    "saturn": "Saturn",
    "uranus": "Uranus",
    "neptune": "Neptune"
}

def ksp_ut_to_jd(ut_seconds, game_epoch_jd=SOL_GAME_EPOCH_JD):
    """Converts KSP Universal Time (seconds since game_epoch) to Julian Date."""
    jd = game_epoch_jd + (ut_seconds / 86400.0)
    # GMAT epoch valid range is Oct 4 1957 (JD 2436116.0) to Feb 28 2100 (JD 2488127.5).
    # If KSP game starts in 1951 (JD 2433647.5), map it into GMAT valid range (e.g. J2000 epoch or +10 yrs)
    # while preserving exact orbital dynamics and relative time.
    if jd < 2436116.5:
        # Shift forward into valid space age (e.g. year 1960 or J2000)
        jd = 2437000.0 + (ut_seconds / 86400.0)
    return jd

def jd_to_gmat_mjd(jd):
    """Converts Julian Date to GMAT Modified Julian Date."""
    return jd - GMAT_MJD_OFFSET

def jd_to_utc_gregorian(jd):
    """Converts Julian Date to Gregorian UTC string format required by GMAT (DD Mon YYYY HH:MM:SS.sss)."""
    # Standard JD to calendar conversion
    jd_adjusted = jd + 0.5
    Z = int(math.floor(jd_adjusted))
    F = jd_adjusted - Z

    if Z < 2299161:
        A = Z
    else:
        alpha = int(math.floor((Z - 1867216.25) / 36524.25))
        A = Z + 1 + alpha - int(math.floor(alpha / 4.0))

    B = A + 1524
    C = int(math.floor((B - 122.1) / 365.25))
    D = int(math.floor(365.25 * C))
    E = int(math.floor((B - D) / 30.6001))

    day = B - D - int(math.floor(30.6001 * E)) + F
    if E < 14:
        month = E - 1
    else:
        month = E - 13

    if month > 2:
        year = C - 4716
    else:
        year = C - 4715

    day_int = int(math.floor(day))
    fraction_day = day - day_int
    total_seconds = fraction_day * 86400.0

    hours = int(total_seconds // 3600)
    minutes = int((total_seconds % 3600) // 60)
    seconds = total_seconds % 60

    months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
    month_str = months[month - 1]

    return f"{day_int:02d} {month_str} {year:04d} {hours:02d}:{minutes:02d}:{seconds:06.3f}"

def map_body_name(ksp_name):
    """Maps KSP body name to GMAT body name."""
    if not ksp_name:
        return "Earth"
    cleaned = str(ksp_name).strip().lower()
    return BODY_NAME_MAP.get(cleaned, ksp_name)
