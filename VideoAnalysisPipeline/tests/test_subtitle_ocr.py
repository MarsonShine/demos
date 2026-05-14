from __future__ import annotations

import unittest

from video_analysis_pipeline.config import SubtitleConfig
from video_analysis_pipeline.models import SubtitleFrameSample
from video_analysis_pipeline.subtitle_ocr import (
    _build_frame_sample,
    merge_subtitle_samples,
    normalize_subtitle_text,
)


class SubtitleOcrTests(unittest.TestCase):
    def test_normalizes_text_for_matching(self) -> None:
        self.assertEqual(
            normalize_subtitle_text("Hi,  Bean Bean.\nSchool is over!"),
            "hi bean bean school is over",
        )

    def test_merges_consecutive_samples_with_same_text(self) -> None:
        samples = [
            SubtitleFrameSample(timestamp_ms=0, text="Hi, Bean Bean.", normalized_text="hi bean bean", confidence=0.8),
            SubtitleFrameSample(timestamp_ms=250, text="Hi, Bean Bean.", normalized_text="hi bean bean", confidence=0.9),
            SubtitleFrameSample(timestamp_ms=500, text="", normalized_text="", confidence=0.0),
            SubtitleFrameSample(timestamp_ms=750, text="Let's go.", normalized_text="let's go", confidence=0.85),
            SubtitleFrameSample(timestamp_ms=1000, text="Let's go.", normalized_text="let's go", confidence=0.87),
        ]

        spans = merge_subtitle_samples(samples, SubtitleConfig(), frame_interval_ms=250)

        self.assertEqual(len(spans), 2)
        self.assertEqual(spans[0].text, "Hi, Bean Bean.")
        self.assertEqual(spans[0].start_ms, 0)
        self.assertEqual(spans[0].end_ms, 500)
        self.assertEqual(spans[1].text, "Let's go.")

    def test_normalizes_single_digit_numbers_for_matching(self) -> None:
        self.assertEqual(
            normalize_subtitle_text("It's 5 o'clock."),
            "it's five o clock",
        )

    def test_filters_single_numeric_noise_token(self) -> None:
        self.assertEqual(normalize_subtitle_text("0"), "zero")
        self.assertFalse(
            _build_frame_sample(
                timestamp_ms=0,
                ocr_result=[([(0, 0), (10, 0), (10, 10), (0, 10)], "0", 0.8)],
                confidence_threshold=0.35,
            ).normalized_text
        )

    def test_build_frame_sample_orders_boxes_left_to_right_with_vertical_jitter(self) -> None:
        ocr_result = [
            (
                [(120, 30), (180, 30), (180, 55), (120, 55)],
                "School is over.",
                0.98,
            ),
            (
                [(20, 32), (70, 32), (70, 58), (20, 58)],
                "Hi,",
                0.99,
            ),
            (
                [(75, 31), (115, 31), (115, 56), (75, 56)],
                "Bean Bean.",
                0.97,
            ),
        ]

        sample = _build_frame_sample(
            timestamp_ms=0,
            ocr_result=ocr_result,
            confidence_threshold=0.35,
        )

        self.assertEqual(sample.text, "Hi, Bean Bean. School is over.")
        self.assertEqual(sample.normalized_text, "hi bean bean school is over")


if __name__ == "__main__":
    unittest.main()
