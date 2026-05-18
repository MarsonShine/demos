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

    @classmethod
    def from_json(cls, payload: dict[str, Any]) -> "SubtitleSpan":
        return cls(
            text=str(payload["text"]),
            normalized_text=str(payload.get("normalized_text", "")),
            start_ms=int(payload["start_ms"]),
            end_ms=int(payload["end_ms"]),
            confidence=float(payload.get("confidence", 0.0)),
            frame_count=int(payload.get("frame_count", 0)),
            source=str(payload.get("source", "ocr")),
            raw_index=int(payload["raw_index"]) if payload.get("raw_index") is not None else None,
            source_texts=[str(item) for item in payload.get("source_texts", [])],
            quality_flags=[str(item) for item in payload.get("quality_flags", [])],
        )


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

    @classmethod
    def from_json(cls, payload: dict[str, Any]) -> "Segment":
        return cls(
            sequence_no=int(payload["sequence_no"]),
            segment_no=int(payload["segment_no"]),
            text=str(payload["text"]),
            start_ms=int(payload["start_ms"]),
            end_ms=int(payload["end_ms"]),
            source_utterance_indexes=[int(item) for item in payload.get("source_utterance_indexes", [])],
            confidence=payload.get("confidence"),
            text_source=str(payload.get("text_source", "asr")),
            alignment_confidence=payload.get("alignment_confidence"),
            ocr_confidence=payload.get("ocr_confidence", payload.get("subtitle_confidence")),
            source_subtitle_index=payload.get("source_subtitle_index"),
            source_word_range=[int(item) for item in payload["source_word_range"]] if payload.get("source_word_range") else None,
            quality_flags=[str(item) for item in payload.get("quality_flags", [])],
        )


@dataclass(slots=True)
class OverviewRow:
    education_stage: str
    subject: str
    sequence_no: int
    movie_name: str
    video_title: str
    muted_video: str
    full_video: str
    background_audio: str
    cover_image: str
    video_description: str
    difficulty: str = ""
    dialogue_audio: str = ""
    topic: str = ""
    source: str = ""

    def to_excel_row(self) -> tuple[object, ...]:
        return (
            self.education_stage,
            self.subject,
            self.sequence_no,
            self.movie_name,
            self.video_title,
            self.muted_video,
            self.full_video,
            self.background_audio,
            self.cover_image,
            self.video_description,
            self.difficulty,
            self.dialogue_audio,
            self.topic,
            self.source,
        )

    def to_json(self) -> dict[str, Any]:
        return {
            "education_stage": self.education_stage,
            "subject": self.subject,
            "sequence_no": self.sequence_no,
            "movie_name": self.movie_name,
            "video_title": self.video_title,
            "muted_video": self.muted_video,
            "full_video": self.full_video,
            "background_audio": self.background_audio,
            "cover_image": self.cover_image,
            "video_description": self.video_description,
            "difficulty": self.difficulty,
            "dialogue_audio": self.dialogue_audio,
            "topic": self.topic,
            "source": self.source,
        }

    @classmethod
    def from_json(cls, payload: dict[str, Any]) -> "OverviewRow":
        return cls(
            education_stage=str(payload.get("education_stage", "")),
            subject=str(payload.get("subject", "")),
            sequence_no=int(payload.get("sequence_no", 1)),
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

    def to_json(self) -> dict[str, Any]:
        return {
            "education_stage": self.education_stage,
            "subject": self.subject,
            "sequence_no": self.sequence_no,
            "movie_name": self.movie_name,
            "video_title": self.video_title,
            "muted_video": self.muted_video,
            "full_video": self.full_video,
            "background_audio": self.background_audio,
            "cover_image": self.cover_image,
            "video_description": self.video_description,
            "difficulty": self.difficulty,
            "dialogue_audio": self.dialogue_audio,
            "topic": self.topic,
            "source": self.source,
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
