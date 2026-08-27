import re

content = """
Propagate 'Coast in LEO' LEOProp(NRHOSat) {NRHOSat.ElapsedSecs = 1800.0};
Maneuver 'Execute TLI Burn' TLI_Burn(NRHOSat);
Propagate 'Coast to Mid-Course' CislunarProp(NRHOSat) {NRHOSat.ElapsedDays = 2.2};
Maneuver 'Execute TCM Burn' TCM_Burn(NRHOSat);
Propagate 'Coast to Lunar Vicinity' CislunarProp(NRHOSat) {NRHOSat.ElapsedDays = 2.6};
Maneuver 'Execute NRHOI Burn' NRHOI_Burn(NRHOSat);
NRHOSat.EarthMoonRot.X  = -173.0;
NRHOSat.EarthMoonRot.Y  = 0.0;
NRHOSat.EarthMoonRot.Z  = 3229.0;
NRHOSat.EarthMoonRot.VX = 0.0;
NRHOSat.EarthMoonRot.VY = -1.6674;
NRHOSat.EarthMoonRot.VZ = 0.0;
Propagate 'Propagate 21 Days in NRHO' LunarNRHOProp(NRHOSat) {NRHOSat.ElapsedDays = 21.0};
"""

for line in content.strip().splitlines():
    line = line.strip()
    if not line: continue
    if line.startswith('Propagate'):
        m = re.search(r'Propagate\s+(?:\'[^\']+\'\s+|\"[^\"]+\"\s+)?([A-Za-z0-9_]+)\s*\(', line)
        pname = m.group(1) if m else 'Prop'
        print('Propagate call with propagator:', pname)
    elif '=' in line and line.endswith(';'):
        m = re.search(r'([A-Za-z0-9_.]+)\s*=\s*([-0-9.]+);', line)
        if m:
            print('Equation step:', m.group(1), '=', m.group(2))
