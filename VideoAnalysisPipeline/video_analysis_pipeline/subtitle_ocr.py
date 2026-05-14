from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import re
from statistics import fmean
from tempfile import TemporaryDirectory

from video_analysis_pipeline.config import SubtitleConfig
from video_analysis_pipeline.media import run_command
from video_analysis_pipeline.models import SubtitleFrameSample, SubtitleSpan


@dataclass(slots=True)
class SubtitleRoiFrame:
    timestamp_ms: int
    image_array: object
    signature: object


@dataclass(slots=True)
class DetectedSubtitleBox:
    top: float
    bottom: float
    left: float
    right: float
    text: str
    score: float

    @property
    def center_y(self) -> float:
        return (self.top + self.bottom) / 2.0

    @property
    def height(self) -> float:
        return max(1.0, self.bottom - self.top)


_NUMBER_WORDS = {
    0: "zero",
    1: "one",
    2: "two",
    3: "three",
    4: "four",
    5: "five",
    6: "six",
    7: "seven",
    8: "eight",
    9: "nine",
    10: "ten",
    11: "eleven",
    12: "twelve",
    13: "thirteen",
    14: "fourteen",
    15: "fifteen",
    16: "sixteen",
    17: "seventeen",
    18: "eighteen",
    19: "nineteen",
    20: "twenty",
    30: "thirty",
    40: "forty",
    50: "fifty",
    60: "sixty",
    70: "seventy",
    80: "eighty",
    90: "ninety",
}


def normalize_subtitle_text(text: str) -> str:
    normalized = (
        text.replace("\n", " ")
        .replace("\r", " ")
        .replace("’", "'")
        .replace("‘", "'")
        .replace("“", '"')
        .replace("”", '"')
        .replace("—", "-")
    )
    normalized = re.sub(r"\bo\s*'?\s*clock\b", "o clock", normalized, flags=re.IGNORECASE)
    normalized = re.sub(r"\b\d+\b", lambda match: _number_to_words(match.group(0)), normalized)
    normalized = " ".join(normalized.split()).strip()
    if not normalized:
        return ""

    collapsed = []
    previous_space = False
    for char in normalized.lower():
        if char.isalnum() or char in {"'", " "}:
            if char == " ":
                if previous_space:
                    continue
                previous_space = True
            else:
                previous_space = False
            collapsed.append(char)
            continue

        if previous_space:
            continue
        collapsed.append(" ")
        previous_space = True

    normalized_tokens = []
    for token in "".join(collapsed).split():
        cleaned = token.strip("'")
        if cleaned:
            normalized_tokens.append(cleaned)
    return " ".join(normalized_tokens)


