from __future__ import annotations

import json
from functools import partial
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import unquote, urlparse

from video_analysis_pipeline.exporter import (
    export_csv,
    export_review_page,
    export_workbook,
    segments_to_rows,
    write_json,
)
from video_analysis_pipeline.models import OverviewRow, Segment
from video_analysis_pipeline.timecode import format_timestamp


DEFAULT_REVIEW_SERVER_HOST = "127.0.0.1"
DEFAULT_REVIEW_SERVER_PORT = 8765


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _write_time_range(entry: dict[str, Any], start_ms: int, end_ms: int) -> None:
    entry["start_ms"] = start_ms
    entry["end_ms"] = end_ms
    entry["start_timecode"] = format_timestamp(start_ms)
    entry["end_timecode"] = format_timestamp(end_ms)
    entry["duration_ms"] = max(0, end_ms - start_ms)


def _segment_from_json(payload: dict[str, Any]) -> Segment:
    return Segment.from_json(payload)


def _resolve_adjustment_index(adjustment: dict[str, Any], segments: list[dict[str, Any]]) -> int:
    if "segment_index" in adjustment:
        candidate = int(adjustment["segment_index"])
        if 0 <= candidate < len(segments):
            return candidate
    if "segment_no" in adjustment:
        segment_no = int(adjustment["segment_no"])
        for index, segment in enumerate(segments):
            if int(segment.get("segment_no", -1)) == segment_no:
                return index
    raise ValueError(f"Unable to resolve segment for adjustment: {adjustment!r}")


def _validate_segment_timings(segments: list[dict[str, Any]]) -> None:
    previous_end_ms: int | None = None
    for index, segment in enumerate(segments):
        start_ms = int(segment["start_ms"])
        end_ms = int(segment["end_ms"])
        if start_ms < 0:
            raise ValueError(f"Segment {index + 1} start_ms must be >= 0.")
        if end_ms <= start_ms:
            raise ValueError(f"Segment {index + 1} end_ms must be greater than start_ms.")
        if previous_end_ms is not None and start_ms < previous_end_ms:
            raise ValueError("Adjusted segments overlap; save was rejected.")
        previous_end_ms = end_ms


def _resolve_review_path(review_path: str, output_root: Path) -> Path:
    raw_path = unquote(str(review_path).strip())
    if not raw_path:
        raise ValueError("review_path is required.")
    if raw_path.startswith("file://"):
        raw_path = urlparse(raw_path).path

    candidate = Path(raw_path.lstrip("/"))
    if not candidate.is_absolute():
        candidate = output_root / raw_path.lstrip("/")

    resolved = candidate.resolve()
    output_root_resolved = output_root.resolve()
    if output_root_resolved != resolved and output_root_resolved not in resolved.parents:
        raise ValueError("Review page must be inside the configured output root.")
    return resolved


def _find_batch_root(output_dir: Path) -> Path | None:
    for candidate in [output_dir, *output_dir.parents]:
        if (candidate / "batch_summary.json").exists():
            return candidate
    return None


def _load_segments_from_output_dir(output_dir: Path) -> list[Segment]:
    segments_payload = _read_json(output_dir / "segments.json")
    return [_segment_from_json(item) for item in segments_payload.get("segments", [])]


def _overview_row_from_manifest(manifest: dict[str, Any]) -> OverviewRow | None:
    payload = manifest.get("overview")
    if not isinstance(payload, dict):
        return None
    return OverviewRow(
        education_stage=str(payload.get("education_stage", "")),
        subject=str(payload.get("subject", "")),
        sequence_no=int(payload.get("sequence_no", manifest.get("sequence_no", 1))),
        movie_name=str(payload.get("movie_name", "")),
        video_title=str(payload.get("video_title", "")),
        muted_video=str(payload.get("muted_video", "")),
        full_video=str(payload.get("full_video", "")),
        background_audio=str(payload.get("background_audio", "")),
        cover_image=str(payload.get("cover_image", "")),
        video_description=str(payload.get("video_description", "")),
        difficulty=str(payload.get("difficulty", "")),
        dialogue_audio=str(payload.get("dialogue_audio", "")),
        topic=str(payload.get("topic", "")),
        source=str(payload.get("source", "")),
    )


