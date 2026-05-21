from __future__ import annotations

import json
import os
import re
import stat
from dataclasses import dataclass
from pathlib import Path
from time import perf_counter
from typing import Any, Sequence

from js_subtitle_converter.progress import ProgressEventCallback, StageProgressTracker, write_json
from video_analysis_pipeline.timecode import format_timestamp, natural_sort_key


_PROPERTY_PATTERN_TEMPLATE = r"(?<![\w$])(?:\"{name}\"|\'{name}\'|{name})\s*:\s*\["
_CHINESE_PUNCTUATION_MAP = str.maketrans(
    {
        "，": ",",
        "。": ".",
        "！": "!",
        "？": "?",
        "：": ":",
        "；": ";",
        "（": "(",
        "）": ")",
        "【": "[",
        "】": "]",
        "《": "<",
        "》": ">",
        "“": '"',
        "”": '"',
        "‘": "'",
        "’": "'",
        "、": ",",
        "　": " ",
    }
)
_SPACE_BEFORE_PUNCTUATION_PATTERN = re.compile(r"\s+([,.;:!?])")
_MULTISPACE_PATTERN = re.compile(r"[ \t]+")
_SUBTITLE_HINT_PATTERN = re.compile(r"(?:\"wordArr\"|\bwordArr\b).*(?:\"timeArr\"|\btimeArr\b)", re.DOTALL)


@dataclass(slots=True)
class SubtitleEntry:
    index: int
    start_ms: int
    end_ms: int
    text: str


@dataclass(slots=True)
class ConversionResult:
    source_js: Path
    output_srt: Path
    entry_count: int
    progress_path: Path
    timings: dict[str, float]
    skipped: bool = False
    skip_reason: str | None = None


@dataclass(slots=True)
class SkippedItem:
    source_js: Path
    output_srt: Path
    reason: str


@dataclass(slots=True)
class ConversionBatchResult:
    converted: list[ConversionResult]
    skipped: list[SkippedItem]
    progress_path: Path
    summary_path: Path


def convert_js_file_to_srt(
    source_js: Path,
    *,
    resume: bool = False,
    cleanup: bool = True,
    progress_callback: ProgressEventCallback | None = None,
) -> ConversionResult:
    source_js = source_js.resolve()
    if not source_js.exists():
        raise FileNotFoundError(f"JS file not found: {source_js}")

    output_srt = source_js.with_suffix(".srt")
    progress_path = _single_progress_path(source_js)
    if resume:
        resumed_result = _try_resume_completed_file(source_js=source_js, output_srt=output_srt, progress_path=progress_path)
        if resumed_result is not None:
            if cleanup:
                _cleanup_directory_after_success(source_js.parent)
            return resumed_result

    tracker = StageProgressTracker(progress_path=progress_path, label=source_js.stem, callback=progress_callback)

    content = tracker.run("read-js", lambda: source_js.read_text(encoding="utf-8-sig"), details={"source_js": str(source_js)})
    word_entries, time_entries = tracker.run("extract-subtitle-arrays", lambda: _extract_subtitle_arrays(content))
    subtitle_entries = tracker.run(
        "build-subtitle-entries",
        lambda: _build_subtitle_entries(word_entries, time_entries),
        details={"word_count": len(word_entries), "time_count": len(time_entries)},
    )
    tracker.run("write-srt", lambda: _write_srt(output_srt, subtitle_entries), details={"output_srt": str(output_srt)})
    tracker.finish(details={"output_srt": str(output_srt), "entry_count": len(subtitle_entries)})
    if cleanup:
        _cleanup_directory_after_success(source_js.parent)
    return ConversionResult(
        source_js=source_js,
        output_srt=output_srt,
        entry_count=len(subtitle_entries),
        progress_path=progress_path,
        timings=dict(tracker.timings),
    )


