from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
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
class AudioConfig:
    method: str = "demucs"
    demucs_model: str = "htdemucs"
    demucs_device: str = "cpu"
    mp3_bitrate_kbps: int = 192
    target_size_ratio: float = 0.0
    target_bitrate_kbps: int = 0
    jobs: int = 0
    cache_enabled: bool = True
    cache_dir: str = ".video_pipeline_cache\\bgm"

    def validate(self) -> None:
        if self.method != "demucs":
            raise ValueError("audio method must be demucs.")
        if not self.demucs_model:
            raise ValueError("audio demucs_model must not be empty.")
        if not self.demucs_device:
            raise ValueError("audio demucs_device must not be empty.")
        if self.mp3_bitrate_kbps < 32:
            raise ValueError("audio mp3_bitrate_kbps must be at least 32.")
        if self.target_size_ratio < 0:
            raise ValueError("audio target_size_ratio must be >= 0.")
        if self.target_bitrate_kbps < 0:
            raise ValueError("audio target_bitrate_kbps must be >= 0.")
        if self.target_size_ratio > 0 and self.target_bitrate_kbps > 0:
            raise ValueError("audio target_size_ratio and target_bitrate_kbps cannot both be set.")
        if self.jobs < 0:
            raise ValueError("audio jobs must be >= 0.")
        if not self.cache_dir:
            raise ValueError("audio cache_dir must not be empty.")


@dataclass(slots=True)
class VideoOutputConfig:
    target_size_ratio: float = 0.0
    target_bitrate_kbps: int = 0
    audio_bitrate_kbps: int = 128
    x264_preset: str = "slow"
    frame_width: int = 0
    frame_height: int = 0
    frame_rate: float = 0.0
    audio_sample_rate_hz: int = 0
    audio_channels: int = 0
    audio_bit_depth: int = 0

    def validate(self) -> None:
        if self.target_size_ratio < 0:
            raise ValueError("video target_size_ratio must be >= 0.")
        if self.target_bitrate_kbps < 0:
            raise ValueError("video target_bitrate_kbps must be >= 0.")
        if self.target_size_ratio > 0 and self.target_bitrate_kbps > 0:
            raise ValueError("video target_size_ratio and target_bitrate_kbps cannot both be set.")
        if self.audio_bitrate_kbps < 32:
            raise ValueError("video audio_bitrate_kbps must be at least 32.")
        if self.x264_preset not in {"ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow"}:
            raise ValueError("video x264_preset must be a valid libx264 preset.")
        if self.frame_width < 0 or self.frame_height < 0:
            raise ValueError("video frame_width and frame_height must be >= 0.")
        if bool(self.frame_width) != bool(self.frame_height):
            raise ValueError("video frame_width and frame_height must be set together.")
        if self.frame_rate < 0:
            raise ValueError("video frame_rate must be >= 0.")
        if self.audio_sample_rate_hz < 0:
            raise ValueError("video audio_sample_rate_hz must be >= 0.")
        if self.audio_channels < 0:
            raise ValueError("video audio_channels must be >= 0.")
        if self.audio_bit_depth not in {0, 32}:
            raise ValueError("video audio_bit_depth currently supports only 32-bit AAC export.")


@dataclass(slots=True)
class StepsConfig:
    """Toggle individual pipeline steps on or off."""
    export_source_video: bool = True
    export_cover: bool = True
    export_muted_video: bool = True
    export_background_audio: bool = True
    generate_summary: bool = True
    export_workbook: bool = True
    export_review_page: bool = True
    export_csv: bool = True


