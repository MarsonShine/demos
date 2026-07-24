"""
Structured JSONL event stream for desktop integration.

Writes one JSON object per line to an event file. Each line is flushed
immediately so the C# host can tail the file reliably. The C# consumer
reads complete lines; partial lines are retried on the next poll cycle.

Event types (fixed contract):
- run_started
- item_started
- stage_changed
- item_completed
- run_failed
- run_completed
"""

from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

SCHEMA_VERSION = "1.0"


class EventWriter:
    """Append-only JSONL event stream with per-line flush."""

    def __init__(self, event_file: Path) -> None:
        self._event_file = event_file
        self._handle = None

    def open(self) -> None:
        self._event_file.parent.mkdir(parents=True, exist_ok=True)
        self._handle = self._event_file.open("a", encoding="utf-8")

    def close(self) -> None:
        if self._handle is not None:
            self._handle.flush()
            os.fsync(self._handle.fileno())
            self._handle.close()
            self._handle = None

    def write_event(self, event: dict[str, Any]) -> None:
        if self._handle is None:
            self.open()
        line = json.dumps(event, ensure_ascii=False, separators=(",", ":"))
        self._handle.write(line + "\n")
        self._handle.flush()

    # ------------------------------------------------------------------
    # Fixed event constructors
    # ------------------------------------------------------------------

    def run_started(
        self,
        run_id: str,
        total_items: int,
        input_root: str,
        output_root: str,
        preset_info: dict[str, Any] | None = None,
    ) -> None:
        self.write_event(
            {
                "schema_version": SCHEMA_VERSION,
                "run_id": run_id,
                "timestamp_utc": _utc_now_iso(),
                "event": "run_started",
                "total_items": total_items,
                "completed_items": 0,
                "input_root": input_root,
                "output_root": output_root,
                "preset": preset_info or {},
            }
        )

    def item_started(
        self,
        run_id: str,
        total_items: int,
        completed_items: int,
        item_index: int,
        relative_dir: str,
    ) -> None:
        self.write_event(
            {
                "schema_version": SCHEMA_VERSION,
                "run_id": run_id,
                "timestamp_utc": _utc_now_iso(),
                "event": "item_started",
                "total_items": total_items,
                "completed_items": completed_items,
                "item_index": item_index,
                "relative_dir": relative_dir,
                "stage": None,
                "status": "running",
            }
        )

    def stage_changed(
        self,
        run_id: str,
        total_items: int,
        completed_items: int,
        item_index: int,
        relative_dir: str,
        stage: str,
        status: str,
        details: dict[str, Any] | None = None,
    ) -> None:
        self.write_event(
            {
                "schema_version": SCHEMA_VERSION,
                "run_id": run_id,
                "timestamp_utc": _utc_now_iso(),
                "event": "stage_changed",
                "total_items": total_items,
                "completed_items": completed_items,
                "item_index": item_index,
                "relative_dir": relative_dir,
                "stage": stage,
                "status": status,
                "details": details or {},
            }
        )

    def item_completed(
        self,
        run_id: str,
        total_items: int,
        completed_items: int,
        item_index: int,
        relative_dir: str,
    ) -> None:
        self.write_event(
            {
                "schema_version": SCHEMA_VERSION,
                "run_id": run_id,
                "timestamp_utc": _utc_now_iso(),
                "event": "item_completed",
                "total_items": total_items,
                "completed_items": completed_items,
                "item_index": item_index,
                "relative_dir": relative_dir,
                "status": "completed",
            }
        )

    def run_completed(
        self,
        run_id: str,
        total_items: int,
        completed_items: int,
    ) -> None:
        self.write_event(
            {
                "schema_version": SCHEMA_VERSION,
                "run_id": run_id,
                "timestamp_utc": _utc_now_iso(),
                "event": "run_completed",
                "total_items": total_items,
                "completed_items": completed_items,
                "status": "completed",
            }
        )

    def run_failed(
        self,
        run_id: str,
        total_items: int,
        completed_items: int,
        item_index: int | None,
        relative_dir: str | None,
        error_summary: str,
        failure_phase: str | None = None,
    ) -> None:
        """Write the terminal event for a failed desktop batch run.

        ``item_index`` and ``relative_dir`` identify the item that was active
        when the failure happened.  They are intentionally ``None`` for
        failures that happen before discovery or after all items have
        completed.  ``failure_phase`` lets the desktop distinguish those cases
        without having to infer them from an error message.
        """
        self.write_event(
            {
                "schema_version": SCHEMA_VERSION,
                "run_id": run_id,
                "timestamp_utc": _utc_now_iso(),
                "event": "run_failed",
                "total_items": total_items,
                "completed_items": completed_items,
                "item_index": item_index,
                "relative_dir": relative_dir,
                "status": "failed",
                "error_summary": _sanitize_error(error_summary),
                "failure_phase": failure_phase,
            }
        )


def _utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _sanitize_error(message: str) -> str:
    """Remove potential secrets from error messages before writing to event log."""
    # Redact anything that looks like an API key or token
    import re

    message = re.sub(r"(?i)(api[_-]?key|subscription[_-]?key|secret|token)=[^\s,;]+", r"\1=***REDACTED***", message)
    message = re.sub(r"(?i)sk-[a-zA-Z0-9]{16,}", "sk-***REDACTED***", message)
    return message[:2000]  # Truncate extremely long error messages