def convert_js_files_in_batch(
    *,
    input_root: Path | None = None,
    js_files: Sequence[Path] | None = None,
    resume: bool = False,
    cleanup: bool = True,
) -> ConversionBatchResult:
    resolved_root, discovered_files = _resolve_batch_inputs(input_root=input_root, js_files=js_files)
    state_dir = resolved_root / ".js_to_srt"
    progress_path = state_dir / "batch_progress.json"
    summary_path = state_dir / "batch_summary.json"
    batch_started_at = perf_counter()
    completed_items = _load_completed_batch_items(progress_path) if resume else set()
    item_progress: dict[str, dict[str, Any]] = {}
    converted: list[ConversionResult] = []
    skipped: list[SkippedItem] = []
    processed_dirs: set[Path] = set()

    def write_batch_progress(current_item: str | None = None, current_stage: str | None = None, status: str = "running") -> None:
        write_json(
            progress_path,
            {
                "status": status,
                "input_root": str(resolved_root),
                "total_items": len(discovered_files),
                "completed_items": sum(1 for item in item_progress.values() if item.get("status") == "completed"),
                "skipped_items": len(skipped),
                "current_item": current_item,
                "current_stage": current_stage,
                "elapsed_seconds": round(perf_counter() - batch_started_at, 3),
                "items": list(item_progress.values()),
            },
        )

    for source_js in discovered_files:
        source_key = str(source_js)
        output_srt = source_js.with_suffix(".srt")
        if source_key in completed_items and output_srt.exists():
            skipped_item = SkippedItem(source_js=source_js, output_srt=output_srt, reason="completed")
            skipped.append(skipped_item)
            item_progress[source_key] = {
                "name": source_js.stem,
                "source_js": str(source_js),
                "output_srt": str(output_srt),
                "status": "completed",
                "current_stage": None,
                "timings_seconds": {},
                "skipped": True,
            }
            write_batch_progress(current_item=source_key, status="running")
            continue

        item_progress[source_key] = {
            "name": source_js.stem,
            "source_js": str(source_js),
            "output_srt": str(output_srt),
            "status": "running",
            "current_stage": None,
            "timings_seconds": {},
            "skipped": False,
        }
        write_batch_progress(current_item=source_key, status="running")

        def on_progress(stage: str, status: str, payload: dict[str, Any], source_key: str = source_key) -> None:
            item_progress[source_key]["status"] = "failed" if status == "failed" else "running"
            item_progress[source_key]["current_stage"] = payload.get("current_stage")
            item_progress[source_key]["timings_seconds"] = payload.get("timings_seconds", {})
            write_batch_progress(current_item=source_key, current_stage=payload.get("current_stage"), status="running")

        result = convert_js_file_to_srt(source_js, cleanup=False, progress_callback=on_progress)
        converted.append(result)
        processed_dirs.add(source_js.parent)
        item_progress[source_key]["status"] = "completed"
        item_progress[source_key]["current_stage"] = None
        item_progress[source_key]["timings_seconds"] = dict(result.timings)
        write_batch_progress(current_item=source_key, status="running")

    if cleanup:
        for directory in sorted(processed_dirs):
            _cleanup_directory_after_success(directory)

    write_json(
        summary_path,
        {
            "input_root": str(resolved_root),
            "progress_json": str(progress_path),
            "elapsed_seconds": round(perf_counter() - batch_started_at, 3),
            "converted": [
                {
                    "source_js": str(item.source_js),
                    "output_srt": str(item.output_srt),
                    "entry_count": item.entry_count,
                    "progress_json": str(item.progress_path),
                    "timings_seconds": item.timings,
                }
                for item in converted
            ],
            "skipped": [
                {
                    "source_js": str(item.source_js),
                    "output_srt": str(item.output_srt),
                    "reason": item.reason,
                }
                for item in skipped
            ],
        },
    )
    write_batch_progress(status="completed")
    if cleanup:
        _delete_tree(state_dir)
    return ConversionBatchResult(converted=converted, skipped=skipped, progress_path=progress_path, summary_path=summary_path)


def discover_js_files(input_root: Path) -> list[Path]:
    input_root = input_root.resolve()
    if not input_root.exists():
        raise FileNotFoundError(f"Input root not found: {input_root}")
    if not input_root.is_dir():
        raise NotADirectoryError(f"Input root must be a directory: {input_root}")

    discovered = [
        path
        for path in input_root.rglob("*.js")
        if ".js_to_srt" not in path.parts and _looks_like_subtitle_js_file(path)
    ]
    return sorted(discovered, key=lambda path: natural_sort_key(str(path.relative_to(input_root))))


def _resolve_batch_inputs(input_root: Path | None, js_files: Sequence[Path] | None) -> tuple[Path, list[Path]]:
    if input_root is None and not js_files:
        raise ValueError("Either input_root or js_files must be provided.")

    if js_files:
        resolved_files = sorted({Path(path).resolve() for path in js_files}, key=lambda path: natural_sort_key(str(path)))
        for path in resolved_files:
            if not path.exists():
                raise FileNotFoundError(f"JS file not found: {path}")
        if input_root is None:
            common_root = Path(os.path.commonpath([str(path.parent) for path in resolved_files]))
            return common_root, resolved_files
        resolved_root = input_root.resolve()
        return resolved_root, sorted(resolved_files, key=lambda path: natural_sort_key(str(path.relative_to(resolved_root))))

    assert input_root is not None
    resolved_root = input_root.resolve()
    discovered_files = discover_js_files(resolved_root)
    if not discovered_files:
        raise FileNotFoundError(f"No subtitle JS files found under: {resolved_root}")
    return resolved_root, discovered_files


