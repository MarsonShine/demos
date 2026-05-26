from __future__ import annotations

import json
import shutil
from dataclasses import dataclass
from pathlib import Path
from time import perf_counter
from statistics import fmean
import re
import tempfile
from typing import Any, Protocol

from video_analysis_pipeline.asr import create_transcriber, normalize_asr_provider
from video_analysis_pipeline.azure_openai_translation import translate_segments_for_education
from video_analysis_pipeline.config import PipelineConfig, SegmentationConfig
from video_analysis_pipeline.azure_openai_summary import generate_video_summary
from video_analysis_pipeline.exporter import (
    export_csv,
    export_review_page,
    export_workbook,
    segments_to_rows,
    write_json,
)
from video_analysis_pipeline.media import (
    BackgroundAudioResult,
    copy_source_video,
    detect_silence,
    extract_audio_mp3,
    extract_background_audio_mp3,
    extract_cover,
    extract_muted_video,
    probe_media,
    resolve_source_video_export_stage,
)
from video_analysis_pipeline.models import OverviewRow, Segment, SubtitleSpan, TranscriptUtterance
from video_analysis_pipeline.progress import ProgressEventCallback, StageProgressTracker
from video_analysis_pipeline.segmentation import build_segments
from video_analysis_pipeline.subtitle_alignment import build_segments_from_subtitles, flatten_asr_words
from video_analysis_pipeline.subtitle_srt import discover_srt_path, parse_srt_file
from video_analysis_pipeline.timecode import natural_sort_key


class Transcriber(Protocol):
    def transcribe(self, audio_path: Path) -> list[TranscriptUtterance]:
        ...


@dataclass(slots=True)
class ProcessedItem:
    sequence_no: int
    source_mp4: Path
    output_dir: Path
    workbook_path: Path | None
    review_page_path: Path | None
    segments: list[Segment]
    overview_row: OverviewRow | None = None
    timings: dict[str, float] | None = None


@dataclass(slots=True)
class BatchInputItem:
    source_mp4: Path
    source_srt: Path
    relative_dir: Path
    source_mp3: Path | None = None


@dataclass(slots=True)
class ExistingOutputItem:
    sequence_no: int
    output_dir: Path
    source_mp4: Path
    subtitle_spans: list[SubtitleSpan]
    segments: list[Segment]
    manifest: dict[str, Any]


def _segments_need_translation(segments: list[Segment]) -> bool:
    return any(segment.text.strip() and not (segment.translated_text or "").strip() for segment in segments)


def _populate_segment_translations(segments: list[Segment], config: PipelineConfig) -> list[Segment]:
    if not _segments_need_translation(segments):
        return segments
    return translate_segments_for_education(segments=segments, config=config.azure_openai)


MOD_FINAL_OUTPUT = "mod"
MOD_ITEM_ROOT_NAME = "dubbing"
MOD_DEFAULT_WORKBOOK_NAME = "movie_dubbing.xlsx"
MOD_REMOVABLE_FILENAMES = frozenset(
    {
        "batch_progress.json",
        "batch_summary.json",
        "dubbing.result.xlsx",
        "manifest.json",
        "progress.json",
        "review.html",
        "segments.csv",
        "segments.json",
        "subtitle_spans.json",
    }
)
WORD_TOKEN_PATTERN = re.compile(r"[A-Za-z]+(?:'[A-Za-z]+)?")
CLAUSE_MARKER_PATTERN = re.compile(
    r"[,:;]|\b(?:because|although|unless|however|while|which|who|that|if|when|before|after|until|since|though|whereas|meanwhile|instead|therefore)\b",
    re.IGNORECASE,
)


