from __future__ import annotations

from pathlib import Path
from statistics import fmean
from typing import Any

from video_analysis_pipeline.config import FasterWhisperConfig
from video_analysis_pipeline.models import TranscriptUtterance, WordTiming


def normalize_whisper_language(value: str) -> str:
    normalized = value.strip()
    if not normalized:
        return ""
    return normalized.split("-")[0].split("_")[0].lower()


class FasterWhisperTranscriber:
    def __init__(self, config: FasterWhisperConfig) -> None:
        self._config = config

    def transcribe(self, audio_path: Path) -> list[TranscriptUtterance]:
        self._config.validate()

        try:
            from faster_whisper import WhisperModel
        except ImportError as exc:
            raise RuntimeError(
                "faster-whisper is not installed. "
                "Run: py -m pip install -r requirements.txt"
            ) from exc

        model = WhisperModel(
            self._config.model_size,
            device=self._config.device,
            compute_type=self._config.compute_type,
            cpu_threads=self._config.cpu_threads,
            num_workers=self._config.num_workers,
            download_root=self._config.download_root or None,
        )

        segments_iterable, _ = model.transcribe(
            str(audio_path),
            language=normalize_whisper_language(self._config.language) or None,
            beam_size=self._config.beam_size,
            word_timestamps=True,
            vad_filter=self._config.vad_filter,
        )

        utterances = [
            self._segment_to_utterance(segment)
            for segment in segments_iterable
            if str(getattr(segment, "text", "")).strip()
        ]
        utterances.sort(key=lambda item: (item.start_ms, item.end_ms))

        if not utterances:
            raise RuntimeError("faster-whisper returned no recognized speech segments.")

        return utterances

    def _segment_to_utterance(self, segment: Any) -> TranscriptUtterance:
        words = []
        for word in getattr(segment, "words", []) or []:
            text = str(getattr(word, "word", "")).strip()
            if not text:
                continue
            start_ms = int(round(float(getattr(word, "start", 0.0)) * 1000))
            end_ms = int(round(float(getattr(word, "end", 0.0)) * 1000))
            probability = getattr(word, "probability", None)
            confidence = float(probability) if probability is not None else None
            words.append(
                WordTiming(
                    text=text,
                    start_ms=start_ms,
                    end_ms=end_ms,
                    confidence=confidence,
                )
            )

        start_ms = int(round(float(getattr(segment, "start", 0.0)) * 1000))
        end_ms = int(round(float(getattr(segment, "end", 0.0)) * 1000))
        if words:
            start_ms = words[0].start_ms
            end_ms = words[-1].end_ms

        confidences = [word.confidence for word in words if word.confidence is not None]
        confidence = float(fmean(confidences)) if confidences else None

        raw_json = {
            "avg_logprob": getattr(segment, "avg_logprob", None),
            "compression_ratio": getattr(segment, "compression_ratio", None),
            "no_speech_prob": getattr(segment, "no_speech_prob", None),
            "seek": getattr(segment, "seek", None),
            "temperature": getattr(segment, "temperature", None),
        }

        return TranscriptUtterance(
            text=str(getattr(segment, "text", "")).strip(),
            start_ms=start_ms,
            end_ms=end_ms,
            confidence=confidence,
            words=words,
            raw_json=raw_json,
        )