def _single_progress_path(source_js: Path) -> Path:
    return source_js.parent / ".js_to_srt" / f"{source_js.stem}.progress.json"


def _load_completed_batch_items(progress_path: Path) -> set[str]:
    if not progress_path.exists():
        return set()
    try:
        payload = json.loads(progress_path.read_text(encoding="utf-8"))
    except Exception:
        return set()
    completed: set[str] = set()
    for item in payload.get("items", []):
        if item.get("status") == "completed" and item.get("source_js"):
            completed.add(str(Path(item["source_js"]).resolve()))
    return completed


def _looks_like_subtitle_js_file(path: Path) -> bool:
    try:
        content = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        return False
    return bool(_SUBTITLE_HINT_PATTERN.search(content))


def _extract_subtitle_arrays(content: str) -> tuple[list[str], list[str]]:
    word_entries = _extract_js_string_array(content, "wordArr")
    time_entries = _extract_js_string_array(content, "timeArr")
    if not word_entries:
        raise ValueError("wordArr is empty.")
    if not time_entries:
        raise ValueError("timeArr is empty.")
    return word_entries, time_entries


def _extract_js_string_array(content: str, property_name: str) -> list[str]:
    property_pattern = re.compile(_PROPERTY_PATTERN_TEMPLATE.format(name=re.escape(property_name)))
    match = property_pattern.search(content)
    if match is None:
        raise ValueError(f"Missing {property_name} array in subtitle JS content.")

    index = match.end() - 1
    values: list[str] = []
    index += 1

    while index < len(content):
        index = _skip_js_spacing_and_comments(content, index)
        if index >= len(content):
            break
        if content[index] == "]":
            return values
        if content[index] not in {'"', "'"}:
            raise ValueError(f"{property_name} must contain only string values.")
        value, index = _parse_js_string(content, index)
        values.append(value)
        index = _skip_js_spacing_and_comments(content, index)
        if index >= len(content):
            break
        if content[index] == ",":
            index += 1
            continue
        if content[index] == "]":
            return values
        raise ValueError(f"Unexpected token while parsing {property_name}: {content[index]!r}")

    raise ValueError(f"Unterminated {property_name} array in subtitle JS content.")


def _skip_js_spacing_and_comments(content: str, index: int) -> int:
    while index < len(content):
        char = content[index]
        if char.isspace():
            index += 1
            continue
        if content.startswith("//", index):
            newline_index = content.find("\n", index)
            index = len(content) if newline_index < 0 else newline_index + 1
            continue
        if content.startswith("/*", index):
            comment_end = content.find("*/", index + 2)
            if comment_end < 0:
                raise ValueError("Unterminated block comment in subtitle JS content.")
            index = comment_end + 2
            continue
        return index
    return index


def _parse_js_string(content: str, index: int) -> tuple[str, int]:
    quote = content[index]
    index += 1
    buffer: list[str] = []
    while index < len(content):
        char = content[index]
        if char == quote:
            return "".join(buffer), index + 1
        if char == "\\":
            if index + 1 >= len(content):
                raise ValueError("Invalid trailing escape in subtitle JS content.")
            escaped, index = _parse_js_escape(content, index + 1, quote)
            buffer.append(escaped)
            continue
        buffer.append(char)
        index += 1
    raise ValueError("Unterminated string in subtitle JS content.")


def _parse_js_escape(content: str, index: int, quote: str) -> tuple[str, int]:
    escape_char = content[index]
    simple_escapes = {
        "\\": "\\",
        "/": "/",
        "b": "\b",
        "f": "\f",
        "n": "\n",
        "r": "\r",
        "t": "\t",
        '"': '"',
        "'": "'",
    }
    if escape_char == quote:
        return quote, index + 1
    if escape_char == "u":
        code_point = content[index + 1 : index + 5]
        if len(code_point) != 4 or not re.fullmatch(r"[0-9a-fA-F]{4}", code_point):
            raise ValueError("Invalid unicode escape in subtitle JS content.")
        return chr(int(code_point, 16)), index + 5
    return simple_escapes.get(escape_char, escape_char), index + 1


