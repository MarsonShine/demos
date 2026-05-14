from __future__ import annotations

from statistics import fmean

from video_analysis_pipeline.config import SegmentationConfig
from video_analysis_pipeline.models import Segment, TimeRange, TranscriptUtterance


def build_segments(
    sequence_no: int,
    utterances: list[TranscriptUtterance],
    non_silent_ranges: list[TimeRange],
    audio_duration_ms: int,
    video_duration_ms: int,
    config: SegmentationConfig,
) -> list[Segment]:
    if not utterances:
        raise ValueError("No utterances were provided for segmentation.")

    sorted_utterances = sorted(utterances, key=lambda item: (item.start_ms, item.end_ms))
    matched_range_indexes = [
        _find_best_non_silent_range_index(item, non_silent_ranges, config.max_boundary_shift_ms)
        for item in sorted_utterances
    ]

    grouped_indexes: list[list[int]] = []
    current_group: list[int] = [0]

    for index in range(1, len(sorted_utterances)):
        previous = sorted_utterances[index - 1]
        current = sorted_utterances[index]
        previous_range = matched_range_indexes[index - 1]
        current_range = matched_range_indexes[index]
        same_voice_chunk = previous_range is not None and previous_range == current_range
        gap_ms = max(0, current.start_ms - previous.end_ms)
        candidate_duration = current.end_ms - sorted_utterances[current_group[0]].start_ms

        should_merge = False
        if (
            same_voice_chunk
            and gap_ms <= config.merge_gap_ms
            and candidate_duration <= config.max_segment_duration_ms
            and (
                not _looks_like_complete_sentence(previous.text)
                or not _looks_like_complete_sentence(current.text)
            )
        ):
            should_merge = True
        elif (
            gap_ms <= max(150, config.merge_gap_ms // 2)
            and candidate_duration <= config.max_segment_duration_ms
            and (
                previous.word_count <= 2
                or current.word_count <= 2
                or previous.duration_ms <= config.min_segment_duration_ms // 2
                or current.duration_ms <= config.min_segment_duration_ms // 2
            )
        ):
            should_merge = True

        if should_merge:
            current_group.append(index)
            continue

        grouped_indexes.append(current_group)
        current_group = [index]

    grouped_indexes.append(current_group)

    segments: list[Segment] = []
    for segment_no, utterance_indexes in enumerate(grouped_indexes, start=1):
        segment = _build_segment(
            sequence_no=sequence_no,
            segment_no=segment_no,
            utterances=sorted_utterances,
            utterance_indexes=utterance_indexes,
            matched_range_indexes=matched_range_indexes,
            non_silent_ranges=non_silent_ranges,
            audio_duration_ms=audio_duration_ms,
            config=config,
        )
        segments.append(segment)

    _remove_segment_overlap(segments)
    _scale_segments_to_video_timeline(segments, audio_duration_ms, video_duration_ms)
    _remove_segment_overlap(segments)
    _apply_quality_flags(segments, config)
    return segments


def _build_segment(
    sequence_no: int,
    segment_no: int,
    utterances: list[TranscriptUtterance],
    utterance_indexes: list[int],
    matched_range_indexes: list[int | None],
    non_silent_ranges: list[TimeRange],
    audio_duration_ms: int,
    config: SegmentationConfig,
) -> Segment:
    first_utterance = utterances[utterance_indexes[0]]
    last_utterance = utterances[utterance_indexes[-1]]
    joined_text = " ".join(utterances[index].text.strip() for index in utterance_indexes if utterances[index].text.strip())
    start_ms = max(0, first_utterance.start_ms - config.lead_in_ms)
    end_ms = min(audio_duration_ms, last_utterance.end_ms + config.tail_out_ms)
    range_indexes = [matched_range_indexes[index] for index in utterance_indexes if matched_range_indexes[index] is not None]
    quality_flags: list[str] = []

    if range_indexes:
        first_range = non_silent_ranges[min(range_indexes)]
        last_range = non_silent_ranges[max(range_indexes)]
        start_ms = max(first_range.start_ms, start_ms)
        end_ms = min(last_range.end_ms, end_ms)

        if end_ms - start_ms < config.min_segment_duration_ms:
            deficit = config.min_segment_duration_ms - (end_ms - start_ms)
            expand_left = deficit // 2
            expand_right = deficit - expand_left
            start_ms = max(first_range.start_ms, start_ms - expand_left)
            end_ms = min(last_range.end_ms, end_ms + expand_right)
    else:
        quality_flags.append("vad_unmatched")

    confidences = [utterances[index].confidence for index in utterance_indexes if utterances[index].confidence is not None]
    confidence = float(fmean(confidences)) if confidences else None

    return Segment(
        sequence_no=sequence_no,
        segment_no=segment_no,
        text=joined_text,
        start_ms=start_ms,
        end_ms=max(start_ms + 1, end_ms),
        source_utterance_indexes=utterance_indexes,
        confidence=confidence,
        quality_flags=quality_flags,
    )


def _find_best_non_silent_range_index(
    utterance: TranscriptUtterance,
    non_silent_ranges: list[TimeRange],
    max_boundary_shift_ms: int,
) -> int | None:
    utterance_range = TimeRange(start_ms=utterance.start_ms, end_ms=utterance.end_ms)
    best_index: int | None = None
    best_overlap = 0

    for index, candidate in enumerate(non_silent_ranges):
        overlap = utterance_range.overlap_duration(candidate)
        if overlap > best_overlap:
            best_overlap = overlap
            best_index = index

    if best_index is not None:
        return best_index

    best_distance: int | None = None
    nearest_index: int | None = None
    for index, candidate in enumerate(non_silent_ranges):
        if utterance.end_ms <= candidate.start_ms:
            distance = candidate.start_ms - utterance.end_ms
        elif candidate.end_ms <= utterance.start_ms:
            distance = utterance.start_ms - candidate.end_ms
        else:
            distance = 0

        if best_distance is None or distance < best_distance:
            best_distance = distance
            nearest_index = index

    if best_distance is not None and best_distance <= max_boundary_shift_ms:
        return nearest_index
    return None


def _remove_segment_overlap(segments: list[Segment]) -> None:
    for index in range(1, len(segments)):
        previous = segments[index - 1]
        current = segments[index]
        if current.start_ms >= previous.end_ms:
            continue

        midpoint = (previous.end_ms + current.start_ms) // 2
        previous.end_ms = max(previous.start_ms + 1, midpoint)
        current.start_ms = min(current.end_ms - 1, midpoint + 1)


def _scale_segments_to_video_timeline(
    segments: list[Segment],
    audio_duration_ms: int,
    video_duration_ms: int,
) -> None:
    if audio_duration_ms <= 0 or video_duration_ms <= 0:
        return

    ratio = video_duration_ms / audio_duration_ms
    if abs(video_duration_ms - audio_duration_ms) < 20:
        ratio = 1.0

    for segment in segments:
        if ratio != 1.0:
            segment.start_ms = int(round(segment.start_ms * ratio))
            segment.end_ms = int(round(segment.end_ms * ratio))
            if "duration_scaled" not in segment.quality_flags:
                segment.quality_flags.append("duration_scaled")

        segment.start_ms = max(0, min(segment.start_ms, video_duration_ms))
        segment.end_ms = max(segment.start_ms + 1, min(segment.end_ms, video_duration_ms))


def _apply_quality_flags(segments: list[Segment], config: SegmentationConfig) -> None:
    for segment in segments:
        if segment.duration_ms > config.max_segment_duration_ms and "long_segment" not in segment.quality_flags:
            segment.quality_flags.append("long_segment")
        if segment.duration_ms < max(250, config.min_segment_duration_ms // 2) and "short_segment" not in segment.quality_flags:
            segment.quality_flags.append("short_segment")
        if segment.confidence is not None and segment.confidence < 0.60 and "low_confidence" not in segment.quality_flags:
            segment.quality_flags.append("low_confidence")


def _looks_like_complete_sentence(text: str) -> bool:
    stripped = text.strip()
    if not stripped:
        return False
    return stripped.endswith((".", "!", "?", "。", "！", "？"))
