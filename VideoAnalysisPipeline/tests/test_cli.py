from __future__ import annotations

import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from video_analysis_pipeline.cli import main
from video_analysis_pipeline.pipeline import ProcessedItem


def _build_config_stub() -> SimpleNamespace:
    return SimpleNamespace(
        asr=SimpleNamespace(provider="faster-whisper"),
        azure_speech=SimpleNamespace(language="en-US"),
        faster_whisper=SimpleNamespace(language="en"),
    )


class CliTests(unittest.TestCase):
    def test_single_segments_routes_to_segment_only_pipeline(self) -> None:
        config = _build_config_stub()
        with patch("video_analysis_pipeline.cli.load_config", return_value=config), patch(
            "video_analysis_pipeline.cli.process_single_video",
            return_value=ProcessedItem(
                sequence_no=1,
                source_mp4=Path("E:\\tmp\\02.mp4"),
                output_dir=Path("E:\\tmp\\output"),
                workbook_path=None,
                review_page_path=Path("E:\\tmp\\output\\review.html"),
                segments=[],
            ),
        ) as process_single_video_mock:
            exit_code = main(
                [
                    "single-segments",
                    "--source-mp4",
                    "E:\\tmp\\clip.mp4",
                    "--output-dir",
                    "E:\\tmp\\output",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertFalse(process_single_video_mock.call_args.kwargs["generate_overview"])
        self.assertIsNone(process_single_video_mock.call_args.kwargs["workbook_output"])

    def test_batch_overview_routes_to_overview_rebuild_pipeline(self) -> None:
        config = _build_config_stub()
        with patch("video_analysis_pipeline.cli.load_config", return_value=config), patch(
            "video_analysis_pipeline.cli.process_batch_overview",
            return_value=[],
        ) as process_batch_overview_mock:
            exit_code = main(
                [
                    "batch-overview",
                    "--output-root",
                    "E:\\tmp\\output",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(process_batch_overview_mock.call_args.kwargs["output_root"], Path("E:\\tmp\\output"))
        self.assertEqual(process_batch_overview_mock.call_args.kwargs["workbook_output"], Path("E:\\tmp\\output") / "dubbing.result.xlsx")

    def test_batch_overview_accepts_legacy_input_root_argument(self) -> None:
        config = _build_config_stub()
        with patch("video_analysis_pipeline.cli.load_config", return_value=config), patch(
            "video_analysis_pipeline.cli.process_batch_overview",
            return_value=[],
        ) as process_batch_overview_mock:
            exit_code = main(
                [
                    "batch-overview",
                    "--input-root",
                    "E:\\tmp\\input",
                    "--output-root",
                    "E:\\tmp\\output",
                ]
            )

        self.assertEqual(exit_code, 0)
        self.assertEqual(process_batch_overview_mock.call_args.kwargs["output_root"], Path("E:\\tmp\\output"))


if __name__ == "__main__":
    unittest.main()