def _number_to_words(token: str) -> str:
    if not token.isdigit():
        return token

    value = int(token)
    if value in _NUMBER_WORDS:
        return _NUMBER_WORDS[value]
    if 21 <= value <= 99:
        tens = (value // 10) * 10
        units = value % 10
        if units == 0:
            return _NUMBER_WORDS.get(tens, token)
        tens_word = _NUMBER_WORDS.get(tens)
        units_word = _NUMBER_WORDS.get(units)
        if tens_word and units_word:
            return f"{tens_word} {units_word}"
    return token


def extract_subtitle_spans(video_path: Path, config: SubtitleConfig) -> list[SubtitleSpan]:
    config.validate()

    try:
        import numpy as np
        from PIL import Image, ImageFilter, ImageOps
        from rapidocr_onnxruntime import RapidOCR
    except ImportError as exc:
        raise RuntimeError(
            "OCR dependencies are not installed. "
            "Run the pipeline through run_pipeline.py so the isolated runtime installs them."
        ) from exc

    with TemporaryDirectory(prefix="video-pipeline-ocr-") as temp_dir:
        frame_dir = Path(temp_dir)
        frame_pattern = frame_dir / "frame_%06d.png"
        run_command(
            [
                "ffmpeg",
                "-y",
                "-hide_banner",
                "-loglevel",
                "error",
                "-i",
                str(video_path),
                "-vf",
                f"fps={config.sample_fps}",
                str(frame_pattern),
            ]
        )

        frame_paths = sorted(frame_dir.glob("frame_*.png"))
        if not frame_paths:
            return []

        frame_interval_ms = max(1, int(round(1000 / config.sample_fps)))
        roi_frames: list[SubtitleRoiFrame] = []

        for index, frame_path in enumerate(frame_paths):
            timestamp_ms = index * frame_interval_ms
            with Image.open(frame_path) as frame_image:
                width, height = frame_image.size
                crop_box = (
                    int(width * config.roi_left_ratio),
                    int(height * config.roi_top_ratio),
                    int(width * config.roi_right_ratio),
                    int(height * config.roi_bottom_ratio),
                )
                cropped = frame_image.crop(crop_box).convert("L")
                prepared = _prepare_subtitle_image(cropped, ImageOps, ImageFilter)
                roi_frames.append(
                    SubtitleRoiFrame(
                        timestamp_ms=timestamp_ms,
                        image_array=np.array(prepared),
                        signature=np.array(prepared.resize((96, 24)), dtype=np.float32),
                    )
                )

        if not roi_frames:
            return []

        visual_groups = _group_frames_by_visual_change(roi_frames, config)
        ocr_engine = RapidOCR()
        raw_spans: list[SubtitleSpan] = []
        for group in visual_groups:
            group_spans = _build_spans_from_group(
                group=group,
                ocr_engine=ocr_engine,
                config=config,
                frame_interval_ms=frame_interval_ms,
            )
            raw_spans.extend(group_spans)

    return _merge_adjacent_spans(raw_spans, config)


def merge_subtitle_samples(
    samples: list[SubtitleFrameSample],
    config: SubtitleConfig,
    frame_interval_ms: int,
) -> list[SubtitleSpan]:
    spans: list[SubtitleSpan] = []
    current_samples: list[SubtitleFrameSample] = []

    def flush() -> None:
        nonlocal current_samples
        if not current_samples:
            return

        best_sample = max(
            current_samples,
            key=lambda item: (len(item.normalized_text), item.confidence, len(item.text)),
        )
        if not _is_meaningful_subtitle(best_sample.normalized_text):
            current_samples = []
            return

        span = SubtitleSpan(
            text=best_sample.text,
            normalized_text=best_sample.normalized_text,
            start_ms=current_samples[0].timestamp_ms,
            end_ms=current_samples[-1].timestamp_ms + frame_interval_ms,
            confidence=float(fmean(item.confidence for item in current_samples)),
            frame_count=len(current_samples),
            source_texts=[item.text for item in current_samples if item.text],
            quality_flags=[],
        )
        if span.duration_ms < config.min_span_duration_ms:
            span.quality_flags.append("short_subtitle_span")
        if span.confidence < config.min_detection_confidence:
            span.quality_flags.append("low_ocr_confidence")
        spans.append(span)
        current_samples = []

    for sample in samples:
        if not sample.normalized_text:
            if current_samples and sample.timestamp_ms - current_samples[-1].timestamp_ms > config.max_blank_gap_ms:
                flush()
            continue

        if not current_samples:
            current_samples.append(sample)
            continue

        similarity = _text_similarity(current_samples[-1].normalized_text, sample.normalized_text)
        if similarity >= config.merge_similarity:
            current_samples.append(sample)
            continue

        flush()
        current_samples.append(sample)

    flush()
    return spans


def _prepare_subtitle_image(cropped_image: object, image_ops_module: object, image_filter_module: object) -> object:
    prepared = image_ops_module.autocontrast(cropped_image)
    prepared = prepared.resize((prepared.width * 2, prepared.height * 2))
    prepared = prepared.filter(image_filter_module.SHARPEN)
    return prepared


def _group_frames_by_visual_change(
    roi_frames: list[SubtitleRoiFrame],
    config: SubtitleConfig,
) -> list[list[SubtitleRoiFrame]]:
    groups: list[list[SubtitleRoiFrame]] = []
    current_group: list[SubtitleRoiFrame] = []
    previous_frame: SubtitleRoiFrame | None = None

    for frame in roi_frames:
        if previous_frame is None:
            current_group = [frame]
            previous_frame = frame
            continue

        difference = _frame_difference(previous_frame.signature, frame.signature)
        if difference > config.visual_change_threshold:
            groups.append(current_group)
            current_group = [frame]
        else:
            current_group.append(frame)
        previous_frame = frame

    if current_group:
        groups.append(current_group)
    return groups


def _build_spans_from_group(
    group: list[SubtitleRoiFrame],
    ocr_engine: object,
    config: SubtitleConfig,
    frame_interval_ms: int,
) -> list[SubtitleSpan]:
    if not group:
        return []

    candidate_samples = _collect_group_samples(
        group=group,
        ocr_engine=ocr_engine,
        confidence_threshold=config.min_detection_confidence,
        representative_frame_window=config.representative_frame_window,
    )
    if not candidate_samples:
        return []

    grouped_samples = _group_samples_by_text(candidate_samples, config.merge_similarity)
    spans: list[SubtitleSpan] = []

    for index, sample_group in enumerate(grouped_samples):
        best_entry = max(
            sample_group,
            key=lambda item: (len(item[1].normalized_text), item[1].confidence, len(item[1].text)),
        )
        best_sample = best_entry[1]
        start_ms = group[0].timestamp_ms if index == 0 else _midpoint_timestamp(
            grouped_samples[index - 1][-1][1].timestamp_ms,
            sample_group[0][1].timestamp_ms,
        )
        end_ms = group[-1].timestamp_ms + frame_interval_ms if index == len(grouped_samples) - 1 else _midpoint_timestamp(
            sample_group[-1][1].timestamp_ms,
            grouped_samples[index + 1][0][1].timestamp_ms,
        )
        spans.append(
            SubtitleSpan(
                text=best_sample.text,
                normalized_text=best_sample.normalized_text,
                start_ms=start_ms,
                end_ms=max(start_ms + 1, end_ms),
                confidence=float(fmean(item[1].confidence for item in sample_group)),
                frame_count=len(group),
                source_texts=[item[1].text for item in sample_group if item[1].text],
                quality_flags=[],
            )
        )

    return spans


def _collect_group_samples(
    group: list[SubtitleRoiFrame],
    ocr_engine: object,
    confidence_threshold: float,
    representative_frame_window: int,
) -> list[tuple[int, SubtitleFrameSample]]:
    sample_stride = max(1, len(group) // 4)
    anchor_indexes = list(range(0, len(group), sample_stride))
    if anchor_indexes[-1] != len(group) - 1:
        anchor_indexes.append(len(group) - 1)

    cached_samples: dict[int, SubtitleFrameSample] = {}
    samples: list[tuple[int, SubtitleFrameSample]] = []

    for anchor_index in anchor_indexes:
        candidate_indexes = {
            max(0, min(len(group) - 1, anchor_index + offset))
            for offset in range(-representative_frame_window, representative_frame_window + 1)
        }
        candidate_samples: list[tuple[int, SubtitleFrameSample]] = []

        for index in sorted(candidate_indexes):
            if index not in cached_samples:
                frame = group[index]
                ocr_result, _ = ocr_engine(frame.image_array)
                cached_samples[index] = _build_frame_sample(
                    timestamp_ms=frame.timestamp_ms,
                    ocr_result=ocr_result,
                    confidence_threshold=confidence_threshold,
                )
            sample = cached_samples[index]
            if sample.normalized_text:
                candidate_samples.append((index, sample))

        if not candidate_samples:
            continue

        best_entry = max(
            candidate_samples,
            key=lambda item: (len(item[1].normalized_text), item[1].confidence, len(item[1].text)),
        )
        samples.append(best_entry)

    return samples


def _group_samples_by_text(
    samples: list[tuple[int, SubtitleFrameSample]],
    similarity_threshold: float,
) -> list[list[tuple[int, SubtitleFrameSample]]]:
    groups: list[list[tuple[int, SubtitleFrameSample]]] = []

    for entry in samples:
        sample = entry[1]
        if not groups:
            groups.append([entry])
            continue

        current_group = groups[-1]
        similarity = max(
            _text_similarity(existing[1].normalized_text, sample.normalized_text)
            for existing in current_group
        )
        if similarity >= similarity_threshold:
            current_group.append(entry)
        else:
            groups.append([entry])

    return groups


def _merge_adjacent_spans(
    spans: list[SubtitleSpan],
    config: SubtitleConfig,
) -> list[SubtitleSpan]:
    if not spans:
        return []

    merged: list[SubtitleSpan] = []
    current = spans[0]

    for candidate in spans[1:]:
        gap_ms = max(0, candidate.start_ms - current.end_ms)
        similarity = _text_similarity(current.normalized_text, candidate.normalized_text)
        if similarity >= config.merge_similarity and gap_ms <= config.max_blank_gap_ms:
            current = _merge_two_spans(current, candidate)
            continue
        merged.append(current)
        current = candidate

    merged.append(current)
    return merged


def _merge_two_spans(left: SubtitleSpan, right: SubtitleSpan) -> SubtitleSpan:
    best_text_span = max(
        [left, right],
        key=lambda item: (len(item.normalized_text), item.confidence, len(item.text)),
    )
    return SubtitleSpan(
        text=best_text_span.text,
        normalized_text=best_text_span.normalized_text,
        start_ms=min(left.start_ms, right.start_ms),
        end_ms=max(left.end_ms, right.end_ms),
        confidence=float(fmean([left.confidence, right.confidence])),
        frame_count=left.frame_count + right.frame_count,
        source_texts=[*left.source_texts, *right.source_texts],
        quality_flags=[*left.quality_flags, *right.quality_flags],
    )


def _midpoint_timestamp(left_ms: int, right_ms: int) -> int:
    return int(round((left_ms + right_ms) / 2))


def _build_frame_sample(
    timestamp_ms: int,
    ocr_result: object,
    confidence_threshold: float,
) -> SubtitleFrameSample:
    if not ocr_result:
        return SubtitleFrameSample(
            timestamp_ms=timestamp_ms,
            text="",
            normalized_text="",
            confidence=0.0,
        )

    detected_boxes: list[DetectedSubtitleBox] = []
    for entry in ocr_result:
        try:
            box, text, score = entry
        except (TypeError, ValueError):
            continue
        score_value = float(score)
        if score_value < confidence_threshold:
            continue
        text_value = " ".join(str(text).split()).strip()
        if not text_value:
            continue
        top = min(float(point[1]) for point in box)
        bottom = max(float(point[1]) for point in box)
        left = min(float(point[0]) for point in box)
        right = max(float(point[0]) for point in box)
        detected_boxes.append(
            DetectedSubtitleBox(
                top=top,
                bottom=bottom,
                left=left,
                right=right,
                text=text_value,
                score=score_value,
            )
        )

    if not detected_boxes:
        return SubtitleFrameSample(
            timestamp_ms=timestamp_ms,
            text="",
            normalized_text="",
            confidence=0.0,
        )

    line_groups = _group_detected_boxes_into_lines(detected_boxes)
    line_texts = [_join_detected_box_texts(line_group) for line_group in line_groups]
    text = " ".join(line_texts).strip()
    if _is_single_numeric_token(text):
        return SubtitleFrameSample(
            timestamp_ms=timestamp_ms,
            text="",
            normalized_text="",
            confidence=0.0,
        )
    normalized_text = normalize_subtitle_text(text)
    if not _is_meaningful_subtitle(normalized_text):
        return SubtitleFrameSample(
            timestamp_ms=timestamp_ms,
            text="",
            normalized_text="",
            confidence=0.0,
        )

    return SubtitleFrameSample(
        timestamp_ms=timestamp_ms,
        text=text,
        normalized_text=normalized_text,
        confidence=float(fmean(box.score for box in detected_boxes)),
        line_texts=line_texts,
    )


def _group_detected_boxes_into_lines(
    detected_boxes: list[DetectedSubtitleBox],
) -> list[list[DetectedSubtitleBox]]:
    sorted_boxes = sorted(detected_boxes, key=lambda item: (item.center_y, item.left))
    groups: list[list[DetectedSubtitleBox]] = []

    for box in sorted_boxes:
        placed = False
        for group in groups:
            center_y = fmean(item.center_y for item in group)
            average_height = fmean(item.height for item in group)
            tolerance = max(8.0, average_height * 0.65, box.height * 0.65)
            if abs(box.center_y - center_y) <= tolerance:
                group.append(box)
                placed = True
                break
        if not placed:
            groups.append([box])

    groups.sort(key=lambda group: fmean(item.center_y for item in group))
    for group in groups:
        group.sort(key=lambda item: item.left)
    return groups


def _join_detected_box_texts(detected_boxes: list[DetectedSubtitleBox]) -> str:
    return " ".join(box.text for box in detected_boxes if box.text).strip()


def _is_single_numeric_token(text: str) -> bool:
    tokens = [re.sub(r"[^0-9A-Za-z]+", "", token) for token in text.split()]
    normalized_tokens = [token for token in tokens if token]
    return len(normalized_tokens) == 1 and normalized_tokens[0].isdigit()


def _frame_difference(left_signature: object, right_signature: object) -> float:
    try:
        import numpy as np
    except ImportError as exc:
        raise RuntimeError("numpy is required for subtitle frame comparison.") from exc

    return float(np.mean(np.abs(left_signature.astype(np.float32) - right_signature.astype(np.float32))))


def _text_similarity(left: str, right: str) -> float:
    if left == right:
        return 1.0

    try:
        from rapidfuzz import fuzz
    except ImportError as exc:
        raise RuntimeError("rapidfuzz is required for subtitle text matching.") from exc

    ratio = float(fuzz.ratio(left, right))
    token_sort = float(fuzz.token_sort_ratio(left, right))
    token_set = float(fuzz.token_set_ratio(left, right))
    return max(ratio, token_sort, token_set) / 100.0


def _is_meaningful_subtitle(normalized_text: str) -> bool:
    alnum_count = sum(1 for char in normalized_text if char.isalnum())
    if alnum_count < 2:
        return False
    tokens = [token for token in normalized_text.split() if token]
    if len(tokens) == 1 and len(tokens[0]) < 2:
        return False
    if len(tokens) == 1 and not any(char.isalpha() for char in tokens[0]):
        return False
    return True
