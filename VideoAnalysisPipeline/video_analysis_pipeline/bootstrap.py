from __future__ import annotations

import hashlib
import os
import subprocess
import sys
from pathlib import Path
from typing import Callable


BOOTSTRAP_ENV_VAR = "VIDEO_ANALYSIS_PIPELINE_BOOTSTRAPPED"
RUNTIME_VENV_DIRNAME = ".video_pipeline_env"
REQUIREMENTS_STAMP = ".requirements.sha256"


def bootstrap_and_run(entrypoint: Callable[[], int]) -> int:
    runtime_python = get_runtime_python()
    current_python = Path(sys.executable).resolve()
    should_bootstrap = os.environ.get(BOOTSTRAP_ENV_VAR) != "1" and current_python != runtime_python

    if should_bootstrap:
        ensure_runtime_environment(runtime_python)
        return reexec_in_runtime(runtime_python)

    return entrypoint()


def get_repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def get_runtime_dir() -> Path:
    return get_repo_root() / RUNTIME_VENV_DIRNAME


def get_runtime_python() -> Path:
    runtime_dir = get_runtime_dir()
    if os.name == "nt":
        return runtime_dir / "Scripts" / "python.exe"
    return runtime_dir / "bin" / "python"


def get_requirements_path() -> Path:
    return get_repo_root() / "requirements.txt"


def get_stamp_path() -> Path:
    return get_runtime_dir() / REQUIREMENTS_STAMP


def ensure_runtime_environment(runtime_python: Path) -> None:
    runtime_dir = get_runtime_dir()
    requirements_path = get_requirements_path()

    if not runtime_python.exists():
        subprocess.run(
            [sys.executable, "-m", "venv", str(runtime_dir)],
            check=True,
        )

    requirements_hash = hashlib.sha256(requirements_path.read_bytes()).hexdigest()
    stamp_path = get_stamp_path()
    current_hash = stamp_path.read_text(encoding="utf-8").strip() if stamp_path.exists() else ""

    if current_hash == requirements_hash:
        return

    subprocess.run(
        [str(runtime_python), "-m", "pip", "install", "--upgrade", "pip"],
        check=True,
    )
    subprocess.run(
        [str(runtime_python), "-m", "pip", "install", "-r", str(requirements_path)],
        check=True,
    )
    stamp_path.write_text(requirements_hash, encoding="utf-8")


def reexec_in_runtime(runtime_python: Path) -> int:
    environment = os.environ.copy()
    environment[BOOTSTRAP_ENV_VAR] = "1"

    command = [str(runtime_python), str(get_repo_root() / "run_pipeline.py"), *sys.argv[1:]]
    completed = subprocess.run(command, env=environment, check=False)
    return int(completed.returncode)
