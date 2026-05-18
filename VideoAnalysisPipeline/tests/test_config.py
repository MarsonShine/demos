from __future__ import annotations

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from video_analysis_pipeline.config import load_config


class ConfigTests(unittest.TestCase):
    def test_loads_faster_whisper_defaults(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            config_path = Path(tmp_dir) / "pipeline_config.json"
            config_path.write_text(
                json.dumps(
                    {
                        "asr": {"provider": "faster-whisper"},
                        "azure_speech": {
                            "subscription_key": "YOUR_AZURE_SPEECH_KEY",
                            "region": "YOUR_AZURE_SPEECH_REGION",
                            "language": "en-US",
                        },
                        "faster_whisper": {
                            "model_size": "base.en",
                            "language": "en",
                            "device": "cpu",
                            "compute_type": "int8",
                            "cpu_threads": 2,
                            "num_workers": 1,
                            "beam_size": 3,
                            "vad_filter": False,
                            "download_root": "",
                        },
                        "segmentation": {},
                    }
                ),
                encoding="utf-8",
            )

            config = load_config(config_path)

            self.assertEqual(config.asr.provider, "faster-whisper")
            self.assertEqual(config.faster_whisper.model_size, "base.en")
            self.assertEqual(config.faster_whisper.cpu_threads, 2)
            self.assertEqual(config.subtitle.sample_fps, 4.0)
            self.assertEqual(config.audio.method, "demucs")
            self.assertEqual(config.audio.demucs_model, "htdemucs")
            self.assertTrue(config.audio.cache_enabled)
            self.assertEqual(config.azure_openai.deployment, "gpt-5.4-mini")
            self.assertEqual(config.overview.education_stage, "小学")

    def test_environment_can_override_provider(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            config_path = Path(tmp_dir) / "pipeline_config.json"
            config_path.write_text(
                json.dumps(
                    {
                        "asr": {"provider": "azure-speech"},
                        "azure_speech": {
                            "subscription_key": "YOUR_AZURE_SPEECH_KEY",
                            "region": "YOUR_AZURE_SPEECH_REGION",
                            "language": "en-US",
                        },
                        "faster_whisper": {"model_size": "base.en"},
                        "segmentation": {},
                    }
                ),
                encoding="utf-8",
            )

            with patch.dict(os.environ, {"ASR_PROVIDER": "fast-whisper"}, clear=False):
                config = load_config(config_path)

            self.assertEqual(config.asr.provider, "fast-whisper")

    def test_environment_can_override_azure_openai_settings(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            config_path = Path(tmp_dir) / "pipeline_config.json"
            config_path.write_text(
                json.dumps(
                    {
                        "azure_speech": {"subscription_key": "", "region": ""},
                        "faster_whisper": {"model_size": "base.en"},
                    }
                ),
                encoding="utf-8",
            )

            with patch.dict(
                os.environ,
                {
                    "AZURE_OPENAI_ENDPOINT": "https://example.openai.azure.com",
                    "AZURE_OPENAI_API_KEY": "test-key",
                    "AZURE_OPENAI_DEPLOYMENT": "gpt-5.4-mini",
                },
                clear=False,
            ):
                config = load_config(config_path)

            self.assertEqual(config.azure_openai.endpoint, "https://example.openai.azure.com")
            self.assertEqual(config.azure_openai.api_key, "test-key")
            self.assertEqual(config.azure_openai.deployment, "gpt-5.4-mini")


if __name__ == "__main__":
    unittest.main()