@dataclass(slots=True)
class AzureOpenAIConfig:
    endpoint: str = ""
    api_key: str = ""
    deployment: str = "gpt-5.4-mini"
    api_version: str = "2024-10-21"
    temperature: float = 0.2
    max_output_tokens: int = 120
    max_input_chars: int = 12_000

    def validate(self) -> None:
        if not self.endpoint or self.endpoint.startswith(PLACEHOLDER_PREFIX):
            raise ValueError(
                "Azure OpenAI endpoint is not configured. "
                "Please edit pipeline_config.json or set AZURE_OPENAI_ENDPOINT."
            )
        if not self.api_key or self.api_key.startswith(PLACEHOLDER_PREFIX):
            raise ValueError(
                "Azure OpenAI api_key is not configured. "
                "Please edit pipeline_config.json or set AZURE_OPENAI_API_KEY."
            )
        if not self.deployment:
            raise ValueError("Azure OpenAI deployment must not be empty.")
        if not self.api_version:
            raise ValueError("Azure OpenAI api_version must not be empty.")
        if not 0 <= self.temperature <= 2:
            raise ValueError("Azure OpenAI temperature must be between 0 and 2.")
        if self.max_output_tokens < 1:
            raise ValueError("Azure OpenAI max_output_tokens must be greater than 0.")
        if self.max_input_chars < 100:
            raise ValueError("Azure OpenAI max_input_chars must be at least 100.")


