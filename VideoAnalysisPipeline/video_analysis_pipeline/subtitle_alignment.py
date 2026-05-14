from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from statistics import fmean, median

from video_analysis_pipeline.config import SegmentationConfig, SubtitleConfig
from video_analysis_pipeline.models import Segment, SubtitleSpan, TranscriptUtterance, WordTiming
from video_analysis_pipeline.subtitle_ocr import normalize_subtitle_text

_TARGETED_REPAIR_FLAGS = {
    "alignment_risk",
    "edge_word_low_confidence",
    "word_duration_outlier",
}


@dataclass(slots=True)
class AsrWordRef:
    global_index: int
    utterance_index: int
    word_index: int
    text: str
    normalized_text: str
    start_ms: int
    end_ms: int
    confidence: float | None = None

    def to_json(self) -> dict[str, object]:
        return {
            "global_index": self.global_index,
            "utterance_index": self.utterance_index,
            "word_index": self.word_index,
            "text": self.text,
            "normalized_text": self.normalized_text,
            "start_ms": self.start_ms,
            "end_ms": self.end_ms,
            "confidence": self.confidence,
        }


def flatten_asr_words(utterances: list[TranscriptUtterance]) -> list[AsrWordRef]:
    words: list[AsrWordRef] = []
    global_index = 0

    for utterance_index, utterance in enumerate(utterances):
        word_items: list[WordTiming]
        if utterance.words:
            word_items = utterance.words
        else:
            word_items = [WordTiming(text=utterance.text, start_ms=utterance.start_ms, end_ms=utterance.end_ms)]

        for word_index, word in enumerate(word_items):
            normalized_text = normalize_subtitle_text(word.text)
            if not normalized_text:
                continue
            words.append(
                AsrWordRef(
                    global_index=global_index,
                    utterance_index=utterance_index,
                    word_index=word_index,
                    text=word.text.strip(),
                    normalized_text=normalized_text,
                    start_ms=word.start_ms,
                    end_ms=word.end_ms,
                    confidence=word.confidence,
                )
            )
            global_index += 1

    return words