def process_single_video(
    source_mp4: Path,
    output_dir: Path,
    sequence_no: int,
    config: PipelineConfig,
    source_srt: Path | None = None,
    source_mp3: Path | None = None,
    template_path: Path | None = None,
    workbook_output: Path | None = None,
    transcriber: Transcriber | None = None,
    progress_callback: ProgressEventCallback | None = None,
    generate_overview: bool = True,
) -> ProcessedItem:
    if not source_mp4.exists():
        raise FileNotFoundError(f"Source video not found: {source_mp4}")

    active_transcriber = transcriber or create_transcriber(config)
    asr_provider = normalize_asr_provider(config.asr.provider)
    steps = config.steps

    output_dir.mkdir(parents=True, exist_ok=True)
    progress_path = output_dir / "progress.json"
    progress_tracker = StageProgressTracker(progress_path=progress_path, label=output_dir.name, callback=progress_callback)
    source_video_export_stage = resolve_source_video_export_stage(config.video)

    if steps.export_source_video:
        source_asset = progress_tracker.run(
            source_video_export_stage,
            lambda: copy_source_video(source_mp4, output_dir / "02.mp4", config.video),
        )
    else:
        source_asset = source_mp4

    if steps.export_cover:
        cover_path = progress_tracker.run(
            "extract-cover",
            lambda: extract_cover(source_asset, output_dir / "01.jpg"),
        )


    else:
        cover_path = output_dir / "01.jpg"

    if steps.export_muted_video:
        muted_video_path = progress_tracker.run(
            "extract-muted-video",
            lambda: extract_muted_video(source_asset, output_dir / "01.mp4"),
        )
    else:
        muted_video_path = output_dir / "01.mp4"

    source_metadata = progress_tracker.run("probe-source-media", lambda: probe_media(source_asset))

    if steps.export_muted_video:
        muted_metadata = progress_tracker.run("probe-muted-video", lambda: probe_media(muted_video_path))
    else:
        muted_metadata = source_metadata

    with tempfile.TemporaryDirectory(prefix="video-analysis-audio-") as temp_dir:
        analysis_audio_path = progress_tracker.run(
            "extract-analysis-audio",
            lambda: extract_audio_mp3(source_asset, Path(temp_dir) / "analysis.mp3"),
        )
        analysis_audio_metadata = progress_tracker.run("probe-analysis-audio", lambda: probe_media(analysis_audio_path))
        if source_mp3 is not None:
            background_audio_result = progress_tracker.run(
                "copy-bgm",
                lambda: _copy_existing_background_audio(source_mp3, output_dir / "03.mp3"),
            )
        elif steps.export_background_audio:
            source_cache_key_material = _build_audio_cache_key_material(
                source_mp4=source_mp4,
                export_source_video=steps.export_source_video,
                config=config,
            )
            background_audio_result = progress_tracker.run(
                "extract-bgm",
                lambda: extract_background_audio_mp3(
                    analysis_audio_path,
                    output_dir / "03.mp3",
                    config.audio,
                    cache_key_material=source_cache_key_material,
                ),
            )
        else:
            background_audio_result = BackgroundAudioResult(path=None, from_cache=False, cache_path=None)
        if background_audio_result.path is not None:
            audio_metadata = progress_tracker.run("probe-bgm-audio", lambda: probe_media(background_audio_result.path))
        else:
            audio_metadata = None
        silence_ranges, non_silent_ranges = progress_tracker.run(
            "detect-silence",
            lambda: detect_silence(
                audio_path=analysis_audio_path,
                total_duration_ms=analysis_audio_metadata.duration_ms,
                silence_threshold_db=config.segmentation.silence_threshold_db,
                min_silence_duration_ms=config.segmentation.min_silence_duration_ms,
            ),
        )
        utterances = progress_tracker.run(
            "transcribe-audio",
            lambda: active_transcriber.transcribe(analysis_audio_path),
            details={"provider": asr_provider},
        )

    audio_path = background_audio_result.path
    resolved_srt_path = discover_srt_path(source_mp4=source_mp4, source_srt=source_srt)
    subtitle_spans = progress_tracker.run(
        "parse-subtitles",
        lambda: parse_srt_file(resolved_srt_path) if resolved_srt_path is not None else [],
        details={"source_path": str(resolved_srt_path) if resolved_srt_path is not None else None},
    )
    subtitle_spans_path = output_dir / "subtitle_spans.json"

    segments, alignment_summary, asr_word_json = progress_tracker.run(
        "build-segments",
        lambda: _build_output_segments(
            sequence_no=sequence_no,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            analysis_audio_metadata=analysis_audio_metadata,
            source_metadata=source_metadata,
            config=config,
            non_silent_ranges=non_silent_ranges,
        ),
    )

    overview_row: OverviewRow | None = None
    if generate_overview:
        display_title = _derive_display_title(source_mp4)
        if steps.generate_summary:
            video_description = progress_tracker.run(
                "generate-video-summary",
                lambda: generate_video_summary(
                    title=display_title,
                    text_blocks=_summary_text_blocks(subtitle_spans, segments),
                    config=config.azure_openai,
                ),
                details={"deployment": config.azure_openai.deployment},
            )
        else:
            video_description = ""
        overview_row = _build_overview_row(
            sequence_no=sequence_no,
            title=display_title,
            source_asset=source_asset,
            muted_video_path=muted_video_path,
            audio_path=audio_path,
            cover_path=cover_path,
            video_description=video_description,
            subtitle_spans=subtitle_spans,
            segments=segments,
            config=config,
        )
        progress_tracker.run(
            "translate-segments",
            lambda: _populate_segment_translations(segments, config),
            details={"deployment": config.azure_openai.deployment, "segment_count": len(segments)},
        )

    manifest_path = output_dir / "manifest.json"
    segments_path = output_dir / "segments.json"
    segments_csv_path = output_dir / "segments.csv"
    review_page_path = output_dir / "review.html"
    segment_rows = segments_to_rows(segments)
    actual_workbook_path = progress_tracker.run(
        "write-artifacts",
        lambda: _write_output_artifacts(
            subtitle_spans_path=subtitle_spans_path,
            segments_path=segments_path,
            segments_csv_path=segments_csv_path,
            review_page_path=review_page_path,
            workbook_output=workbook_output if overview_row is not None else None,
            template_path=template_path,
            sequence_no=sequence_no,
            source_asset=source_asset,
            resolved_srt_path=resolved_srt_path,
            audio_path=audio_path,
            asr_provider=asr_provider,
            subtitle_spans=subtitle_spans,
            alignment_summary=alignment_summary,
            silence_ranges=silence_ranges,
            non_silent_ranges=non_silent_ranges,
            asr_word_json=asr_word_json,
            utterances=utterances,
            segments=segments,
            segment_rows=segment_rows,
            overview_row=overview_row,
            write_workbook=steps.export_workbook,
            write_review_page=steps.export_review_page,
            write_csv=steps.export_csv,
        ),
    )

    progress_tracker.finish(details={"segment_count": len(segments)})
    manifest_payload = _build_manifest_payload(
        sequence_no=sequence_no,
        source_asset=source_asset,
        cover_path=cover_path,
        muted_video_path=muted_video_path,
        audio_path=audio_path,
        workbook_path=actual_workbook_path,
        manifest_path=manifest_path,
        subtitle_spans_path=subtitle_spans_path,
        segments_path=segments_path,
        segments_csv_path=segments_csv_path,
        review_page_path=review_page_path,
        progress_path=progress_path,
        asr_provider=asr_provider,
        resolved_srt_path=resolved_srt_path,
        subtitle_spans=subtitle_spans,
        alignment_summary=alignment_summary,
        source_metadata=source_metadata,
        muted_metadata=muted_metadata,
        analysis_audio_metadata=analysis_audio_metadata,
        audio_metadata=audio_metadata,
        background_audio_result=background_audio_result,
        config=config,
        overview_row=overview_row,
        timings=progress_tracker.timings,
    )
    write_json(manifest_path, manifest_payload)

    return ProcessedItem(
        sequence_no=sequence_no,
        source_mp4=source_asset,
        output_dir=output_dir,
        workbook_path=actual_workbook_path,
        review_page_path=review_page_path,
        segments=segments,
        overview_row=overview_row,
        timings=dict(progress_tracker.timings),
    )


def _build_leading_title_segment(
    sequence_no: int,
    subtitle_spans: list[SubtitleSpan],
    utterances: list[TranscriptUtterance],
    audio_duration_ms: int,
    video_duration_ms: int,
    config: SegmentationConfig,
    aligned_segments: list[Segment] | None = None,
) -> Segment | None:
    if not subtitle_spans or not utterances:
        return None

    first_subtitle_start_ms = getattr(subtitle_spans[0], "start_ms", None)
    if first_subtitle_start_ms is None or first_subtitle_start_ms <= 0:
        return None

    first_subtitle_audio_start = _scale_video_to_audio(first_subtitle_start_ms, audio_duration_ms, video_duration_ms)
    word_refs = flatten_asr_words(utterances)
    first_aligned_word_index = None
    first_aligned_segment_start_ms = None
    if aligned_segments:
        for segment in aligned_segments:
            if segment.text_source == "srt" and segment.source_word_range:
                first_aligned_word_index = segment.source_word_range[0]
                first_aligned_segment_start_ms = segment.start_ms
                break

    if first_aligned_word_index is not None:
        leading_words = [word for word in word_refs if word.global_index < first_aligned_word_index]
    else:
        leading_utterances = [
            (utterance_index, utterance)
            for utterance_index, utterance in enumerate(utterances)
            if utterance.end_ms <= max(0, first_subtitle_audio_start - 300)
        ]
        if leading_utterances:
            last_leading_end_ms = leading_utterances[-1][1].end_ms
            leading_words = [word for word in word_refs if word.end_ms <= last_leading_end_ms]
        else:
            leading_words = [
                word
                for word in word_refs
                if word.end_ms <= max(0, first_subtitle_audio_start - 150)
            ]

    if not leading_words:
        return None
    intro_text = _join_intro_words(leading_words)
    if not intro_text:
        return None
    confidences = [word.confidence for word in leading_words if word.confidence is not None]
    start_audio_ms = max(0, leading_words[0].start_ms - config.lead_in_ms)
    end_audio_ms = min(audio_duration_ms, leading_words[-1].end_ms + config.tail_out_ms)
    source_utterance_indexes = sorted({word.utterance_index for word in leading_words})

    start_video_ms = _scale_audio_to_video(start_audio_ms, audio_duration_ms, video_duration_ms)
    end_video_ms = _scale_audio_to_video(end_audio_ms, audio_duration_ms, video_duration_ms)
    next_segment_start_ms = first_subtitle_start_ms
    if first_aligned_segment_start_ms is not None:
        next_segment_start_ms = min(next_segment_start_ms, first_aligned_segment_start_ms)
    end_video_ms = min(end_video_ms, next_segment_start_ms - 1)
    if end_video_ms <= start_video_ms:
        return None
    intro_segment = Segment(
        sequence_no=sequence_no,
        segment_no=1,
        text=intro_text,
        start_ms=start_video_ms,
        end_ms=max(start_video_ms + 1, end_video_ms),
        source_utterance_indexes=source_utterance_indexes,
        confidence=float(fmean(confidences)) if confidences else None,
        text_source="asr-title",
        alignment_confidence=None,
        source_word_range=[leading_words[0].global_index, leading_words[-1].global_index],
        quality_flags=["title_segment_from_asr"],
    )
    return intro_segment


