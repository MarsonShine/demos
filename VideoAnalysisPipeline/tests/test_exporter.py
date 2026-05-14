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
            self.assertIn("backwardStepMs", html)
            self.assertIn("forwardStepMs", html)
            self.assertIn('value="15"', html)
            self.assertIn("moveStartBackwardButton", html)
            self.assertIn("moveStartForwardButton", html)
            self.assertIn("moveEndBackwardButton", html)
            self.assertIn("moveEndForwardButton", html)
            self.assertIn("adjustSegmentBoundary('start_ms', -1)", html)
            self.assertIn("adjustSegmentBoundary('end_ms', 1)", html)
            self.assertIn("saveAdjustmentsButton", html)
            self.assertNotIn("exportAdjustmentsButton", html)
            self.assertIn("postAdjustmentsToApi", html)
            self.assertIn("/api/review/save", html)
            self.assertIn("review-server --output-root output", html)

    def test_review_page_only_anomalies_filter_ignores_benign_flags(self) -> None:
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
                ),
                Segment(
                    sequence_no=1,
                    segment_no=2,
                    text="Hello, Nishal.",
                    start_ms=10_633,
                    end_ms=12_233,
                    text_source="srt",
                    quality_flags=["subtitle_aligned", "alignment_risk", "subtitle_end_shifted"],
                ),
            ]

            export_review_page(
                output_path=output_path,
                video_path="02.mp4",
                segments=segments,
                title="Review",
            )

            html = output_path.read_text(encoding="utf-8")
            self.assertIn("const benignFlags = new Set(['subtitle_aligned']);", html)
            self.assertIn("const anomalyFlags = getAnomalyFlags(segment);", html)
            self.assertIn("anomalyFlags.length === 0", html)
            self.assertIn("Number(item.dataset.index) === index", html)
            self.assertIn("function formatSignedMilliseconds(delta)", html)
            self.assertIn("localStorage.setItem(storageKey, JSON.stringify(payload));", html)
            self.assertIn("localStorage.removeItem(storageKey);", html)
            self.assertIn("commitSavedAdjustments()", html)
            self.assertIn("const defaultApiBase = 'http://127.0.0.1:8765';", html)


if __name__ == "__main__":
    unittest.main()
