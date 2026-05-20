from __future__ import annotations

import hashlib
import json
import locale
import os
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

from video_analysis_pipeline.config import AudioConfig, VideoOutputConfig
from video_analysis_pipeline.models import MediaMetadata, TimeRange
from video_analysis_pipeline.timecode import seconds_to_milliseconds


SILENCE_START_PATTERN = re.compile(r"silence_start:\s*(?P<seconds>\d+(?:\.\d+)?)")
SILENCE_END_PATTERN = re.compile(
    r"silence_end:\s*(?P<seconds>\d+(?:\.\d+)?)\s*\|\s*silence_duration:\s*(?P<duration>\d+(?:\.\d+)?)"
)
TARGET_SIZE_SAFETY_RATIO = 0.985
MIN_AUDIO_BITRATE_KBPS = 32
MIN_VIDEO_BITRATE_KBPS = 64


@dataclass(slots=True)
class BackgroundAudioResult:
    path: Path
    from_cache: bool
    cache_path: Path | None = None


def _decode_subprocess_output(output: bytes | str | None) -> str:
    if output is None:
        return ""
    if isinstance(output, str):
        return output

    preferred_encoding = locale.getpreferredencoding(False)
    candidate_encodings = ["utf-8"]
    if preferred_encoding:
        candidate_encodings.append(preferred_encoding)
    if os.name == "nt":
        candidate_encodings.extend(["mbcs", "cp1252"])

    tried_encodings: set[str] = set()
    for encoding in candidate_encodings:
        normalized_encoding = encoding.lower()
        if normalized_encoding in tried_encodings:
            continue
        tried_encodings.add(normalized_encoding)
        try:
            return output.decode(encoding)
        except (LookupError, UnicodeDecodeError):
            continue

    fallback_encoding = preferred_encoding or "utf-8"
    return output.decode(fallback_encoding, errors="replace")


def _run_text_command(args: list[str]) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(
        args,
        capture_output=True,
        check=False,
    )
    return subprocess.CompletedProcess(
        args=completed.args,
        returncode=completed.returncode,
        stdout=_decode_subprocess_output(completed.stdout),
        stderr=_decode_subprocess_output(completed.stderr),
    )


def run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
    completed = _run_text_command(args)
    if completed.returncode != 0:
        raise RuntimeError(
            "Command failed.\n"
            f"Command: {' '.join(args)}\n"
            f"Exit code: {completed.returncode}\n"
            f"STDOUT:\n{completed.stdout}\n"
            f"STDERR:\n{completed.stderr}"
        )
    return completed


def _calculate_target_bitrate_kbps(
    target_size_kb: int,
    duration_ms: int,
    minimum_kbps: int,
    label: str,
) -> int:
    if target_size_kb <= 0:
        raise ValueError(f"{label} target_size_kb must be greater than 0 to derive bitrate.")
    if duration_ms <= 0:
        raise ValueError(f"{label} duration must be greater than 0 to derive bitrate.")

    bitrate_kbps = int((target_size_kb * 8192 * TARGET_SIZE_SAFETY_RATIO) / duration_ms)
    if bitrate_kbps < minimum_kbps:
        raise ValueError(
            f"{label} target_size_kb={target_size_kb} is too small for duration {duration_ms}ms. "
            f"Required bitrate would fall below {minimum_kbps} kbps."
        )
    return bitrate_kbps


def _resolve_background_audio_bitrate_kbps(config: AudioConfig, source_size_kb: float, duration_ms: int) -> int:
    if config.target_size_ratio > 0:
        target_size_kb = round(source_size_kb * config.target_size_ratio)
        return _calculate_target_bitrate_kbps(
            target_size_kb=target_size_kb,
            duration_ms=duration_ms,
            minimum_kbps=MIN_AUDIO_BITRATE_KBPS,
            label="audio",
        )
    return config.mp3_bitrate_kbps


def _resolve_video_export_bitrates_kbps(config: VideoOutputConfig, metadata: MediaMetadata, target_size_kb: int) -> tuple[int, int]:
    total_bitrate_kbps = _calculate_target_bitrate_kbps(
        target_size_kb=target_size_kb,
        duration_ms=metadata.duration_ms,
        minimum_kbps=1,
        label="video",
    )

    if metadata.audio_streams > 0:
        video_bitrate_kbps = total_bitrate_kbps - config.audio_bitrate_kbps
        if video_bitrate_kbps < MIN_VIDEO_BITRATE_KBPS:
            raise ValueError(
                f"video target_size_kb={target_size_kb} is too small after reserving "
                f"{config.audio_bitrate_kbps} kbps for audio."
            )
        return video_bitrate_kbps, config.audio_bitrate_kbps

    if total_bitrate_kbps < MIN_VIDEO_BITRATE_KBPS:
        raise ValueError(
            f"video target_size_kb={target_size_kb} is too small for duration {metadata.duration_ms}ms."
        )
    return total_bitrate_kbps, 0