def _join_intro_words(words: list) -> str:
    parts: list[str] = []
    for word in words:
        token = getattr(word, "text", "").strip()
        if not token:
            continue
        if not parts:
            parts.append(token)
            continue
        if token.startswith("'") or token[:1] in {".", ",", "!", "?", ";", ":", ")", "%"}:
            parts[-1] = f"{parts[-1]}{token}"
            continue
        parts.append(token)
    return " ".join(parts).strip()


def _renumber_segments(segments: list[Segment]) -> None:
    for index, segment in enumerate(segments, start=1):
        segment.segment_no = index


def _segment_sort_key(segment: Segment) -> tuple[int, int, int, int]:
    subtitle_index = segment.source_subtitle_index if segment.source_subtitle_index is not None else 1_000_000
    title_priority = 0 if segment.text_source == "asr-title" else 1
    return (segment.start_ms, segment.end_ms, title_priority, subtitle_index)


def _normalize_segment_order(segments: list[Segment]) -> None:
    segments.sort(key=_segment_sort_key)
    _renumber_segments(segments)


def _scale_audio_to_video(milliseconds: int, audio_duration_ms: int, video_duration_ms: int) -> int:
    if audio_duration_ms <= 0 or video_duration_ms <= 0:
        return max(0, milliseconds)
    ratio = video_duration_ms / audio_duration_ms
    if abs(video_duration_ms - audio_duration_ms) < 20:
        ratio = 1.0
    return int(round(milliseconds * ratio))


def _scale_video_to_audio(milliseconds: int, audio_duration_ms: int, video_duration_ms: int) -> int:
    if audio_duration_ms <= 0 or video_duration_ms <= 0:
        return max(0, milliseconds)
    ratio = audio_duration_ms / video_duration_ms
    if abs(video_duration_ms - audio_duration_ms) < 20:
        ratio = 1.0
    return int(round(milliseconds * ratio))


def discover_batch_inputs(
    input_root: Path,
    source_name: str | None = None,
    srt_name: str | None = None,
) -> list[BatchInputItem]:
    if not input_root.exists():
        raise FileNotFoundError(f"Input root not found: {input_root}")

    directories = [input_root, *sorted((path for path in input_root.rglob("*") if path.is_dir()), key=_batch_directory_sort_key)]
    discovered_items: list[BatchInputItem] = []

    for directory in directories:
        mp4_candidates = _sorted_media_files(directory, "*.mp4")
        srt_candidates = _sorted_media_files(directory, "*.srt")
        mp3_candidates = _sorted_media_files(directory, "*.mp3")

        if not mp4_candidates and not srt_candidates:
            continue

        if len(mp4_candidates) != 1 or len(srt_candidates) != 1 or len(mp3_candidates) > 1:
            raise RuntimeError(
                f"Expected exactly one MP4, one SRT, and at most one MP3 in {directory}, "
                f"found {len(mp4_candidates)} MP4, {len(srt_candidates)} SRT, and {len(mp3_candidates)} MP3."
            )

        source_mp4 = mp4_candidates[0]
        source_srt = srt_candidates[0]
        source_mp3 = mp3_candidates[0] if mp3_candidates else None
        if source_name is not None and source_mp4.name != source_name:
            continue
        if srt_name is not None and source_srt.name != srt_name:
            continue

        relative_dir = directory.relative_to(input_root)
        discovered_items.append(
            BatchInputItem(
                source_mp4=source_mp4,
                source_srt=source_srt,
                relative_dir=relative_dir,
                source_mp3=source_mp3,
            )
        )

    if not discovered_items:
        raise FileNotFoundError(f"No folders with exactly one MP4 and one SRT were found under {input_root}.")

    return discovered_items


def _sorted_media_files(directory: Path, pattern: str) -> list[Path]:
    return sorted(
        (path for path in directory.glob(pattern) if path.is_file()),
        key=lambda item: natural_sort_key(item.name),
    )


def _batch_directory_sort_key(path: Path) -> list[object]:
    return natural_sort_key(path.as_posix())


def _resolve_batch_output_dir(output_root: Path, relative_dir: Path) -> Path:
    if relative_dir == Path("."):
        return output_root
    return output_root / relative_dir


def _resolve_final_batch_output_dir(
    output_root: Path,
    relative_dir: Path,
    sequence_no: int,
    final_output: str,
) -> Path:
    if final_output == MOD_FINAL_OUTPUT:
        return output_root / MOD_ITEM_ROOT_NAME / str(sequence_no)
    return _resolve_batch_output_dir(output_root, relative_dir)


def _resolve_batch_workbook_output(
    output_root: Path,
    workbook_output: Path | None,
    final_output: str,
) -> Path | None:
    if workbook_output is not None:
        return workbook_output
    if final_output == MOD_FINAL_OUTPUT:
        return output_root / MOD_DEFAULT_WORKBOOK_NAME
    return None


def _cleanup_mod_output_artifacts(
    output_root: Path,
    item_dirs: list[Path],
    protected_paths: list[Path | None],
) -> None:
    protected_resolved = {path.resolve() for path in protected_paths if path is not None and path.exists()}
    for directory in [output_root, *item_dirs]:
        for file_name in MOD_REMOVABLE_FILENAMES:
            candidate = directory / file_name
            if not candidate.exists() or not candidate.is_file():
                continue
            if candidate.resolve() in protected_resolved:
                continue
            candidate.unlink()