@dataclass(slots=True)
class OverviewConfig:
    education_stage: str = "小学"
    subject: str = "[167070462398963715]英语"
    difficulty: str = ""
    dialogue_audio: str = ""
    topic: str = ""
    source: str = "[7]绘本配音"


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
    video: VideoOutputConfig
    audio: AudioConfig
    azure_openai: AzureOpenAIConfig
    overview: OverviewConfig
    segmentation: SegmentationConfig
    steps: StepsConfig = field(default_factory=StepsConfig)


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
    video_data = dict(data.get("video", {}))
    audio_data = dict(data.get("audio", {}))
    azure_openai_data = dict(data.get("azure_openai", {}))
    overview_data = dict(data.get("overview", {}))
    segmentation_data = dict(data.get("segmentation", {}))
    steps_data = dict(data.get("steps", {}))

    env_provider = os.getenv("ASR_PROVIDER")
    env_key = os.getenv("AZURE_SPEECH_KEY")
    env_region = os.getenv("AZURE_SPEECH_REGION")
    env_language = os.getenv("AZURE_SPEECH_LANGUAGE")
    env_fw_model = os.getenv("FASTER_WHISPER_MODEL_SIZE")
    env_fw_language = os.getenv("FASTER_WHISPER_LANGUAGE")
    env_fw_device = os.getenv("FASTER_WHISPER_DEVICE")
    env_fw_compute_type = os.getenv("FASTER_WHISPER_COMPUTE_TYPE")
    env_fw_download_root = os.getenv("FASTER_WHISPER_DOWNLOAD_ROOT")
    env_audio_demucs_model = os.getenv("AUDIO_DEMUCS_MODEL")
    env_audio_demucs_device = os.getenv("AUDIO_DEMUCS_DEVICE")
    env_audio_cache_dir = os.getenv("AUDIO_CACHE_DIR")
    env_audio_cache_enabled = os.getenv("AUDIO_CACHE_ENABLED")
    env_aoai_endpoint = os.getenv("AZURE_OPENAI_ENDPOINT")
    env_aoai_api_key = os.getenv("AZURE_OPENAI_API_KEY")
    env_aoai_deployment = os.getenv("AZURE_OPENAI_DEPLOYMENT")
    env_aoai_api_version = os.getenv("AZURE_OPENAI_API_VERSION")

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
    if env_audio_demucs_model:
        audio_data["demucs_model"] = env_audio_demucs_model
    if env_audio_demucs_device:
        audio_data["demucs_device"] = env_audio_demucs_device
    if env_audio_cache_dir:
        audio_data["cache_dir"] = env_audio_cache_dir
    if env_audio_cache_enabled:
        audio_data["cache_enabled"] = env_audio_cache_enabled.lower() in {"1", "true", "yes", "on"}
    if env_aoai_endpoint:
        azure_openai_data["endpoint"] = env_aoai_endpoint
    if env_aoai_api_key:
        azure_openai_data["api_key"] = env_aoai_api_key
    if env_aoai_deployment:
        azure_openai_data["deployment"] = env_aoai_deployment
    if env_aoai_api_version:
        azure_openai_data["api_version"] = env_aoai_api_version

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

    video_config = VideoOutputConfig(
        target_size_ratio=float(video_data.get("target_size_ratio", 0.0)),
        target_bitrate_kbps=int(video_data.get("target_bitrate_kbps", 0)),
        audio_bitrate_kbps=int(video_data.get("audio_bitrate_kbps", 128)),
        x264_preset=str(video_data.get("x264_preset", "slow")),
        frame_width=int(video_data.get("frame_width", 0)),
        frame_height=int(video_data.get("frame_height", 0)),
        frame_rate=float(video_data.get("frame_rate", 0.0)),
        audio_sample_rate_hz=int(video_data.get("audio_sample_rate_hz", 0)),
        audio_channels=int(video_data.get("audio_channels", 0)),
        audio_bit_depth=int(video_data.get("audio_bit_depth", 0)),
    )

    audio_config = AudioConfig(
        method=str(audio_data.get("method", "demucs")),
        demucs_model=str(audio_data.get("demucs_model", "htdemucs")),
        demucs_device=str(audio_data.get("demucs_device", "cpu")),
        mp3_bitrate_kbps=int(audio_data.get("mp3_bitrate_kbps", 192)),
        target_size_ratio=float(audio_data.get("target_size_ratio", 0.0)),
        target_bitrate_kbps=int(audio_data.get("target_bitrate_kbps", 0)),
        jobs=int(audio_data.get("jobs", 0)),
        cache_enabled=bool(audio_data.get("cache_enabled", True)),
        cache_dir=str(audio_data.get("cache_dir", ".video_pipeline_cache\\bgm")),
    )

    azure_openai_config = AzureOpenAIConfig(
        endpoint=str(azure_openai_data.get("endpoint", "")),
        api_key=str(azure_openai_data.get("api_key", "")),
        deployment=str(azure_openai_data.get("deployment", "gpt-5.4-mini")),
        api_version=str(azure_openai_data.get("api_version", "2024-10-21")),
        temperature=float(azure_openai_data.get("temperature", 0.2)),
        max_output_tokens=int(azure_openai_data.get("max_output_tokens", 120)),
        max_input_chars=int(azure_openai_data.get("max_input_chars", 12_000)),
    )

    overview_config = OverviewConfig(
        education_stage=str(overview_data.get("education_stage", "小学")),
        subject=str(overview_data.get("subject", "[167070462398963715]英语")),
        difficulty=str(overview_data.get("difficulty", "")),
        dialogue_audio=str(overview_data.get("dialogue_audio", "")),
        topic=str(overview_data.get("topic", "")),
        source=str(overview_data.get("source", "[7]绘本配音")),
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

    steps_config = StepsConfig(
        export_source_video=bool(steps_data.get("export_source_video", True)),
        export_cover=bool(steps_data.get("export_cover", True)),
        export_muted_video=bool(steps_data.get("export_muted_video", True)),
        export_background_audio=bool(steps_data.get("export_background_audio", True)),
        generate_summary=bool(steps_data.get("generate_summary", True)),
        export_workbook=bool(steps_data.get("export_workbook", True)),
        export_review_page=bool(steps_data.get("export_review_page", True)),
        export_csv=bool(steps_data.get("export_csv", True)),
    )

    return PipelineConfig(
        asr=asr_config,
        azure_speech=azure_config,
        faster_whisper=faster_whisper_config,
        subtitle=subtitle_config,
        video=video_config,
        audio=audio_config,
        azure_openai=azure_openai_config,
        overview=overview_config,
        segmentation=segmentation_config,
        steps=steps_config,
    )
