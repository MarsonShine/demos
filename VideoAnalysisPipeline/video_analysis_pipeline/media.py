from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
from pathlib import Path

from video_analysis_pipeline.models import MediaMetadata, TimeRange
from video_analysis_pipeline.timecode import seconds_to_milliseconds


SILENCE_START_PATTERN = re.compile(r"silence_start:\s*(?P<seconds>\d+(?:\.\d+)?)")
SILENCE_END_PATTERN = re.compile(
    r"silence_end:\s*(?P<seconds>\d+(?:\.\d+)?)\s*\|\s*silence_duration:\s*(?P<duration>\d+(?:\.\d+)?)"
)


def run_command(args: list[str]) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(
        args,
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            "Command failed.\n"
            f"Command: {' '.join(args)}\n"
            f"Exit code: {completed.returncode}\n"
            f"STDOUT:\n{completed.stdout}\n"
            f"STDERR:\n{completed.stderr}"
        )
    return completed


def copy_source_video(source_path: Path, target_path: Path) -> Path:
    target_path.parent.mkdir(parents=True, exist_ok=True)
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
    completed = subprocess.run(
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
        ],
        capture_output=True,
        text=True,
        check=False,
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
