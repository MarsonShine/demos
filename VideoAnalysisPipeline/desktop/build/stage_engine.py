"""Create a reproducible, self-contained Windows x64 Python engine.

The desktop application must never create a virtual environment or run pip on
an end-user machine.  This script is intentionally a *release-build* tool:
it builds ``desktop/engine`` from a verified CPython embeddable distribution,
a pre-resolved wheelhouse, the repository's Python sources, FFmpeg, and
optionally the required ML models.  The resulting directory is what Inno
Setup packages into the offline installer.

Example (offline/reproducible build)::

    python desktop/build/stage_engine.py ^
      --output-dir desktop/engine ^
      --python-zip artifacts/python-3.12.10-embed-amd64.zip ^
      --wheelhouse artifacts/wheels-win_amd64-cp312 ^
      --models-dir artifacts/models ^
      --ffmpeg-dir artifacts/ffmpeg

``--allow-python-download``, ``--allow-dependency-download``, and
``--allow-model-download`` are explicit opt-ins for a connected build machine.
They are deliberately not used by the installed desktop application.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
import zipfile
from datetime import datetime, timezone
from email.parser import Parser
from pathlib import Path, PurePosixPath
from typing import Iterable
from urllib import request


BUILD_DIR = Path(__file__).resolve().parent
DESKTOP_DIR = BUILD_DIR.parent
REPOSITORY_ROOT = DESKTOP_DIR.parent

# Keep the runtime version and checksum together.  The checksum is the
# CPython package checksum published in the official 3.12.10 SBOM.
PYTHON_VERSION = "3.12.10"
PYTHON_EMBED_FILENAME = f"python-{PYTHON_VERSION}-embed-amd64.zip"
PYTHON_EMBED_URL = f"https://www.python.org/ftp/python/{PYTHON_VERSION}/{PYTHON_EMBED_FILENAME}"
PYTHON_EMBED_SHA256 = "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3"

LOCK_FILE = BUILD_DIR / "requirements-lock.txt"
MODEL_MANIFEST_NAME = "models.manifest.json"
ENGINE_MANIFEST_NAME = "engine.manifest.json"

REQUIRED_ENGINE_FILES = (
    "python/python.exe",
    "python/python312.dll",
    "python/python312._pth",
    "python/app/video_analysis_pipeline/desktop_entry.py",
    "ffmpeg/ffmpeg.exe",
    "ffmpeg/ffprobe.exe",
    ENGINE_MANIFEST_NAME,
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

THIRD_PARTY_LICENSES = {
    "Python": {"license": "PSF License", "url": "https://docs.python.org/3/license.html"},
    "PyTorch": {"license": "BSD-3-Clause", "url": "https://github.com/pytorch/pytorch/blob/main/LICENSE"},
    "demucs": {"license": "MIT", "url": "https://github.com/facebookresearch/demucs/blob/main/LICENSE"},
    "faster-whisper": {"license": "MIT", "url": "https://github.com/SYSTRAN/faster-whisper/blob/master/LICENSE"},
    "FFmpeg": {"license": "GPL v3+ with libx264", "url": "https://ffmpeg.org/legal.html"},
    "x264": {"license": "GPL v2+", "url": "https://www.videolan.org/developers/x264.html"},
    "openpyxl": {"license": "MIT", "url": "https://github.com/theorchard/openpyxl/blob/main/LICENSE"},
    "Pillow": {"license": "MIT-CMU", "url": "https://github.com/python-pillow/Pillow/blob/main/LICENSE"},
    "RapidFuzz": {"license": "MIT", "url": "https://github.com/maxbachmann/rapidfuzz/blob/main/LICENSE"},
    "rapidocr-onnxruntime": {"license": "Apache-2.0", "url": "https://github.com/RapidAI/RapidOCR/blob/main/LICENSE"},
    "onnxruntime": {"license": "MIT", "url": "https://github.com/microsoft/onnxruntime/blob/main/LICENSE"},
}


class StageError(RuntimeError):
    """An actionable staging failure."""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Stage the VideoAnalysisDesktop Python engine")
    parser.add_argument("--output-dir", type=Path, required=True, help="Final engine directory, normally desktop/engine")
    parser.add_argument(
        "--python-zip",
        type=Path,
        help=f"Verified local {PYTHON_EMBED_FILENAME}; required unless --allow-python-download is supplied",
    )
    parser.add_argument(
        "--allow-python-download",
        action="store_true",
        help="Allow downloading the pinned CPython archive to desktop/build/cache (build machines only)",
    )
    parser.add_argument(
        "--wheelhouse",
        type=Path,
        help="Directory containing all pre-resolved win_amd64/cp312 dependency wheels",
    )
    parser.add_argument(
        "--allow-dependency-download",
        action="store_true",
        help="Allow pip download to populate desktop/build/cache/wheels-win_amd64-cp312 (build machines only)",
    )
    parser.add_argument("--models-dir", type=Path, help="Existing models directory containing models.manifest.json")
    parser.add_argument(
        "--allow-model-download",
        action="store_true",
        help="Allow download_models.py to download models (build machines only)",
    )
    parser.add_argument("--skip-models", action="store_true", help="Create a development-only engine without models")
    parser.add_argument("--ffmpeg-dir", type=Path, help="Directory containing ffmpeg.exe and ffprobe.exe")
    parser.add_argument(
        "--clean-output",
        action="store_true",
        help="Replace an existing output directory after a successful stage; never implied by default",
    )
    parser.add_argument("--skip-validation", action="store_true", help="Skip the post-stage self-check (development only)")
    parser.add_argument(
        "--keep-failed-stage",
        action="store_true",
        help="Keep the temporary .engine.staging-* directory if staging fails",
    )
    return parser.parse_args()


def sha256_file(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def normalized_name(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def safe_extract_zip(archive: Path, destination: Path) -> None:
    """Extract a zip archive without accepting path traversal entries."""
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as source:
        for entry in source.infolist():
            member = PurePosixPath(entry.filename)
            if member.is_absolute() or ".." in member.parts:
                raise StageError(f"Unsafe archive member in {archive.name}: {entry.filename}")
            target = destination.joinpath(*member.parts)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open(entry) as src, target.open("wb") as dst:
                shutil.copyfileobj(src, dst)


def verify_embedded_python_archive(path: Path) -> str:
    if not path.is_file():
        raise StageError(f"Embedded Python archive does not exist: {path}")
    actual_hash = sha256_file(path)
    if actual_hash.lower() != PYTHON_EMBED_SHA256:
        raise StageError(
            "Embedded Python SHA-256 mismatch. "
            f"Expected {PYTHON_EMBED_SHA256}, got {actual_hash}. "
            "Use the pinned official CPython 3.12.10 amd64 embeddable archive."
        )
    return actual_hash


def acquire_embedded_python(args: argparse.Namespace) -> tuple[Path, str, str]:
    """Return archive path, verified digest, and a provenance label."""
    if args.python_zip is not None:
        archive = args.python_zip.expanduser().resolve()
        return archive, verify_embedded_python_archive(archive), "local-archive"

    cache_dir = BUILD_DIR / "cache"
    archive = cache_dir / PYTHON_EMBED_FILENAME
    if archive.exists():
        return archive, verify_embedded_python_archive(archive), "verified-cache"

    if not args.allow_python_download:
        raise StageError(
            "No embedded Python archive was supplied. Pass --python-zip <path> for an offline build, "
            "or explicitly opt in with --allow-python-download."
        )

    cache_dir.mkdir(parents=True, exist_ok=True)
    partial = archive.with_suffix(archive.suffix + ".part")
    try:
        print(f"Downloading pinned CPython {PYTHON_VERSION} from {PYTHON_EMBED_URL}...")
        request.urlretrieve(PYTHON_EMBED_URL, partial)
        verified_hash = verify_embedded_python_archive(partial)
        partial.replace(archive)
        return archive, verified_hash, "downloaded-from-python.org"
    except Exception:
        partial.unlink(missing_ok=True)
        raise


def configure_embedded_python(python_dir: Path) -> Path:
    """Enable site packages and the staged application for the embeddable runtime."""
    python_exe = python_dir / "python.exe"
    dll = python_dir / "python312.dll"
    pth = python_dir / "python312._pth"
    for required in (python_exe, dll, pth):
        if not required.is_file():
            raise StageError(f"The CPython archive did not contain expected file: {required.name}")

    original_lines = pth.read_text(encoding="utf-8").splitlines()
    retained = [line for line in original_lines if line.strip() not in {"Lib/site-packages", "app", "import site", "#import site"}]
    retained.extend(["Lib/site-packages", "app", "import site"])
    pth.write_text("\n".join(retained) + "\n", encoding="utf-8")

    (python_dir / "Lib" / "site-packages").mkdir(parents=True, exist_ok=True)
    (python_dir / "Scripts").mkdir(parents=True, exist_ok=True)
    return python_exe


def read_wheel_metadata(wheel: Path) -> tuple[str, str]:
    with zipfile.ZipFile(wheel) as archive:
        metadata_names = [name for name in archive.namelist() if name.endswith(".dist-info/METADATA")]
        if len(metadata_names) != 1:
            raise StageError(f"Wheel must contain exactly one .dist-info/METADATA: {wheel.name}")
        metadata = Parser().parsestr(archive.read(metadata_names[0]).decode("utf-8", errors="replace"))
    name = metadata.get("Name")
    version = metadata.get("Version")
    if not name or not version:
        raise StageError(f"Wheel metadata is missing Name or Version: {wheel.name}")
    return name, version


def parse_direct_requirements(lock_file: Path) -> dict[str, str]:
    """Read exact direct pins from the release lock file for sanity checking."""
    direct: dict[str, str] = {}
    for raw_line in lock_file.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or line.startswith("-") or line.startswith("--"):
            continue
        match = re.match(r"^([A-Za-z0-9_.-]+)==([^\s;]+)", line)
        if not match:
            raise StageError(f"requirements-lock.txt must use exact package pins; unsupported line: {raw_line}")
        direct[normalized_name(match.group(1))] = match.group(2)
    return direct


def populate_wheelhouse_from_network(destination: Path) -> None:
    if not LOCK_FILE.is_file():
        raise StageError(f"Dependency lock file not found: {LOCK_FILE}")
    destination.mkdir(parents=True, exist_ok=True)
    command = [
        sys.executable,
        "-m",
        "pip",
        "download",
        "--dest",
        str(destination),
        "--only-binary=:all:",
        "--implementation",
        "cp",
        "--python-version",
        "312",
        "--abi",
        "cp312",
        "--platform",
        "win_amd64",
        "-r",
        str(LOCK_FILE),
    ]
    print("Downloading pinned dependency wheels for win_amd64/cp312...")
    subprocess.run(command, check=True)


def collect_wheels(args: argparse.Namespace) -> tuple[list[Path], list[dict[str, str]]]:
    if args.wheelhouse is not None:
        wheelhouse = args.wheelhouse.expanduser().resolve()
    else:
        wheelhouse = BUILD_DIR / "cache" / "wheels-win_amd64-cp312"
        if not args.allow_dependency_download:
            raise StageError(
                "No wheelhouse was supplied. Pass --wheelhouse <dir> for an offline build, "
                "or explicitly opt in with --allow-dependency-download."
            )
        populate_wheelhouse_from_network(wheelhouse)

    if not wheelhouse.is_dir():
        raise StageError(f"Wheelhouse does not exist: {wheelhouse}")
    wheels = sorted(wheelhouse.glob("*.whl"), key=lambda item: item.name.lower())
    if not wheels:
        raise StageError(f"Wheelhouse contains no wheels: {wheelhouse}")

    package_versions: dict[str, str] = {}
    details: list[dict[str, str]] = []
    for wheel in wheels:
        name, version = read_wheel_metadata(wheel)
        key = normalized_name(name)
        if key in package_versions:
            raise StageError(
                f"Wheelhouse contains multiple versions of {name}: {package_versions[key]} and {version}. "
                "Use a freshly resolved wheelhouse."
            )
        package_versions[key] = version
        details.append({"name": name, "version": version, "file": wheel.name, "sha256": sha256_file(wheel)})

    expected = parse_direct_requirements(LOCK_FILE)
    missing = sorted(name for name in expected if name not in package_versions)
    mismatched = sorted(
        f"{name} expected {version}, got {package_versions[name]}"
        for name, version in expected.items()
        if name in package_versions and package_versions[name] != version
    )
    if missing or mismatched:
        details_text = []
        if missing:
            details_text.append("missing: " + ", ".join(missing))
        if mismatched:
            details_text.append("version mismatch: " + "; ".join(mismatched))
        raise StageError("Wheelhouse does not match requirements-lock.txt (" + "; ".join(details_text) + ")")
    return wheels, details


def wheel_destination(member: PurePosixPath, site_packages: Path, scripts_dir: Path, include_dir: Path) -> Path | None:
    """Map a wheel member to the portable Python layout."""
    parts = member.parts
    if len(parts) >= 3 and parts[0].endswith(".data"):
        data_kind = parts[1]
        tail = parts[2:]
        if data_kind in {"purelib", "platlib"}:
            return site_packages.joinpath(*tail)
        if data_kind == "scripts":
            return scripts_dir.joinpath(*tail)
        if data_kind == "headers":
            return include_dir.joinpath(*tail)
        if data_kind == "data":
            return site_packages.joinpath(*tail)
        raise StageError(f"Unsupported wheel .data target: {member}")
    return site_packages.joinpath(*parts)


def install_wheels(wheels: Iterable[Path], python_dir: Path) -> None:
    """Install wheels by extracting them; runtime intentionally contains no pip."""
    site_packages = python_dir / "Lib" / "site-packages"
    scripts_dir = python_dir / "Scripts"
    include_dir = python_dir / "Include"
    for directory in (site_packages, scripts_dir, include_dir):
        directory.mkdir(parents=True, exist_ok=True)

    for wheel in wheels:
        print(f"  Installing wheel: {wheel.name}")
        with zipfile.ZipFile(wheel) as archive:
            for entry in archive.infolist():
                member = PurePosixPath(entry.filename)
                if member.is_absolute() or ".." in member.parts:
                    raise StageError(f"Unsafe wheel member in {wheel.name}: {entry.filename}")
                target = wheel_destination(member, site_packages, scripts_dir, include_dir)
                if target is None:
                    continue
                if entry.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(entry) as src, target.open("wb") as dst:
                    shutil.copyfileobj(src, dst)


def copy_application_sources(python_dir: Path) -> dict[str, str]:
    """Copy application modules rather than relying on the source checkout at runtime."""
    app_dir = python_dir / "app"
    for package_name in ("video_analysis_pipeline", "js_subtitle_converter"):
        source = REPOSITORY_ROOT / package_name
        if not source.is_dir():
            raise StageError(f"Application source package not found: {source}")
        target = app_dir / package_name
        shutil.copytree(source, target, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))

    config_source = REPOSITORY_ROOT / "pipeline_config.json"
    if not config_source.is_file():
        raise StageError(f"Default pipeline configuration not found: {config_source}")
    shutil.copy2(config_source, python_dir / "pipeline_config.json")
    shutil.copy2(config_source, app_dir / "pipeline_config.json")

    hashes: dict[str, str] = {}
    for file in sorted(app_dir.rglob("*")):
        if file.is_file():
            hashes[file.relative_to(python_dir).as_posix()] = sha256_file(file)
    hashes["pipeline_config.json"] = sha256_file(python_dir / "pipeline_config.json")
    return hashes


def find_ffmpeg_directory(requested: Path | None) -> Path:
    if requested is not None:
        candidate = requested.expanduser().resolve()
    else:
        ffmpeg = shutil.which("ffmpeg.exe") or shutil.which("ffmpeg")
        ffprobe = shutil.which("ffprobe.exe") or shutil.which("ffprobe")
        if not ffmpeg or not ffprobe:
            raise StageError("FFmpeg was not found. Pass --ffmpeg-dir containing ffmpeg.exe and ffprobe.exe.")
        ffmpeg_parent = Path(ffmpeg).resolve().parent
        ffprobe_parent = Path(ffprobe).resolve().parent
        if ffmpeg_parent != ffprobe_parent:
            raise StageError("ffmpeg and ffprobe came from different directories. Pass --ffmpeg-dir explicitly.")
        candidate = ffmpeg_parent
    for binary in ("ffmpeg.exe", "ffprobe.exe"):
        if not (candidate / binary).is_file():
            raise StageError(f"Required FFmpeg binary not found: {candidate / binary}")
    return candidate


def copy_ffmpeg(stage_dir: Path, source_dir: Path) -> dict[str, str]:
    destination = stage_dir / "ffmpeg"
    destination.mkdir(parents=True, exist_ok=True)
    copied: dict[str, str] = {}
    for source in source_dir.iterdir():
        if source.is_file() and source.suffix.lower() in {".exe", ".dll"}:
            target = destination / source.name
            shutil.copy2(source, target)
            copied[target.relative_to(stage_dir).as_posix()] = sha256_file(target)
    for binary in ("ffmpeg.exe", "ffprobe.exe"):
        if not (destination / binary).is_file():
            raise StageError(f"Failed to stage {binary}")
    validate_ffmpeg_runtime(destination)
    return copied


def validate_ffmpeg_runtime(directory: Path) -> None:
    """Reject copied package-manager shims before an engine is published.

    A Chocolatey/Scoop shim is a small executable that can work in its original
    package-manager directory but points to a missing relative target after it
    is copied into the portable engine.  Executing the staged copy is therefore
    the only meaningful validation.
    """
    for binary in ("ffmpeg.exe", "ffprobe.exe"):
        executable = directory / binary
        result = subprocess.run(
            [str(executable), "-version"],
            cwd=directory,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=10,
        )
        output = (result.stdout or result.stderr).strip()
        if result.returncode != 0:
            raise StageError(f"Staged {binary} is not runnable ({result.returncode}): {output[:1000]}")


def stage_models(args: argparse.Namespace, stage_dir: Path, python_exe: Path) -> dict[str, object]:
    models_dir = stage_dir / "models"
    if args.skip_models:
        return {"included": False, "reason": "--skip-models"}

    if args.models_dir is not None:
        source = args.models_dir.expanduser().resolve()
        manifest = source / MODEL_MANIFEST_NAME
        if not manifest.is_file():
            raise StageError(f"Supplied --models-dir has no {MODEL_MANIFEST_NAME}: {source}")
        shutil.copytree(source, models_dir)
    elif args.allow_model_download:
        download_script = BUILD_DIR / "download_models.py"
        print("Downloading ML models into the staged engine...")
        subprocess.run([str(python_exe), str(download_script), str(models_dir)], check=True)
    else:
        raise StageError(
            "No models source was supplied. Pass --models-dir <dir>, --skip-models for a development-only build, "
            "or explicitly opt in with --allow-model-download."
        )

    manifest_path = models_dir / MODEL_MANIFEST_NAME
    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise StageError(f"Invalid model manifest: {manifest_path}: {exc}") from exc
    if not payload.get("models"):
        raise StageError(f"Model manifest contains no models: {manifest_path}")

    for model in payload["models"]:
        relative_path = model.get("path")
        expected_hash = model.get("sha256")
        if not relative_path or not expected_hash:
            raise StageError(f"Model manifest has an incomplete entry: {model}")
        model_path = resolve_relative_path(models_dir, str(relative_path))
        if not model_path.exists():
            raise StageError(f"Model listed in manifest is missing: {model_path}")
        actual_hash = hash_model_manifest_path(model_path)
        if actual_hash != expected_hash:
            raise StageError(f"Model SHA-256 mismatch for {model.get('name', relative_path)}")

    return {
        "included": True,
        "manifest": MODEL_MANIFEST_NAME,
        "manifest_sha256": sha256_file(manifest_path),
        "models": payload["models"],
    }


def hash_path(path: Path) -> str:
    """Hash a file or a directory deterministically, including relative names."""
    if path.is_file():
        return sha256_file(path)
    if not path.is_dir():
        raise StageError(f"Cannot hash missing path: {path}")
    hasher = hashlib.sha256()
    for item in sorted((candidate for candidate in path.rglob("*") if candidate.is_file()), key=lambda value: value.as_posix().lower()):
        hasher.update(item.relative_to(path).as_posix().encode("utf-8"))
        hasher.update(b"\0")
        with item.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                hasher.update(chunk)
    return hasher.hexdigest()


def resolve_relative_path(root: Path, value: str) -> Path:
    """Resolve a Windows/POSIX manifest path without allowing traversal."""
    member = PurePosixPath(value.replace("\\", "/"))
    if member.is_absolute() or ".." in member.parts:
        raise StageError(f"Unsafe manifest path: {value}")
    return root.joinpath(*member.parts)


def hash_model_manifest_path(path: Path) -> str:
    """Match the hash convention used by download_models.py.

    The model manifest predates the engine manifest and hashes a directory as
    the concatenation of its sorted file contents (without path names).  Keep
    this compatibility check separate from ``hash_path``, which deliberately
    includes relative paths for source-tree integrity.
    """
    if path.is_file():
        return sha256_file(path)
    if not path.is_dir():
        raise StageError(f"Cannot hash missing model path: {path}")
    hasher = hashlib.sha256()
    for item in sorted((candidate for candidate in path.rglob("*") if candidate.is_file())):
        with item.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                hasher.update(chunk)
    return hasher.hexdigest()


def generate_licenses(stage_dir: Path) -> None:
    licenses_dir = stage_dir / "licenses"
    licenses_dir.mkdir(parents=True, exist_ok=True)
    lines = ["VideoAnalysisDesktop - Third-Party Notices", "=" * 50, "", "This product includes:", ""]
    for name, info in THIRD_PARTY_LICENSES.items():
        lines.extend((name, f"  License: {info['license']}", f"  Reference: {info['url']}", ""))
    (licenses_dir / "THIRD_PARTY_NOTICES.txt").write_text("\n".join(lines), encoding="utf-8")
    (licenses_dir / "licenses.json").write_text(
        json.dumps({"schema_version": "1.0", "packages": [{"name": name, **info} for name, info in THIRD_PARTY_LICENSES.items()]}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def run_command(command: list[str], *, description: str) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(command, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        details = (result.stderr or result.stdout).strip()
        raise StageError(f"{description} failed ({result.returncode}): {details[:2000]}")
    return result


def self_check(stage_dir: Path, *, require_models: bool) -> dict[str, str]:
    """Exercise the staged runtime before it is made visible as the output."""
    for relative in REQUIRED_ENGINE_FILES[:-1]:
        if not (stage_dir / relative).is_file():
            raise StageError(f"Staged engine is missing required file: {relative}")
    if require_models and not (stage_dir / "models" / MODEL_MANIFEST_NAME).is_file():
        raise StageError("Staged production engine is missing models.manifest.json")

    python_exe = stage_dir / "python" / "python.exe"
    versions: dict[str, str] = {}
    python = run_command([str(python_exe), "--version"], description="Embedded Python version check")
    versions["python"] = (python.stdout or python.stderr).strip()
    import_check = "; ".join(f"import {module}" for module in REQUIRED_IMPORTS.values())
    run_command([str(python_exe), "-c", import_check], description="Embedded Python import check")
    run_command(
        [str(python_exe), "-m", "video_analysis_pipeline.desktop_entry", "--help"],
        description="Desktop entry point check",
    )
    for binary in ("ffmpeg.exe", "ffprobe.exe"):
        result = run_command([str(stage_dir / "ffmpeg" / binary), "-version"], description=f"{binary} check")
        versions[binary] = (result.stdout.splitlines() or ["ok"])[0]
    return versions


def critical_hashes(stage_dir: Path, app_hashes: dict[str, str], ffmpeg_hashes: dict[str, str]) -> dict[str, str]:
    hashes = {
        "python/python.exe": sha256_file(stage_dir / "python" / "python.exe"),
        "python/python312.dll": sha256_file(stage_dir / "python" / "python312.dll"),
        "python/python312._pth": sha256_file(stage_dir / "python" / "python312._pth"),
        "requirements-lock.txt": sha256_file(stage_dir / "requirements-lock.txt"),
        **app_hashes,
        **ffmpeg_hashes,
    }
    return dict(sorted(hashes.items()))


def write_engine_manifest(
    stage_dir: Path,
    *,
    python_archive_hash: str,
    python_provenance: str,
    wheels: list[dict[str, str]],
    app_hashes: dict[str, str],
    ffmpeg_hashes: dict[str, str],
    model_info: dict[str, object],
    versions: dict[str, str],
) -> None:
    manifest = {
        "schema_version": "1.0",
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "platform": "win-x64",
        "python": {
            "version": PYTHON_VERSION,
            "archive_name": PYTHON_EMBED_FILENAME,
            "archive_sha256": python_archive_hash,
            "archive_source": python_provenance,
        },
        "project": {
            "packages": ["video_analysis_pipeline", "js_subtitle_converter"],
            "source_tree_sha256": hash_path(stage_dir / "python" / "app"),
        },
        "dependencies": {
            "lock_file": "requirements-lock.txt",
            "lock_sha256": sha256_file(stage_dir / "requirements-lock.txt"),
            "wheels": wheels,
        },
        "models": model_info,
        "runtime_versions": versions,
        "critical_files": critical_hashes(stage_dir, app_hashes, ffmpeg_hashes),
    }
    (stage_dir / ENGINE_MANIFEST_NAME).write_text(
        json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True), encoding="utf-8"
    )


def ensure_safe_output_target(output_dir: Path) -> Path:
    resolved = output_dir.expanduser().resolve()
    protected = {REPOSITORY_ROOT.resolve(), DESKTOP_DIR.resolve(), BUILD_DIR.resolve(), Path(resolved.anchor)}
    if resolved in protected or resolved.parent == resolved:
        raise StageError(f"Refusing to stage into unsafe output directory: {resolved}")
    return resolved


def publish_stage(stage_dir: Path, output_dir: Path, *, clean_output: bool) -> None:
    if output_dir.exists():
        if any(output_dir.iterdir()):
            if not clean_output:
                raise StageError(
                    f"Output directory already exists and is not empty: {output_dir}. "
                    "Review it, then rerun with --clean-output to replace it."
                )
            shutil.rmtree(output_dir)
        else:
            output_dir.rmdir()
    stage_dir.replace(output_dir)


def main() -> int:
    args = parse_args()
    if args.skip_models and (args.models_dir is not None or args.allow_model_download):
        raise StageError("--skip-models cannot be combined with --models-dir or --allow-model-download")
    if not args.skip_models and args.models_dir is not None and args.allow_model_download:
        raise StageError("Use either --models-dir or --allow-model-download, not both")

    output_dir = ensure_safe_output_target(args.output_dir)
    if output_dir.exists() and any(output_dir.iterdir()) and not args.clean_output:
        print(
            f"ERROR: Output directory already exists and is not empty: {output_dir}. "
            "Review it, then rerun with --clean-output to replace it.",
            file=sys.stderr,
        )
        return 1
    output_dir.parent.mkdir(parents=True, exist_ok=True)
    stage_dir = output_dir.parent / f".{output_dir.name}.staging-{uuid.uuid4().hex}"
    print(f"Staging self-contained engine in: {stage_dir}")

    try:
        archive, python_archive_hash, python_provenance = acquire_embedded_python(args)
        print(f"Using verified CPython archive: {archive}")
        safe_extract_zip(archive, stage_dir / "python")
        python_exe = configure_embedded_python(stage_dir / "python")

        print("Collecting dependency wheels...")
        wheels, wheel_details = collect_wheels(args)
        print("Installing dependency wheels into the embedded runtime...")
        install_wheels(wheels, stage_dir / "python")

        print("Copying application sources...")
        app_hashes = copy_application_sources(stage_dir / "python")
        shutil.copy2(LOCK_FILE, stage_dir / "requirements-lock.txt")

        print("Copying FFmpeg runtime...")
        ffmpeg_hashes = copy_ffmpeg(stage_dir, find_ffmpeg_directory(args.ffmpeg_dir))

        print("Staging ML models...")
        model_info = stage_models(args, stage_dir, python_exe)

        generate_licenses(stage_dir)
        shutil.copy2(BUILD_DIR / "engine_check.py", stage_dir / "engine_check.py")

        versions: dict[str, str] = {}
        if not args.skip_validation:
            print("Running engine self-check...")
            versions = self_check(stage_dir, require_models=not args.skip_models)
        else:
            print("WARNING: post-stage validation was skipped")

        write_engine_manifest(
            stage_dir,
            python_archive_hash=python_archive_hash,
            python_provenance=python_provenance,
            wheels=wheel_details,
            app_hashes=app_hashes,
            ffmpeg_hashes=ffmpeg_hashes,
            model_info=model_info,
            versions=versions,
        )

        # Validate the final manifest and all critical files before publishing it.
        if not args.skip_validation:
            run_command(
                [str(python_exe), str(stage_dir / "engine_check.py"), "--engine-dir", str(stage_dir), "--verify-hashes"],
                description="Engine manifest self-check",
            )

        publish_stage(stage_dir, output_dir, clean_output=args.clean_output)
        print(f"Engine staged successfully at: {output_dir}")
        return 0
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        if stage_dir.exists() and not args.keep_failed_stage:
            shutil.rmtree(stage_dir, ignore_errors=True)
        elif stage_dir.exists():
            print(f"Failed staging directory retained for diagnosis: {stage_dir}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
