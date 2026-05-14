from __future__ import annotations

from video_analysis_pipeline.azure_asr import AzureSpeechTranscriber
from video_analysis_pipeline.config import PipelineConfig
from video_analysis_pipeline.faster_whisper_asr import FasterWhisperTranscriber


def normalize_asr_provider(value: str) -> str:
    normalized = value.strip().lower()
    aliases = {
        "fast-whisper": "faster-whisper",
        "faster_whisper": "faster-whisper",
        "azure": "azure-speech",
        "azure_speech": "azure-speech",
    }
    return aliases.get(normalized, normalized)


def create_transcriber(config: PipelineConfig) -> AzureSpeechTranscriber | FasterWhisperTranscriber:
    provider = normalize_asr_provider(config.asr.provider)
    if provider == "faster-whisper":
        config.faster_whisper.validate()
        return FasterWhisperTranscriber(config.faster_whisper)
    if provider == "azure-speech":
        config.azure_speech.validate()
        return AzureSpeechTranscriber(config.azure_speech)

    raise ValueError(
        f"Unsupported ASR provider: {config.asr.provider}. "
        "Use 'faster-whisper' or 'azure-speech'."
    )
