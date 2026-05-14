from __future__ import annotations

import unittest

from video_analysis_pipeline.config import SegmentationConfig, SubtitleConfig
from video_analysis_pipeline.models import SubtitleSpan, TranscriptUtterance, WordTiming
from video_analysis_pipeline.subtitle_alignment import build_segments_from_subtitles


class SubtitleAlignmentTests(unittest.TestCase):
    def test_subtitle_spans_split_merged_asr_utterance(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Hi, Bean Bean. School is over. Let's go to the playground.",
                start_ms=0,
                end_ms=5120,
                words=[
                    WordTiming(text="Hi,", start_ms=0, end_ms=540),
                    WordTiming(text="Bean", start_ms=560, end_ms=720),
                    WordTiming(text="Bean.", start_ms=720, end_ms=960),
                    WordTiming(text="School", start_ms=1720, end_ms=2120),
                    WordTiming(text="is", start_ms=2120, end_ms=2340),
                    WordTiming(text="over.", start_ms=2340, end_ms=2680),
                    WordTiming(text="Let's", start_ms=3620, end_ms=4240),
                    WordTiming(text="go", start_ms=4240, end_ms=4420),
                    WordTiming(text="to", start_ms=4420, end_ms=4660),
                    WordTiming(text="the", start_ms=4660, end_ms=4740),
                    WordTiming(text="playground.", start_ms=4740, end_ms=5120),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="Hi, Bean Bean. School is over.",
                normalized_text="hi bean bean school is over",
                start_ms=0,
                end_ms=3000,
                confidence=0.95,
                frame_count=10,
            ),
            SubtitleSpan(
                text="Let's go to the playground.",
                normalized_text="let's go to the playground",
                start_ms=3200,
                end_ms=5400,
                confidence=0.96,
                frame_count=8,
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=5_120,
            video_duration_ms=5_250,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertEqual(segments[0].text, "Hi, Bean Bean. School is over.")
        self.assertEqual(segments[1].text, "Let's go to the playground.")
        self.assertEqual(segments[0].text_source, "ocr")
        self.assertEqual(summary["matched_segments"], 2)

    def test_prefers_asr_text_when_ocr_has_single_character_noise(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="What time is it now?",
                start_ms=20_020,
                end_ms=21_740,
                words=[
                    WordTiming(text="What", start_ms=20_020, end_ms=20_640),
                    WordTiming(text="time", start_ms=20_640, end_ms=20_980),
                    WordTiming(text="is", start_ms=20_980, end_ms=21_320),
                    WordTiming(text="it", start_ms=21_320, end_ms=21_460),
                    WordTiming(text="now?", start_ms=21_460, end_ms=21_740),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="What time e is it now?",
                normalized_text="what time e is it now",
                start_ms=19_750,
                end_ms=23_000,
                confidence=0.98,
                frame_count=8,
            )
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=21_740,
            video_duration_ms=23_000,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 1)
        self.assertEqual(segments[0].text, "What time is it now?")
        self.assertEqual(segments[0].text_source, "hybrid")
        self.assertEqual(summary["matched_segments"], 1)

    def test_aligns_numeric_ocr_with_spoken_number_asr(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="It's five o'clock.",
                start_ms=12_680,
                end_ms=13_940,
                words=[
                    WordTiming(text="It's", start_ms=12_680, end_ms=13_300),
                    WordTiming(text="five", start_ms=13_300, end_ms=13_520),
                    WordTiming(text="o", start_ms=13_520, end_ms=13_700),
                    WordTiming(text="'clock.", start_ms=13_700, end_ms=13_940),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="It's 5 o'clock.",
                normalized_text="it's five o clock",
                start_ms=11_625,
                end_ms=15_250,
                confidence=0.99,
                frame_count=12,
            )
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=13_940,
            video_duration_ms=15_250,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 1)
        self.assertEqual(segments[0].text, "It's 5 o'clock.")
        self.assertEqual(segments[0].text_source, "ocr")
        self.assertEqual(summary["matched_segments"], 1)
        self.assertNotIn("alignment_failed", segments[0].quality_flags)
        self.assertEqual(segments[0].source_word_range, [0, 3])

    def test_uses_srt_source_and_alignment_mode_for_srt_segments(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="What time is it now?",
                start_ms=20_020,
                end_ms=21_740,
                words=[
                    WordTiming(text="What", start_ms=20_020, end_ms=20_640),
                    WordTiming(text="time", start_ms=20_640, end_ms=20_980),
                    WordTiming(text="is", start_ms=20_980, end_ms=21_320),
                    WordTiming(text="it", start_ms=21_320, end_ms=21_460),
                    WordTiming(text="now?", start_ms=21_460, end_ms=21_740),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="What time is it now?",
                normalized_text="what time is it now",
                start_ms=19_750,
                end_ms=23_000,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=8,
            )
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=21_740,
            video_duration_ms=23_000,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 1)
        self.assertEqual(segments[0].text_source, "srt")
        self.assertEqual(summary["alignment_mode"], "srt-asr-forced-alignment")

    def test_repairs_only_flagged_segment_using_srt_timing(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="What can we do today?",
                start_ms=24_100,
                end_ms=27_740,
                words=[
                    WordTiming(text="What", start_ms=24_100, end_ms=25_000, confidence=0.97),
                    WordTiming(text="can", start_ms=25_000, end_ms=25_460, confidence=0.99),
                    WordTiming(text="we", start_ms=25_460, end_ms=25_860, confidence=1.00),
                    WordTiming(text="do", start_ms=25_860, end_ms=26_400, confidence=1.00),
                    WordTiming(text="today?", start_ms=26_400, end_ms=27_740, confidence=0.99),
                ],
            ),
            TranscriptUtterance(
                text="Look for Tigers.",
                start_ms=27_740,
                end_ms=30_520,
                words=[
                    WordTiming(text="Look", start_ms=27_740, end_ms=28_180, confidence=0.33),
                    WordTiming(text="for", start_ms=28_180, end_ms=29_960, confidence=0.96),
                    WordTiming(text="Tigers.", start_ms=29_960, end_ms=30_520, confidence=0.12),
                ],
            ),
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="What can we do today?",
                normalized_text="what can we do today",
                start_ms=24_800,
                end_ms=28_366,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=1,
            ),
            SubtitleSpan(
                text="Look for tigers!",
                normalized_text="look for tigers",
                start_ms=29_433,
                end_ms=31_533,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=2,
            ),
        ]

        segments, _, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=30_520,
            video_duration_ms=31_533,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertEqual(segments[0].start_ms, 24_817)
        self.assertEqual(segments[0].end_ms, 28_681)
        self.assertEqual(segments[1].start_ms, 29_433)
        self.assertEqual(segments[1].end_ms, 31_533)
        self.assertIn("targeted_subtitle_timing_repair", segments[1].quality_flags)

    def test_flags_boundary_risk_for_suspicious_neighbor_segments(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="What can we do today?",
                start_ms=24_100,
                end_ms=27_740,
                words=[
                    WordTiming(text="What", start_ms=24_100, end_ms=25_000, confidence=0.97),
                    WordTiming(text="can", start_ms=25_000, end_ms=25_460, confidence=0.99),
                    WordTiming(text="we", start_ms=25_460, end_ms=25_860, confidence=1.00),
                    WordTiming(text="do", start_ms=25_860, end_ms=26_400, confidence=1.00),
                    WordTiming(text="today?", start_ms=26_400, end_ms=27_740, confidence=0.99),
                ],
            ),
            TranscriptUtterance(
                text="Look for Tigers.",
                start_ms=27_740,
                end_ms=30_520,
                words=[
                    WordTiming(text="Look", start_ms=27_740, end_ms=28_180, confidence=0.33),
                    WordTiming(text="for", start_ms=28_180, end_ms=29_960, confidence=0.96),
                    WordTiming(text="Tigers.", start_ms=29_960, end_ms=30_520, confidence=0.12),
                ],
            ),
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="What can we do today?",
                normalized_text="what can we do today",
                start_ms=24_800,
                end_ms=28_366,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=1,
            ),
            SubtitleSpan(
                text="Look for tigers!",
                normalized_text="look for tigers",
                start_ms=29_433,
                end_ms=31_533,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=2,
            ),
        ]

        segments, _, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=30_520,
            video_duration_ms=31_533,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertIn("neighbor_boundary_drift", segments[0].quality_flags)
        self.assertIn("neighbor_boundary_drift", segments[1].quality_flags)
        self.assertIn("edge_word_low_confidence", segments[1].quality_flags)
        self.assertIn("word_duration_outlier", segments[1].quality_flags)


if __name__ == "__main__":
    unittest.main()
