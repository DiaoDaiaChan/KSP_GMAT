"""Static contract tests for the GMAT-native calculation boundary.

These tests do not solve an orbit. They prevent the bridge from silently
reintroducing a second propagator or an analytical burn estimator.
"""

from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
ENGINE = (ROOT / "gmat_service" / "gmat_engine.py").read_text(encoding="utf-8")
MECHJEB = (ROOT / "src" / "KSPGMAT" / "MechJebInterop.cs").read_text(encoding="utf-8")


def test_no_analytical_orbit_solver_in_runtime_bridge():
    forbidden = (
        "lunar_solver",
        "vis-viva",
        "mdot =",
        "m_final =",
        "9.80665",
    )
    for token in forbidden:
        assert token not in ENGINE
    assert "gmat.RunScript()" in ENGINE
    assert "gmat.GetRuntimeObject" in ENGINE


def test_mechjeb_is_input_only():
    assert "9.80665" not in MECHJEB
    assert "stage.deltaV =" not in MECHJEB


def test_finite_burn_node_comes_from_gmat_boundary_states():
    assert "FiniteBurn.GetDeltaVInertial" in ENGINE
    assert "finite_burn_vnb_delta" in ENGINE
    assert '"has_native_delta_v": True' in ENGINE
    assert '"principia_writeable": True' in ENGINE
    assert "ImpulsiveBurn is no longer supported" in ENGINE
    assert "Create ImpulsiveBurn" not in ENGINE


if __name__ == "__main__":
    test_no_analytical_orbit_solver_in_runtime_bridge()
    test_mechjeb_is_input_only()
    test_finite_burn_node_comes_from_gmat_boundary_states()
    print("GMAT native authority checks passed")
