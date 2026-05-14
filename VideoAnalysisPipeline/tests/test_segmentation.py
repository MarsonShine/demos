from __future__ import annotations

import unittest

from video_analysis_pipeline.config import SegmentationConfig
from video_analysis_pipeline.models import TimeRange, TranscriptUtterance
from video_analysis_pipeline.segmentation import build_segments


class SegmentationTests(unittest.TestCase):
    def test_keeps_sentences_separate_when_voice_chunks_are_separate(self) -> None:
        utterances = [
            TranscriptUtterance(text="Hi, Binbin. School is over.", start_ms=216, end_ms=3304, confidence=0.91),
            TranscriptUtterance(text="Let's go to the playground.", start_ms=3600, end_ms=5797, confidence=0.93),
            TranscriptUtterance(text="OK.", start_ms=7315, end_ms=8737, confidence=0.97),
        ]
        voice_chunks = [
            TimeRange(start_ms=180, end_ms=3340),
            TimeRange(start_ms=3550, end_ms=5850),
            TimeRange(start_ms=7280, end_ms=8780),
        ]

        segments = build_segments(
            sequence_no=1,
            utterances=utterances,
            non_silent_ranges=voice_chunks,
            audio_duration_ms=32_313,
            video_duration_ms=32_200,
            config=SegmentationConfig(),
        )

        self.assertEqual(len(segments), 3)
        self.assertEqual([segment.text for segment in segments], [item.text for item in utterances])
        self.assertLess(segments[0].start_ms, segments[0].end_ms)
        self.assertLess(segments[1].start_ms, segments[1].end_ms)
        self.assertLess(segments[2].start_ms, segments[2].end_ms)

    def test_merges_utterances_inside_the_same_voice_chunk(self) -> None:
        utterances = [
            TranscriptUtterance(text="Hello", start_ms=1000, end_ms=1400, confidence=0.90),
            TranscriptUtterance(text="world.", start_ms=1450, end_ms=1900, confidence=0.92),
        ]
        voice_chunks = [TimeRange(start_ms=900, end_ms=2100)]

        segments = build_segments(
            sequence_no=1,
            utterances=utterances,
            non_silent_ranges=voice_chunks,
            audio_duration_ms=5_000,
            video_duration_ms=5_000,
            config=SegmentationConfig(),
        )

        self.assertEqual(len(segments), 1)
        self.assertEqual(segments[0].text, "Hello world.")
        self.assertEqual(segments[0].sequence_no, 1)
        self.assertEqual(segments[0].segment_no, 1)

    def test_does_not_merge_complete_sentences_even_in_same_voice_chunk(self) -> None:
        utterances = [
            TranscriptUtterance(text="Hi, Binbin. School is over.", start_ms=216, end_ms=3304, confidence=0.91),
            TranscriptUtterance(text="Let's go to the playground.", start_ms=3600, end_ms=5797, confidence=0.93),
        ]
        voice_chunks = [TimeRange(start_ms=180, end_ms=5850)]

        segments = build_segments(
            sequence_no=1,
            utterances=utterances,
            non_silent_ranges=voice_chunks,
            audio_duration_ms=32_313,
            video_duration_ms=32_200,
            config=SegmentationConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertEqual(segments[0].text, "Hi, Binbin. School is over.")
        self.assertEqual(segments[1].text, "Let's go to the playground.")


if __name__ == "__main__":
    unittest.main()
