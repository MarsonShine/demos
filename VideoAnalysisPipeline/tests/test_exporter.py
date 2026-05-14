from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from video_analysis_pipeline.exporter import export_review_page
from video_analysis_pipeline.models import Segment


class ExporterTests(unittest.TestCase):
    def test_exports_review_page_with_video_and_segments(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            output_path = Path(tmp_dir) / "review.html"
            segments = [
                Segment(
                    sequence_no=1,
                    segment_no=1,
                    text="Dan finds a big box.",
                    start_ms=5333,
                    end_ms=9166,
                    text_source="srt",
                    quality_flags=["subtitle_aligned"],
                )
            ]

            export_review_page(
                output_path=output_path,
                video_path="02.mp4",
                segments=segments,
                title="Review",
            )

            html = output_path.read_text(encoding="utf-8")
            self.assertIn('src="02.mp4"', html)
            self.assertIn("Dan finds a big box.", html)
            self.assertIn("segments-data", html)
            self.assertIn("playSegment", html)
            self.assertIn("onlyAnomalies", html)


if __name__ == "__main__":
    unittest.main()
