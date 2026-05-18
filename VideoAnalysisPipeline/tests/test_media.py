from __future__ import annotations

import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from video_analysis_pipeline.config import AudioConfig
from video_analysis_pipeline.media import _decode_subprocess_output, detect_silence, extract_background_audio_mp3


class MediaTests(unittest.TestCase):
    @patch("video_analysis_pipeline.media.locale.getpreferredencoding", return_value="gbk")
    def test_decode_subprocess_output_falls_back_to_cp1252(self, _mock_encoding: object) -> None:
        decoded = _decode_subprocess_output(b'prefix \x93quoted\x94 suffix')

        self.assertEqual(decoded, 'prefix "quoted" suffix'.replace('"', "\u201c", 1).replace('"', "\u201d", 1))

    @patch("video_analysis_pipeline.media.locale.getpreferredencoding", return_value="gbk")
    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_detect_silence_parses_stderr_when_ffmpeg_output_is_not_gbk(
        self,
        mock_run: object,
        _mock_encoding: object,
    ) -> None:
        mock_run.return_value = subprocess.CompletedProcess(
            args=["ffmpeg"],
            returncode=0,
            stdout=b"",
            stderr=(
                b"metadata \x93quoted\x94 value\n"
                b"[silencedetect @ 000000] silence_start: 0.500\n"
                b"[silencedetect @ 000000] silence_end: 1.250 | silence_duration: 0.750\n"
            ),
        )

        silence_ranges, non_silent_ranges = detect_silence(
            audio_path=Path("audio.mp3"),
            total_duration_ms=2000,
            silence_threshold_db=-35.0,
            min_silence_duration_ms=500,
        )

        self.assertEqual([(item.start_ms, item.end_ms) for item in silence_ranges], [(500, 1250)])
        self.assertEqual([(item.start_ms, item.end_ms) for item in non_silent_ranges], [(0, 500), (1250, 2000)])

    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_uses_demucs_no_vocals_output(self, mock_run: object) -> None:
        def fake_run(args: list[str], capture_output: bool, check: bool) -> subprocess.CompletedProcess[bytes]:
            output_root = Path(args[args.index("-o") + 1])
            model_name = args[args.index("-n") + 1]
            source_audio = Path(args[-1])
            accompaniment_path = output_root / model_name / source_audio.stem / "no_vocals.mp3"
            accompaniment_path.parent.mkdir(parents=True, exist_ok=True)
            accompaniment_path.write_bytes(b"bgm-bytes")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout=b"", stderr=b"")

        mock_run.side_effect = fake_run

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_audio = Path(tmp_dir) / "analysis.mp3"
            output_audio = Path(tmp_dir) / "03.mp3"
            source_audio.write_bytes(b"input")

            result = extract_background_audio_mp3(source_audio, output_audio, AudioConfig())

            self.assertEqual(result, output_audio)
            self.assertEqual(output_audio.read_bytes(), b"bgm-bytes")

    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_keeps_temp_dir_when_output_is_missing(self, mock_run: object) -> None:
        mock_run.return_value = subprocess.CompletedProcess(args=["demucs"], returncode=0, stdout=b"", stderr=b"")

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_audio = Path(tmp_dir) / "analysis.mp3"
            output_audio = Path(tmp_dir) / "03.mp3"
            source_audio.write_bytes(b"input")

            with self.assertRaisesRegex(RuntimeError, "Temp directory kept for inspection:") as context:
                extract_background_audio_mp3(source_audio, output_audio, AudioConfig())

            error_message = str(context.exception)
            temp_dir_prefix = "Temp directory kept for inspection: "
            temp_dir_start = error_message.index(temp_dir_prefix) + len(temp_dir_prefix)
            temp_dir_end = error_message.find("\n", temp_dir_start)
            preserved_temp_dir = Path(error_message[temp_dir_start:temp_dir_end].strip())
            self.assertTrue(preserved_temp_dir.exists())
            shutil.rmtree(preserved_temp_dir, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
