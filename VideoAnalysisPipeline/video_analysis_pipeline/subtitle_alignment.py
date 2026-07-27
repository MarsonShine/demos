from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
import re
from statistics import fmean, median

from video_analysis_pipeline.config import SegmentationConfig, SubtitleConfig
from video_analysis_pipeline.models import Segment, SubtitleSpan, TimeRange, TranscriptUtterance, WordTiming
from video_analysis_pipeline.subtitle_ocr import normalize_subtitle_text

_TARGETED_REPAIR_FLAGS = {
    "alignment_risk",
    "edge_word_low_confidence",
    "word_duration_outlier",
    "low_alignment_confidence",
}
_MAX_TARGETED_REPAIR_SHIFT_MS = 700
_BOUNDARY_RELAXATION_THRESHOLD_MS = 120
_MAX_BOUNDARY_RELAXATION_MS = 900
_SRT_SILENCE_GAP_TOLERANCE_MS = 40
_EDGE_WORD_LOW_CONFIDENCE = 0.55
_ASR_STICKY_BOUNDARY_TOLERANCE_MS = 20


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


@dataclass(slots=True)
class AlignmentCandidate:
    start_word_index: int
    end_word_index: int
    alignment_score: float
    text_score: float
    time_score: float
    selection_score: float


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
    boundary_silence_ranges: list[TimeRange] | None = None,
    low_confidence_boundary_silence_ranges: list[TimeRange] | None = None,
) -> tuple[list[Segment], dict[str, object], list[AsrWordRef]]:
    subtitle_config.validate()
    asr_words = flatten_asr_words(utterances)
    if not subtitle_spans or not asr_words:
        return [], {"matched_segments": 0, "unmatched_segments": 0}, asr_words

    segments: list[Segment] = []
    matched_count = 0
    unmatched_count = 0
    alignments = _align_subtitle_spans(
        subtitle_spans=subtitle_spans,
        asr_words=asr_words,
        audio_duration_ms=audio_duration_ms,
        video_duration_ms=video_duration_ms,
        segmentation_config=segmentation_config,
        subtitle_config=subtitle_config,
    )

    for segment_no, (subtitle_span, alignment) in enumerate(zip(subtitle_spans, alignments), start=1):
        default_text_source = _default_text_source(subtitle_span)
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

        start_word_index = alignment.start_word_index
        end_word_index = alignment.end_word_index
        score = alignment.alignment_score
        text_score = alignment.text_score
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
    _repair_flagged_segments(segments, subtitle_spans, asr_words)
    _relax_srt_segment_boundaries(segments, subtitle_spans)
    repaired_srt_gap_boundaries = _repair_low_confidence_early_srt_boundaries(
        segments=segments,
        subtitle_spans=subtitle_spans,
        asr_words=asr_words,
        audio_duration_ms=audio_duration_ms,
        video_duration_ms=video_duration_ms,
        segmentation_config=segmentation_config,
        boundary_silence_ranges=low_confidence_boundary_silence_ranges or [],
    )
    repaired_silence_boundaries = _repair_srt_boundaries_with_silence(
        segments=segments,
        subtitle_spans=subtitle_spans,
        asr_words=asr_words,
        boundary_silence_ranges=boundary_silence_ranges or [],
        audio_duration_ms=audio_duration_ms,
        video_duration_ms=video_duration_ms,
        segmentation_config=segmentation_config,
    )
    _remove_segment_overlap(segments)
    _refresh_shift_flags(segments, subtitle_spans, subtitle_config)
    _clear_timing_quality_flags(segments)
    _apply_segment_quality_checks(segments, subtitle_spans, asr_words)
    return (
        segments,
        {
            "matched_segments": matched_count,
            "unmatched_segments": unmatched_count,
            "total_subtitle_spans": len(subtitle_spans),
            "alignment_mode": _alignment_mode(subtitle_spans),
            "srt_gap_validated_boundaries": repaired_srt_gap_boundaries,
            "silence_validated_boundaries": repaired_silence_boundaries,
        },
        asr_words,
    )


