from __future__ import annotations

import argparse
import sys
from pathlib import Path

from js_subtitle_converter.converter import convert_js_file_to_srt, convert_js_files_in_batch


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="js-subtitle-converter",
        description="Convert subtitle JS files that contain wordArr/timeArr into sibling SRT files.",
    )
    subparsers = parser.add_subparsers(dest="command", required=True)

    single_parser = subparsers.add_parser("single", help="Convert one subtitle JS file into a sibling SRT file.")
    single_parser.add_argument("--source-js", type=Path, required=True, help="Source JS file that contains wordArr and timeArr.")
    single_parser.add_argument(
        "--resume",
        action="store_true",
        help="Skip conversion when the sibling SRT and progress JSON already show a completed run.",
    )
    single_parser.add_argument(
        "--no-cleanup",
        action="store_true",
        help="Do not delete non-mp4/srt/mp3 files after successful conversion.",
    )

    batch_parser = subparsers.add_parser("batch", help="Convert multiple subtitle JS files, either by scanning a root folder or by passing explicit files.")
    batch_parser.add_argument("--input-root", type=Path, default=None, help="Root folder to scan recursively for subtitle JS files.")
    batch_parser.add_argument(
        "--source-js",
        type=Path,
        action="append",
        default=None,
        help="Explicit subtitle JS file to convert. Repeat this option to pass multiple files.",
    )
    batch_parser.add_argument(
        "--resume",
        action="store_true",
        help="Resume a batch run by skipping files already marked completed in .js_to_srt/batch_progress.json.",
    )
    batch_parser.add_argument(
        "--no-cleanup",
        action="store_true",
        help="Do not delete non-mp4/srt/mp3 files after successful batch conversion.",
    )

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        if args.command == "single":
            result = convert_js_file_to_srt(args.source_js, resume=args.resume, cleanup=not args.no_cleanup)
            if result.skipped:
                print(f"Skipped {result.source_js}")
            else:
                print(f"Converted {result.source_js}")
            print(f"SRT: {result.output_srt}")
            print(f"Progress: {result.progress_path}")
            return 0

        if args.input_root is None and not args.source_js:
            raise ValueError("batch requires --input-root or at least one --source-js.")

        result = convert_js_files_in_batch(
            input_root=args.input_root,
            js_files=args.source_js,
            resume=args.resume,
            cleanup=not args.no_cleanup,
        )
        print(f"Converted items: {len(result.converted)}")
        print(f"Skipped items: {len(result.skipped)}")
        print(f"Progress: {result.progress_path}")
        print(f"Summary: {result.summary_path}")
        return 0
    except Exception as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())