def copy_source_video(source_path: Path, target_path: Path, config: VideoOutputConfig | None = None) -> Path:
    target_path.parent.mkdir(parents=True, exist_ok=True)
    if config is not None:
        config.validate()
    if config is not None and config.target_size_ratio > 0:
        source_size_kb = source_path.stat().st_size / 1024
        target_size_kb = round(source_size_kb * config.target_size_ratio)
        source_metadata = probe_media(source_path)
        video_bitrate_kbps, audio_bitrate_kbps = _resolve_video_export_bitrates_kbps(config, source_metadata, target_size_kb)
        actual_target_path = target_path
        if source_path.resolve() == target_path.resolve():
            actual_target_path = target_path.with_name(f"{target_path.stem}.transcoding{target_path.suffix}")

        command = [
            "ffmpeg",
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source_path),
            "-c:v",
            "libx264",
            "-preset",
            "medium",
            "-pix_fmt",
            "yuv420p",
            "-b:v",
            f"{video_bitrate_kbps}k",
            "-maxrate",
            f"{video_bitrate_kbps}k",
            "-bufsize",
            f"{max(video_bitrate_kbps * 2, video_bitrate_kbps)}k",
        ]
        if audio_bitrate_kbps > 0:
            command.extend(["-c:a", "aac", "-b:a", f"{audio_bitrate_kbps}k"])
        else:
            command.append("-an")
        command.extend(["-movflags", "+faststart", str(actual_target_path)])
        run_command(command)
        if actual_target_path != target_path:
            actual_target_path.replace(target_path)
        return target_path

    if source_path.resolve() != target_path.resolve():
        shutil.copy2(source_path, target_path)
    return target_path


def extract_cover(source_path: Path, output_path: Path) -> Path:
    run_command(
        [
            "ffmpeg",
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source_path),
            "-frames:v",
            "1",
            "-q:v",
            "2",
            str(output_path),
        ]
    )
    return output_path


def extract_muted_video(source_path: Path, output_path: Path) -> Path:
    run_command(
        [
            "ffmpeg",
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source_path),
            "-an",
            "-c:v",
            "copy",
            str(output_path),
        ]
    )
    return output_path


def extract_audio_mp3(source_path: Path, output_path: Path) -> Path:
    run_command(
        [
            "ffmpeg",
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-i",
            str(source_path),
            "-vn",
            "-ac",
            "1",
            "-ar",
            "44100",
            "-c:a",
            "libmp3lame",
            "-b:a",
            "192k",
            str(output_path),
        ]
    )
    return output_path


def extract_background_audio_mp3(
    source_path: Path,
    output_path: Path,
    config: AudioConfig,
    cache_key_material: str | None = None,
) -> BackgroundAudioResult:
    config.validate()
    output_path.parent.mkdir(parents=True, exist_ok=True)
    effective_cache_key = cache_key_material or _default_audio_cache_key_material(source_path)
    cache_path = _resolve_bgm_cache_path(config, effective_cache_key)
    if config.cache_enabled and cache_path.exists():
        shutil.copy2(cache_path, output_path)
        return BackgroundAudioResult(path=output_path, from_cache=True, cache_path=cache_path)
    temp_dir = Path(tempfile.mkdtemp(prefix="video-analysis-demucs-"))
    should_cleanup_temp_dir = False
    try:
        source_metadata = probe_media(source_path)
        source_size_kb = source_path.stat().st_size / 1024
        mp3_bitrate_kbps = _resolve_background_audio_bitrate_kbps(config, source_size_kb, source_metadata.duration_ms)
        separation_root = temp_dir / "separated"
        command = [
            sys.executable,
            "-m",
            "demucs.separate",
            "--two-stems=vocals",
            "--mp3",
            "--mp3-bitrate",
            str(mp3_bitrate_kbps),
            "--device",
            config.demucs_device,
            "-n",
            config.demucs_model,
            "-o",
            str(separation_root),
        ]
        if config.jobs > 0:
            command.extend(["-j", str(config.jobs)])
        command.append(str(source_path))

        completed = _run_text_command(command)
        if completed.returncode != 0:
            raise RuntimeError(
                "Demucs background-audio separation failed.\n"
                f"Command: {' '.join(command)}\n"
                f"Temp directory kept for inspection: {temp_dir}\n"
                f"Exit code: {completed.returncode}\n"
                f"STDOUT:\n{completed.stdout}\n"
                f"STDERR:\n{completed.stderr}"
            )

        candidates, expected_dir = _find_demucs_background_candidates(separation_root, config.demucs_model, source_path.stem)
        if not candidates:
            available_files = sorted(
                str(path.relative_to(temp_dir))
                for path in temp_dir.rglob("*")
                if path.is_file()
            )
            raise RuntimeError(
                "Demucs completed but no background stem was produced.\n"
                "Expected one of: no_vocals.*, accompaniment.*\n"
                f"Expected it under: {expected_dir}\n"
                f"Temp directory kept for inspection: {temp_dir}\n"
                f"Files found: {available_files}"
            )

        shutil.copy2(candidates[0], output_path)
        if config.cache_enabled:
            cache_path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(candidates[0], cache_path)
        should_cleanup_temp_dir = True
        return BackgroundAudioResult(
            path=output_path,
            from_cache=False,
            cache_path=cache_path if config.cache_enabled else None,
        )
    finally:
        if should_cleanup_temp_dir:
            shutil.rmtree(temp_dir, ignore_errors=True)


