from __future__ import annotations

import argparse
import sys
from pathlib import Path

from video_analysis_pipeline.config import load_config
from video_analysis_pipeline.pipeline import (
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
    batch_parser.add_argument("--language", type=str, default=None, help="Override ASR language, for example en or en-US.")
    batch_parser.add_argument("--asr-provider", type=str, default=None, help="Override ASR provider, for example faster-whisper or azure-speech.")

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
            workbook_output = args.workbook_output or (args.output_root / "dubbing.result.xlsx")
            results = process_batch(
                input_root=args.input_root,
                output_root=args.output_root,
                source_name=args.source_name,
                config=config,
                srt_name=args.srt_name,
                template_path=template_path,
                workbook_output=workbook_output,
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
