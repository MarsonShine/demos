from __future__ import annotations

import argparse
import sys
from pathlib import Path

from video_analysis_pipeline.config import load_config
from video_analysis_pipeline.pipeline import process_batch, process_single_video


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
        config = load_config(args.config)
        if args.asr_provider:
            config.asr.provider = args.asr_provider
        if args.language:
            config.azure_speech.language = args.language
            config.faster_whisper.language = args.language
        template_path = resolve_template_path(args.template)

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
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
