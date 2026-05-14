from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path


PLACEHOLDER_PREFIX = "YOUR_"


@dataclass(slots=True)
class AsrConfig:
    provider: str = "faster-whisper"


@dataclass(slots=True)
class AzureSpeechConfig:
    subscription_key: str
    region: str
    language: str = "en-US"

    def validate(self) -> None:
        if not self.subscription_key or self.subscription_key.startswith(PLACEHOLDER_PREFIX):
            raise ValueError(
                "Azure Speech subscription_key is not configured. "
                "Please edit pipeline_config.json or set AZURE_SPEECH_KEY."
            )
        if not self.region or self.region.startswith(PLACEHOLDER_PREFIX):
            raise ValueError(
                "Azure Speech region is not configured. "
                "Please edit pipeline_config.json or set AZURE_SPEECH_REGION."
            )


@dataclass(slots=True)
class FasterWhisperConfig:
    model_size: str = "base.en"
    language: str = "en"
    device: str = "cpu"
    compute_type: str = "int8"
    cpu_threads: int = 4
    num_workers: int = 1
    beam_size: int = 5
    vad_filter: bool = False
    download_root: str = ""

    def validate(self) -> None:
        if not self.model_size:
            raise ValueError(
                "faster-whisper model_size is not configured. "
                "Please edit pipeline_config.json or set FASTER_WHISPER_MODEL_SIZE."
            )
        if not self.device:
            raise ValueError("faster-whisper device must not be empty.")
        if not self.compute_type:
            raise ValueError("faster-whisper compute_type must not be empty.")
        if self.cpu_threads < 1:
            raise ValueError("faster-whisper cpu_threads must be greater than 0.")
        if self.num_workers < 1:
            raise ValueError("faster-whisper num_workers must be greater than 0.")
        if self.beam_size < 1:
            raise ValueError("faster-whisper beam_size must be greater than 0.")


@dataclass(slots=True)
class SubtitleConfig:
    sample_fps: float = 4.0
    roi_left_ratio: float = 0.10
    roi_right_ratio: float = 0.90
    roi_top_ratio: float = 0.72
    roi_bottom_ratio: float = 0.96
    min_detection_confidence: float = 0.35
    merge_similarity: float = 0.90
    max_blank_gap_ms: int = 500
    min_span_duration_ms: int = 250
    visual_change_threshold: float = 12.0
    representative_frame_window: int = 1
    alignment_min_text_score: float = 72.0
    alignment_search_window_ms: int = 2_000
    alignment_max_shift_ms: int = 1_200
    alignment_max_words_ahead: int = 80
    allow_asr_fallback: bool = True

    def validate(self) -> None:
        if self.sample_fps <= 0:
            raise ValueError("subtitle sample_fps must be greater than 0.")
        for name, value in {
            "roi_left_ratio": self.roi_left_ratio,
            "roi_right_ratio": self.roi_right_ratio,
            "roi_top_ratio": self.roi_top_ratio,
            "roi_bottom_ratio": self.roi_bottom_ratio,
        }.items():
            if not 0 <= value <= 1:
                raise ValueError(f"subtitle {name} must be between 0 and 1.")
        if self.roi_left_ratio >= self.roi_right_ratio:
            raise ValueError("subtitle roi_left_ratio must be less than roi_right_ratio.")
        if self.roi_top_ratio >= self.roi_bottom_ratio:
            raise ValueError("subtitle roi_top_ratio must be less than roi_bottom_ratio.")
        if not 0 <= self.min_detection_confidence <= 1:
            raise ValueError("subtitle min_detection_confidence must be between 0 and 1.")
        if not 0 <= self.merge_similarity <= 1:
            raise ValueError("subtitle merge_similarity must be between 0 and 1.")
        if self.max_blank_gap_ms < 0:
            raise ValueError("subtitle max_blank_gap_ms must be >= 0.")
        if self.min_span_duration_ms < 0:
            raise ValueError("subtitle min_span_duration_ms must be >= 0.")
        if self.visual_change_threshold < 0:
            raise ValueError("subtitle visual_change_threshold must be >= 0.")
        if self.representative_frame_window < 0:
            raise ValueError("subtitle representative_frame_window must be >= 0.")
        if not 0 <= self.alignment_min_text_score <= 100:
            raise ValueError("subtitle alignment_min_text_score must be between 0 and 100.")
        if self.alignment_search_window_ms <= 0:
            raise ValueError("subtitle alignment_search_window_ms must be > 0.")
        if self.alignment_max_shift_ms < 0:
            raise ValueError("subtitle alignment_max_shift_ms must be >= 0.")
        if self.alignment_max_words_ahead < 1:
            raise ValueError("subtitle alignment_max_words_ahead must be > 0.")


@dataclass(slots=True)
class SegmentationConfig:
    silence_threshold_db: float = -35.0
    min_silence_duration_ms: int = 250
    merge_gap_ms: int = 350
    lead_in_ms: int = 80
    tail_out_ms: int = 120
    min_segment_duration_ms: int = 500
    max_segment_duration_ms: int = 12_000
    max_boundary_shift_ms: int = 800


@dataclass(slots=True)
class PipelineConfig:
    asr: AsrConfig
    azure_speech: AzureSpeechConfig
    faster_whisper: FasterWhisperConfig
    subtitle: SubtitleConfig
    segmentation: SegmentationConfig