def _resolve_bgm_cache_path(config: AudioConfig, cache_key_material: str) -> Path:
    cache_key = hashlib.sha256(
        (
            f"{cache_key_material}|{config.method}|{config.demucs_model}|{config.demucs_device}|"
            f"{config.mp3_bitrate_kbps}|{config.target_size_ratio}|{config.jobs}"
        ).encode("utf-8")
    ).hexdigest()
    cache_root = Path(config.cache_dir)
    if not cache_root.is_absolute():
        cache_root = Path.cwd() / cache_root
    return cache_root.resolve() / cache_key / "03.mp3"


def _default_audio_cache_key_material(source_path: Path) -> str:
    stats = source_path.stat()
    return f"{source_path.resolve()}|{stats.st_size}|{stats.st_mtime_ns}"


def _find_demucs_background_candidates(
    separation_root: Path,
    model_name: str,
    track_stem: str,
) -> tuple[list[Path], Path]:
    expected_dir = separation_root / model_name / track_stem
    valid_names = {
        "no_vocals.mp3",
        "no_vocals.wav",
        "no_vocals.flac",
        "accompaniment.mp3",
        "accompaniment.wav",
        "accompaniment.flac",
    }

    candidates = sorted(path for path in expected_dir.glob("*") if path.is_file() and path.name in valid_names)
    if candidates:
        return candidates, expected_dir

    candidates = sorted(path for path in separation_root.rglob("*") if path.is_file() and path.name in valid_names)
    return candidates, expected_dir


def probe_media(path: Path) -> MediaMetadata:
    completed = run_command(
        [
            "ffprobe",
            "-v",
            "error",
            "-print_format",
            "json",
            "-show_entries",
            "format=duration:stream=codec_type,width,height,sample_rate,channels",
            str(path),
        ]
    )

    payload = json.loads(completed.stdout)
    streams = payload.get("streams", [])
    video_streams = [stream for stream in streams if stream.get("codec_type") == "video"]
    audio_streams = [stream for stream in streams if stream.get("codec_type") == "audio"]
    first_video = video_streams[0] if video_streams else {}
    first_audio = audio_streams[0] if audio_streams else {}
    duration_ms = seconds_to_milliseconds(float(payload["format"]["duration"]))

    return MediaMetadata(
        path=str(path),
        duration_ms=duration_ms,
        video_streams=len(video_streams),
        audio_streams=len(audio_streams),
        width=int(first_video["width"]) if "width" in first_video else None,
        height=int(first_video["height"]) if "height" in first_video else None,
        sample_rate=int(first_audio["sample_rate"]) if "sample_rate" in first_audio else None,
        channels=int(first_audio["channels"]) if "channels" in first_audio else None,
    )


def detect_silence(
    audio_path: Path,
    total_duration_ms: int,
    silence_threshold_db: float,
    min_silence_duration_ms: int,
) -> tuple[list[TimeRange], list[TimeRange]]:
    completed = _run_text_command(
        [
            "ffmpeg",
            "-hide_banner",
            "-nostats",
            "-i",
            str(audio_path),
            "-af",
            f"silencedetect=noise={silence_threshold_db}dB:d={min_silence_duration_ms / 1000:.3f}",
            "-f",
            "null",
            os.devnull,
        ]
    )

    if completed.returncode != 0:
        raise RuntimeError(
            "ffmpeg silencedetect failed.\n"
            f"STDERR:\n{completed.stderr}"
        )

    silence_ranges: list[TimeRange] = []
    current_start_ms: int | None = None

    for line in completed.stderr.splitlines():
        start_match = SILENCE_START_PATTERN.search(line)
        if start_match:
            current_start_ms = seconds_to_milliseconds(float(start_match.group("seconds")))
            continue

        end_match = SILENCE_END_PATTERN.search(line)
        if end_match and current_start_ms is not None:
            end_ms = seconds_to_milliseconds(float(end_match.group("seconds")))
            silence_ranges.append(TimeRange(start_ms=current_start_ms, end_ms=end_ms))
            current_start_ms = None

    if current_start_ms is not None:
        silence_ranges.append(TimeRange(start_ms=current_start_ms, end_ms=total_duration_ms))

    non_silent_ranges: list[TimeRange] = []
    cursor = 0
    for silence in silence_ranges:
        if silence.start_ms > cursor:
            non_silent_ranges.append(TimeRange(start_ms=cursor, end_ms=silence.start_ms))
        cursor = max(cursor, silence.end_ms)

    if cursor < total_duration_ms:
        non_silent_ranges.append(TimeRange(start_ms=cursor, end_ms=total_duration_ms))

    if not non_silent_ranges:
        non_silent_ranges.append(TimeRange(start_ms=0, end_ms=total_duration_ms))

    return silence_ranges, non_silent_ranges