def process_batch(
    input_root: Path,
    output_root: Path,
    source_name: str | None,
    config: PipelineConfig,
    srt_name: str | None = None,
    template_path: Path | None = None,
    workbook_output: Path | None = None,
    generate_overview: bool = True,
    final_output: str = "standard",
    resume: bool = False,
) -> list[ProcessedItem]:
    discovered_inputs = discover_batch_inputs(
        input_root=input_root,
        source_name=source_name,
        srt_name=srt_name,
    )

    output_root.mkdir(parents=True, exist_ok=True)
    batch_progress_path = output_root / "batch_progress.json"
    batch_started_at = perf_counter()
    actual_workbook_output = _resolve_batch_workbook_output(output_root, workbook_output, final_output)
    item_progress: dict[str, dict[str, Any]] = {}
    completed_output_dirs: set[str] = set()
    if resume:
        completed_output_dirs = _load_completed_batch_output_dirs(batch_progress_path)
        completed_output_dirs_from_outputs = _discover_completed_batch_output_dirs(
            output_root=output_root,
            discovered_inputs=discovered_inputs,
            final_output=final_output,
        )
        recovered_output_dirs = completed_output_dirs_from_outputs - completed_output_dirs
        if recovered_output_dirs:
            print(f"Resume recovered {len(recovered_output_dirs)} completed item(s) from existing output artifacts.")
        completed_output_dirs |= completed_output_dirs_from_outputs

    def write_batch_progress(current_item: str | None = None, current_stage: str | None = None, status: str = "running") -> None:
        write_json(
            batch_progress_path,
            {
                "status": status,
                "input_root": str(input_root),
                "output_root": str(output_root),
                "total_items": len(discovered_inputs),
                "completed_items": sum(1 for item in item_progress.values() if item.get("status") == "completed"),
                "current_item": current_item,
                "current_stage": current_stage,
                "elapsed_seconds": round(perf_counter() - batch_started_at, 3),
                "items": list(item_progress.values()),
            },
        )

    processed_items: list[ProcessedItem] = []
    for sequence_no, batch_item in enumerate(discovered_inputs, start=1):
        output_dir = _resolve_final_batch_output_dir(
            output_root=output_root,
            relative_dir=batch_item.relative_dir,
            sequence_no=sequence_no,
            final_output=final_output,
        )
        item_key = str(output_dir)
        if item_key in completed_output_dirs:
            resumed_item = _try_load_completed_batch_item(output_dir)
            if resumed_item is not None:
                item_progress[item_key] = {
                    "sequence_no": sequence_no,
                    "name": batch_item.source_mp4.stem,
                    "output_dir": str(output_dir),
                    "status": "completed",
                    "current_stage": None,
                    "timings_seconds": resumed_item.timings or {},
                }
                processed_items.append(resumed_item)
                write_batch_progress(current_item=item_key, status="running")
                continue
            print(f"WARNING: Could not resume completed item from {output_dir}; reprocessing it.")

        item_progress[item_key] = {
            "sequence_no": sequence_no,
            "name": batch_item.source_mp4.stem,
            "output_dir": str(output_dir),
            "status": "running",
            "current_stage": None,
            "timings_seconds": {},
        }
        write_batch_progress(current_item=item_key, status="running")

        def on_progress(stage: str, status: str, payload: dict[str, Any], item_key: str = item_key) -> None:
            item_progress[item_key]["status"] = "failed" if status == "failed" else "running"
            item_progress[item_key]["current_stage"] = payload.get("current_stage")
            item_progress[item_key]["timings_seconds"] = payload.get("timings_seconds", {})
            write_batch_progress(current_item=item_key, current_stage=payload.get("current_stage"), status="running")

        processed_items.append(
            process_single_video(
                source_mp4=batch_item.source_mp4,
                output_dir=output_dir,
                sequence_no=sequence_no,
                config=config,
                source_srt=batch_item.source_srt,
                source_mp3=batch_item.source_mp3,
                template_path=None,
                workbook_output=None,
                progress_callback=on_progress,
                generate_overview=generate_overview,
            )
        )
        item_progress[item_key]["status"] = "completed"
        item_progress[item_key]["current_stage"] = None
        item_progress[item_key]["timings_seconds"] = processed_items[-1].timings or {}
        write_batch_progress(current_item=item_key, status="running")

    if generate_overview and actual_workbook_output is not None:
        all_rows = []
        overview_rows: list[OverviewRow] = []
        for item in processed_items:
            all_rows.extend(segments_to_rows(item.segments))
            if item.overview_row is not None:
                overview_rows.append(item.overview_row)
        export_workbook(
            output_path=actual_workbook_output,
            rows=all_rows,
            overview_rows=overview_rows,
            template_path=template_path,
        )

    batch_summary_path = output_root / "batch_summary.json"
    write_json(
        batch_summary_path,
        {
            "input_root": str(input_root),
            "output_root": str(output_root),
            "items": [
                {
                    "sequence_no": item.sequence_no,
                    "source_mp4": str(item.source_mp4),
                    "output_dir": str(item.output_dir),
                    "segment_count": len(item.segments),
                    "overview": item.overview_row.to_json() if item.overview_row is not None else None,
                    "timings_seconds": item.timings or {},
                    "progress_json": str(item.output_dir / "progress.json"),
                }
                for item in processed_items
            ],
            "discovered_inputs": [
                {
                    "source_mp4": str(batch_item.source_mp4),
                    "source_srt": str(batch_item.source_srt),
                    "relative_dir": str(batch_item.relative_dir),
                }
                for batch_item in discovered_inputs
            ],
            "elapsed_seconds": round(perf_counter() - batch_started_at, 3),
            "progress_json": str(batch_progress_path),
            "workbook": str(actual_workbook_output) if actual_workbook_output is not None else None,
        },
    )
    write_batch_progress(status="completed")

    if final_output == MOD_FINAL_OUTPUT:
        _cleanup_mod_output_artifacts(
            output_root=output_root,
            item_dirs=[item.output_dir for item in processed_items],
            protected_paths=[actual_workbook_output],
        )

    return processed_items