def load_config(path: Path) -> PipelineConfig:
    if not path.exists():
        raise FileNotFoundError(
            f"Config file not found: {path}. "
            "Create pipeline_config.json in the repository root."
        )

    data = json.loads(path.read_text(encoding="utf-8"))

    asr_data = dict(data.get("asr", {}))
    azure_data = dict(data.get("azure_speech", {}))
    faster_whisper_data = dict(data.get("faster_whisper", {}))
    subtitle_data = dict(data.get("subtitle", {}))
    segmentation_data = dict(data.get("segmentation", {}))

    env_provider = os.getenv("ASR_PROVIDER")
    env_key = os.getenv("AZURE_SPEECH_KEY")
    env_region = os.getenv("AZURE_SPEECH_REGION")
    env_language = os.getenv("AZURE_SPEECH_LANGUAGE")
    env_fw_model = os.getenv("FASTER_WHISPER_MODEL_SIZE")
    env_fw_language = os.getenv("FASTER_WHISPER_LANGUAGE")
    env_fw_device = os.getenv("FASTER_WHISPER_DEVICE")
    env_fw_compute_type = os.getenv("FASTER_WHISPER_COMPUTE_TYPE")
    env_fw_download_root = os.getenv("FASTER_WHISPER_DOWNLOAD_ROOT")

    if env_provider:
        asr_data["provider"] = env_provider
    if env_key:
        azure_data["subscription_key"] = env_key
    if env_region:
        azure_data["region"] = env_region
    if env_language:
        azure_data["language"] = env_language
    if env_fw_model:
        faster_whisper_data["model_size"] = env_fw_model
    if env_fw_language:
        faster_whisper_data["language"] = env_fw_language
    if env_fw_device:
        faster_whisper_data["device"] = env_fw_device
    if env_fw_compute_type:
        faster_whisper_data["compute_type"] = env_fw_compute_type
    if env_fw_download_root:
        faster_whisper_data["download_root"] = env_fw_download_root

    asr_config = AsrConfig(
        provider=str(asr_data.get("provider", "faster-whisper")),
    )

    azure_config = AzureSpeechConfig(
        subscription_key=str(azure_data.get("subscription_key", "")),
        region=str(azure_data.get("region", "")),
        language=str(azure_data.get("language", "en-US")),
    )

    faster_whisper_config = FasterWhisperConfig(
        model_size=str(faster_whisper_data.get("model_size", "base.en")),
        language=str(faster_whisper_data.get("language", "en")),
        device=str(faster_whisper_data.get("device", "cpu")),
        compute_type=str(faster_whisper_data.get("compute_type", "int8")),
        cpu_threads=int(faster_whisper_data.get("cpu_threads", 4)),
        num_workers=int(faster_whisper_data.get("num_workers", 1)),
        beam_size=int(faster_whisper_data.get("beam_size", 5)),
        vad_filter=bool(faster_whisper_data.get("vad_filter", False)),
        download_root=str(faster_whisper_data.get("download_root", "")),
    )

    subtitle_config = SubtitleConfig(
        sample_fps=float(subtitle_data.get("sample_fps", 4.0)),
        roi_left_ratio=float(subtitle_data.get("roi_left_ratio", 0.10)),
        roi_right_ratio=float(subtitle_data.get("roi_right_ratio", 0.90)),
        roi_top_ratio=float(subtitle_data.get("roi_top_ratio", 0.72)),
        roi_bottom_ratio=float(subtitle_data.get("roi_bottom_ratio", 0.96)),
        min_detection_confidence=float(subtitle_data.get("min_detection_confidence", 0.35)),
        merge_similarity=float(subtitle_data.get("merge_similarity", 0.90)),
        max_blank_gap_ms=int(subtitle_data.get("max_blank_gap_ms", 500)),
        min_span_duration_ms=int(subtitle_data.get("min_span_duration_ms", 250)),
        visual_change_threshold=float(subtitle_data.get("visual_change_threshold", 12.0)),
        representative_frame_window=int(subtitle_data.get("representative_frame_window", 1)),
        alignment_min_text_score=float(subtitle_data.get("alignment_min_text_score", 72.0)),
        alignment_search_window_ms=int(subtitle_data.get("alignment_search_window_ms", 2_000)),
        alignment_max_shift_ms=int(subtitle_data.get("alignment_max_shift_ms", 1_200)),
        alignment_max_words_ahead=int(subtitle_data.get("alignment_max_words_ahead", 80)),
        allow_asr_fallback=bool(subtitle_data.get("allow_asr_fallback", True)),
    )

    segmentation_config = SegmentationConfig(
        silence_threshold_db=float(segmentation_data.get("silence_threshold_db", -35.0)),
        min_silence_duration_ms=int(segmentation_data.get("min_silence_duration_ms", 250)),
        merge_gap_ms=int(segmentation_data.get("merge_gap_ms", 350)),
        lead_in_ms=int(segmentation_data.get("lead_in_ms", 80)),
        tail_out_ms=int(segmentation_data.get("tail_out_ms", 120)),
        min_segment_duration_ms=int(segmentation_data.get("min_segment_duration_ms", 500)),
        max_segment_duration_ms=int(segmentation_data.get("max_segment_duration_ms", 12_000)),
        max_boundary_shift_ms=int(segmentation_data.get("max_boundary_shift_ms", 800)),
    )

    return PipelineConfig(
        asr=asr_config,
        azure_speech=azure_config,
        faster_whisper=faster_whisper_config,
        subtitle=subtitle_config,
        segmentation=segmentation_config,
    )
