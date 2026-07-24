"""Validate a staged or installed VideoAnalysisDesktop engine.

This script is copied to ``<install>\\engine\\engine_check.py`` by
``stage_engine.py``.  It deliberately does not require Azure credentials: it
checks the immutable runtime shipped by the installer, while credential checks
belong to the administrator configuration flow.

Examples::

    <engine>\\python\\python.exe <engine>\\engine_check.py --verify-hashes
    python desktop/build/engine_check.py --engine-dir desktop/engine --json
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path, PurePosixPath
from typing import Any


BUILD_DIR = Path(__file__).resolve().parent
DEFAULT_DEV_ENGINE = BUILD_DIR.parent / "engine"
MANIFEST_NAME = "engine.manifest.json"
MODEL_MANIFEST_NAME = "models.manifest.json"

REQUIRED_FILES = (
    "python/python.exe",
    "python/python312.dll",
    "python/python312._pth",
    "python/app/video_analysis_pipeline/desktop_entry.py",
    "ffmpeg/ffmpeg.exe",
    "ffmpeg/ffprobe.exe",
    MANIFEST_NAME,
)

REQUIRED_IMPORTS = {
    "video_analysis_pipeline": "video_analysis_pipeline",
    "azure-cognitiveservices-speech": "azure.cognitiveservices.speech",
    "demucs": "demucs",
    "faster-whisper": "faster_whisper",
    "torch": "torch",
    "openpyxl": "openpyxl",
    "Pillow": "PIL",
    "rapidfuzz": "rapidfuzz",
    "rapidocr-onnxruntime": "rapidocr_onnxruntime",
    "requests": "requests",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Validate a VideoAnalysisDesktop engine")
    parser.add_argument("--engine-dir", type=Path, help="Engine root; defaults to the directory containing this script")
    parser.add_argument(
        "--models-dir",
        type=Path,
        help="Installed model root; defaults to engine/models or the ProgramData location used by the installer",
    )
    parser.add_argument("--verify-hashes", action="store_true", help="Verify all manifest critical-file SHA-256 entries")
    parser.add_argument("--require-models", action="store_true", help="Fail if the engine has no verified offline models")
    parser.add_argument("--json", action="store_true", help="Print a JSON result")
    return parser.parse_args()


def sha256_file(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def hash_model_manifest_path(path: Path) -> str:
    """Match the directory hashing convention in download_models.py."""
    if path.is_file():
        return sha256_file(path)
    hasher = hashlib.sha256()
    for item in sorted(candidate for candidate in path.rglob("*") if candidate.is_file()):
        with item.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                hasher.update(chunk)
    return hasher.hexdigest()


def check_result(component: str, status: str, message: str) -> dict[str, str]:
    return {"component": component, "status": status, "message": message}


def run_checked(command: list[str]) -> tuple[bool, str]:
    try:
        result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    except OSError as exc:
        return False, str(exc)
    output = (result.stdout or result.stderr).strip()
    return result.returncode == 0, output[:500]


def resolve_manifest_path(engine_dir: Path, relative: str) -> Path:
    member = PurePosixPath(relative.replace("\\", "/"))
    if member.is_absolute() or ".." in member.parts:
        raise ValueError(f"Unsafe manifest path: {relative}")
    return engine_dir.joinpath(*member.parts)


def check_layout(engine_dir: Path) -> list[dict[str, str]]:
    results: list[dict[str, str]] = []
    for relative in REQUIRED_FILES:
        path = engine_dir / relative
        results.append(
            check_result(f"layout-{relative}", "ok" if path.is_file() else "fail", str(path) if path.is_file() else f"Missing: {path}")
        )
    return results


def load_manifest(engine_dir: Path) -> tuple[dict[str, Any] | None, list[dict[str, str]]]:
    path = engine_dir / MANIFEST_NAME
    if not path.is_file():
        return None, [check_result("engine-manifest", "fail", f"Missing: {path}")]
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return None, [check_result("engine-manifest", "fail", f"Invalid JSON: {exc}")]
    if manifest.get("schema_version") != "1.0":
        return manifest, [check_result("engine-manifest", "fail", "Unsupported or missing schema_version")]
    if manifest.get("platform") != "win-x64":
        return manifest, [check_result("engine-manifest", "fail", "Manifest platform must be win-x64")]
    return manifest, [check_result("engine-manifest", "ok", "schema_version=1.0, platform=win-x64")]


def check_python(engine_dir: Path) -> list[dict[str, str]]:
    results: list[dict[str, str]] = []
    python_exe = engine_dir / "python" / "python.exe"
    if not python_exe.is_file():
        return [check_result("python", "fail", f"Missing: {python_exe}")]

    ok, output = run_checked([str(python_exe), "--version"])
    results.append(check_result("python", "ok" if ok else "fail", output or "No version output"))
    if not ok:
        return results

    for display_name, module_name in REQUIRED_IMPORTS.items():
        ok, output = run_checked([str(python_exe), "-c", f"import {module_name}"])
        results.append(check_result(f"python-import-{display_name}", "ok" if ok else "fail", "imported" if ok else output))

    ok, output = run_checked([str(python_exe), "-m", "video_analysis_pipeline.desktop_entry", "--help"])
    results.append(check_result("desktop-entry", "ok" if ok else "fail", "--help succeeded" if ok else output))
    return results


def check_ffmpeg(engine_dir: Path) -> list[dict[str, str]]:
    results: list[dict[str, str]] = []
    for binary in ("ffmpeg.exe", "ffprobe.exe"):
        path = engine_dir / "ffmpeg" / binary
        if not path.is_file():
            results.append(check_result(f"ffmpeg-{binary}", "fail", f"Missing: {path}"))
            continue
        ok, output = run_checked([str(path), "-version"])
        version = output.splitlines()[0] if output else "No version output"
        results.append(check_result(f"ffmpeg-{binary}", "ok" if ok else "fail", version))
    return results


def check_models(models_dir: Path, manifest: dict[str, Any] | None, require_models: bool) -> list[dict[str, str]]:
    results: list[dict[str, str]] = []
    model_info = (manifest or {}).get("models")
    if not isinstance(model_info, dict) or not model_info.get("included"):
        status = "fail" if require_models else "warn"
        return [check_result("models", status, "No offline models are included in this engine")]

    manifest_path = models_dir / MODEL_MANIFEST_NAME
    if not manifest_path.is_file():
        return [check_result("models-manifest", "fail", f"Missing: {manifest_path}")]
    expected_manifest_hash = model_info.get("manifest_sha256")
    actual_manifest_hash = sha256_file(manifest_path)
    if expected_manifest_hash and actual_manifest_hash != expected_manifest_hash:
        results.append(check_result("models-manifest", "fail", "models.manifest.json SHA-256 mismatch"))
        return results

    try:
        model_manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        return [check_result("models-manifest", "fail", f"Invalid JSON: {exc}")]
    results.append(check_result("models-manifest", "ok", "verified"))
    for model in model_manifest.get("models", []):
        name = str(model.get("name", "unnamed-model"))
        relative = model.get("path")
        expected = model.get("sha256")
        if not relative or not expected:
            results.append(check_result(f"model-{name}", "fail", "Manifest entry lacks path or sha256"))
            continue
        try:
            path = resolve_manifest_path(models_dir, str(relative))
        except ValueError as exc:
            results.append(check_result(f"model-{name}", "fail", str(exc)))
            continue
        if not path.exists():
            results.append(check_result(f"model-{name}", "fail", f"Missing: {path}"))
            continue
        actual = hash_model_manifest_path(path)
        results.append(check_result(f"model-{name}", "ok" if actual == expected else "fail", "verified" if actual == expected else "SHA-256 mismatch"))
    return results


def check_critical_hashes(engine_dir: Path, manifest: dict[str, Any] | None) -> list[dict[str, str]]:
    if manifest is None:
        return [check_result("critical-file-hashes", "fail", "Cannot verify hashes without a valid manifest")]
    values = manifest.get("critical_files")
    if not isinstance(values, dict) or not values:
        return [check_result("critical-file-hashes", "fail", "Manifest contains no critical_files hashes")]
    results: list[dict[str, str]] = []
    for relative, expected in sorted(values.items()):
        try:
            path = resolve_manifest_path(engine_dir, str(relative))
        except ValueError as exc:
            results.append(check_result(f"hash-{relative}", "fail", str(exc)))
            continue
        if not path.is_file():
            results.append(check_result(f"hash-{relative}", "fail", f"Missing: {path}"))
            continue
        actual = sha256_file(path)
        results.append(check_result(f"hash-{relative}", "ok" if actual == expected else "fail", "verified" if actual == expected else "SHA-256 mismatch"))
    return results


def default_models_dir(engine_dir: Path) -> Path:
    staged_models = engine_dir / "models"
    if staged_models.exists():
        return staged_models
    program_data = Path(os.environ.get("PROGRAMDATA", r"C:\ProgramData"))
    return program_data / "Company" / "VideoAnalysisDesktop" / "models"


def run_engine_check(
    engine_dir: Path,
    *,
    models_dir: Path | None,
    verify_hashes: bool,
    require_models: bool,
) -> dict[str, Any]:
    engine_dir = engine_dir.expanduser().resolve()
    resolved_models_dir = (models_dir or default_models_dir(engine_dir)).expanduser().resolve()
    checks: list[dict[str, str]] = []
    checks.extend(check_layout(engine_dir))
    manifest, manifest_checks = load_manifest(engine_dir)
    checks.extend(manifest_checks)
    checks.extend(check_python(engine_dir))
    checks.extend(check_ffmpeg(engine_dir))
    checks.extend(check_models(resolved_models_dir, manifest, require_models))
    if verify_hashes:
        checks.extend(check_critical_hashes(engine_dir, manifest))
    failed = [check for check in checks if check["status"] == "fail"]
    return {
        "schema_version": "1.0",
        "engine_dir": str(engine_dir),
        "models_dir": str(resolved_models_dir),
        "overall_ok": not failed,
        "checks": checks,
    }


def main() -> int:
    args = parse_args()
    if args.engine_dir is not None:
        engine_dir = args.engine_dir
    elif (BUILD_DIR / MANIFEST_NAME).is_file():
        engine_dir = BUILD_DIR
    else:
        engine_dir = DEFAULT_DEV_ENGINE
    result = run_engine_check(
        engine_dir,
        models_dir=args.models_dir,
        verify_hashes=args.verify_hashes,
        require_models=args.require_models,
    )
    if args.json:
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        print("VideoAnalysisDesktop Engine Check")
        print("=" * 48)
        for check in result["checks"]:
            icon = {"ok": "OK", "warn": "WARN", "fail": "FAIL"}[check["status"]]
            print(f"[{icon:4}] {check['component']}: {check['message']}")
        print(f"\nOverall: {'PASS' if result['overall_ok'] else 'FAIL'}")
    return 0 if result["overall_ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