def process_single_overview(
    output_dir: Path,
    config: PipelineConfig,
    template_path: Path | None = None,
    workbook_output: Path | None = None,
    progress_callback: ProgressEventCallback | None = None,
) -> ProcessedItem:
    existing_output = _load_existing_output(output_dir)
    progress_path = output_dir / "progress.json"
    progress_tracker = StageProgressTracker(progress_path=progress_path, label=output_dir.name, callback=progress_callback)

    source_asset = _resolve_existing_asset_path(existing_output.output_dir, existing_output.manifest, "source_mp4", "02.mp4")
    muted_video_path = _resolve_existing_asset_path(existing_output.output_dir, existing_output.manifest, "muted_video", "01.mp4")
    audio_path = _resolve_existing_optional_asset_path(existing_output.output_dir, existing_output.manifest, "audio_mp3", "03.mp3")
    cover_path = _resolve_existing_asset_path(existing_output.output_dir, existing_output.manifest, "cover_image", "01.jpg")
    workbook_path = workbook_output or _resolve_existing_workbook_path(existing_output.output_dir, existing_output.manifest)
    display_title = _resolve_existing_title(existing_output)

    video_description = progress_tracker.run(
        "generate-video-summary",
        lambda: generate_video_summary(
            title=display_title,
            text_blocks=_summary_text_blocks(existing_output.subtitle_spans, existing_output.segments),
            config=config.azure_openai,
        ),
        details={"deployment": config.azure_openai.deployment},
    )
    overview_row = _build_overview_row(
        sequence_no=existing_output.sequence_no,
        title=display_title,
        source_asset=source_asset,
        muted_video_path=muted_video_path,
        audio_path=audio_path,
        cover_path=cover_path,
        video_description=video_description,
        subtitle_spans=existing_output.subtitle_spans,
        segments=existing_output.segments,
        config=config,
    )
    progress_tracker.run(
        "translate-segments",
        lambda: _populate_segment_translations(existing_output.segments, config),
        details={"deployment": config.azure_openai.deployment, "segment_count": len(existing_output.segments)},
    )
    segment_rows = segments_to_rows(existing_output.segments)
    actual_workbook_path = progress_tracker.run(
        "write-overview-workbook",
        lambda: export_workbook(
            output_path=workbook_path,
            rows=segment_rows,
            overview_rows=[overview_row],
            template_path=template_path,
        ),
    )
    progress_tracker.finish(details={"segment_count": len(existing_output.segments)})

    manifest_payload = dict(existing_output.manifest)
    manifest_payload["overview"] = overview_row.to_json()
    manifest_outputs = manifest_payload.setdefault("outputs", {})
    manifest_outputs["workbook"] = str(actual_workbook_path)
    manifest_outputs["progress_json"] = str(progress_path)
    manifest_payload["timings_seconds"] = {
        **_coerce_timings_map(existing_output.manifest.get("timings_seconds")),
        **dict(progress_tracker.timings),
    }
    write_json(existing_output.output_dir / "manifest.json", manifest_payload)

    segments_payload = _read_json_payload(existing_output.output_dir / "segments.json")
    segments_payload["segments"] = [segment.to_json() for segment in existing_output.segments]
    segments_payload["overview"] = overview_row.to_json()
    write_json(existing_output.output_dir / "segments.json", segments_payload)

    return ProcessedItem(
        sequence_no=existing_output.sequence_no,
        source_mp4=source_asset,
        output_dir=existing_output.output_dir,
        workbook_path=actual_workbook_path,
        review_page_path=Path(str(manifest_outputs.get("review_html"))) if manifest_outputs.get("review_html") else None,
        segments=existing_output.segments,
        overview_row=overview_row,
        timings=dict(progress_tracker.timings),
    )


def process_batch_overview(
    output_root: Path,
    config: PipelineConfig,
    template_path: Path | None = None,
    workbook_output: Path | None = None,
) -> list[ProcessedItem]:
    discovered_outputs = discover_generated_outputs(output_root)
    output_root.mkdir(parents=True, exist_ok=True)
    batch_progress_path = output_root / "batch_progress.json"
    batch_started_at = perf_counter()
    item_progress: dict[str, dict[str, Any]] = {}

    def write_batch_progress(current_item: str | None = None, current_stage: str | None = None, status: str = "running") -> None:
        write_json(
            batch_progress_path,
            {
                "status": status,
                "output_root": str(output_root),
                "total_items": len(discovered_outputs),
                "completed_items": sum(1 for item in item_progress.values() if item.get("status") == "completed"),
                "current_item": current_item,
                "current_stage": current_stage,
                "elapsed_seconds": round(perf_counter() - batch_started_at, 3),
                "items": list(item_progress.values()),
            },
        )

    processed_items: list[ProcessedItem] = []
    for output_dir in discovered_outputs:
        existing_output = _load_existing_output(output_dir)
        item_key = str(output_dir)
        item_progress[item_key] = {
            "sequence_no": existing_output.sequence_no,
            "name": output_dir.name,
            "output_dir": str(output_dir),
            "status": "running",
            "current_stage": None,
            "timings_seconds": {},
        }
        write_batch_progress(current_item=item_key, status="running")

        def on_progress(stage: str, status: str, payload: dict[str, Any], item_key: str = item_key) -> None:
            item_progress[item_key]["status"] = "failed" if status == "failed" else "running"
            item_progress[item_key]["current_stage"] = payload.get("current_stage")
            item_progress[item_key]["timings_seconds"] = payload.get("timings_seconds", {})
            write_batch_progress(current_item=item_key, current_stage=payload.get("current_stage"), status="running")

        processed_items.append(
            process_single_overview(
                output_dir=output_dir,
                config=config,
                template_path=template_path,
                workbook_output=output_dir / "dubbing.result.xlsx",
                progress_callback=on_progress,
            )
        )
        item_progress[item_key]["status"] = "completed"
        item_progress[item_key]["current_stage"] = None
        item_progress[item_key]["timings_seconds"] = processed_items[-1].timings or {}
        write_batch_progress(current_item=item_key, status="running")

    actual_workbook_output = workbook_output or (output_root / "dubbing.result.xlsx")
    all_rows = []
    overview_rows: list[OverviewRow] = []
    for item in processed_items:
        _populate_segment_translations(item.segments, config)
        all_rows.extend(segments_to_rows(item.segments))
        if item.overview_row is not None:
            overview_rows.append(item.overview_row)
    export_workbook(
        output_path=actual_workbook_output,
        rows=all_rows,
        overview_rows=overview_rows,
        template_path=template_path,
    )

    batch_summary_path = output_root / "batch_summary.json"
    batch_summary_payload = _read_json_payload(batch_summary_path) if batch_summary_path.exists() else {}
    batch_summary_payload["output_root"] = str(output_root)
    batch_summary_payload["items"] = [
        {
            "sequence_no": item.sequence_no,
            "source_mp4": str(item.source_mp4),
            "output_dir": str(item.output_dir),
            "segment_count": len(item.segments),
            "overview": item.overview_row.to_json() if item.overview_row is not None else None,
            "timings_seconds": item.timings or {},
            "progress_json": str(item.output_dir / "progress.json"),
        }
        for item in processed_items
    ]
    batch_summary_payload["elapsed_seconds"] = round(perf_counter() - batch_started_at, 3)
    batch_summary_payload["progress_json"] = str(batch_progress_path)
    batch_summary_payload["workbook"] = str(actual_workbook_output)
    write_json(batch_summary_path, batch_summary_payload)
    write_batch_progress(status="completed")

    return processed_items


