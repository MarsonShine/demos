from __future__ import annotations

import unittest

from video_analysis_pipeline.config import SegmentationConfig
from video_analysis_pipeline.models import Segment, SubtitleSpan, TranscriptUtterance, WordTiming
from video_analysis_pipeline.pipeline import _build_leading_title_segment


class PipelineTests(unittest.TestCase):
    def test_builds_leading_title_segment_from_asr_before_first_srt(self) -> None:
        subtitle_spans = [
            SubtitleSpan(
                text="Dan finds a big box.",
                normalized_text="dan finds a big box",
                start_ms=5333,
                end_ms=9166,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=2,
            )
        ]
        utterances = [
            TranscriptUtterance(
                text="Looking for Dragons by Richard Brown and Kate Ruttle.",
                start_ms=0,
                end_ms=5320,
                words=[
                    WordTiming(text="Looking", start_ms=0, end_ms=980, confidence=0.40),
                    WordTiming(text="for", start_ms=980, end_ms=1320, confidence=0.84),
                    WordTiming(text="Dragons", start_ms=1320, end_ms=1600, confidence=0.79),
                    WordTiming(text="by", start_ms=1600, end_ms=3000, confidence=0.65),
                    WordTiming(text="Richard", start_ms=3000, end_ms=3360, confidence=0.99),
                    WordTiming(text="Brown", start_ms=3360, end_ms=3800, confidence=0.97),
                    WordTiming(text="and", start_ms=3800, end_ms=4280, confidence=0.92),
                    WordTiming(text="Kate", start_ms=4280, end_ms=4760, confidence=0.88),
                    WordTiming(text="Ruttle.", start_ms=4760, end_ms=5320, confidence=0.86),
                ],
            ),
            TranscriptUtterance(
                text="Dan finds a big box.",
                start_ms=7040,
                end_ms=9240,
                words=[
                    WordTiming(text="Dan", start_ms=7040, end_ms=7400, confidence=0.99),
                    WordTiming(text="finds", start_ms=7400, end_ms=7800, confidence=0.98),
                    WordTiming(text="a", start_ms=7800, end_ms=7940, confidence=0.94),
                    WordTiming(text="big", start_ms=7940, end_ms=8260, confidence=0.99),
                    WordTiming(text="box.", start_ms=8260, end_ms=9120, confidence=0.96),
                ],
            ),
        ]
        aligned_segments = [
            Segment(
                sequence_no=1,
                segment_no=1,
                text="Dan finds a big box.",
                start_ms=7040,
                end_ms=9120,
                text_source="srt",
                source_subtitle_index=0,
                source_word_range=[9, 13],
                source_utterance_indexes=[1],
            )
        ]

        segment = _build_leading_title_segment(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=41_378,
            video_duration_ms=41_533,
            config=SegmentationConfig(),
            aligned_segments=aligned_segments,
        )

        self.assertIsNotNone(segment)
        assert segment is not None
        self.assertEqual(segment.text, "Looking for Dragons by Richard Brown and Kate Ruttle.")
        self.assertEqual(segment.text_source, "asr-title")
        self.assertIn("title_segment_from_asr", segment.quality_flags)
        self.assertEqual(segment.source_word_range, [0, 8])

    def test_title_segment_stops_before_first_aligned_subtitle_words(self) -> None:
        subtitle_spans = [
            SubtitleSpan(
                text="What can we do today?",
                normalized_text="what can we do today",
                start_ms=10880,
                end_ms=13013,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=1,
            )
        ]
        utterances = [
            TranscriptUtterance(
                text="Looking for Dragons by Richard Brown and Kate Ruttle.",
                start_ms=0,
                end_ms=5320,
                words=[
                    WordTiming(text="Looking", start_ms=0, end_ms=980, confidence=0.40),
                    WordTiming(text="for", start_ms=980, end_ms=1320, confidence=0.84),
                    WordTiming(text="Dragons", start_ms=1320, end_ms=1600, confidence=0.79),
                    WordTiming(text="by", start_ms=1600, end_ms=3000, confidence=0.65),
                    WordTiming(text="Richard", start_ms=3000, end_ms=3360, confidence=0.99),
                    WordTiming(text="Brown", start_ms=3360, end_ms=3800, confidence=0.97),
                    WordTiming(text="and", start_ms=3800, end_ms=4280, confidence=0.92),
                    WordTiming(text="Kate", start_ms=4280, end_ms=4760, confidence=0.88),
                    WordTiming(text="Ruttle.", start_ms=4760, end_ms=5320, confidence=0.86),
                ],
            ),
            TranscriptUtterance(
                text="What can we do today? Look for dragons!",
                start_ms=10080,
                end_ms=16140,
                words=[
                    WordTiming(text="What", start_ms=10080, end_ms=10520, confidence=0.95),
                    WordTiming(text="can", start_ms=10520, end_ms=10820, confidence=0.98),
                    WordTiming(text="we", start_ms=10820, end_ms=10980, confidence=0.98),
                    WordTiming(text="do", start_ms=10980, end_ms=11200, confidence=0.98),
                    WordTiming(text="today?", start_ms=11200, end_ms=13020, confidence=0.92),
                    WordTiming(text="Look", start_ms=13890, end_ms=14510, confidence=0.88),
                ],
            ),
        ]
        aligned_segments = [
            Segment(
                sequence_no=1,
                segment_no=1,
                text="What can we do today?",
                start_ms=10015,
                end_ms=13019,
                text_source="srt",
                source_subtitle_index=0,
                source_word_range=[9, 13],
                source_utterance_indexes=[1],
            )
        ]

        segment = _build_leading_title_segment(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=41_378,
            video_duration_ms=41_533,
            config=SegmentationConfig(),
            aligned_segments=aligned_segments,
        )

        self.assertIsNotNone(segment)
        assert segment is not None
        self.assertEqual(segment.text, "Looking for Dragons by Richard Brown and Kate Ruttle.")
        self.assertEqual(segment.source_word_range, [0, 8])
        self.assertLess(segment.end_ms, aligned_segments[0].start_ms)

    def test_title_segment_is_clamped_before_first_aligned_segment_start(self) -> None:
        subtitle_spans = [
            SubtitleSpan(
                text="What can we do today?",
                normalized_text="what can we do today",
                start_ms=10880,
                end_ms=13013,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=1,
            )
        ]
        utterances = [
            TranscriptUtterance(
                text="Looking for Dragons",
                start_ms=0,
                end_ms=9990,
                words=[
                    WordTiming(text="Looking", start_ms=0, end_ms=3200, confidence=0.90),
                    WordTiming(text="for", start_ms=3200, end_ms=6400, confidence=0.90),
                    WordTiming(text="Dragons", start_ms=6400, end_ms=9990, confidence=0.90),
                ],
            ),
            TranscriptUtterance(
                text="What can we do today?",
                start_ms=10080,
                end_ms=13020,
                words=[
                    WordTiming(text="What", start_ms=10080, end_ms=10520, confidence=0.95),
                    WordTiming(text="can", start_ms=10520, end_ms=10820, confidence=0.98),
                    WordTiming(text="we", start_ms=10820, end_ms=10980, confidence=0.98),
                    WordTiming(text="do", start_ms=10980, end_ms=11200, confidence=0.98),
                    WordTiming(text="today?", start_ms=11200, end_ms=13020, confidence=0.92),
                ],
            ),
        ]
        aligned_segments = [
            Segment(
                sequence_no=1,
                segment_no=1,
                text="What can we do today?",
                start_ms=10015,
                end_ms=13019,
                text_source="srt",
                source_subtitle_index=0,
                source_word_range=[3, 7],
                source_utterance_indexes=[1],
            )
        ]

        segment = _build_leading_title_segment(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=41_378,
            video_duration_ms=41_533,
            config=SegmentationConfig(),
            aligned_segments=aligned_segments,
        )

        self.assertIsNotNone(segment)
        assert segment is not None
        self.assertEqual(segment.end_ms, aligned_segments[0].start_ms - 1)


if __name__ == "__main__":
    unittest.main()