def apply_review_adjustments(review_page_path: Path, adjustments: list[dict[str, Any]]) -> dict[str, Any]:
    review_page_path = review_page_path.resolve()
    output_dir = review_page_path.parent
    manifest_path = output_dir / "manifest.json"
    segments_path = output_dir / "segments.json"
    subtitle_spans_path = output_dir / "subtitle_spans.json"

    manifest = _read_json(manifest_path)
    segments_payload = _read_json(segments_path)
    subtitle_payload = _read_json(subtitle_spans_path) if subtitle_spans_path.exists() else None

    segment_entries = segments_payload.get("segments")
    if not isinstance(segment_entries, list):
        raise ValueError("segments.json is missing the segments array.")

    subtitle_entries = None
    if subtitle_payload is not None:
        subtitle_entries = subtitle_payload.get("subtitle_spans")
        if not isinstance(subtitle_entries, list):
            raise ValueError("subtitle_spans.json is missing the subtitle_spans array.")

    applied_adjustments: list[dict[str, Any]] = []
    for adjustment in adjustments:
        segment_index = _resolve_adjustment_index(adjustment, segment_entries)
        segment_entry = segment_entries[segment_index]
        original_start_ms = int(segment_entry["start_ms"])
        original_end_ms = int(segment_entry["end_ms"])
        start_ms = int(adjustment["start_ms"])
        end_ms = int(adjustment["end_ms"])
        _write_time_range(segment_entry, start_ms, end_ms)

        subtitle_index = segment_entry.get("source_subtitle_index")
        segments_subtitle_entries = segments_payload.get("subtitle", {}).get("spans")
        if isinstance(subtitle_index, int):
            if isinstance(segments_subtitle_entries, list) and 0 <= subtitle_index < len(segments_subtitle_entries):
                _write_time_range(segments_subtitle_entries[subtitle_index], start_ms, end_ms)
            if isinstance(subtitle_entries, list) and 0 <= subtitle_index < len(subtitle_entries):
                _write_time_range(subtitle_entries[subtitle_index], start_ms, end_ms)

        applied_adjustments.append(
            {
                "segment_index": segment_index,
                "segment_no": int(segment_entry["segment_no"]),
                "text": str(segment_entry.get("text", "")),
                "original_start_ms": original_start_ms,
                "original_end_ms": original_end_ms,
                "start_ms": start_ms,
                "end_ms": end_ms,
                "delta_start_ms": start_ms - original_start_ms,
                "delta_end_ms": end_ms - original_end_ms,
            }
        )

    _validate_segment_timings(segment_entries)

    segment_models = [_segment_from_json(item) for item in segment_entries]
    segment_rows = segments_to_rows(segment_models)

    segments_payload["segments"] = [item.to_json() for item in segment_models]
    if isinstance(segments_payload.get("subtitle", {}).get("spans"), list):
        segments_payload["subtitle"]["spans"] = segments_payload["subtitle"]["spans"]
    write_json(segments_path, segments_payload)

    if subtitle_payload is not None:
        write_json(subtitle_spans_path, subtitle_payload)

    outputs_payload = manifest.setdefault("outputs", {})
    segments_csv_path = Path(outputs_payload.get("segments_csv") or (output_dir / "segments.csv"))
    review_output_path = Path(outputs_payload.get("review_html") or review_page_path)
    workbook_output_path = Path(outputs_payload.get("workbook") or (output_dir / "dubbing.result.xlsx"))
    outputs_payload["segments_csv"] = str(segments_csv_path)
    outputs_payload["review_html"] = str(review_output_path)
    outputs_payload["workbook"] = str(workbook_output_path)
    write_json(manifest_path, manifest)

    export_csv(segments_csv_path, segment_rows)
    overview_row = _overview_row_from_manifest(manifest)
    export_workbook(
        workbook_output_path,
        segment_rows,
        overview_rows=[overview_row] if overview_row is not None else None,
        template_path=None,
    )

    sequence_no = int(segments_payload.get("sequence_no") or manifest.get("sequence_no") or 1)
    video_name = Path(str(manifest.get("source_mp4") or "02.mp4")).name
    export_review_page(
        output_path=review_output_path,
        video_path=video_name,
        segments=segment_models,
        title=f"Sequence {sequence_no} review",
    )

    batch_workbook_path: Path | None = None
    batch_root = _find_batch_root(output_dir)
    if batch_root is not None:
        batch_summary_path = batch_root / "batch_summary.json"
        batch_summary_payload = _read_json(batch_summary_path)
        for item in batch_summary_payload.get("items", []):
            item_output_dir = Path(item["output_dir"]).resolve()
            if item_output_dir == output_dir:
                item["segment_count"] = len(segment_models)
                break
        write_json(batch_summary_path, batch_summary_payload)

        merged_rows: list[tuple[object, ...]] = []
        overview_rows: list[OverviewRow] = []
        for item in batch_summary_payload.get("items", []):
            item_output_dir = Path(item["output_dir"]).resolve()
            merged_rows.extend(segments_to_rows(_load_segments_from_output_dir(item_output_dir)))
            item_manifest = _read_json(item_output_dir / "manifest.json")
            item_overview_row = _overview_row_from_manifest(item_manifest)
            if item_overview_row is not None:
                overview_rows.append(item_overview_row)
        batch_workbook_path = batch_root / "dubbing.result.xlsx"
        export_workbook(batch_workbook_path, merged_rows, overview_rows=overview_rows, template_path=None)

    updated_files = [
        str(manifest_path),
        str(segments_path),
        str(segments_csv_path),
        str(review_output_path),
        str(workbook_output_path),
    ]
    if subtitle_payload is not None:
        updated_files.append(str(subtitle_spans_path))
    if batch_workbook_path is not None:
        updated_files.extend([str(batch_root / "batch_summary.json"), str(batch_workbook_path)])

    return {
        "review_page_path": str(review_output_path),
        "output_dir": str(output_dir),
        "adjustment_count": len(applied_adjustments),
        "adjustments": applied_adjustments,
        "updated_files": updated_files,
        "batch_workbook_path": str(batch_workbook_path) if batch_workbook_path is not None else None,
    }