def _build_output_segments(
    sequence_no: int,
    subtitle_spans: list[SubtitleSpan],
    utterances: list[TranscriptUtterance],
    analysis_audio_metadata: Any,
    source_metadata: Any,
    config: PipelineConfig,
    non_silent_ranges: list[Any],
) -> tuple[list[Segment], dict[str, object], list[dict[str, object]]]:
    alignment_summary = {
        "alignment_mode": "asr-only",
        "matched_segments": 0,
        "unmatched_segments": 0,
        "total_subtitle_spans": len(subtitle_spans),
    }
    asr_word_json: list[dict[str, object]] = []

    segments: list[Segment]
    if subtitle_spans:
        segments, alignment_summary, asr_words = build_segments_from_subtitles(
            sequence_no=sequence_no,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=analysis_audio_metadata.duration_ms,
            video_duration_ms=source_metadata.duration_ms,
            segmentation_config=config.segmentation,
            subtitle_config=config.subtitle,
        )
        asr_word_json = [item.to_json() for item in asr_words]
        title_segment = _build_leading_title_segment(
            sequence_no=sequence_no,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=analysis_audio_metadata.duration_ms,
            video_duration_ms=source_metadata.duration_ms,
            config=config.segmentation,
            aligned_segments=segments,
        )
        if title_segment is not None:
            segments = [title_segment, *segments]
            _renumber_segments(segments)
            alignment_summary["prepended_title_segment"] = True
        if not segments and config.subtitle.allow_asr_fallback:
            segments = build_segments(
                sequence_no=sequence_no,
                utterances=utterances,
                non_silent_ranges=non_silent_ranges,
                audio_duration_ms=analysis_audio_metadata.duration_ms,
                video_duration_ms=source_metadata.duration_ms,
                config=config.segmentation,
            )
            alignment_summary["alignment_mode"] = "asr-only-fallback"
    else:
        if not config.subtitle.allow_asr_fallback:
            raise RuntimeError("No subtitle spans were detected and ASR fallback is disabled.")
        segments = build_segments(
            sequence_no=sequence_no,
            utterances=utterances,
            non_silent_ranges=non_silent_ranges,
            audio_duration_ms=analysis_audio_metadata.duration_ms,
            video_duration_ms=source_metadata.duration_ms,
            config=config.segmentation,
        )
        alignment_summary["alignment_mode"] = "asr-only-fallback"

    _normalize_segment_order(segments)
    return segments, alignment_summary, asr_word_json


def _write_output_artifacts(
    subtitle_spans_path: Path,
    segments_path: Path,
    segments_csv_path: Path,
    review_page_path: Path,
    workbook_output: Path | None,
    template_path: Path | None,
    sequence_no: int,
    source_asset: Path,
    resolved_srt_path: Path | None,
    audio_path: Path | None,
    asr_provider: str,
    subtitle_spans: list[SubtitleSpan],
    alignment_summary: dict[str, object],
    silence_ranges: list[Any],
    non_silent_ranges: list[Any],
    asr_word_json: list[dict[str, object]],
    utterances: list[TranscriptUtterance],
    segments: list[Segment],
    segment_rows: list[tuple[int, int, str, str, str]],
    overview_row: OverviewRow | None,
    write_workbook: bool = True,
    write_review_page: bool = True,
    write_csv: bool = True,
) -> Path | None:
    write_json(
        subtitle_spans_path,
        {
            "sequence_no": sequence_no,
            "source_mp4": str(source_asset),
            "source_srt": str(resolved_srt_path) if resolved_srt_path is not None else None,
            "subtitle_spans": [item.to_json() for item in subtitle_spans],
        },
    )
    write_json(
        segments_path,
        {
            "sequence_no": sequence_no,
            "source_mp4": str(source_asset),
            "audio_mp3": str(audio_path.resolve()) if audio_path is not None else None,
            "asr": {
                "provider": asr_provider,
            },
            "subtitle": {
                "spans_path": str(subtitle_spans_path),
                "source": "srt" if resolved_srt_path is not None else "none",
                "source_path": str(resolved_srt_path) if resolved_srt_path is not None else None,
                "spans": [item.to_json() for item in subtitle_spans],
            },
            "alignment_summary": alignment_summary,
            "silence_ranges": [item.to_json() for item in silence_ranges],
            "non_silent_ranges": [item.to_json() for item in non_silent_ranges],
            "asr_words": asr_word_json,
            "utterances": [item.to_json() for item in utterances],
            "segments": [item.to_json() for item in segments],
            "overview": overview_row.to_json() if overview_row is not None else None,
        },
    )
    if write_csv:
        export_csv(output_path=segments_csv_path, rows=segment_rows)
    if write_review_page:
        export_review_page(
            output_path=review_page_path,
            video_path="02.mp4",
            segments=segments,
            title=f"Sequence {sequence_no} review",
        )

    if workbook_output is None or overview_row is None or not write_workbook:
        return None
    return export_workbook(
        output_path=workbook_output,
        rows=segment_rows,
        overview_rows=[overview_row],
        template_path=template_path,
    )


def _build_manifest_payload(
    sequence_no: int,
    source_asset: Path,
    cover_path: Path,
    muted_video_path: Path,
    audio_path: Path,
    workbook_path: Path | None,
    manifest_path: Path,
    subtitle_spans_path: Path,
    segments_path: Path,
    segments_csv_path: Path,
    review_page_path: Path,
    progress_path: Path,
    asr_provider: str,
    resolved_srt_path: Path | None,
    subtitle_spans: list[SubtitleSpan],
    alignment_summary: dict[str, object],
    source_metadata: Any,
    muted_metadata: Any,
    analysis_audio_metadata: Any,
    audio_metadata: Any | None,
    background_audio_result: BackgroundAudioResult,
    config: PipelineConfig,
    overview_row: OverviewRow | None,
    timings: dict[str, float],
) -> dict[str, Any]:
    return {
        "sequence_no": sequence_no,
        "source_mp4": str(source_asset),
        "cover_image": str(cover_path),
        "muted_video": str(muted_video_path),
        "audio_mp3": str(audio_path) if audio_path is not None else None,
        "audio_processing": {
            "analysis_audio_kind": "temporary-full-mix",
            "output_audio_kind": "bgm" if audio_path is not None else "none",
            "separation_method": config.audio.method if audio_path is not None and background_audio_result.source_path is None else None,
            "separation_model": config.audio.demucs_model if audio_path is not None and background_audio_result.source_path is None else None,
            "separation_device": config.audio.demucs_device if audio_path is not None and background_audio_result.source_path is None else None,
            "from_cache": background_audio_result.from_cache,
            "cache_path": str(background_audio_result.cache_path) if background_audio_result.cache_path is not None else None,
            "provided_source_path": str(background_audio_result.source_path) if background_audio_result.source_path is not None else None,
        },
        "asr": {
            "provider": asr_provider,
        },
        "outputs": {
            "manifest_json": str(manifest_path),
            "subtitle_spans_json": str(subtitle_spans_path),
            "segments_json": str(segments_path),
            "segments_csv": str(segments_csv_path),
            "review_html": str(review_page_path),
            "workbook": str(workbook_path) if workbook_path is not None else None,
            "progress_json": str(progress_path),
        },
        "subtitle": {
            "detected_span_count": len(subtitle_spans),
            "source": "srt" if resolved_srt_path is not None else "none",
            "source_path": str(resolved_srt_path) if resolved_srt_path is not None else None,
            "alignment_mode": alignment_summary["alignment_mode"],
        },
        "media": {
            "source": source_metadata.to_json(),
            "muted_video": muted_metadata.to_json(),
            "analysis_audio": {
                **analysis_audio_metadata.to_json(),
                "path": None,
            },
            "audio": audio_metadata.to_json() if audio_metadata is not None else None,
        },
        "overview": overview_row.to_json() if overview_row is not None else None,
        "timings_seconds": timings,
    }


