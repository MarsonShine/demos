from __future__ import annotations

import unittest

from video_analysis_pipeline.asr import create_transcriber, normalize_asr_provider
from video_analysis_pipeline.config import (
    AudioConfig,
    AsrConfig,
    AzureOpenAIConfig,
    AzureSpeechConfig,
    FasterWhisperConfig,
    OverviewConfig,
    PipelineConfig,
    SegmentationConfig,
    SubtitleConfig,
)
from video_analysis_pipeline.faster_whisper_asr import FasterWhisperTranscriber, normalize_whisper_language


class AsrFactoryTests(unittest.TestCase):
    def build_config(self, provider: str) -> PipelineConfig:
        return PipelineConfig(
            asr=AsrConfig(provider=provider),
            azure_speech=AzureSpeechConfig(
                subscription_key="test-key",
                region="eastus",
                language="en-US",
            ),
            faster_whisper=FasterWhisperConfig(),
            subtitle=SubtitleConfig(),
            audio=AudioConfig(),
            azure_openai=AzureOpenAIConfig(endpoint="https://example.openai.azure.com", api_key="key"),
            overview=OverviewConfig(),
            segmentation=SegmentationConfig(),
        )

    def test_provider_alias_is_normalized(self) -> None:
        self.assertEqual(normalize_asr_provider("fast-whisper"), "faster-whisper")
        self.assertEqual(normalize_asr_provider("azure"), "azure-speech")

    def test_whisper_language_is_normalized(self) -> None:
        self.assertEqual(normalize_whisper_language("en-US"), "en")
        self.assertEqual(normalize_whisper_language("zh_CN"), "zh")

    def test_factory_uses_faster_whisper_for_alias(self) -> None:
        transcriber = create_transcriber(self.build_config("fast-whisper"))
        self.assertIsInstance(transcriber, FasterWhisperTranscriber)

    def test_factory_rejects_unknown_provider(self) -> None:
        with self.assertRaises(ValueError):
            create_transcriber(self.build_config("unknown"))


if __name__ == "__main__":
    unittest.main()
