from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from video_analysis_pipeline.config import load_config
from video_analysis_pipeline.pipeline import (
    MOD_FINAL_OUTPUT,
    process_batch,
    process_batch_overview,
    process_single_overview,
    process_single_video,
)
from video_analysis_pipeline.review_api import (
    DEFAULT_REVIEW_SERVER_HOST,
    DEFAULT_REVIEW_SERVER_PORT,
    serve_review_api,
)


_SKIP_STEP_CHOICES = [
    "source-video",
    "cover",
    "muted-video",
    "background-audio",
    "summary",
    "workbook",
    "review-page",
    "csv",
]

_SKIP_STEP_TO_FIELD = {
    "source-video": "export_source_video",
    "cover": "export_cover",
    "muted-video": "export_muted_video",
    "background-audio": "export_background_audio",
    "summary": "generate_summary",
    "workbook": "export_workbook",
    "review-page": "export_review_page",
    "csv": "export_csv",
}

_BITRATE_PATTERN = re.compile(r"^(?P<value>\d+(?:\.\d+)?)\s*(?P<unit>k|kbps)?$", re.IGNORECASE)
_FRAME_SIZE_PATTERN = re.compile(r"^(?P<width>\d+)\s*[xX]\s*(?P<height>\d+)$")


def add_resume_arg(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--resume",
        action="store_true",
        help="Resume a batch run by skipping items already marked completed in output_root/batch_progress.json.",
    )


def add_skip_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--skip",
        nargs="+",
        choices=_SKIP_STEP_CHOICES,
        metavar="STEP",
        default=None,
        help=(
            "Skip one or more pipeline steps. "
            f"Valid steps: {', '.join(_SKIP_STEP_CHOICES)}. "
            "Example: --skip cover summary workbook"
        ),
    )