def discover_generated_outputs(output_root: Path) -> list[Path]:
    if not output_root.exists():
        raise FileNotFoundError(f"Output root not found: {output_root}")

    batch_summary_path = output_root / "batch_summary.json"
    if batch_summary_path.exists():
        batch_summary_payload = _read_json_payload(batch_summary_path)
        items = batch_summary_payload.get("items")
        if isinstance(items, list):
            discovered_from_summary = []
            for item in items:
                output_dir_value = item.get("output_dir") if isinstance(item, dict) else None
                if not output_dir_value:
                    continue
                output_dir = Path(str(output_dir_value))
                if (output_dir / "manifest.json").exists() and (output_dir / "segments.json").exists():
                    discovered_from_summary.append(output_dir)
            if discovered_from_summary:
                return discovered_from_summary

    discovered_outputs = sorted(
        (
            path.parent
            for path in output_root.rglob("manifest.json")
            if (path.parent / "segments.json").exists()
        ),
        key=lambda item: natural_sort_key(str(item.relative_to(output_root))),
    )
    if not discovered_outputs:
        raise FileNotFoundError(f"No generated outputs with manifest.json and segments.json were found under {output_root}.")
    return discovered_outputs


def _load_existing_output(output_dir: Path) -> ExistingOutputItem:
    manifest_path = output_dir / "manifest.json"
    segments_path = output_dir / "segments.json"
    if not manifest_path.exists():
        raise FileNotFoundError(f"manifest.json not found under {output_dir}.")
    if not segments_path.exists():
        raise FileNotFoundError(f"segments.json not found under {output_dir}.")

    manifest_payload = _read_json_payload(manifest_path)
    segments_payload = _read_json_payload(segments_path)
    segment_entries = segments_payload.get("segments")
    if not isinstance(segment_entries, list):
        raise ValueError(f"segments.json in {output_dir} is missing the segments array.")

    subtitle_entries: list[dict[str, Any]] = []
    subtitle_spans_path = output_dir / "subtitle_spans.json"
    if subtitle_spans_path.exists():
        subtitle_payload = _read_json_payload(subtitle_spans_path)
        subtitle_value = subtitle_payload.get("subtitle_spans")
        if isinstance(subtitle_value, list):
            subtitle_entries = [item for item in subtitle_value if isinstance(item, dict)]
    if not subtitle_entries:
        subtitle_value = segments_payload.get("subtitle", {}).get("spans")
        if isinstance(subtitle_value, list):
            subtitle_entries = [item for item in subtitle_value if isinstance(item, dict)]

    source_mp4_value = manifest_payload.get("source_mp4") or segments_payload.get("source_mp4") or str(output_dir / "02.mp4")
    return ExistingOutputItem(
        sequence_no=int(manifest_payload.get("sequence_no") or segments_payload.get("sequence_no") or 1),
        output_dir=output_dir,
        source_mp4=Path(str(source_mp4_value)),
        subtitle_spans=[SubtitleSpan.from_json(item) for item in subtitle_entries],
        segments=[Segment.from_json(item) for item in segment_entries if isinstance(item, dict)],
        manifest=manifest_payload,
    )


