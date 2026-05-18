from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from time import perf_counter
from typing import Any, Callable

from video_analysis_pipeline.exporter import write_json


ProgressEventCallback = Callable[[str, str, dict[str, Any]], None]


@dataclass(slots=True)
class StageProgressTracker:
    progress_path: Path
    label: str
    callback: ProgressEventCallback | None = None
    timings: dict[str, float] = field(default_factory=dict)

    def run(self, stage: str, action: Callable[[], Any], details: dict[str, Any] | None = None) -> Any:
        payload_details = details or {}
        self._emit(stage=stage, status="running", details=payload_details)
        start = perf_counter()
        try:
            result = action()
        except Exception as exc:
            elapsed_seconds = perf_counter() - start
            self.timings[stage] = round(elapsed_seconds, 3)
            self._emit(stage=stage, status="failed", details={**payload_details, "error": str(exc)})
            raise

        elapsed_seconds = perf_counter() - start
        self.timings[stage] = round(elapsed_seconds, 3)
        self._emit(stage=stage, status="completed", details=payload_details)
        return result

    def finish(self, details: dict[str, Any] | None = None) -> None:
        self._emit(stage=None, status="completed", details=details or {})

    def _emit(self, stage: str | None, status: str, details: dict[str, Any]) -> None:
        payload = {
            "label": self.label,
            "status": status,
            "current_stage": stage,
            "timings_seconds": self.timings,
            "details": details,
        }
        self.progress_path.parent.mkdir(parents=True, exist_ok=True)
        write_json(self.progress_path, payload)
        if stage:
            print(f"[{self.label}] {stage}: {status}")
        if self.callback is not None:
            self.callback(stage or "", status, payload)
