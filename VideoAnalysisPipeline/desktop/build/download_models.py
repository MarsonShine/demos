"""
Download and stage ML models for offline deployment.

Downloads:
- faster-whisper base.en model
- demucs htdemucs model

Models are saved to the specified output directory and a models.manifest.json
is generated recording source, version, SHA-256, and license information.
"""

from __future__ import annotations

import hashlib
import json
import os
import sys
from pathlib import Path

# Model specifications
MODELS = [
    {
        "name": "faster-whisper-base.en",
        "source": "Systran/faster-whisper-base.en",
        "source_type": "huggingface",
        "license": "MIT",
        "version": "1.0",
        "download_root_env": "FASTER_WHISPER_DOWNLOAD_ROOT",
    },
    {
        "name": "demucs-htdemucs",
        "source": "facebook/demucs",
        "source_type": "torchhub",
        "license": "MIT",
        "version": "v4",
        "download_root_env": "TORCH_HOME",
    },
]


def download_whisper_model(output_dir: Path) -> dict:
    """Download faster-whisper base.en model by running a minimal transcribe."""
    from faster_whisper import download_model

    model_dir = output_dir / "faster-whisper" / "models--Systran--faster-whisper-base.en"
    if model_dir.exists() and any(model_dir.rglob("*.bin")):
        print(f"  Whisper model already exists at {model_dir}")
    else:
        print("  Downloading faster-whisper base.en model...")
        download_model("base.en", cache_dir=str(output_dir / "faster-whisper"))
        print("  Done.")

    # Compute hash of the model files
    hasher = hashlib.sha256()
    for f in sorted(model_dir.rglob("*")):
        if f.is_file():
            hasher.update(f.read_bytes())
    return {
        "name": "faster-whisper-base.en",
        "source": "Systran/faster-whisper-base.en",
        "version": "1.0",
        "license": "MIT",
        "sha256": hasher.hexdigest(),
        "path": str(model_dir.relative_to(output_dir)),
    }


def download_demucs_model(output_dir: Path) -> dict:
    """Download demucs htdemucs model by loading it once."""
    model_dir = output_dir / "torch" / "hub" / "checkpoints"
    expected_file = model_dir / "htdemucs-6a5f5f05.th"
    if expected_file.exists():
        print(f"  Demucs model already exists at {expected_file}")
    else:
        print("  Downloading demucs htdemucs model...")
        from demucs import pretrained
        pretrained.get_model("htdemucs")
        print("  Done.")

    # Hash the model file
    hasher = hashlib.sha256()
    if expected_file.exists():
        hasher.update(expected_file.read_bytes())

    return {
        "name": "demucs-htdemucs",
        "source": "facebook/demucs",
        "version": "v4",
        "license": "MIT",
        "sha256": hasher.hexdigest() if expected_file.exists() else "",
        "path": str(expected_file.relative_to(output_dir)) if expected_file.exists() else "",
    }


def main() -> int:
    output_dir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("desktop/engine/models")
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Staging models to: {output_dir}")

    # Set environment variables for model download
    os.environ["FASTER_WHISPER_DOWNLOAD_ROOT"] = str(output_dir / "faster-whisper")
    os.environ["TORCH_HOME"] = str(output_dir / "torch")

    manifest_entries = []

    print("\n[1/2] faster-whisper base.en")
    manifest_entries.append(download_whisper_model(output_dir))

    print("\n[2/2] demucs htdemucs")
    manifest_entries.append(download_demucs_model(output_dir))

    # Write manifest
    manifest_path = output_dir / "models.manifest.json"
    manifest = {
        "schema_version": "1.0",
        "created_utc": __import__("datetime").datetime.now(tz=__import__("datetime").timezone.utc).isoformat(),
        "models": manifest_entries,
    }
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"\nManifest written to: {manifest_path}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