def _read_json_payload(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _load_completed_batch_output_dirs(batch_progress_path: Path) -> set[str]:
    if not batch_progress_path.exists():
        return set()

    payload = _read_json_payload(batch_progress_path)
    items = payload.get("items")
    if not isinstance(items, list):
        return set()

    completed_output_dirs: set[str] = set()
    for item in items:
        if not isinstance(item, dict):
            continue
        if item.get("status") != "completed":
            continue
        output_dir_value = item.get("output_dir")
        if output_dir_value:
            completed_output_dirs.add(str(output_dir_value))
    return completed_output_dirs


def _discover_completed_batch_output_dirs(
    output_root: Path,
    discovered_inputs: list[BatchInputItem],
    final_output: str,
) -> set[str]:
    completed_output_dirs: set[str] = set()
    for sequence_no, batch_item in enumerate(discovered_inputs, start=1):
        output_dir = _resolve_final_batch_output_dir(
            output_root=output_root,
            relative_dir=batch_item.relative_dir,
            sequence_no=sequence_no,
            final_output=final_output,
        )
        if _try_load_completed_batch_item(output_dir) is not None:
            completed_output_dirs.add(str(output_dir))
    return completed_output_dirs


def _try_load_completed_batch_item(output_dir: Path) -> ProcessedItem | None:
    try:
        existing_output = _load_existing_output(output_dir)
    except (FileNotFoundError, ValueError, json.JSONDecodeError):
        return None

    manifest_outputs = existing_output.manifest.get("outputs")
    review_page_path = None
    workbook_path = None
    if isinstance(manifest_outputs, dict):
        review_page_path = _resolve_existing_optional_path(existing_output.output_dir, manifest_outputs.get("review_html"))
        workbook_path = _resolve_existing_optional_path(existing_output.output_dir, manifest_outputs.get("workbook"))

    overview_payload = existing_output.manifest.get("overview")
    overview_row = OverviewRow.from_json(overview_payload) if isinstance(overview_payload, dict) else None
    source_asset = _resolve_existing_asset_path(existing_output.output_dir, existing_output.manifest, "source_mp4", "02.mp4")
    timings = _coerce_timings_map(existing_output.manifest.get("timings_seconds"))
    return ProcessedItem(
        sequence_no=existing_output.sequence_no,
        source_mp4=source_asset,
        output_dir=existing_output.output_dir,
        workbook_path=workbook_path,
        review_page_path=review_page_path,
        segments=existing_output.segments,
        overview_row=overview_row,
        timings=timings or None,
    )


def _resolve_existing_asset_path(output_dir: Path, manifest: dict[str, Any], manifest_key: str, default_name: str) -> Path:
    value = manifest.get(manifest_key)
    if value:
        candidate = Path(str(value))
        if candidate.is_absolute():
            return candidate
        return output_dir / candidate
    return output_dir / default_name


def _resolve_existing_optional_asset_path(output_dir: Path, manifest: dict[str, Any], manifest_key: str, default_name: str) -> Path | None:
    if manifest_key in manifest:
        value = manifest.get(manifest_key)
        if not value:
            return None
        candidate = Path(str(value))
        if candidate.is_absolute():
            return candidate
        return output_dir / candidate

    fallback_path = output_dir / default_name
    if fallback_path.exists():
        return fallback_path
    return None


def _resolve_existing_optional_path(output_dir: Path, value: object | None) -> Path | None:
    if not value:
        return None
    candidate = Path(str(value))
    if not candidate.is_absolute():
        candidate = output_dir / candidate
    if candidate.exists():
        return candidate
    return None


def _resolve_existing_workbook_path(output_dir: Path, manifest: dict[str, Any]) -> Path:
    outputs = manifest.get("outputs")
    if isinstance(outputs, dict) and outputs.get("workbook"):
        workbook_path = Path(str(outputs["workbook"]))
        if workbook_path.is_absolute():
            return workbook_path
        return output_dir / workbook_path
    return output_dir / "dubbing.result.xlsx"


def _resolve_existing_title(existing_output: ExistingOutputItem) -> str:
    overview_payload = existing_output.manifest.get("overview")
    if isinstance(overview_payload, dict):
        existing_title = str(overview_payload.get("video_title") or overview_payload.get("movie_name") or "").strip()
        if existing_title:
            return existing_title
    derived_from_dir = _derive_display_title(Path(existing_output.output_dir.name))
    if derived_from_dir:
        return derived_from_dir
    return _derive_display_title(existing_output.source_mp4)


def _coerce_timings_map(payload: Any) -> dict[str, float]:
    if not isinstance(payload, dict):
        return {}
    timings: dict[str, float] = {}
    for key, value in payload.items():
        try:
            timings[str(key)] = float(value)
        except (TypeError, ValueError):
            continue
    return timings


def _build_audio_cache_key_material(
    source_mp4: Path,
    export_source_video: bool,
    config: PipelineConfig,
) -> str:
    stats = source_mp4.stat()
    key_parts = [
        str(source_mp4.resolve()),
        str(stats.st_size),
        str(stats.st_mtime_ns),
    ]
    if export_source_video:
        key_parts.extend(
            [
                str(config.video.audio_bitrate_kbps),
                str(config.video.audio_sample_rate_hz),
                str(config.video.audio_channels),
                str(config.video.audio_bit_depth),
            ]
        )
    return "|".join(key_parts)


def _derive_display_title(source_path: Path) -> str:
    title = source_path.stem.replace("_", " ").strip()
    title = re.sub(r"^(?:\d+|[Pp]\d+)\s+", "", title)
    parts = title.split()
    if parts and re.fullmatch(r"(?:\d+[A-Za-z]+|[A-Za-z]+\d+)", parts[0]):
        parts = parts[1:]
    normalized_title = " ".join(parts).strip()
    return normalized_title or title


def _summary_text_blocks(subtitle_spans: list[SubtitleSpan], segments: list[Segment]) -> list[str]:
    if subtitle_spans:
        return [item.text for item in subtitle_spans]
    return [segment.text for segment in segments]


def _build_overview_row(
    sequence_no: int,
    title: str,
    source_asset: Path,
    muted_video_path: Path,
    audio_path: Path | None,
    cover_path: Path,
    video_description: str,
    subtitle_spans: list[SubtitleSpan],
    segments: list[Segment],
    config: PipelineConfig,
) -> OverviewRow:
    return OverviewRow(
        education_stage=config.overview.education_stage,
        subject=config.overview.subject,
        sequence_no=sequence_no,
        movie_name=title,
        video_title=title,
        muted_video=muted_video_path.name,
        full_video=source_asset.name,
        background_audio=audio_path.name if audio_path is not None else "",
        cover_image=cover_path.name,
        video_description=video_description,
        difficulty=config.overview.difficulty or _estimate_difficulty(subtitle_spans, segments),
        dialogue_audio=config.overview.dialogue_audio,
        topic=config.overview.topic,
        source=config.overview.source,
    )


def _copy_existing_background_audio(source_mp3: Path, output_path: Path) -> BackgroundAudioResult:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    if source_mp3.resolve() != output_path.resolve():
        shutil.copy2(source_mp3, output_path)
    return BackgroundAudioResult(path=output_path, from_cache=False, cache_path=None, source_path=source_mp3)


def _estimate_difficulty(subtitle_spans: list[SubtitleSpan], segments: list[Segment]) -> str:
    text_blocks = [span.text.strip() for span in subtitle_spans if span.text.strip()]
    if not text_blocks:
        text_blocks = [segment.text.strip() for segment in segments if segment.text.strip()]
    if not text_blocks:
        return "1"

    words: list[str] = []
    word_counts: list[int] = []
    clause_markers = 0
    for text in text_blocks:
        matched_words = [match.group(0).lower() for match in WORD_TOKEN_PATTERN.finditer(text)]
        if not matched_words:
            continue
        words.extend(matched_words)
        word_counts.append(len(matched_words))
        clause_markers += len(CLAUSE_MARKER_PATTERN.findall(text))

    if not words or not word_counts:
        return "1"

    total_words = len(words)
    avg_words_per_sentence = total_words / len(word_counts)
    max_words_per_sentence = max(word_counts)
    avg_word_length = sum(len(word) for word in words) / total_words
    long_word_ratio = sum(1 for word in words if len(word) >= 8) / total_words
    very_long_word_ratio = sum(1 for word in words if len(word) >= 11) / total_words
    lexical_diversity = len(set(words)) / total_words
    lexical_diversity_weight = min(1.0, total_words / 50)
    clause_density = clause_markers / len(word_counts)

    score = 1.0
    score += min(1.8, max(0.0, (avg_words_per_sentence - 5.0) / 6.0) * 1.8)
    score += min(1.0, max(0.0, (max_words_per_sentence - 9.0) / 8.0) * 1.0)
    score += min(0.8, max(0.0, (avg_word_length - 5.0) / 2.0) * 0.8)
    score += min(1.0, max(0.0, (long_word_ratio - 0.08) / 0.17) * 1.0)
    score += min(0.7, max(0.0, (very_long_word_ratio - 0.02) / 0.08) * 0.7)
    score += min(1.0, max(0.0, (clause_density - 0.25) / 1.25) * 1.0)
    score += min(0.7, max(0.0, (lexical_diversity - 0.55) / 0.25) * 0.7 * lexical_diversity_weight)

    return str(max(1, min(5, int(round(score)))))
