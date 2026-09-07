// ============================================================================
// ESA PyKEP v3 (kep3) Official Core Astrodynamics Translation Unit
// Copyright (c) 2023-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Pure MSVC compilation bundle without external LLVM/Heyoka dependency
// ============================================================================

#define kep3_EXPORTS 1

#include "../../../pykep_sourcecode/src/epoch.cpp"
#include "../../../pykep_sourcecode/src/planet.cpp"
#include "../../../pykep_sourcecode/src/lambert_problem.cpp"
#include "../../../pykep_sourcecode/src/linalg.cpp"
#include "../../../pykep_sourcecode/src/udpla/keplerian.cpp"
#include "../../../pykep_sourcecode/src/udpla/jpl_lp.cpp"
#include "../../../pykep_sourcecode/src/core_astro/flyby.cpp"
#include "../../../pykep_sourcecode/src/core_astro/ic2par2ic.cpp"
#include "../../../pykep_sourcecode/src/core_astro/ic2mee2ic.cpp"
#include "../../../pykep_sourcecode/src/core_astro/mima.cpp"
#include "../../../pykep_sourcecode/src/core_astro/mee2par2mee.cpp"
#include "../../../pykep_sourcecode/src/core_astro/stm.cpp"
#include "../../../pykep_sourcecode/src/core_astro/propagate_lagrangian.cpp"
#include "../../../pykep_sourcecode/src/core_astro/basic_transfers.cpp"
#include "../../../pykep_sourcecode/src/core_astro/encodings.cpp"
