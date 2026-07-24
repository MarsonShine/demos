"""Batch preflight for the desktop host.

The production batch command intentionally stops on the first malformed
directory.  Preflight applies the same per-directory rules, but scans every
candidate so the desktop can show the operator all structural problems in one
pass.  Its result is atomically replaced only after the complete JSON payload
has been serialized.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

from video_analysis_pipeline.exporter import write_json
from video_analysis_pipeline.pipeline import BatchInputItem
from video_analysis_pipeline.timecode import natural_sort_key


def run_preflight(input_root: Path, result_file: Path) -> dict[str, Any]:
    """
    Scan *input_root* and write a structured JSON result to *result_file*.

    Returns the same dict written to disk for programmatic use.
    """
    errors: list[dict[str, Any]] = []
    items: list[dict[str, Any]] = []

    if not input_root.exists():
        errors.append(
            {
                "type": "input_root_missing",
                "message": f"Input root does not exist: {input_root}",
            }
        )
        result = _build_result(0, items, errors, passed=False)
        _write_result(result_file, result)
        return result

    discovered, scan_errors = _scan_batch_inputs(input_root)
    errors.extend(scan_errors)

    # Build the materialized snapshot after structure validation.  Files can
    # still disappear or become unreadable while this scan is running; record
    # that as a per-item error rather than abandoning the whole preflight.
    for idx, item in enumerate(discovered, start=1):
        try:
            items.append(_build_item_snapshot(idx, item))
        except OSError as exc:
            errors.append(
                {
                    "type": "input_file_unreadable",
                    "message": f"Cannot read input files in {_relative_dir(item.relative_dir)}: {exc}",
                    "item_index": idx,
                    "relative_dir": _relative_dir(item.relative_dir),
                }
            )

    total_input_size = sum(
        item["mp4_size_bytes"] + item["srt_size_bytes"] + (item["mp3_size_bytes"] or 0)
        for item in items
    )

    passed = len(errors) == 0
    result = _build_result(len(items), items, errors, passed=passed, total_input_size=total_input_size)
    _write_result(result_file, result)
    return result


def _build_result(
    total_items: int,
    items: list[dict[str, Any]],
    errors: list[dict[str, Any]],
    passed: bool,
    total_input_size: int = 0,
) -> dict[str, Any]:
    snapshot_sha256 = _calculate_snapshot_sha256(items) if passed else None
    return {
        "schema_version": "1.0",
        "passed": passed,
        "total_items": total_items,
        "total_input_size_bytes": total_input_size,
        "items": items,
        "errors": errors,
        # This is deliberately based on canonical item fingerprints rather
        # than the JSON file bytes, absolute root path, or dictionary order.
        "snapshot_sha256": snapshot_sha256,
    }


def _write_result(result_file: Path, result: dict[str, Any]) -> None:
    """Atomically replace the preflight result after complete serialization."""
    write_json(result_file, result)


def _scan_batch_inputs(input_root: Path) -> tuple[list[BatchInputItem], list[dict[str, Any]]]:
    """Return all valid inputs and all structural errors under *input_root*.

    This mirrors :func:`discover_batch_inputs`'s candidate and count rules,
    except that it records malformed directories and continues scanning.
    """
    errors: list[dict[str, Any]] = []
    try:
        child_directories = sorted(
            (path for path in input_root.rglob("*") if path.is_dir()),
            key=lambda path: natural_sort_key(_relative_dir(path.relative_to(input_root))),
        )
    except OSError as exc:
        return [], [
            {
                "type": "input_root_unreadable",
                "message": f"Cannot scan input root {input_root}: {exc}",
            }
        ]

    discovered: list[BatchInputItem] = []
    for directory in [input_root, *child_directories]:
        relative_dir = directory.relative_to(input_root)
        relative_dir_text = _relative_dir(relative_dir)
        try:
            mp4_candidates = _sorted_media_files(directory, "*.mp4")
            srt_candidates = _sorted_media_files(directory, "*.srt")
            mp3_candidates = _sorted_media_files(directory, "*.mp3")
        except OSError as exc:
            errors.append(
                {
                    "type": "directory_unreadable",
                    "message": f"Cannot inspect {relative_dir_text}: {exc}",
                    "relative_dir": relative_dir_text,
                }
            )
            continue

        # Match production discovery: a directory containing only MP3 files
        # is not a candidate batch item.
        if not mp4_candidates and not srt_candidates:
            continue

        if len(mp4_candidates) != 1 or len(srt_candidates) != 1 or len(mp3_candidates) > 1:
            errors.append(
                {
                    "type": "invalid_directory_structure",
                    "message": (
                        "Expected exactly one MP4, one SRT, and at most one MP3 in "
                        f"{relative_dir_text}; found {len(mp4_candidates)} MP4, "
                        f"{len(srt_candidates)} SRT, and {len(mp3_candidates)} MP3."
                    ),
                    "relative_dir": relative_dir_text,
                    "mp4_count": len(mp4_candidates),
                    "srt_count": len(srt_candidates),
                    "mp3_count": len(mp3_candidates),
                }
            )
            continue

        discovered.append(
            BatchInputItem(
                source_mp4=mp4_candidates[0],
                source_srt=srt_candidates[0],
                relative_dir=relative_dir,
                source_mp3=mp3_candidates[0] if mp3_candidates else None,
            )
        )

    if not discovered and not errors:
        errors.append(
            {
                "type": "no_candidates_found",
                "message": f"No folders with exactly one MP4 and one SRT were found under {input_root}.",
            }
        )
    return discovered, errors


def _sorted_media_files(directory: Path, pattern: str) -> list[Path]:
    return sorted(
        (path for path in directory.glob(pattern) if path.is_file()),
        key=lambda path: natural_sort_key(path.name),
    )


def _build_item_snapshot(index: int, item: BatchInputItem) -> dict[str, Any]:
    mp4_stat = item.source_mp4.stat()
    srt_stat = item.source_srt.stat()
    mp3_stat = item.source_mp3.stat() if item.source_mp3 else None

    mp4_head_hash, mp4_tail_hash = _hash_file_head_tail(item.source_mp4)
    srt_hash = _hash_file_full(item.source_srt)
    mp3_head_hash = None
    mp3_tail_hash = None
    if item.source_mp3:
        mp3_head_hash, mp3_tail_hash = _hash_file_head_tail(item.source_mp3)

    return {
        "index": index,
        "relative_dir": _relative_dir(item.relative_dir),
        "source_mp4": str(item.source_mp4),
        "source_srt": str(item.source_srt),
        "source_mp3": str(item.source_mp3) if item.source_mp3 else None,
        "mp4_size_bytes": mp4_stat.st_size,
        "mp4_modified_utc": _format_mtime(mp4_stat.st_mtime),
        "mp4_head_sha256": mp4_head_hash,
        "mp4_tail_sha256": mp4_tail_hash,
        "srt_size_bytes": srt_stat.st_size,
        "srt_modified_utc": _format_mtime(srt_stat.st_mtime),
        "srt_sha256": srt_hash,
        "mp3_size_bytes": mp3_stat.st_size if mp3_stat else None,
        "mp3_modified_utc": _format_mtime(mp3_stat.st_mtime) if mp3_stat else None,
        "mp3_head_sha256": mp3_head_hash,
        "mp3_tail_sha256": mp3_tail_hash,
    }


def _calculate_snapshot_sha256(items: list[dict[str, Any]]) -> str:
    """Calculate a deterministic input snapshot independent of absolute paths."""
    canonical_items = [
        {
            "relative_dir": str(item["relative_dir"]).replace("\\", "/"),
            "mp4": {
                "name": Path(str(item["source_mp4"])).name,
                "size_bytes": item["mp4_size_bytes"],
                "modified_utc": item["mp4_modified_utc"],
                "head_sha256": item["mp4_head_sha256"],
                "tail_sha256": item["mp4_tail_sha256"],
            },
            "srt": {
                "name": Path(str(item["source_srt"])).name,
                "size_bytes": item["srt_size_bytes"],
                "modified_utc": item["srt_modified_utc"],
                "sha256": item["srt_sha256"],
            },
            "mp3": (
                {
                    "name": Path(str(item["source_mp3"])).name,
                    "size_bytes": item["mp3_size_bytes"],
                    "modified_utc": item["mp3_modified_utc"],
                    "head_sha256": item["mp3_head_sha256"],
                    "tail_sha256": item["mp3_tail_sha256"],
                }
                if item["source_mp3"] is not None
                else None
            ),
        }
        for item in sorted(items, key=lambda item: natural_sort_key(str(item["relative_dir"]).replace("\\", "/")))
    ]
    canonical_json = json.dumps(canonical_items, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical_json.encode("utf-8")).hexdigest()


def _relative_dir(relative_dir: Path) -> str:
    """Normalize a relative directory for stable JSON across Windows/Linux."""
    return relative_dir.as_posix() or "."


def _hash_file_head_tail(path: Path, chunk_size: int = 1 * 1024 * 1024) -> tuple[str, str]:
    """Return SHA-256 hex digests of the first and last *chunk_size* bytes of a file."""
    file_size = path.stat().st_size
    head_hasher = hashlib.sha256()
    tail_hasher = hashlib.sha256()

    with path.open("rb") as f:
        # Head
        head_data = f.read(chunk_size)
        head_hasher.update(head_data)

        # Tail
        if file_size > chunk_size:
            tail_offset = max(chunk_size, file_size - chunk_size)
            f.seek(tail_offset)
            tail_data = f.read(chunk_size)
        else:
            tail_data = head_data
        tail_hasher.update(tail_data)

    return head_hasher.hexdigest(), tail_hasher.hexdigest()


def _hash_file_full(path: Path) -> str:
    """Return SHA-256 hex digest of the entire file."""
    hasher = hashlib.sha256()
    with path.open("rb") as f:
        while True:
            chunk = f.read(1 * 1024 * 1024)  # 1 MiB chunks
            if not chunk:
                break
            hasher.update(chunk)
    return hasher.hexdigest()


def _format_mtime(timestamp: float) -> str:
    """Format a stat st_mtime as an ISO 8601 UTC string."""
    from datetime import datetime, timezone

    return datetime.fromtimestamp(timestamp, tz=timezone.utc).isoformat()
