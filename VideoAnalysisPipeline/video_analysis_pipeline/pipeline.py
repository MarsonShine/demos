from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from statistics import fmean
import tempfile
from typing import Protocol

from video_analysis_pipeline.asr import create_transcriber, normalize_asr_provider
from video_analysis_pipeline.config import PipelineConfig, SegmentationConfig
from video_analysis_pipeline.exporter import (
    export_csv,
    export_review_page,
    export_workbook,
    segments_to_rows,
    write_json,
)
from video_analysis_pipeline.media import (
    copy_source_video,
    detect_silence,
    extract_audio_mp3,
    extract_background_audio_mp3,
    extract_cover,
    extract_muted_video,
    probe_media,
)
from video_analysis_pipeline.models import Segment, SubtitleSpan, TranscriptUtterance
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


@dataclass(slots=True)
class BatchInputItem:
    source_mp4: Path
    source_srt: Path
    relative_dir: Path


def process_single_video(
    source_mp4: Path,
    output_dir: Path,
    sequence_no: int,
    config: PipelineConfig,
    source_srt: Path | None = None,
    template_path: Path | None = None,
    workbook_output: Path | None = None,
    transcriber: Transcriber | None = None,
) -> ProcessedItem:
    if not source_mp4.exists():
        raise FileNotFoundError(f"Source video not found: {source_mp4}")

    active_transcriber = transcriber or create_transcriber(config)
    asr_provider = normalize_asr_provider(config.asr.provider)

    output_dir.mkdir(parents=True, exist_ok=True)

    source_asset = copy_source_video(source_mp4, output_dir / "02.mp4")
    cover_path = extract_cover(source_asset, output_dir / "01.jpg")
    muted_video_path = extract_muted_video(source_asset, output_dir / "01.mp4")
    with tempfile.TemporaryDirectory(prefix="video-analysis-audio-") as temp_dir:
        analysis_audio_path = extract_audio_mp3(source_asset, Path(temp_dir) / "analysis.mp3")
        audio_path = extract_background_audio_mp3(analysis_audio_path, output_dir / "03.mp3", config.audio)
        analysis_audio_metadata = probe_media(analysis_audio_path)
        audio_metadata = probe_media(audio_path)
        silence_ranges, non_silent_ranges = detect_silence(
            audio_path=analysis_audio_path,
            total_duration_ms=analysis_audio_metadata.duration_ms,
            silence_threshold_db=config.segmentation.silence_threshold_db,
            min_silence_duration_ms=config.segmentation.min_silence_duration_ms,
        )
        utterances = active_transcriber.transcribe(analysis_audio_path)

    source_metadata = probe_media(source_asset)
    muted_metadata = probe_media(muted_video_path)

    resolved_srt_path = discover_srt_path(source_mp4=source_mp4, source_srt=source_srt)
    subtitle_spans = parse_srt_file(resolved_srt_path) if resolved_srt_path is not None else []
    subtitle_spans_path = output_dir / "subtitle_spans.json"

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

    manifest_path = output_dir / "manifest.json"
    segments_path = output_dir / "segments.json"
    segments_csv_path = output_dir / "segments.csv"
    review_page_path = output_dir / "review.html"
    segment_rows = segments_to_rows(segments)
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
        manifest_path,
        {
            "sequence_no": sequence_no,
            "source_mp4": str(source_asset),
            "cover_image": str(cover_path),
            "muted_video": str(muted_video_path),
            "audio_mp3": str(audio_path),
            "audio_processing": {
                "analysis_audio_kind": "temporary-full-mix",
                "output_audio_kind": "bgm",
                "separation_method": config.audio.method,
                "separation_model": config.audio.demucs_model,
                "separation_device": config.audio.demucs_device,
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
                "workbook": str(workbook_output) if workbook_output is not None else None,
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
                "audio": audio_metadata.to_json(),
            },
        },
    )
    write_json(
        segments_path,
        {
            "sequence_no": sequence_no,
            "source_mp4": str(source_asset),
            "audio_mp3": str(audio_path),
            "audio_processing": {
                "analysis_audio_kind": "temporary-full-mix",
                "output_audio_kind": "bgm",
                "separation_method": config.audio.method,
                "separation_model": config.audio.demucs_model,
                "separation_device": config.audio.demucs_device,
            },
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
        },
    )
    export_csv(
        output_path=segments_csv_path,
        rows=segment_rows,
    )
    export_review_page(
        output_path=review_page_path,
        video_path="02.mp4",
        segments=segments,
        title=f"Sequence {sequence_no} review",
    )

    actual_workbook_path: Path | None = None
    if workbook_output is not None:
        actual_workbook_path = export_workbook(
            output_path=workbook_output,
            rows=segment_rows,
            template_path=template_path,
        )

    return ProcessedItem(
        sequence_no=sequence_no,
        source_mp4=source_asset,
        output_dir=output_dir,
        workbook_path=actual_workbook_path,
        review_page_path=review_page_path,
        segments=segments,
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

        if not mp4_candidates and not srt_candidates:
            continue

        if len(mp4_candidates) != 1 or len(srt_candidates) != 1:
            raise RuntimeError(
                f"Expected exactly one MP4 and one SRT in {directory}, "
                f"found {len(mp4_candidates)} MP4 and {len(srt_candidates)} SRT."
            )

        source_mp4 = mp4_candidates[0]
        source_srt = srt_candidates[0]
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


def process_batch(
    input_root: Path,
    output_root: Path,
    source_name: str | None,
    config: PipelineConfig,
    srt_name: str | None = None,
    template_path: Path | None = None,
    workbook_output: Path | None = None,
) -> list[ProcessedItem]:
    discovered_inputs = discover_batch_inputs(
        input_root=input_root,
        source_name=source_name,
        srt_name=srt_name,
    )

    processed_items: list[ProcessedItem] = []
    for sequence_no, batch_item in enumerate(discovered_inputs, start=1):
        output_dir = _resolve_batch_output_dir(output_root, batch_item.relative_dir)
        processed_items.append(
            process_single_video(
                source_mp4=batch_item.source_mp4,
                output_dir=output_dir,
                sequence_no=sequence_no,
                config=config,
                source_srt=batch_item.source_srt,
                template_path=None,
                workbook_output=None,
            )
        )

    if workbook_output is not None:
        all_rows = []
        for item in processed_items:
            all_rows.extend(segments_to_rows(item.segments))
        export_workbook(
            output_path=workbook_output,
            rows=all_rows,
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
        },
    )

    return processed_items