def build_segments_from_subtitles(
    sequence_no: int,
    subtitle_spans: list[SubtitleSpan],
    utterances: list[TranscriptUtterance],
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> tuple[list[Segment], dict[str, object], list[AsrWordRef]]:
    subtitle_config.validate()
    asr_words = flatten_asr_words(utterances)
    if not subtitle_spans or not asr_words:
        return [], {"matched_segments": 0, "unmatched_segments": 0}, asr_words

    segments: list[Segment] = []
    matched_count = 0
    unmatched_count = 0
    cursor_index = 0

    for segment_no, subtitle_span in enumerate(subtitle_spans, start=1):
        default_text_source = _default_text_source(subtitle_span)
        alignment = _align_subtitle_span(
            subtitle_span=subtitle_span,
            asr_words=asr_words,
            cursor_index=cursor_index,
            audio_duration_ms=audio_duration_ms,
            video_duration_ms=video_duration_ms,
            segmentation_config=segmentation_config,
            subtitle_config=subtitle_config,
        )

        if alignment is None:
            unmatched_count += 1
            segment = Segment(
                sequence_no=sequence_no,
                segment_no=segment_no,
                text=subtitle_span.text,
                start_ms=subtitle_span.start_ms,
                end_ms=max(subtitle_span.start_ms + 1, subtitle_span.end_ms),
                confidence=None,
                text_source=default_text_source,
                alignment_confidence=None,
                ocr_confidence=subtitle_span.confidence,
                source_subtitle_index=segment_no - 1,
                quality_flags=["alignment_failed", "subtitle_timing_fallback"],
            )
            segments.append(segment)
            continue

        start_word_index, end_word_index, score, text_score = alignment
        cursor_index = end_word_index + 1
        matched_count += 1
        matched_words = asr_words[start_word_index : end_word_index + 1]
        matched_utterance_indexes = sorted({word.utterance_index for word in matched_words})
        candidate_asr_text = _join_asr_words(matched_words)
        segment_text, text_source, text_quality_flags = _choose_segment_text(
            subtitle_text=subtitle_span.text,
            subtitle_normalized=subtitle_span.normalized_text,
            asr_text=candidate_asr_text,
            min_text_score=subtitle_config.alignment_min_text_score,
            subtitle_source=default_text_source,
        )

        start_audio_ms = max(0, matched_words[0].start_ms - segmentation_config.lead_in_ms)
        end_audio_ms = min(audio_duration_ms, matched_words[-1].end_ms + segmentation_config.tail_out_ms)
        start_video_ms = _scale_audio_to_video(start_audio_ms, audio_duration_ms, video_duration_ms)
        end_video_ms = _scale_audio_to_video(end_audio_ms, audio_duration_ms, video_duration_ms)

        quality_flags = ["subtitle_aligned", *text_quality_flags]
        if text_score < subtitle_config.alignment_min_text_score + 5:
            quality_flags.append("low_alignment_confidence")
        if abs(start_video_ms - subtitle_span.start_ms) > subtitle_config.alignment_max_shift_ms:
            quality_flags.append("subtitle_start_shifted")
        if abs(end_video_ms - subtitle_span.end_ms) > subtitle_config.alignment_max_shift_ms:
            quality_flags.append("subtitle_end_shifted")

        confidences = [word.confidence for word in matched_words if word.confidence is not None]
        segment = Segment(
            sequence_no=sequence_no,
            segment_no=segment_no,
            text=segment_text,
            start_ms=start_video_ms,
            end_ms=max(start_video_ms + 1, end_video_ms),
            source_utterance_indexes=matched_utterance_indexes,
            confidence=float(fmean(confidences)) if confidences else None,
            text_source=text_source,
            alignment_confidence=score / 100.0,
            ocr_confidence=subtitle_span.confidence,
            source_subtitle_index=segment_no - 1,
            source_word_range=[matched_words[0].global_index, matched_words[-1].global_index],
            quality_flags=quality_flags,
        )
        segments.append(segment)

    _remove_segment_overlap(segments)
    _apply_segment_quality_checks(segments, subtitle_spans, asr_words)
    _repair_flagged_segments(segments, subtitle_spans)
    return (
        segments,
        {
            "matched_segments": matched_count,
            "unmatched_segments": unmatched_count,
            "total_subtitle_spans": len(subtitle_spans),
            "alignment_mode": _alignment_mode(subtitle_spans),
        },
        asr_words,
    )


def _align_subtitle_span(
    subtitle_span: SubtitleSpan,
    asr_words: list[AsrWordRef],
    cursor_index: int,
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> tuple[int, int, float, float] | None:
    query_text = subtitle_span.normalized_text
    query_tokens = [token for token in query_text.split() if token]
    if not query_tokens:
        return None

    expected_audio_start = _scale_video_to_audio(subtitle_span.start_ms, audio_duration_ms, video_duration_ms)
    expected_audio_end = _scale_video_to_audio(subtitle_span.end_ms, audio_duration_ms, video_duration_ms)
    search_window_ms = subtitle_config.alignment_search_window_ms

    candidate_indexes = [
        word.global_index
        for word in asr_words
        if word.global_index >= cursor_index
        and word.end_ms >= expected_audio_start - search_window_ms
        and word.start_ms <= expected_audio_end + search_window_ms
    ]

    if not candidate_indexes:
        candidate_start = min(cursor_index, max(0, len(asr_words) - 1))
        candidate_end = min(len(asr_words) - 1, candidate_start + subtitle_config.alignment_max_words_ahead)
    else:
        candidate_start = candidate_indexes[0]
        candidate_end = min(
            len(asr_words) - 1,
            max(candidate_indexes[-1], candidate_start + len(query_tokens) + 2),
        )

    candidate_end = min(candidate_end, candidate_start + subtitle_config.alignment_max_words_ahead)
    min_window_length = max(1, len(query_tokens) - 2)
    max_window_length = max(min_window_length, len(query_tokens) + 4)

    best: tuple[int, int, float, float] | None = None

    for start_index in range(candidate_start, candidate_end + 1):
        for window_length in range(min_window_length, max_window_length + 1):
            end_index = start_index + window_length - 1
            if end_index > candidate_end:
                break

            candidate_text = " ".join(word.normalized_text for word in asr_words[start_index : end_index + 1])
            text_score = _text_score(query_text, candidate_text)
            if best is not None and text_score + 5 < best[3]:
                continue

            time_score = _time_score(
                expected_audio_start=expected_audio_start,
                expected_audio_end=expected_audio_end,
                candidate_start=asr_words[start_index].start_ms,
                candidate_end=asr_words[end_index].end_ms,
                search_window_ms=search_window_ms,
            )
            total_score = text_score * 0.75 + time_score * 0.25

            if best is None or total_score > best[2]:
                best = (start_index, end_index, total_score, text_score)

    if best is None or best[3] < subtitle_config.alignment_min_text_score:
        return None
    return best


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


def _time_score(
    expected_audio_start: int,
    expected_audio_end: int,
    candidate_start: int,
    candidate_end: int,
    search_window_ms: int,
) -> float:
    delta = abs(expected_audio_start - candidate_start) + abs(expected_audio_end - candidate_end)
    penalty = min(100.0, (delta / max(1, search_window_ms * 2)) * 100.0)
    return 100.0 - penalty


def _text_score(expected_text: str, candidate_text: str) -> float:
    try:
        from rapidfuzz import fuzz
    except ImportError as exc:
        raise RuntimeError("rapidfuzz is required for OCR/ASR alignment.") from exc

    ratio = float(fuzz.ratio(expected_text, candidate_text))
    partial = float(fuzz.partial_ratio(expected_text, candidate_text))
    token_sort = float(fuzz.token_sort_ratio(expected_text, candidate_text))
    token_set = float(fuzz.token_set_ratio(expected_text, candidate_text))
    expected_len = len(expected_text.split())
    candidate_len = len(candidate_text.split())
    length_penalty = min(20.0, abs(expected_len - candidate_len) * 4.0)
    return max(0.0, ratio * 0.1 + partial * 0.2 + token_sort * 0.35 + token_set * 0.35 - length_penalty)


def _join_asr_words(words: list[AsrWordRef]) -> str:
    parts: list[str] = []
    for word in words:
        token = word.text.strip()
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


def _choose_segment_text(
    subtitle_text: str,
    subtitle_normalized: str,
    asr_text: str,
    min_text_score: float,
    subtitle_source: str,
) -> tuple[str, str, list[str]]:
    try:
        from rapidfuzz import fuzz
    except ImportError as exc:
        raise RuntimeError("rapidfuzz is required for OCR/ASR alignment.") from exc

    asr_normalized = normalize_subtitle_text(asr_text)
    ratio = float(fuzz.ratio(subtitle_normalized, asr_normalized))
    partial = float(fuzz.partial_ratio(subtitle_normalized, asr_normalized))
    token_sort = float(fuzz.token_sort_ratio(subtitle_normalized, asr_normalized))
    token_set = float(fuzz.token_set_ratio(subtitle_normalized, asr_normalized))

    if subtitle_normalized == asr_normalized:
        return subtitle_text, subtitle_source, []

    best_similarity = max(partial, token_sort, token_set)
    if best_similarity >= min_text_score and _should_prefer_asr_text(
        subtitle_normalized=subtitle_normalized,
        asr_normalized=asr_normalized,
        ratio=ratio,
        token_sort=token_sort,
        token_set=token_set,
    ):
        return asr_text, "hybrid", ["subtitle_text_normalized_from_asr"]
    return subtitle_text, subtitle_source, []


def _should_prefer_asr_text(
    subtitle_normalized: str,
    asr_normalized: str,
    ratio: float,
    token_sort: float,
    token_set: float,
) -> bool:
    subtitle_tokens = [token for token in subtitle_normalized.split() if token]
    asr_tokens = [token for token in asr_normalized.split() if token]
    if not subtitle_tokens or not asr_tokens:
        return False

    if Counter(subtitle_tokens) == Counter(asr_tokens) and subtitle_tokens != asr_tokens:
        return True

    subtitle_counter = Counter(subtitle_tokens)
    asr_counter = Counter(asr_tokens)
    extra_ocr_tokens: list[str] = []
    for token, count in subtitle_counter.items():
        extra_count = count - asr_counter.get(token, 0)
        if extra_count > 0:
            extra_ocr_tokens.extend([token] * extra_count)

    if any(len(token) == 1 for token in extra_ocr_tokens):
        return True
    if extra_ocr_tokens and token_set >= 95:
        return True
    if token_sort >= 92 and ratio < 98:
        return True
    return False


def _remove_segment_overlap(segments: list[Segment]) -> None:
    for index in range(1, len(segments)):
        previous = segments[index - 1]
        current = segments[index]
        if current.start_ms >= previous.end_ms:
            continue

        midpoint = (previous.end_ms + current.start_ms) // 2
        previous.end_ms = max(previous.start_ms + 1, midpoint)
        current.start_ms = min(current.end_ms - 1, midpoint + 1)


def _apply_segment_quality_checks(
    segments: list[Segment],
    subtitle_spans: list[SubtitleSpan],
    asr_words: list[AsrWordRef],
) -> None:
    repeated_duration_medians: dict[str, float] = {}
    durations_by_text: dict[str, list[int]] = {}

    for segment in segments:
        if segment.source_subtitle_index is None:
            continue
        subtitle_span = subtitle_spans[segment.source_subtitle_index]
        durations_by_text.setdefault(subtitle_span.normalized_text, []).append(segment.duration_ms)

    for normalized_text, durations in durations_by_text.items():
        if len(durations) < 2:
            continue
        repeated_duration_medians[normalized_text] = float(median(durations))

    for segment in segments:
        if segment.alignment_confidence is not None and segment.alignment_confidence < 0.88:
            _append_quality_flag(segment, "alignment_risk")

        if segment.source_subtitle_index is not None:
            subtitle_span = subtitle_spans[segment.source_subtitle_index]
            median_duration = repeated_duration_medians.get(subtitle_span.normalized_text)
            if median_duration is not None and (
                segment.duration_ms < median_duration * 0.65
                or segment.duration_ms > median_duration * 1.5
            ):
                _append_quality_flag(segment, "repeated_phrase_duration_outlier")

        if segment.source_word_range is None:
            continue

        start_word_index, end_word_index = segment.source_word_range
        matched_words = asr_words[start_word_index : end_word_index + 1]
        if not matched_words:
            continue

        edge_confidences = [matched_words[0].confidence, matched_words[-1].confidence]
        if any(confidence is not None and confidence < 0.55 for confidence in edge_confidences):
            _append_quality_flag(segment, "edge_word_low_confidence")

        word_durations = [max(1, word.end_ms - word.start_ms) for word in matched_words]
        median_word_duration = float(median(word_durations))
        if any(duration > max(1200, median_word_duration * 2.75) for duration in word_durations):
            _append_quality_flag(segment, "word_duration_outlier")

    for previous_segment, current_segment in zip(segments, segments[1:]):
        if previous_segment.source_subtitle_index is None or current_segment.source_subtitle_index is None:
            continue

        previous_subtitle = subtitle_spans[previous_segment.source_subtitle_index]
        current_subtitle = subtitle_spans[current_segment.source_subtitle_index]
        expected_gap_ms = current_subtitle.start_ms - previous_subtitle.end_ms
        actual_gap_ms = current_segment.start_ms - previous_segment.end_ms
        if abs(actual_gap_ms - expected_gap_ms) > 800:
            _append_quality_flag(previous_segment, "neighbor_boundary_drift")
            _append_quality_flag(current_segment, "neighbor_boundary_drift")


def _append_quality_flag(segment: Segment, flag: str) -> None:
    if flag not in segment.quality_flags:
        segment.quality_flags.append(flag)


def _repair_flagged_segments(
    segments: list[Segment],
    subtitle_spans: list[SubtitleSpan],
) -> None:
    for index, segment in enumerate(segments):
        if segment.source_subtitle_index is None:
            continue
        if not _should_target_segment_for_repair(segment):
            continue

        subtitle_span = subtitle_spans[segment.source_subtitle_index]
        if subtitle_span.source != "srt":
            continue

        minimum_start = 0 if index == 0 else segments[index - 1].end_ms + 1
        maximum_end = subtitle_span.end_ms
        if index < len(segments) - 1:
            maximum_end = min(maximum_end, segments[index + 1].start_ms - 1)
        if maximum_end <= minimum_start:
            continue

        updated_start = segment.start_ms
        updated_end = segment.end_ms
        changed = False

        if abs(segment.start_ms - subtitle_span.start_ms) >= 250:
            updated_start = min(max(subtitle_span.start_ms, minimum_start), maximum_end - 1)
            changed = True
        if abs(segment.end_ms - subtitle_span.end_ms) >= 250:
            updated_end = max(min(subtitle_span.end_ms, maximum_end), updated_start + 1)
            changed = True

        if not changed:
            continue

        segment.start_ms = updated_start
        segment.end_ms = max(updated_start + 1, updated_end)
        _append_quality_flag(segment, "targeted_subtitle_timing_repair")


def _should_target_segment_for_repair(segment: Segment) -> bool:
    return any(flag in _TARGETED_REPAIR_FLAGS for flag in segment.quality_flags)


def _default_text_source(subtitle_span: SubtitleSpan) -> str:
    if subtitle_span.source == "srt":
        return "srt"
    return "ocr"


def _alignment_mode(subtitle_spans: list[SubtitleSpan]) -> str:
    if subtitle_spans and all(span.source == "srt" for span in subtitle_spans):
        return "srt-asr-forced-alignment"
    return "ocr-asr-hybrid"
