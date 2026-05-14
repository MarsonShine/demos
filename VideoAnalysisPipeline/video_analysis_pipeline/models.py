from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any

from video_analysis_pipeline.timecode import format_timestamp


@dataclass(slots=True)
class TimeRange:
    start_ms: int
    end_ms: int

    @property
    def duration_ms(self) -> int:
        return max(0, self.end_ms - self.start_ms)

    def overlaps(self, other: "TimeRange") -> bool:
        return self.start_ms < other.end_ms and other.start_ms < self.end_ms

    def overlap_duration(self, other: "TimeRange") -> int:
        start = max(self.start_ms, other.start_ms)
        end = min(self.end_ms, other.end_ms)
        return max(0, end - start)

    def to_json(self) -> dict[str, int]:
        return {
            "start_ms": self.start_ms,
            "end_ms": self.end_ms,
            "duration_ms": self.duration_ms,
        }


@dataclass(slots=True)
class WordTiming:
    text: str
    start_ms: int
    end_ms: int
    confidence: float | None = None

    def to_json(self) -> dict[str, Any]:
        return {
            "text": self.text,
            "start_ms": self.start_ms,
            "end_ms": self.end_ms,
            "confidence": self.confidence,
        }


@dataclass(slots=True)
class TranscriptUtterance:
    text: str
    start_ms: int
    end_ms: int
    confidence: float | None = None
    words: list[WordTiming] = field(default_factory=list)
    raw_json: dict[str, Any] | None = None

    @property
    def duration_ms(self) -> int:
        return max(0, self.end_ms - self.start_ms)

    @property
    def word_count(self) -> int:
        if self.words:
            return len(self.words)
        return len([part for part in self.text.split() if part])

    def to_json(self) -> dict[str, Any]:
        return {
            "text": self.text,
            "start_ms": self.start_ms,
            "end_ms": self.end_ms,
            "duration_ms": self.duration_ms,
            "confidence": self.confidence,
            "words": [word.to_json() for word in self.words],
            "raw_json": self.raw_json,
        }


@dataclass(slots=True)
class SubtitleFrameSample:
    timestamp_ms: int
    text: str
    normalized_text: str
    confidence: float
    line_texts: list[str] = field(default_factory=list)

    def to_json(self) -> dict[str, Any]:
        return {
            "timestamp_ms": self.timestamp_ms,
            "timestamp": format_timestamp(self.timestamp_ms),
            "text": self.text,
            "normalized_text": self.normalized_text,
            "confidence": self.confidence,
            "line_texts": self.line_texts,
        }


@dataclass(slots=True)
class SubtitleSpan:
    text: str
    normalized_text: str
    start_ms: int
    end_ms: int
    confidence: float
    frame_count: int
    source: str = "ocr"
    raw_index: int | None = None
    source_texts: list[str] = field(default_factory=list)
    quality_flags: list[str] = field(default_factory=list)

    @property
    def duration_ms(self) -> int:
        return max(0, self.end_ms - self.start_ms)

    def to_json(self) -> dict[str, Any]:
        return {
            "text": self.text,
            "normalized_text": self.normalized_text,
            "start_ms": self.start_ms,
            "end_ms": self.end_ms,
            "start_timecode": format_timestamp(self.start_ms),
            "end_timecode": format_timestamp(self.end_ms),
            "duration_ms": self.duration_ms,
            "confidence": self.confidence,
            "frame_count": self.frame_count,
            "source": self.source,
            "raw_index": self.raw_index,
            "source_texts": self.source_texts,
            "quality_flags": self.quality_flags,
        }


@dataclass(slots=True)
class Segment:
    sequence_no: int
    segment_no: int
    text: str
    start_ms: int
    end_ms: int
    source_utterance_indexes: list[int] = field(default_factory=list)
    confidence: float | None = None
    text_source: str = "asr"
    alignment_confidence: float | None = None
    ocr_confidence: float | None = None
    source_subtitle_index: int | None = None
    source_word_range: list[int] | None = None
    quality_flags: list[str] = field(default_factory=list)

    @property
    def duration_ms(self) -> int:
        return max(0, self.end_ms - self.start_ms)

    def to_excel_row(self) -> tuple[int, int, str, str, str]:
        return (
            self.sequence_no,
            self.segment_no,
            self.text,
            format_timestamp(self.start_ms),
            format_timestamp(self.end_ms),
        )

    def to_json(self) -> dict[str, Any]:
        return {
            "sequence_no": self.sequence_no,
            "segment_no": self.segment_no,
            "text": self.text,
            "start_ms": self.start_ms,
            "end_ms": self.end_ms,
            "start_timecode": format_timestamp(self.start_ms),
            "end_timecode": format_timestamp(self.end_ms),
            "duration_ms": self.duration_ms,
            "confidence": self.confidence,
            "text_source": self.text_source,
            "alignment_confidence": self.alignment_confidence,
            "subtitle_confidence": self.ocr_confidence,
            "ocr_confidence": self.ocr_confidence,
            "source_subtitle_index": self.source_subtitle_index,
            "source_word_range": self.source_word_range,
            "source_utterance_indexes": self.source_utterance_indexes,
            "quality_flags": self.quality_flags,
        }


@dataclass(slots=True)
class MediaMetadata:
    path: str
    duration_ms: int
    video_streams: int
    audio_streams: int
    width: int | None = None
    height: int | None = None
    sample_rate: int | None = None
    channels: int | None = None

    def to_json(self) -> dict[str, Any]:
        return {
            "path": self.path,
            "duration_ms": self.duration_ms,
            "video_streams": self.video_streams,
            "audio_streams": self.audio_streams,
            "width": self.width,
            "height": self.height,
            "sample_rate": self.sample_rate,
            "channels": self.channels,
        }
