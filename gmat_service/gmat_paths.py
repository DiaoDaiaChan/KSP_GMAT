"""Resolve the GMAT installation without baking a machine-specific path in."""

import os
from pathlib import Path


def _configured_root(service_dir):
    candidates = (
        service_dir.parent / "Config" / "backend.cfg",
        service_dir.parent / "GameData" / "KSPGMAT" / "Config" / "backend.cfg",
    )
    for config in candidates:
        if not config.exists():
            continue
        for line in config.read_text(encoding="utf-8-sig").splitlines():
            key, separator, value = line.partition("=")
            if separator and key.strip() == "gmat_root":
                return value.strip().strip('"')
    return ""


def _looks_like_gmat_root(path):
    return (path / "GMAT.exe").exists() or (path / "gmatpy").is_dir()


def resolve_gmat_root(service_dir=None):
    service = Path(service_dir or Path(__file__).resolve().parent)
    configured = os.environ.get("GMAT_ROOT", "").strip() or _configured_root(service)
    if configured:
        configured_path = Path(configured).expanduser().resolve()
        if _looks_like_gmat_root(configured_path):
            return str(configured_path)
        parent = configured_path.parent
        if _looks_like_gmat_root(parent):
            return str(parent)

    # Supports both layouts: service directly under GMAT/bin and this repo
    # nested one directory below the GMAT installation.
    candidates = (service.parent, service.parent.parent)
    for candidate in candidates:
        if _looks_like_gmat_root(candidate):
            return str(candidate.resolve())
    return str(service.parent.resolve())