def add_export_size_args(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "--video-target-size-ratio",
        type=str,
        default=None,
        help=(
            "Optional compression ratio (0 <= ratio <= 1) or explicit video bitrate in kbps for exported 02.mp4. "
            "Examples: 0.0833, 64, 500, 64k, 500kbps. The minimum usable video bitrate floor is 64 kbps."
        ),
    )
    parser.add_argument(
        "--video-audio-bitrate-kbps",
        type=int,
        default=None,
        help=(
            "Optional embedded AAC bitrate in kbps for the audio track inside exported 02.mp4. "
            "For the smallest generally usable 02.mp4, use 32."
        ),
    )
    parser.add_argument(
        "--video-x264-preset",
        type=str,
        default=None,
        help=(
            "Optional libx264 preset for exported 02.mp4. "
            "Valid values: ultrafast, superfast, veryfast, faster, fast, medium, slow, slower, veryslow."
        ),
    )
    parser.add_argument(
        "--video-h264-profile",
        choices=("baseline", "main", "high"),
        default=None,
        help="Optional H.264 profile for exported 02.mp4. Use main to match Main@L3.1 Android-compatible exports.",
    )
    parser.add_argument(
        "--video-h264-level",
        type=str,
        default=None,
        help="Optional H.264 level for exported 02.mp4, for example 3.1.",
    )
    parser.add_argument(
        "--video-keyframe-interval-seconds",
        type=float,
        default=None,
        help=(
            "Optional maximum keyframe interval in seconds for exported 02.mp4. "
            "Use 1 for accurate Android MediaPlayer seeking."
        ),
    )
    parser.add_argument(
        "--video-reference-frames",
        type=int,
        default=None,
        help="Optional H.264 reference-frame count for exported 02.mp4, for example 3.",
    )
    parser.add_argument(
        "--video-mp4-muxer",
        choices=("mp4", "psp"),
        default=None,
        help="Optional MP4 muxer for exported 02.mp4. Use psp to match the screenshot's MPEG-4 (Sony PSP) format profile.",
    )
    parser.add_argument(
        "--video-frame-size",
        type=str,
        default=None,
        help="Optional explicit 02.mp4 frame size in WIDTHxHEIGHT form, for example 1280x720.",
    )
    parser.add_argument(
        "--video-fps",
        type=float,
        default=None,
        help="Optional explicit 02.mp4 frame rate, for example 25.",
    )
    parser.add_argument(
        "--video-audio-sample-rate-hz",
        type=int,
        default=None,
        help="Optional embedded AAC sample rate for 02.mp4, for example 44100.",
    )
    parser.add_argument(
        "--video-audio-channels",
        type=int,
        default=None,
        help="Optional embedded AAC channel count for 02.mp4, for example 2 for stereo.",
    )
    parser.add_argument(
        "--video-audio-bit-depth",
        type=int,
        default=None,
        help="Optional embedded AAC sample size hint for 02.mp4. Currently 32 is supported.",
    )
    parser.add_argument(
        "--audio-target-size-ratio",
        type=str,
        default=None,
        help=(
            "Optional compression ratio (0 <= ratio <= 1) or explicit MP3 bitrate in kbps for exported 03.mp3. "
            "Examples: 0.4380, 32, 64, 128, 32k, 128kbps. The minimum usable background-audio bitrate floor is 32 kbps."
        ),
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="video-analysis-pipeline",
        description="Generate dubbing assets, subtitle-aligned timings, review HTML, and Excel segment data from source videos.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    single_parser = subparsers.add_parser("single", help="Process one MP4 + optional SRT file.")
    single_parser.add_argument("--config", type=Path, default=Path("pipeline_config.json"), help="Path to pipeline_config.json.")
    single_parser.add_argument("--source-mp4", type=Path, required=True, help="Source MP4 file.")
    single_parser.add_argument("--source-srt", type=Path, default=None, help="Optional SRT file. If omitted, the tool auto-discovers a sidecar SRT.")
    single_parser.add_argument("--output-dir", type=Path, required=True, help="Output folder for generated assets and review page.")
    single_parser.add_argument("--template", type=Path, default=None, help="Optional Excel template. Defaults to dubbing.xlsx when present.")
    single_parser.add_argument("--workbook-output", type=Path, default=None, help="Optional output workbook path.")
    single_parser.add_argument("--sequence-no", type=int, default=1, help="Sequence number written into exported rows.")
    single_parser.add_argument("--language", type=str, default=None, help="Override ASR language, for example en or en-US.")
    single_parser.add_argument("--asr-provider", type=str, default=None, help="Override ASR provider, for example faster-whisper or azure-speech.")
    add_export_size_args(single_parser)
    add_skip_args(single_parser)

    single_segments_parser = subparsers.add_parser(
        "single-segments",
        help="Generate only segment resources for one MP4 + optional SRT file without Azure overview workbook export.",
    )
    single_segments_parser.add_argument("--config", type=Path, default=Path("pipeline_config.json"), help="Path to pipeline_config.json.")
    single_segments_parser.add_argument("--source-mp4", type=Path, required=True, help="Source MP4 file.")
    single_segments_parser.add_argument("--source-srt", type=Path, default=None, help="Optional SRT file. If omitted, the tool auto-discovers a sidecar SRT.")
    single_segments_parser.add_argument("--output-dir", type=Path, required=True, help="Output folder for generated assets and review page.")
    single_segments_parser.add_argument("--sequence-no", type=int, default=1, help="Sequence number written into exported rows.")
    single_segments_parser.add_argument("--language", type=str, default=None, help="Override ASR language, for example en or en-US.")
    single_segments_parser.add_argument("--asr-provider", type=str, default=None, help="Override ASR provider, for example faster-whisper or azure-speech.")
    add_export_size_args(single_segments_parser)
    add_skip_args(single_segments_parser)

    single_overview_parser = subparsers.add_parser(
        "single-overview",
        help="Generate or refresh the overview workbook from an existing output folder without rerunning segmentation.",
    )
    single_overview_parser.add_argument("--config", type=Path, default=Path("pipeline_config.json"), help="Path to pipeline_config.json.")
    single_overview_parser.add_argument("--output-dir", type=Path, required=True, help="Existing output folder that already contains manifest.json and segments.json.")
    single_overview_parser.add_argument("--template", type=Path, default=None, help="Optional Excel template. Defaults to dubbing.xlsx when present.")
    single_overview_parser.add_argument("--workbook-output", type=Path, default=None, help="Optional output workbook path.")

    batch_parser = subparsers.add_parser(
        "batch",
        help="Recursively process folders that each contain exactly one MP4 and one SRT.",
    )
    batch_parser.add_argument("--config", type=Path, default=Path("pipeline_config.json"), help="Path to pipeline_config.json.")
    batch_parser.add_argument("--input-root", type=Path, required=True, help="Root folder to scan recursively for per-folder MP4 + SRT pairs.")
    batch_parser.add_argument("--output-root", type=Path, required=True, help="Root output folder. Input-relative subfolders are mirrored here.")
    batch_parser.add_argument("--source-name", type=str, default=None, help="Optional filter: only process folders whose single MP4 matches this filename.")
    batch_parser.add_argument("--srt-name", type=str, default=None, help="Optional filter: only process folders whose single SRT matches this filename.")
    batch_parser.add_argument("--template", type=Path, default=None, help="Optional Excel template. Defaults to dubbing.xlsx when present.")
    batch_parser.add_argument("--workbook-output", type=Path, default=None, help="Optional merged workbook output path.")
    batch_parser.add_argument(
        "--final-output",
        type=str,
        choices=["standard", MOD_FINAL_OUTPUT],
        default="standard",
        help="Final output layout. Use 'mod' to emit dubbing/<sequence_no> folders and movie_dubbing.xlsx.",
    )
    batch_parser.add_argument("--language", type=str, default=None, help="Override ASR language, for example en or en-US.")
    batch_parser.add_argument("--asr-provider", type=str, default=None, help="Override ASR provider, for example faster-whisper or azure-speech.")
    add_export_size_args(batch_parser)
    add_skip_args(batch_parser)
    add_resume_arg(batch_parser)

    batch_segments_parser = subparsers.add_parser(
        "batch-segments",
        help="Recursively generate only segment resources for folders that each contain exactly one MP4 and one SRT.",
    )
    batch_segments_parser.add_argument("--config", type=Path, default=Path("pipeline_config.json"), help="Path to pipeline_config.json.")
    batch_segments_parser.add_argument("--input-root", type=Path, required=True, help="Root folder to scan recursively for per-folder MP4 + SRT pairs.")
    batch_segments_parser.add_argument("--output-root", type=Path, required=True, help="Root output folder. Input-relative subfolders are mirrored here.")
    batch_segments_parser.add_argument("--source-name", type=str, default=None, help="Optional filter: only process folders whose single MP4 matches this filename.")
    batch_segments_parser.add_argument("--srt-name", type=str, default=None, help="Optional filter: only process folders whose single SRT matches this filename.")
    batch_segments_parser.add_argument("--language", type=str, default=None, help="Override ASR language, for example en or en-US.")
    batch_segments_parser.add_argument("--asr-provider", type=str, default=None, help="Override ASR provider, for example faster-whisper or azure-speech.")
    add_export_size_args(batch_segments_parser)
    add_skip_args(batch_segments_parser)
    add_resume_arg(batch_segments_parser)

    batch_overview_parser = subparsers.add_parser(
        "batch-overview",
        help="Generate or refresh overview workbooks from existing output folders without rerunning segmentation.",
    )
    batch_overview_parser.add_argument("--config", type=Path, default=Path("pipeline_config.json"), help="Path to pipeline_config.json.")
    batch_overview_parser.add_argument(
        "--input-root",
        type=Path,
        default=None,
        help="Optional compatibility argument. Overview rebuild reads existing outputs from --output-root and ignores --input-root.",
    )
    batch_overview_parser.add_argument("--output-root", type=Path, required=True, help="Existing output root that already contains generated item folders.")
    batch_overview_parser.add_argument("--template", type=Path, default=None, help="Optional Excel template. Defaults to dubbing.xlsx when present.")
    batch_overview_parser.add_argument("--workbook-output", type=Path, default=None, help="Optional merged workbook output path.")

    review_server_parser = subparsers.add_parser(
        "review-server",
        help="Serve generated review pages and allow in-browser saves to rewrite output artifacts.",
    )
    review_server_parser.add_argument("--output-root", type=Path, default=Path("output"), help="Root output folder that contains review.html files.")
    review_server_parser.add_argument("--host", type=str, default=DEFAULT_REVIEW_SERVER_HOST, help="Host interface for the local review server.")
    review_server_parser.add_argument("--port", type=int, default=DEFAULT_REVIEW_SERVER_PORT, help="Port for the local review server.")

    return parser


def resolve_template_path(template_path: Path | None) -> Path | None:
    if template_path is not None:
        return template_path
    default_template = Path("dubbing.xlsx")
    if default_template.exists():
        return default_template
    return None


def resolve_batch_workbook_output(
    output_root: Path,
    workbook_output: Path | None,
    final_output: str,
) -> Path:
    if workbook_output is not None:
        return workbook_output
    if final_output == MOD_FINAL_OUTPUT:
        return output_root / "movie_dubbing.xlsx"
    return output_root / "dubbing.result.xlsx"


def _resolve_ratio_or_bitrate(value: str, option_name: str) -> tuple[float, int]:
    normalized_value = value.strip().lower()
    if normalized_value in {"max", "middle", "min"}:
        raise ValueError(
            f"{option_name} no longer accepts max/middle/min. "
            "Use a ratio between 0 and 1, or an explicit bitrate like 64, 128, 500, 64k, 128kbps."
        )
    bitrate_match = _BITRATE_PATTERN.fullmatch(normalized_value)
    if bitrate_match is not None:
        parsed_value = float(bitrate_match.group("value"))
        if bitrate_match.group("unit") or parsed_value > 1:
            if parsed_value <= 0:
                raise ValueError(f"{option_name} bitrate values must be greater than 0.")
            return 0.0, max(1, int(round(parsed_value)))
    try:
        parsed_ratio = float(normalized_value)
    except ValueError as exc:
        raise ValueError(
            f"{option_name} must be a ratio between 0 and 1, or an explicit bitrate like 64, 128, 500, 64k, 128kbps."
        ) from exc
    if not 0 <= parsed_ratio <= 1:
        raise ValueError(f"{option_name} ratio values must be between 0 and 1.")
    return parsed_ratio, 0


def resolve_video_target_size_ratio(value: str) -> tuple[float, int]:
    return _resolve_ratio_or_bitrate(value, "video-target-size-ratio")


def resolve_video_frame_size(value: str) -> tuple[int, int]:
    match = _FRAME_SIZE_PATTERN.fullmatch(value.strip())
    if match is None:
        raise ValueError("video-frame-size must use WIDTHxHEIGHT format, for example 1280x720.")
    return int(match.group("width")), int(match.group("height"))


def resolve_audio_target_size_ratio(value: str) -> tuple[float, int]:
    return _resolve_ratio_or_bitrate(value, "audio-target-size-ratio")


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "review-server":
            try:
                serve_review_api(output_root=args.output_root, host=args.host, port=args.port)
                return 0
            except KeyboardInterrupt:
                print("Review server stopped.")
                return 0

        config = load_config(args.config)
        asr_provider = getattr(args, "asr_provider", None)
        if asr_provider:
            config.asr.provider = asr_provider
        language = getattr(args, "language", None)
        if language:
            config.azure_speech.language = language
            config.faster_whisper.language = language
        video_target_size_ratio = getattr(args, "video_target_size_ratio", None)
        if video_target_size_ratio is not None:
            resolved_video_target_ratio, resolved_video_target_bitrate = resolve_video_target_size_ratio(video_target_size_ratio)
            config.video.target_size_ratio = resolved_video_target_ratio
            config.video.target_bitrate_kbps = resolved_video_target_bitrate
        video_audio_bitrate_kbps = getattr(args, "video_audio_bitrate_kbps", None)
        if video_audio_bitrate_kbps is not None:
            config.video.audio_bitrate_kbps = video_audio_bitrate_kbps
        video_x264_preset = getattr(args, "video_x264_preset", None)
        if video_x264_preset is not None:
            config.video.x264_preset = video_x264_preset
        video_h264_profile = getattr(args, "video_h264_profile", None)
        if video_h264_profile is not None:
            config.video.h264_profile = video_h264_profile
        video_h264_level = getattr(args, "video_h264_level", None)
        if video_h264_level is not None:
            config.video.h264_level = video_h264_level
        video_keyframe_interval_seconds = getattr(args, "video_keyframe_interval_seconds", None)
        if video_keyframe_interval_seconds is not None:
            config.video.keyframe_interval_seconds = video_keyframe_interval_seconds
        video_reference_frames = getattr(args, "video_reference_frames", None)
        if video_reference_frames is not None:
            config.video.reference_frames = video_reference_frames
        video_mp4_muxer = getattr(args, "video_mp4_muxer", None)
        if video_mp4_muxer is not None:
            config.video.mp4_muxer = video_mp4_muxer
        video_frame_size = getattr(args, "video_frame_size", None)
        if video_frame_size is not None:
            config.video.frame_width, config.video.frame_height = resolve_video_frame_size(video_frame_size)
        video_fps = getattr(args, "video_fps", None)
        if video_fps is not None:
            config.video.frame_rate = video_fps
        video_audio_sample_rate_hz = getattr(args, "video_audio_sample_rate_hz", None)
        if video_audio_sample_rate_hz is not None:
            config.video.audio_sample_rate_hz = video_audio_sample_rate_hz
        video_audio_channels = getattr(args, "video_audio_channels", None)
        if video_audio_channels is not None:
            config.video.audio_channels = video_audio_channels
        video_audio_bit_depth = getattr(args, "video_audio_bit_depth", None)
        if video_audio_bit_depth is not None:
            config.video.audio_bit_depth = video_audio_bit_depth
        audio_target_size_ratio = getattr(args, "audio_target_size_ratio", None)
        if audio_target_size_ratio is not None:
            resolved_audio_target_ratio, resolved_audio_target_bitrate = resolve_audio_target_size_ratio(audio_target_size_ratio)
            config.audio.target_size_ratio = resolved_audio_target_ratio
            config.audio.target_bitrate_kbps = resolved_audio_target_bitrate
        skip_steps = getattr(args, "skip", None)
        if skip_steps:
            for step in skip_steps:
                setattr(config.steps, _SKIP_STEP_TO_FIELD[step], False)
        template_path = resolve_template_path(getattr(args, "template", None))

        if args.command == "single":
            workbook_output = args.workbook_output or (args.output_dir / "dubbing.result.xlsx")
            result = process_single_video(
                source_mp4=args.source_mp4,
                output_dir=args.output_dir,
                sequence_no=args.sequence_no,
                config=config,
                source_srt=args.source_srt,
                template_path=template_path,
                workbook_output=workbook_output,
            )
            print(f"Processed {result.source_mp4}")
            print(f"Output directory: {result.output_dir}")
            print(f"Workbook: {result.workbook_path}")
            print(f"Review page: {result.review_page_path}")
            print(f"Segments: {len(result.segments)}")
            return 0

        if args.command == "single-segments":
            result = process_single_video(
                source_mp4=args.source_mp4,
                output_dir=args.output_dir,
                sequence_no=args.sequence_no,
                config=config,
                source_srt=args.source_srt,
                template_path=None,
                workbook_output=None,
                generate_overview=False,
            )
            print(f"Processed {result.source_mp4}")
            print(f"Output directory: {result.output_dir}")
            print(f"Review page: {result.review_page_path}")
            print(f"Segments: {len(result.segments)}")
            return 0

        if args.command == "single-overview":
            workbook_output = args.workbook_output or (args.output_dir / "dubbing.result.xlsx")
            result = process_single_overview(
                output_dir=args.output_dir,
                config=config,
                template_path=template_path,
                workbook_output=workbook_output,
            )
            print(f"Output directory: {result.output_dir}")
            print(f"Workbook: {result.workbook_path}")
            print(f"Segments: {len(result.segments)}")
            return 0

        if args.command == "batch":
            workbook_output = resolve_batch_workbook_output(
                output_root=args.output_root,
                workbook_output=args.workbook_output,
                final_output=args.final_output,
            )
            results = process_batch(
                input_root=args.input_root,
                output_root=args.output_root,
                source_name=args.source_name,
                config=config,
                srt_name=args.srt_name,
                template_path=template_path,
                workbook_output=workbook_output,
                final_output=args.final_output,
                resume=args.resume,
            )
            print(f"Processed items: {len(results)}")
            print(f"Workbook: {workbook_output}")
            return 0

        if args.command == "batch-segments":
            results = process_batch(
                input_root=args.input_root,
                output_root=args.output_root,
                source_name=args.source_name,
                config=config,
                srt_name=args.srt_name,
                template_path=None,
                workbook_output=None,
                generate_overview=False,
                resume=args.resume,
            )
            print(f"Processed items: {len(results)}")
            return 0

        workbook_output = args.workbook_output or (args.output_root / "dubbing.result.xlsx")
        results = process_batch_overview(
            output_root=args.output_root,
            config=config,
            template_path=template_path,
            workbook_output=workbook_output,
        )
        print(f"Processed items: {len(results)}")
        print(f"Workbook: {workbook_output}")
        return 0
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