class ReviewRequestHandler(SimpleHTTPRequestHandler):
    def end_headers(self) -> None:
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        super().end_headers()

    def do_OPTIONS(self) -> None:
        self.send_response(HTTPStatus.NO_CONTENT)
        self.end_headers()

    def do_POST(self) -> None:
        if self.path.rstrip("/") != "/api/review/save":
            self.send_error(HTTPStatus.NOT_FOUND, "Unknown API route")
            return
        try:
            content_length = int(self.headers.get("Content-Length", "0"))
            payload = json.loads(self.rfile.read(content_length) or b"{}")
            output_root = Path(self.directory).resolve()
            review_path = _resolve_review_path(str(payload.get("review_path", "")), output_root)
            adjustments = payload.get("adjustments") or []
            if not isinstance(adjustments, list):
                raise ValueError("adjustments must be an array.")
            result = apply_review_adjustments(review_path, adjustments)
            self._send_json(HTTPStatus.OK, {"ok": True, **result})
        except FileNotFoundError as exc:
            self._send_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": str(exc)})
        except ValueError as exc:
            self._send_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": str(exc)})
        except Exception as exc:
            self._send_json(HTTPStatus.INTERNAL_SERVER_ERROR, {"ok": False, "error": str(exc)})

    def _send_json(self, status_code: HTTPStatus, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False, indent=2).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def serve_review_api(output_root: Path, host: str = DEFAULT_REVIEW_SERVER_HOST, port: int = DEFAULT_REVIEW_SERVER_PORT) -> None:
    resolved_output_root = output_root.resolve()
    if not resolved_output_root.exists():
        raise FileNotFoundError(f"Output root not found: {resolved_output_root}")

    handler = partial(ReviewRequestHandler, directory=str(resolved_output_root))
    with ThreadingHTTPServer((host, port), handler) as server:
        print(f"Review server: http://{host}:{port}/")
        print(f"Serving output root: {resolved_output_root}")
        server.serve_forever()
