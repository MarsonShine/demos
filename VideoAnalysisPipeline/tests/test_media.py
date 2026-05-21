from __future__ import annotations

import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from video_analysis_pipeline.config import AudioConfig, VideoOutputConfig
from video_analysis_pipeline.media import (
    _calculate_target_bitrate_kbps,
    _decode_subprocess_output,
    _resolve_bgm_cache_path,
    copy_source_video,
    detect_silence,
    extract_background_audio_mp3,
)
from video_analysis_pipeline.models import MediaMetadata


class MediaTests(unittest.TestCase):
    def test_calculate_target_bitrate_kbps_from_target_size(self) -> None:
        bitrate_kbps = _calculate_target_bitrate_kbps(target_size_kb=955, duration_ms=240_000, minimum_kbps=32, label="audio")

        self.assertEqual(bitrate_kbps, 32)

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

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_uses_demucs_no_vocals_output(self, mock_run: object, mock_probe_media: object) -> None:
        mock_probe_media.return_value = MediaMetadata(path="analysis.mp3", duration_ms=240_000, video_streams=0, audio_streams=1)

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

            self.assertEqual(result.path, output_audio)
            self.assertFalse(result.from_cache)
            self.assertEqual(output_audio.read_bytes(), b"bgm-bytes")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_derives_bitrate_from_target_size(
        self,
        mock_run: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(path="analysis.mp3", duration_ms=240_000, video_streams=0, audio_streams=1)

        def fake_run(args: list[str], capture_output: bool, check: bool) -> subprocess.CompletedProcess[bytes]:
            self.assertEqual(args[args.index("--mp3-bitrate") + 1], "32")
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
            source_audio.write_bytes(b"x" * (9550 * 1024))  # 9550 KB; ratio=0.1 => target=955 KB => bitrate=32

            result = extract_background_audio_mp3(
                source_audio,
                output_audio,
                AudioConfig(target_size_ratio=0.1),
            )

            self.assertEqual(result.path, output_audio)
            self.assertEqual(output_audio.read_bytes(), b"bgm-bytes")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_clamps_to_minimum_bitrate_for_tiny_targets(
        self,
        mock_run: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(path="analysis.mp3", duration_ms=240_000, video_streams=0, audio_streams=1)

        def fake_run(args: list[str], capture_output: bool, check: bool) -> subprocess.CompletedProcess[bytes]:
            self.assertEqual(args[args.index("--mp3-bitrate") + 1], "32")
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
            source_audio.write_bytes(b"x" * (100 * 1024))

            result = extract_background_audio_mp3(
                source_audio,
                output_audio,
                AudioConfig(target_size_ratio=0.01),
            )

            self.assertEqual(result.path, output_audio)
            self.assertEqual(output_audio.read_bytes(), b"bgm-bytes")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_uses_explicit_target_bitrate(
        self,
        mock_run: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(path="analysis.mp3", duration_ms=240_000, video_streams=0, audio_streams=1)

        def fake_run(args: list[str], capture_output: bool, check: bool) -> subprocess.CompletedProcess[bytes]:
            self.assertEqual(args[args.index("--mp3-bitrate") + 1], "64")
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
            source_audio.write_bytes(b"x" * (5000 * 1024))

            result = extract_background_audio_mp3(
                source_audio,
                output_audio,
                AudioConfig(target_bitrate_kbps=64),
            )

            self.assertEqual(result.path, output_audio)
            self.assertEqual(output_audio.read_bytes(), b"bgm-bytes")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_transcodes_when_target_size_is_configured(
        self,
        mock_run_command: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(path="source.mp4", duration_ms=240_000, video_streams=1, audio_streams=1)

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[0], "ffmpeg")
            self.assertEqual(args[args.index("-b:a") + 1], "128k")
            self.assertEqual(args[args.index("-b:v") + 1], "179k")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (9149 * 1024))  # 9149 KB; ratio=1.0 => target=9149 KB => video bitrate=179

            result = copy_source_video(source_video, output_video, VideoOutputConfig(target_size_ratio=1.0, audio_bitrate_kbps=128))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_lowers_audio_bitrate_when_target_is_tight(
        self,
        mock_run_command: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(path="source.mp4", duration_ms=45_000, video_streams=1, audio_streams=1)

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[args.index("-b:a") + 1], "76k")
            self.assertEqual(args[args.index("-b:v") + 1], "64k")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (784 * 1024))

            result = copy_source_video(source_video, output_video, VideoOutputConfig(target_size_ratio=1.0, audio_bitrate_kbps=128))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_uses_minimum_bitrates_when_target_is_too_small(
        self,
        mock_run_command: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(path="source.mp4", duration_ms=120_000, video_streams=1, audio_streams=1)

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[args.index("-b:a") + 1], "32k")
            self.assertEqual(args[args.index("-b:v") + 1], "64k")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (784 * 1024))

            result = copy_source_video(source_video, output_video, VideoOutputConfig(target_size_ratio=1.0, audio_bitrate_kbps=128))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_downscales_when_target_bitrate_cannot_support_source_resolution(
        self,
        mock_run_command: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(
            path="source.mp4",
            duration_ms=60_000,
            video_streams=1,
            audio_streams=1,
            width=854,
            height=480,
        )

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[args.index("-b:a") + 1], "32k")
            self.assertEqual(args[args.index("-b:v") + 1], "64k")
            self.assertEqual(args[args.index("-vf") + 1], "scale=384:216:flags=lanczos")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (3000 * 1024))

            result = copy_source_video(source_video, output_video, VideoOutputConfig(target_size_ratio=0.1, audio_bitrate_kbps=128))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_downscales_small_source_video_instead_of_raising_target_size(
        self,
        mock_run_command: object,
        mock_probe_media: object,
    ) -> None:
        mock_probe_media.return_value = MediaMetadata(
            path="source.mp4",
            duration_ms=60_000,
            video_streams=1,
            audio_streams=1,
            width=854,
            height=480,
        )

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[args.index("-b:a") + 1], "32k")
            self.assertEqual(args[args.index("-b:v") + 1], "64k")
            self.assertEqual(args[args.index("-vf") + 1], "scale=384:216:flags=lanczos")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (1800 * 1024))

            result = copy_source_video(source_video, output_video, VideoOutputConfig(target_size_ratio=0.1, audio_bitrate_kbps=128))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_clamps_explicit_video_bitrate_to_minimum_usable_floor(self, mock_run_command: object, mock_probe_media: object) -> None:
        mock_probe_media.return_value = MediaMetadata(
            path="source.mp4",
            duration_ms=60_000,
            video_streams=1,
            audio_streams=1,
            width=854,
            height=480,
        )

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[args.index("-b:a") + 1], "32k")
            self.assertEqual(args[args.index("-b:v") + 1], "64k")
            self.assertEqual(args[args.index("-vf") + 1], "scale=384:216:flags=lanczos")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (3000 * 1024))

            result = copy_source_video(source_video, output_video, VideoOutputConfig(target_bitrate_kbps=32, audio_bitrate_kbps=32))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_applies_explicit_export_profile(self, mock_run_command: object, mock_probe_media: object) -> None:
        mock_probe_media.return_value = MediaMetadata(
            path="source.mp4",
            duration_ms=60_000,
            video_streams=1,
            audio_streams=1,
            width=1920,
            height=1080,
        )

        def fake_run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
            self.assertEqual(args[args.index("-b:v") + 1], "2000k")
            self.assertEqual(args[args.index("-b:a") + 1], "128k")
            self.assertEqual(args[args.index("-vf") + 1], "scale=1280:720:force_original_aspect_ratio=decrease:flags=lanczos,pad=1280:720:(ow-iw)/2:(oh-ih)/2")
            self.assertEqual(args[args.index("-r") + 1], "25")
            self.assertEqual(args[args.index("-ar") + 1], "44100")
            self.assertEqual(args[args.index("-ac") + 1], "2")
            self.assertEqual(args[args.index("-sample_fmt") + 1], "fltp")
            Path(args[-1]).write_bytes(b"compressed-video")
            return subprocess.CompletedProcess(args=args, returncode=0, stdout="", stderr="")

        mock_run_command.side_effect = fake_run_command

        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"x" * (10_000 * 1024))

            result = copy_source_video(
                source_video,
                output_video,
                VideoOutputConfig(
                    target_bitrate_kbps=2000,
                    audio_bitrate_kbps=128,
                    frame_width=1280,
                    frame_height=720,
                    frame_rate=25,
                    audio_sample_rate_hz=44100,
                    audio_channels=2,
                    audio_bit_depth=32,
                ),
            )

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"compressed-video")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.run_command")
    def test_copy_source_video_copies_source_without_transcoding_when_no_target_ratio_or_bitrate_is_requested(
        self,
        mock_run_command: object,
        mock_probe_media: object,
    ) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            source_video = Path(tmp_dir) / "source.mp4"
            output_video = Path(tmp_dir) / "02.mp4"
            source_video.write_bytes(b"original-video")

            result = copy_source_video(source_video, output_video, VideoOutputConfig(audio_bitrate_kbps=128))

            self.assertEqual(result, output_video)
            self.assertEqual(output_video.read_bytes(), b"original-video")
            mock_probe_media.assert_not_called()
            mock_run_command.assert_not_called()

    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_reuses_cache(self, mock_run: object) -> None:
        mock_run.side_effect = AssertionError("demucs should not run when cache is warm")

        with tempfile.TemporaryDirectory() as tmp_dir:
            cache_dir = Path(tmp_dir) / "cache"
            source_audio = Path(tmp_dir) / "analysis.mp3"
            output_audio = Path(tmp_dir) / "03.mp3"
            source_audio.write_bytes(b"input")
            config = AudioConfig(cache_dir=str(cache_dir))

            cache_key_material = "source-key"
            cached_result_path = _resolve_bgm_cache_path(config, cache_key_material)
            cached_result_path.parent.mkdir(parents=True, exist_ok=True)
            cached_result_path.write_bytes(b"cached-bgm")

            result = extract_background_audio_mp3(source_audio, output_audio, config, cache_key_material=cache_key_material)

            self.assertTrue(result.from_cache)
            self.assertEqual(output_audio.read_bytes(), b"cached-bgm")

    @patch("video_analysis_pipeline.media.probe_media")
    @patch("video_analysis_pipeline.media.subprocess.run")
    def test_extract_background_audio_mp3_keeps_temp_dir_when_output_is_missing(self, mock_run: object, mock_probe_media: object) -> None:
        mock_probe_media.return_value = MediaMetadata(path="analysis.mp3", duration_ms=240_000, video_streams=0, audio_streams=1)
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
