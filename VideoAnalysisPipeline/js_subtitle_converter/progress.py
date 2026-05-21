from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from time import perf_counter
from typing import Any, Callable


ProgressEventCallback = Callable[[str, str, dict[str, Any]], None]


def write_json(path: Path, payload: object) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


@dataclass(slots=True)
class StageProgressTracker:
    progress_path: Path
    label: str
    callback: ProgressEventCallback | None = None
    timings: dict[str, float] = field(default_factory=dict)

    def run(self, stage: str, action: Callable[[], Any], details: dict[str, Any] | None = None) -> Any:
        payload_details = details or {}
        self._emit(stage=stage, status="running", details=payload_details)
        started_at = perf_counter()
        try:
            result = action()
        except Exception as exc:
            self.timings[stage] = round(perf_counter() - started_at, 3)
            self._emit(stage=stage, status="failed", details={**payload_details, "error": str(exc)})
            raise

        self.timings[stage] = round(perf_counter() - started_at, 3)
        self._emit(stage=stage, status="completed", details=payload_details)
        return result

    def finish(self, details: dict[str, Any] | None = None) -> None:
        self._emit(stage=None, status="completed", details=details or {})

    def _emit(self, stage: str | None, status: str, details: dict[str, Any]) -> None:
        payload = {
            "label": self.label,
            "status": status,
            "current_stage": stage,
            "timings_seconds": dict(self.timings),
            "details": details,
        }
        write_json(self.progress_path, payload)
        if stage:
            print(f"[{self.label}] {stage}: {status}")
        if self.callback is not None:
            self.callback(stage or "", status, payload)