def _build_subtitle_entries(word_entries: Sequence[str], time_entries: Sequence[str]) -> list[SubtitleEntry]:
    if len(time_entries) % 2 != 0:
        raise ValueError("timeArr length must be even because each subtitle requires a start and end time.")
    expected_time_count = len(word_entries) * 2
    if len(time_entries) != expected_time_count:
        raise ValueError(
            f"wordArr/timeArr length mismatch: expected {expected_time_count} time entries for {len(word_entries)} subtitles, got {len(time_entries)}."
        )

    entries: list[SubtitleEntry] = []
    for index, text in enumerate(word_entries, start=1):
        start_ms = _parse_millisecond_string(time_entries[(index - 1) * 2])
        end_ms = _parse_millisecond_string(time_entries[(index - 1) * 2 + 1])
        if end_ms <= start_ms:
            raise ValueError(f"Subtitle {index} has non-increasing time range: {start_ms} -> {end_ms}.")
        entries.append(
            SubtitleEntry(
                index=index,
                start_ms=start_ms,
                end_ms=end_ms,
                text=_normalize_word_entry(text),
            )
        )
    return entries


def _parse_millisecond_string(value: str) -> int:
    normalized = value.strip()
    if not normalized.isdigit():
        raise ValueError(f"Invalid timeArr value: {value!r}. Expected milliseconds like 011214.")
    return int(normalized)


def _normalize_word_entry(text: str) -> str:
    normalized = text.translate(_CHINESE_PUNCTUATION_MAP)
    normalized = _SPACE_BEFORE_PUNCTUATION_PATTERN.sub(r"\1", normalized)
    normalized_lines = [_MULTISPACE_PATTERN.sub(" ", line).strip() for line in normalized.replace("\r\n", "\n").split("\n")]
    return "\n".join(line for line in normalized_lines if line).strip()


def _write_srt(output_srt: Path, entries: Sequence[SubtitleEntry]) -> Path:
    output_srt.parent.mkdir(parents=True, exist_ok=True)
    payload = "\n\n".join(_format_srt_entry(entry) for entry in entries) + "\n"
    output_srt.write_text(payload, encoding="utf-8")
    return output_srt


def _try_resume_completed_file(source_js: Path, output_srt: Path, progress_path: Path) -> ConversionResult | None:
    if not output_srt.exists() or not progress_path.exists():
        return None
    try:
        payload = json.loads(progress_path.read_text(encoding="utf-8"))
    except Exception:
        return None

    if payload.get("status") != "completed":
        return None

    details = payload.get("details") if isinstance(payload.get("details"), dict) else {}
    entry_count = details.get("entry_count", 0)
    timings = payload.get("timings_seconds") if isinstance(payload.get("timings_seconds"), dict) else {}
    return ConversionResult(
        source_js=source_js,
        output_srt=output_srt,
        entry_count=int(entry_count),
        progress_path=progress_path,
        timings={str(key): float(value) for key, value in timings.items()},
        skipped=True,
        skip_reason="completed",
    )


def _format_srt_entry(entry: SubtitleEntry) -> str:
    return "\n".join(
        [
            str(entry.index),
            f"{_format_srt_timestamp(entry.start_ms)} --> {_format_srt_timestamp(entry.end_ms)}",
            entry.text,
        ]
    )


def _format_srt_timestamp(milliseconds: int) -> str:
    return format_timestamp(milliseconds).replace(".", ",")


def _cleanup_directory_after_success(directory: Path) -> None:
    allowed_suffixes = {".mp4", ".srt", ".mp3"}
    progress_dir = directory / ".js_to_srt"
    if progress_dir.exists() and progress_dir.is_dir():
        for path in sorted(progress_dir.rglob("*"), reverse=True):
            if path.is_file():
                _delete_file(path)
            elif path.is_dir():
                _delete_dir(path)
        _delete_dir(progress_dir)

    for child in directory.iterdir():
        if not child.is_file():
            continue
        if child.suffix.lower() in allowed_suffixes:
            continue
        _delete_file(child)


def _delete_file(path: Path) -> None:
    try:
        path.unlink(missing_ok=True)
        return
    except PermissionError:
        pass
    except FileNotFoundError:
        return

    try:
        current_mode = path.stat().st_mode
        path.chmod(current_mode | stat.S_IWRITE)
        path.unlink(missing_ok=True)
    except Exception as exc:
        print(f"WARNING: Could not delete file during cleanup: {path} ({exc})")


def _delete_dir(path: Path) -> None:
    try:
        path.rmdir()
    except FileNotFoundError:
        return
    except Exception as exc:
        print(f"WARNING: Could not delete directory during cleanup: {path} ({exc})")


def _delete_tree(path: Path) -> None:
    if not path.exists() or not path.is_dir():
        return
    for child in sorted(path.rglob("*"), reverse=True):
        if child.is_file():
            _delete_file(child)
        elif child.is_dir():
            _delete_dir(child)
    _delete_dir(path)