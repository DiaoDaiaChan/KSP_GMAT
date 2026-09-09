// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge (Master Root)
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT Interplanetary Trajectory & MGA Engine
// ============================================================================

#include "pykep_common.h"

// Modular Translation Units
#include "pykep_mga_ballistic.cpp"
#include "pykep_mga_hybrid.cpp"
#include "pykep_stm_bplane.cpp"
#include "pykep_primer_vector.cpp"
#include "pykep_mee.cpp"
#include "pykep_sims_flanagan.cpp"
#include "pykep_spice.cpp"
#include "pykep_align_injection.cpp"
#include "pykep_transfer_window.cpp"
#include "pykep_wsb_manifold.cpp"
#include "pykep_gpu_porkchop.cpp"
