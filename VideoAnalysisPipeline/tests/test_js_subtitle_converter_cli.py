from __future__ import annotations

import unittest
from pathlib import Path
from unittest.mock import patch

from js_subtitle_converter.cli import main
from js_subtitle_converter.converter import ConversionBatchResult, ConversionResult


class JsSubtitleConverterCliTests(unittest.TestCase):
    def test_single_routes_resume_flag_to_converter(self) -> None:
        with patch(
            "js_subtitle_converter.cli.convert_js_file_to_srt",
            return_value=ConversionResult(
                source_js=Path("E:\\tmp\\clip.js"),
                output_srt=Path("E:\\tmp\\clip.srt"),
                entry_count=3,
                progress_path=Path("E:\\tmp\\.js_to_srt\\clip.progress.json"),
                timings={},
            ),
        ) as convert_mock:
            exit_code = main([
                "single",
                "--source-js",
                "E:\\tmp\\clip.js",
                "--resume",
            ])

        self.assertEqual(exit_code, 0)
        self.assertTrue(convert_mock.call_args.kwargs["resume"])
        self.assertTrue(convert_mock.call_args.kwargs["cleanup"])

    def test_batch_routes_explicit_js_files(self) -> None:
        with patch(
            "js_subtitle_converter.cli.convert_js_files_in_batch",
            return_value=ConversionBatchResult(
                converted=[],
                skipped=[],
                progress_path=Path("E:\\tmp\\.js_to_srt\\batch_progress.json"),
                summary_path=Path("E:\\tmp\\.js_to_srt\\batch_summary.json"),
            ),
        ) as convert_mock:
            exit_code = main([
                "batch",
                "--source-js",
                "E:\\tmp\\A.js",
                "--source-js",
                "E:\\tmp\\B.js",
                "--resume",
            ])

        self.assertEqual(exit_code, 0)
        self.assertEqual(convert_mock.call_args.kwargs["js_files"], [Path("E:\\tmp\\A.js"), Path("E:\\tmp\\B.js")])
        self.assertTrue(convert_mock.call_args.kwargs["resume"])
        self.assertTrue(convert_mock.call_args.kwargs["cleanup"])

    def test_single_can_disable_cleanup(self) -> None:
        with patch(
            "js_subtitle_converter.cli.convert_js_file_to_srt",
            return_value=ConversionResult(
                source_js=Path("E:\\tmp\\clip.js"),
                output_srt=Path("E:\\tmp\\clip.srt"),
                entry_count=3,
                progress_path=Path("E:\\tmp\\.js_to_srt\\clip.progress.json"),
                timings={},
            ),
        ) as convert_mock:
            exit_code = main([
                "single",
                "--source-js",
                "E:\\tmp\\clip.js",
                "--no-cleanup",
            ])

        self.assertEqual(exit_code, 0)
        self.assertFalse(convert_mock.call_args.kwargs["cleanup"])

    def test_batch_requires_input_root_or_source_js(self) -> None:
        exit_code = main(["batch"])

        self.assertEqual(exit_code, 1)


if __name__ == "__main__":
    unittest.main()