def _align_subtitle_spans(
    subtitle_spans: list[SubtitleSpan],
    asr_words: list[AsrWordRef],
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> list[AlignmentCandidate | None]:
    if subtitle_spans and all(span.source == "srt" for span in subtitle_spans):
        return _align_srt_subtitle_spans(
            subtitle_spans=subtitle_spans,
            asr_words=asr_words,
            audio_duration_ms=audio_duration_ms,
            video_duration_ms=video_duration_ms,
            segmentation_config=segmentation_config,
            subtitle_config=subtitle_config,
        )

    alignments: list[AlignmentCandidate | None] = []
    cursor_index = 0
    for subtitle_span in subtitle_spans:
        alignment = _align_subtitle_span_greedily(
            subtitle_span=subtitle_span,
            asr_words=asr_words,
            cursor_index=cursor_index,
            audio_duration_ms=audio_duration_ms,
            video_duration_ms=video_duration_ms,
            segmentation_config=segmentation_config,
            subtitle_config=subtitle_config,
        )
        alignments.append(alignment)
        if alignment is not None:
            cursor_index = alignment.end_word_index + 1
    return alignments


def _align_srt_subtitle_spans(
    subtitle_spans: list[SubtitleSpan],
    asr_words: list[AsrWordRef],
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> list[AlignmentCandidate | None]:
    alignments: list[AlignmentCandidate | None] = [None] * len(subtitle_spans)
    candidates_by_index = [
        _collect_alignment_candidates(
            subtitle_span=subtitle_span,
            asr_words=asr_words,
            cursor_index=0,
            audio_duration_ms=audio_duration_ms,
            video_duration_ms=video_duration_ms,
            segmentation_config=segmentation_config,
            subtitle_config=subtitle_config,
        )
        for subtitle_span in subtitle_spans
    ]

    run_start: int | None = None
    for index, candidates in enumerate(candidates_by_index):
        if candidates:
            if run_start is None:
                run_start = index
            continue

        if run_start is not None:
            _resolve_srt_alignment_run(
                alignments=alignments,
                subtitle_spans=subtitle_spans,
                candidates_by_index=candidates_by_index,
                run_start=run_start,
                run_end=index - 1,
                asr_words=asr_words,
                audio_duration_ms=audio_duration_ms,
                video_duration_ms=video_duration_ms,
                segmentation_config=segmentation_config,
                subtitle_config=subtitle_config,
            )
            run_start = None

    if run_start is not None:
        _resolve_srt_alignment_run(
            alignments=alignments,
            subtitle_spans=subtitle_spans,
            candidates_by_index=candidates_by_index,
            run_start=run_start,
            run_end=len(subtitle_spans) - 1,
            asr_words=asr_words,
            audio_duration_ms=audio_duration_ms,
            video_duration_ms=video_duration_ms,
            segmentation_config=segmentation_config,
            subtitle_config=subtitle_config,
        )

    return alignments


def _resolve_srt_alignment_run(
    alignments: list[AlignmentCandidate | None],
    subtitle_spans: list[SubtitleSpan],
    candidates_by_index: list[list[AlignmentCandidate]],
    run_start: int,
    run_end: int,
    asr_words: list[AsrWordRef],
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> None:
    run_candidates = candidates_by_index[run_start : run_end + 1]
    resolved_run = _select_best_candidate_path(run_candidates)
    if resolved_run is None:
        cursor_index = 0
        if run_start > 0 and alignments[run_start - 1] is not None:
            cursor_index = alignments[run_start - 1].end_word_index + 1
        for relative_index, subtitle_span in enumerate(subtitle_spans[run_start : run_end + 1], start=run_start):
            alignment = _align_subtitle_span_greedily(
                subtitle_span=subtitle_span,
                asr_words=asr_words,
                cursor_index=cursor_index,
                audio_duration_ms=audio_duration_ms,
                video_duration_ms=video_duration_ms,
                segmentation_config=segmentation_config,
                subtitle_config=subtitle_config,
            )
            alignments[relative_index] = alignment
            if alignment is not None:
                cursor_index = alignment.end_word_index + 1
        return

    for offset, alignment in enumerate(resolved_run, start=run_start):
        alignments[offset] = alignment


def _select_best_candidate_path(
    candidates_by_span: list[list[AlignmentCandidate]],
) -> list[AlignmentCandidate] | None:
    if not candidates_by_span:
        return []

    score_rows: list[list[float]] = [
        [float("-inf")] * len(candidates)
        for candidates in candidates_by_span
    ]
    previous_rows: list[list[int | None]] = [
        [None] * len(candidates)
        for candidates in candidates_by_span
    ]

    for candidate_index, candidate in enumerate(candidates_by_span[0]):
        score_rows[0][candidate_index] = candidate.selection_score

    for span_index in range(1, len(candidates_by_span)):
        current_candidates = candidates_by_span[span_index]
        previous_candidates = candidates_by_span[span_index - 1]
        for current_index, current_candidate in enumerate(current_candidates):
            best_score = float("-inf")
            best_previous_index: int | None = None
            for previous_index, previous_candidate in enumerate(previous_candidates):
                previous_score = score_rows[span_index - 1][previous_index]
                if previous_score == float("-inf"):
                    continue
                if previous_candidate.end_word_index >= current_candidate.start_word_index:
                    continue
                combined_score = previous_score + current_candidate.selection_score
                if combined_score > best_score:
                    best_score = combined_score
                    best_previous_index = previous_index
            if best_previous_index is None:
                continue
            score_rows[span_index][current_index] = best_score
            previous_rows[span_index][current_index] = best_previous_index

    final_scores = score_rows[-1]
    best_final_score = max(final_scores, default=float("-inf"))
    if best_final_score == float("-inf"):
        return None

    best_final_index = final_scores.index(best_final_score)
    path: list[AlignmentCandidate] = []
    span_index = len(candidates_by_span) - 1
    current_index: int | None = best_final_index
    while span_index >= 0 and current_index is not None:
        path.append(candidates_by_span[span_index][current_index])
        current_index = previous_rows[span_index][current_index]
        span_index -= 1
    path.reverse()
    if len(path) != len(candidates_by_span):
        return None
    return path


def _align_subtitle_span_greedily(
    subtitle_span: SubtitleSpan,
    asr_words: list[AsrWordRef],
    cursor_index: int,
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> AlignmentCandidate | None:
    candidates = _collect_alignment_candidates(
        subtitle_span=subtitle_span,
        asr_words=asr_words,
        cursor_index=cursor_index,
        audio_duration_ms=audio_duration_ms,
        video_duration_ms=video_duration_ms,
        segmentation_config=segmentation_config,
        subtitle_config=subtitle_config,
    )
    if not candidates:
        return None
    return candidates[0]


def _collect_alignment_candidates(
    subtitle_span: SubtitleSpan,
    asr_words: list[AsrWordRef],
    cursor_index: int,
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    subtitle_config: SubtitleConfig,
) -> list[AlignmentCandidate]:
    query_text = subtitle_span.normalized_text
    query_tokens = [token for token in query_text.split() if token]
    if not query_tokens:
        return []

    expected_audio_start = _scale_video_to_audio(subtitle_span.start_ms, audio_duration_ms, video_duration_ms)
    expected_audio_end = _scale_video_to_audio(subtitle_span.end_ms, audio_duration_ms, video_duration_ms)
    search_window_ms = subtitle_config.alignment_search_window_ms
    backward_search_ms = search_window_ms
    forward_search_ms = search_window_ms
    candidate_backtrack_words = 0
    if subtitle_span.source == "srt":
        backward_search_ms += min(1_200, max(400, subtitle_span.duration_ms // 2))
        candidate_backtrack_words = 2 if len(query_tokens) <= 3 else 1

    candidate_indexes = [
        word.global_index
        for word in asr_words
        if word.global_index >= cursor_index
        and word.end_ms >= expected_audio_start - backward_search_ms
        and word.start_ms <= expected_audio_end + forward_search_ms
    ]

    if not candidate_indexes:
        candidate_start = min(cursor_index, max(0, len(asr_words) - 1))
        candidate_end = min(len(asr_words) - 1, candidate_start + subtitle_config.alignment_max_words_ahead)
    else:
        candidate_start = max(cursor_index, candidate_indexes[0] - candidate_backtrack_words)
        candidate_end = min(
            len(asr_words) - 1,
            max(candidate_indexes[-1], candidate_start + len(query_tokens) + 2),
        )

    candidate_end = min(candidate_end, candidate_start + subtitle_config.alignment_max_words_ahead)
    min_window_length = max(1, len(query_tokens) - 2)
    max_window_length = max(min_window_length, len(query_tokens) + 4)

    candidates: list[AlignmentCandidate] = []

    for start_index in range(candidate_start, candidate_end + 1):
        for window_length in range(min_window_length, max_window_length + 1):
            end_index = start_index + window_length - 1
            if end_index > candidate_end:
                break

            candidate_words = asr_words[start_index : end_index + 1]
            candidate_tokens = [word.normalized_text for word in candidate_words]
            candidate_text = " ".join(candidate_tokens)
            text_score = _text_score(query_text, candidate_text)
            if text_score < subtitle_config.alignment_min_text_score:
                continue

            time_score = _time_score(
                expected_audio_start=expected_audio_start,
                expected_audio_end=expected_audio_end,
                candidate_start=asr_words[start_index].start_ms,
                candidate_end=asr_words[end_index].end_ms,
                search_window_ms=search_window_ms,
            )
            alignment_score = text_score * 0.75 + time_score * 0.25
            selection_score = alignment_score
            if subtitle_span.source == "srt":
                selection_score += _srt_selection_bonus(
                    query_tokens=query_tokens,
                    candidate_tokens=candidate_tokens,
                )
            candidates.append(
                AlignmentCandidate(
                    start_word_index=start_index,
                    end_word_index=end_index,
                    alignment_score=alignment_score,
                    text_score=text_score,
                    time_score=time_score,
                    selection_score=selection_score,
                )
            )

    candidates.sort(
        key=lambda item: (
            item.selection_score,
            item.text_score,
            item.time_score,
            -item.start_word_index,
            -item.end_word_index,
        ),
        reverse=True,
    )
    return candidates[:12]


def _srt_selection_bonus(
    query_tokens: list[str],
    candidate_tokens: list[str],
) -> float:
    if not query_tokens or not candidate_tokens:
        return 0.0

    prefix_matches = _matching_prefix_token_count(query_tokens, candidate_tokens)
    suffix_matches = _matching_suffix_token_count(query_tokens, candidate_tokens)

    bonus = 0.0
    if query_tokens == candidate_tokens:
        bonus += 16.0

    bonus += min(6.0, prefix_matches * 3.0)
    bonus += min(6.0, suffix_matches * 3.0)

    if prefix_matches == 0:
        bonus -= 6.0
    if suffix_matches == 0:
        bonus -= 6.0

    return bonus


def _matching_prefix_token_count(left: list[str], right: list[str]) -> int:
    count = 0
    for left_token, right_token in zip(left, right):
        if _token_signature(left_token) != _token_signature(right_token):
            break
        count += 1
    return count


def _matching_suffix_token_count(left: list[str], right: list[str]) -> int:
    count = 0
    for left_token, right_token in zip(reversed(left), reversed(right)):
        if _token_signature(left_token) != _token_signature(right_token):
            break
        count += 1
    return count


def _token_signature(token: str) -> str:
    return re.sub(r"[^0-9a-z]+", "", token.casefold())


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
    if subtitle_source == "srt":
        return subtitle_text, subtitle_source, []

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


def _repair_low_confidence_early_srt_boundaries(
    segments: list[Segment],
    subtitle_spans: list[SubtitleSpan],
    asr_words: list[AsrWordRef],
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
    boundary_silence_ranges: list[TimeRange],
) -> int:
    repaired_count = 0
    for previous, current in zip(segments, segments[1:]):
        if previous.source_subtitle_index is None or current.source_subtitle_index is None:
            continue
        if previous.source_word_range is None or current.source_word_range is None:
            continue
        if current.source_word_range[0] != previous.source_word_range[1] + 1:
            continue

        previous_subtitle = subtitle_spans[previous.source_subtitle_index]
        current_subtitle = subtitle_spans[current.source_subtitle_index]
        if previous_subtitle.source != "srt" or current_subtitle.source != "srt":
            continue

        subtitle_gap_ms = current_subtitle.start_ms - previous_subtitle.end_ms
        if subtitle_gap_ms <= 0 or subtitle_gap_ms > segmentation_config.max_boundary_shift_ms:
            continue
        if current.start_ms - previous.end_ms > segmentation_config.merge_gap_ms:
            continue
        if previous.end_ms >= previous_subtitle.end_ms or current.start_ms >= current_subtitle.start_ms:
            continue

        last_word = asr_words[previous.source_word_range[1]]
        first_word = asr_words[current.source_word_range[0]]
        if first_word.confidence is None or first_word.confidence >= _EDGE_WORD_LOW_CONFIDENCE:
            continue
        if abs(first_word.start_ms - last_word.end_ms) > _ASR_STICKY_BOUNDARY_TOLERANCE_MS:
            continue

        first_word_start_ms = _scale_audio_to_video(first_word.start_ms, audio_duration_ms, video_duration_ms)
        early_start_ms = previous_subtitle.end_ms - first_word_start_ms
        if (
            early_start_ms < _BOUNDARY_RELAXATION_THRESHOLD_MS
            or early_start_ms > segmentation_config.max_boundary_shift_ms
        ):
            continue
        if previous_subtitle.end_ms <= previous.start_ms or current_subtitle.start_ms >= current.end_ms:
            continue

        candidates: list[tuple[int, int]] = []
        for silence in boundary_silence_ranges:
            silence_start_ms = _scale_audio_to_video(silence.start_ms, audio_duration_ms, video_duration_ms)
            silence_end_ms = _scale_audio_to_video(silence.end_ms, audio_duration_ms, video_duration_ms)
            if silence_start_ms < previous_subtitle.end_ms - _SRT_SILENCE_GAP_TOLERANCE_MS:
                continue
            if silence_end_ms > current_subtitle.start_ms + _SRT_SILENCE_GAP_TOLERANCE_MS:
                continue
            if silence_start_ms <= previous.start_ms or silence_end_ms >= current.end_ms:
                continue
            if silence_start_ms <= previous.end_ms or silence_end_ms <= current.start_ms:
                continue
            candidates.append((silence_start_ms, silence_end_ms))

        if not candidates:
            continue

        silence_start_ms, silence_end_ms = max(candidates, key=lambda item: (item[1], item[1] - item[0]))
        previous.end_ms = silence_start_ms
        current.start_ms = silence_end_ms
        _append_quality_flag(previous, "srt_gap_validated_end")
        _append_quality_flag(current, "srt_gap_validated_start")
        repaired_count += 1

    return repaired_count


def repair_leading_title_boundary(
    title_segment: Segment,
    first_aligned_segment: Segment,
    subtitle_spans: list[SubtitleSpan],
    asr_words: list[AsrWordRef],
    boundary_silence_ranges: list[TimeRange],
    audio_duration_ms: int,
    video_duration_ms: int,
) -> bool:
    if "title_segment_from_asr" not in title_segment.quality_flags:
        return False
    if title_segment.source_word_range is None or first_aligned_segment.source_word_range is None:
        return False
    if first_aligned_segment.source_subtitle_index is None:
        return False
    if first_aligned_segment.source_word_range[0] != title_segment.source_word_range[1] + 1:
        return False

    first_subtitle = subtitle_spans[first_aligned_segment.source_subtitle_index]
    if first_subtitle.source != "srt":
        return False

    last_title_word = asr_words[title_segment.source_word_range[1]]
    first_aligned_word = asr_words[first_aligned_segment.source_word_range[0]]
    if first_aligned_word.confidence is None or first_aligned_word.confidence >= _EDGE_WORD_LOW_CONFIDENCE:
        return False
    if abs(first_aligned_word.start_ms - last_title_word.end_ms) > _ASR_STICKY_BOUNDARY_TOLERANCE_MS:
        return False

    first_word_start_ms = _scale_audio_to_video(
        first_aligned_word.start_ms,
        audio_duration_ms,
        video_duration_ms,
    )
    if first_subtitle.start_ms - first_word_start_ms < _BOUNDARY_RELAXATION_THRESHOLD_MS:
        return False

    last_title_word_end_ms = _scale_audio_to_video(
        last_title_word.end_ms,
        audio_duration_ms,
        video_duration_ms,
    )
    candidates: list[tuple[int, int]] = []
    for silence in boundary_silence_ranges:
        silence_start_ms = _scale_audio_to_video(silence.start_ms, audio_duration_ms, video_duration_ms)
        silence_end_ms = _scale_audio_to_video(silence.end_ms, audio_duration_ms, video_duration_ms)
        if silence_start_ms < last_title_word_end_ms - _SRT_SILENCE_GAP_TOLERANCE_MS:
            continue
        if silence_start_ms > first_subtitle.start_ms + _SRT_SILENCE_GAP_TOLERANCE_MS:
            continue
        if silence_end_ms < first_subtitle.start_ms - _SRT_SILENCE_GAP_TOLERANCE_MS:
            continue
        if silence_start_ms <= title_segment.end_ms or silence_end_ms <= first_aligned_segment.start_ms:
            continue
        if silence_end_ms >= first_aligned_segment.end_ms:
            continue
        candidates.append((silence_start_ms, silence_end_ms))

    if not candidates:
        return False

    silence_start_ms, silence_end_ms = max(
        candidates,
        key=lambda item: (item[1] - item[0], -abs(((item[0] + item[1]) // 2) - first_subtitle.start_ms)),
    )
    title_segment.end_ms = silence_start_ms
    first_aligned_segment.start_ms = silence_end_ms
    _append_quality_flag(title_segment, "title_boundary_silence_end")
    _append_quality_flag(first_aligned_segment, "title_boundary_silence_start")
    return True


def _repair_srt_boundaries_with_silence(
    segments: list[Segment],
    subtitle_spans: list[SubtitleSpan],
    asr_words: list[AsrWordRef],
    boundary_silence_ranges: list[TimeRange],
    audio_duration_ms: int,
    video_duration_ms: int,
    segmentation_config: SegmentationConfig,
) -> int:
    repaired_count = 0
    for previous, current in zip(segments, segments[1:]):
        if previous.source_subtitle_index is None or current.source_subtitle_index is None:
            continue
        if previous.source_word_range is None or current.source_word_range is None:
            continue

        previous_subtitle = subtitle_spans[previous.source_subtitle_index]
        current_subtitle = subtitle_spans[current.source_subtitle_index]
        if previous_subtitle.source != "srt" or current_subtitle.source != "srt":
            continue
        if current.start_ms - previous.end_ms > segmentation_config.merge_gap_ms:
            continue

        first_word = asr_words[current.source_word_range[0]]
        candidates: list[tuple[int, int]] = []
        for silence in boundary_silence_ranges:
            if first_word.start_ms >= silence.start_ms or first_word.end_ms <= silence.end_ms:
                continue

            silence_start_ms = _scale_audio_to_video(silence.start_ms, audio_duration_ms, video_duration_ms)
            silence_end_ms = _scale_audio_to_video(silence.end_ms, audio_duration_ms, video_duration_ms)
            if current.start_ms >= silence_start_ms:
                continue
            if silence_start_ms < previous_subtitle.end_ms - _SRT_SILENCE_GAP_TOLERANCE_MS:
                continue
            if silence_end_ms > current_subtitle.start_ms + _SRT_SILENCE_GAP_TOLERANCE_MS:
                continue
            if abs(silence_start_ms - previous.end_ms) > segmentation_config.max_boundary_shift_ms:
                continue
            if silence_start_ms <= previous.start_ms or silence_end_ms >= current.end_ms:
                continue
            candidates.append((silence_start_ms, silence_end_ms))

        if not candidates:
            continue

        silence_start_ms, silence_end_ms = max(candidates, key=lambda item: (item[1], item[1] - item[0]))
        previous.end_ms = max(previous.start_ms + 1, silence_start_ms)
        current.start_ms = min(current.end_ms - 1, max(previous.end_ms + 1, silence_end_ms))
        _append_quality_flag(previous, "silence_validated_end")
        _append_quality_flag(current, "silence_validated_start")
        repaired_count += 1

    return repaired_count


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
        if any(confidence is not None and confidence < _EDGE_WORD_LOW_CONFIDENCE for confidence in edge_confidences):
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
    asr_words: list[AsrWordRef],
) -> None:
    for index in range(len(segments) - 1, -1, -1):
        segment = segments[index]
        if segment.source_subtitle_index is None:
            continue
        if not _should_target_segment_for_repair(segment):
            continue

        subtitle_span = subtitle_spans[segment.source_subtitle_index]
        if subtitle_span.source != "srt":
            continue

        segment_minimum_start = 0
        if index > 0:
            segment_minimum_start = segments[index - 1].end_ms + 1
            previous_segment = segments[index - 1]
            previous_subtitle_index = previous_segment.source_subtitle_index
            subtitle_minimum_start = 0
            if previous_subtitle_index is not None:
                previous_subtitle = subtitle_spans[previous_subtitle_index]
                if previous_subtitle.source == "srt":
                    subtitle_minimum_start = previous_subtitle.end_ms + 1
            else:
                subtitle_minimum_start = 0
        else:
            subtitle_minimum_start = 0

        segment_maximum_end = subtitle_span.end_ms
        subtitle_maximum_end = subtitle_span.end_ms
        if index < len(segments) - 1:
            next_segment = segments[index + 1]
            segment_maximum_end = min(segment_maximum_end, next_segment.start_ms - 1)
            next_subtitle_index = next_segment.source_subtitle_index
            if next_subtitle_index is not None:
                next_subtitle = subtitle_spans[next_subtitle_index]
                if next_subtitle.source == "srt":
                    subtitle_maximum_end = min(subtitle_maximum_end, next_subtitle.start_ms - 1)
        if segment_maximum_end <= segment_minimum_start and subtitle_maximum_end <= subtitle_minimum_start:
            continue

        updated_start, updated_end = _choose_repaired_srt_timing(
            segment=segment,
            subtitle_span=subtitle_span,
            asr_words=asr_words,
            segment_minimum_start=segment_minimum_start,
            segment_maximum_end=segment_maximum_end,
            subtitle_minimum_start=subtitle_minimum_start,
            subtitle_maximum_end=subtitle_maximum_end,
        )
        if updated_start == segment.start_ms and updated_end == segment.end_ms:
            continue

        segment.start_ms = updated_start
        segment.end_ms = max(updated_start + 1, updated_end)
        _append_quality_flag(segment, "targeted_subtitle_timing_repair")


def _choose_repaired_srt_timing(
    segment: Segment,
    subtitle_span: SubtitleSpan,
    asr_words: list[AsrWordRef],
    segment_minimum_start: int,
    segment_maximum_end: int,
    subtitle_minimum_start: int,
    subtitle_maximum_end: int,
) -> tuple[int, int]:
    start_shift = abs(segment.start_ms - subtitle_span.start_ms)
    end_shift = abs(segment.end_ms - subtitle_span.end_ms)
    large_shift = start_shift > _MAX_TARGETED_REPAIR_SHIFT_MS or end_shift > _MAX_TARGETED_REPAIR_SHIFT_MS
    subtitle_duration = subtitle_span.duration_ms
    prefix_matches, suffix_matches, required_matches = _segment_boundary_match_quality(
        segment,
        subtitle_span,
        asr_words,
    )
    strong_boundary_match = prefix_matches >= required_matches and suffix_matches >= required_matches

    should_fallback_to_subtitle = (
        "alignment_failed" in segment.quality_flags
        or "low_alignment_confidence" in segment.quality_flags
        or not strong_boundary_match
        or (
            large_shift
            and subtitle_duration <= 2_500
            and _should_target_segment_for_repair(segment)
        )
        or (
            "neighbor_boundary_drift" in segment.quality_flags
            and large_shift
            and subtitle_duration <= 2_200
        )
        or (
            "alignment_risk" in segment.quality_flags
            and large_shift
            and subtitle_duration <= 1_800
        )
    )

    if should_fallback_to_subtitle:
        updated_start = max(subtitle_minimum_start, subtitle_span.start_ms)
        updated_end = min(subtitle_maximum_end, subtitle_span.end_ms)
        if updated_end <= updated_start:
            updated_end = min(subtitle_maximum_end, max(updated_start + 1, segment.end_ms))
        return updated_start, max(updated_start + 1, updated_end)

    updated_start = segment.start_ms
    updated_end = segment.end_ms

    if (
        segment.start_ms > subtitle_span.start_ms + _BOUNDARY_RELAXATION_THRESHOLD_MS
        and segment.start_ms - subtitle_span.start_ms <= _MAX_BOUNDARY_RELAXATION_MS
    ):
        updated_start = max(segment_minimum_start, subtitle_span.start_ms)

    if (
        subtitle_span.end_ms > segment.end_ms + _BOUNDARY_RELAXATION_THRESHOLD_MS
        and subtitle_span.end_ms - segment.end_ms <= _MAX_BOUNDARY_RELAXATION_MS
    ):
        updated_end = min(segment_maximum_end, subtitle_span.end_ms)

    if updated_end <= updated_start:
        updated_end = min(segment_maximum_end, max(updated_start + 1, segment.end_ms))
        updated_start = min(updated_start, updated_end - 1)

    return updated_start, max(updated_start + 1, updated_end)


def _relax_srt_segment_boundaries(
    segments: list[Segment],
    subtitle_spans: list[SubtitleSpan],
) -> None:
    for index, segment in enumerate(segments):
        if segment.source_subtitle_index is None:
            continue

        subtitle_span = subtitle_spans[segment.source_subtitle_index]
        if subtitle_span.source != "srt":
            continue

        minimum_start = 0 if index == 0 else segments[index - 1].end_ms + 1
        maximum_end = subtitle_span.end_ms if index == len(segments) - 1 else segments[index + 1].start_ms - 1
        if maximum_end <= minimum_start:
            continue

        updated_start = segment.start_ms
        updated_end = segment.end_ms

        if (
            segment.start_ms > subtitle_span.start_ms + _BOUNDARY_RELAXATION_THRESHOLD_MS
            and segment.start_ms - subtitle_span.start_ms <= _MAX_BOUNDARY_RELAXATION_MS
        ):
            updated_start = max(minimum_start, subtitle_span.start_ms)

        if (
            subtitle_span.end_ms > segment.end_ms + _BOUNDARY_RELAXATION_THRESHOLD_MS
            and subtitle_span.end_ms - segment.end_ms <= _MAX_BOUNDARY_RELAXATION_MS
        ):
            updated_end = min(maximum_end, subtitle_span.end_ms)

        if updated_end <= updated_start:
            updated_end = min(maximum_end, max(updated_start + 1, segment.end_ms))
            updated_start = min(updated_start, updated_end - 1)

        segment.start_ms = updated_start
        segment.end_ms = max(updated_start + 1, updated_end)


def _segment_boundary_match_quality(
    segment: Segment,
    subtitle_span: SubtitleSpan,
    asr_words: list[AsrWordRef],
) -> tuple[int, int, int]:
    query_tokens = [token for token in subtitle_span.normalized_text.split() if token]
    segment_tokens: list[str] = []
    if segment.source_word_range is not None:
        start_word_index, end_word_index = segment.source_word_range
        matched_words = asr_words[start_word_index : end_word_index + 1]
        segment_tokens = [word.normalized_text for word in matched_words if word.normalized_text]
    if not segment_tokens:
        segment_tokens = [token for token in normalize_subtitle_text(segment.text).split() if token]
    if not query_tokens or not segment_tokens:
        return 0, 0, 1
    prefix_matches = _matching_prefix_token_count(query_tokens, segment_tokens)
    suffix_matches = _matching_suffix_token_count(query_tokens, segment_tokens)
    required_matches = min(2, len(query_tokens))
    return prefix_matches, suffix_matches, required_matches


def _refresh_shift_flags(
    segments: list[Segment],
    subtitle_spans: list[SubtitleSpan],
    subtitle_config: SubtitleConfig,
) -> None:
    for segment in segments:
        if segment.source_subtitle_index is None:
            continue
        subtitle_span = subtitle_spans[segment.source_subtitle_index]
        segment.quality_flags = [
            flag
            for flag in segment.quality_flags
            if flag not in {"subtitle_start_shifted", "subtitle_end_shifted"}
        ]
        if abs(segment.start_ms - subtitle_span.start_ms) > subtitle_config.alignment_max_shift_ms:
            _append_quality_flag(segment, "subtitle_start_shifted")
        if abs(segment.end_ms - subtitle_span.end_ms) > subtitle_config.alignment_max_shift_ms:
            _append_quality_flag(segment, "subtitle_end_shifted")


def _clear_timing_quality_flags(segments: list[Segment]) -> None:
    flags_to_clear = {"neighbor_boundary_drift", "repeated_phrase_duration_outlier"}
    for segment in segments:
        segment.quality_flags = [flag for flag in segment.quality_flags if flag not in flags_to_clear]


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
