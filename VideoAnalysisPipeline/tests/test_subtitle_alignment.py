from __future__ import annotations

import unittest

from video_analysis_pipeline.config import SegmentationConfig, SubtitleConfig
from video_analysis_pipeline.models import Segment, SubtitleSpan, TimeRange, TranscriptUtterance, WordTiming
from video_analysis_pipeline.subtitle_alignment import (
    build_segments_from_subtitles,
    flatten_asr_words,
    repair_leading_title_boundary,
)


class SubtitleAlignmentTests(unittest.TestCase):
    def test_repairs_sticky_first_srt_word_after_generated_title_at_silence(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Postal workers. A postal worker.",
                start_ms=0,
                end_ms=6_000,
                words=[
                    WordTiming(text="Postal", start_ms=0, end_ms=1_000, confidence=0.36),
                    WordTiming(text="workers.", start_ms=1_000, end_ms=1_560, confidence=0.42),
                    WordTiming(text="A", start_ms=1_560, end_ms=2_980, confidence=0.10),
                    WordTiming(text="postal", start_ms=4_240, end_ms=4_780, confidence=0.99),
                    WordTiming(text="worker.", start_ms=4_780, end_ms=5_300, confidence=0.99),
                ],
            )
        ]
        title_segment = Segment(
            sequence_no=1,
            segment_no=1,
            text="Postal workers.",
            start_ms=0,
            end_ms=1_479,
            source_word_range=[0, 1],
            quality_flags=["title_segment_from_asr"],
        )
        first_segment = Segment(
            sequence_no=1,
            segment_no=2,
            text="A postal worker.",
            start_ms=1_480,
            end_ms=5_300,
            text_source="srt",
            source_subtitle_index=0,
            source_word_range=[2, 4],
        )
        subtitle_spans = [
            SubtitleSpan(
                text="A postal worker.",
                normalized_text="a postal worker",
                start_ms=3_840,
                end_ms=5_300,
                confidence=1.0,
                frame_count=1,
                source="srt",
            )
        ]
        asr_words = flatten_asr_words(utterances)

        repaired = repair_leading_title_boundary(
            title_segment=title_segment,
            first_aligned_segment=first_segment,
            subtitle_spans=subtitle_spans,
            asr_words=asr_words,
            boundary_silence_ranges=[TimeRange(start_ms=1_933, end_ms=4_240)],
            audio_duration_ms=7_000,
            video_duration_ms=7_000,
        )

        self.assertTrue(repaired)
        self.assertEqual((title_segment.end_ms, first_segment.start_ms), (1_933, 4_240))
        self.assertIn("title_boundary_silence_end", title_segment.quality_flags)
        self.assertIn("title_boundary_silence_start", first_segment.quality_flags)

        unchanged_title = Segment(
            sequence_no=1,
            segment_no=1,
            text="Postal workers.",
            start_ms=0,
            end_ms=1_479,
            source_word_range=[0, 1],
            quality_flags=["title_segment_from_asr"],
        )
        unchanged_first = Segment(
            sequence_no=1,
            segment_no=2,
            text="A postal worker.",
            start_ms=1_480,
            end_ms=5_300,
            text_source="srt",
            source_subtitle_index=0,
            source_word_range=[2, 4],
        )
        self.assertFalse(
            repair_leading_title_boundary(
                title_segment=unchanged_title,
                first_aligned_segment=unchanged_first,
                subtitle_spans=subtitle_spans,
                asr_words=asr_words,
                boundary_silence_ranges=[],
                audio_duration_ms=7_000,
                video_duration_ms=7_000,
            )
        )
        self.assertEqual((unchanged_title.end_ms, unchanged_first.start_ms), (1_479, 1_480))

    def test_repairs_low_confidence_first_word_stuck_before_srt_gap(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Turned blue. He should have kept what he had.",
                start_ms=1_000,
                end_ms=6_000,
                words=[
                    WordTiming(text="Turned", start_ms=1_000, end_ms=1_600, confidence=0.99),
                    WordTiming(text="blue.", start_ms=1_600, end_ms=2_400, confidence=0.99),
                    WordTiming(text="He", start_ms=2_400, end_ms=3_200, confidence=0.43),
                    WordTiming(text="should", start_ms=3_200, end_ms=3_800, confidence=0.99),
                    WordTiming(text="have", start_ms=3_800, end_ms=4_200, confidence=0.99),
                    WordTiming(text="kept", start_ms=4_200, end_ms=4_600, confidence=0.99),
                    WordTiming(text="what", start_ms=4_600, end_ms=5_000, confidence=0.99),
                    WordTiming(text="he", start_ms=5_000, end_ms=5_300, confidence=0.99),
                    WordTiming(text="had.", start_ms=5_300, end_ms=5_800, confidence=0.99),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="Turned blue.",
                normalized_text="turned blue",
                start_ms=1_000,
                end_ms=2_800,
                confidence=1.0,
                frame_count=1,
                source="srt",
            ),
            SubtitleSpan(
                text="He should have kept what he had.",
                normalized_text="he should have kept what he had",
                start_ms=3_300,
                end_ms=6_200,
                confidence=1.0,
                frame_count=1,
                source="srt",
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=7_000,
            video_duration_ms=7_000,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
            low_confidence_boundary_silence_ranges=[TimeRange(start_ms=2_900, end_ms=3_200)],
        )

        self.assertEqual((segments[0].end_ms, segments[1].start_ms), (2_900, 3_200))
        self.assertIn("srt_gap_validated_end", segments[0].quality_flags)
        self.assertIn("srt_gap_validated_start", segments[1].quality_flags)
        self.assertEqual(summary["srt_gap_validated_boundaries"], 1)

        unchanged_segments, unchanged_summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=7_000,
            video_duration_ms=7_000,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertNotIn("srt_gap_validated_end", unchanged_segments[0].quality_flags)
        self.assertNotIn("srt_gap_validated_start", unchanged_segments[1].quality_flags)
        self.assertEqual(unchanged_summary["srt_gap_validated_boundaries"], 0)

    def test_repairs_sticky_asr_boundary_at_silence_inside_srt_gap(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Previous tail. Current line.",
                start_ms=1_000,
                end_ms=4_000,
                words=[
                    WordTiming(text="Previous", start_ms=1_000, end_ms=1_500, confidence=0.99),
                    WordTiming(text="tail.", start_ms=1_500, end_ms=2_000, confidence=0.99),
                    WordTiming(text="Current", start_ms=2_000, end_ms=3_500, confidence=0.70),
                    WordTiming(text="line.", start_ms=3_500, end_ms=4_000, confidence=0.99),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="Previous tail.",
                normalized_text="previous tail",
                start_ms=900,
                end_ms=2_600,
                confidence=1.0,
                frame_count=1,
                source="srt",
            ),
            SubtitleSpan(
                text="Current line.",
                normalized_text="current line",
                start_ms=3_300,
                end_ms=6_300,
                confidence=1.0,
                frame_count=1,
                source="srt",
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=7_000,
            video_duration_ms=7_000,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
            boundary_silence_ranges=[TimeRange(start_ms=2_800, end_ms=3_100)],
        )

        self.assertEqual((segments[0].end_ms, segments[1].start_ms), (2_800, 3_100))
        self.assertIn("silence_validated_end", segments[0].quality_flags)
        self.assertIn("silence_validated_start", segments[1].quality_flags)
        self.assertEqual(summary["silence_validated_boundaries"], 1)

    def test_does_not_repair_when_srt_start_is_inside_boundary_silence(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Previous tail. Current line.",
                start_ms=1_000,
                end_ms=4_000,
                words=[
                    WordTiming(text="Previous", start_ms=1_000, end_ms=1_500, confidence=0.99),
                    WordTiming(text="tail.", start_ms=1_500, end_ms=2_000, confidence=0.99),
                    WordTiming(text="Current", start_ms=2_000, end_ms=3_500, confidence=0.70),
                    WordTiming(text="line.", start_ms=3_500, end_ms=4_000, confidence=0.99),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="Previous tail.",
                normalized_text="previous tail",
                start_ms=900,
                end_ms=2_600,
                confidence=1.0,
                frame_count=1,
                source="srt",
            ),
            SubtitleSpan(
                text="Current line.",
                normalized_text="current line",
                start_ms=3_000,
                end_ms=6_300,
                confidence=1.0,
                frame_count=1,
                source="srt",
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=7_000,
            video_duration_ms=7_000,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
            boundary_silence_ranges=[TimeRange(start_ms=2_800, end_ms=3_100)],
        )

        self.assertNotIn("silence_validated_end", segments[0].quality_flags)
        self.assertNotIn("silence_validated_start", segments[1].quality_flags)
        self.assertEqual(summary["silence_validated_boundaries"], 0)

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
        self.assertEqual(segments[0].text, "What time is it now?")

    def test_keeps_srt_text_even_when_asr_is_similar_but_different(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Can do it.",
                start_ms=5_000,
                end_ms=6_200,
                words=[
                    WordTiming(text="Can", start_ms=5_000, end_ms=5_350),
                    WordTiming(text="do", start_ms=5_350, end_ms=5_700),
                    WordTiming(text="it.", start_ms=5_700, end_ms=6_200),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="I can do it.",
                normalized_text="i can do it",
                start_ms=4_900,
                end_ms=6_400,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=9,
            )
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=6_200,
            video_duration_ms=6_400,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 1)
        self.assertEqual(segments[0].text, "I can do it.")
        self.assertEqual(segments[0].text_source, "srt")
        self.assertNotIn("subtitle_text_normalized_from_asr", segments[0].quality_flags)
        self.assertEqual(summary["matched_segments"], 1)

    def test_srt_alignment_does_not_steal_boundary_words_from_neighbor_segment(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="This yellow duck is for you. Said Tom.",
                start_ms=96_640,
                end_ms=101_400,
                words=[
                    WordTiming(text="This", start_ms=96_640, end_ms=97_760),
                    WordTiming(text="yellow", start_ms=97_760, end_ms=98_120),
                    WordTiming(text="duck", start_ms=98_120, end_ms=98_640),
                    WordTiming(text="is", start_ms=98_640, end_ms=99_120),
                    WordTiming(text="for", start_ms=99_120, end_ms=99_440),
                    WordTiming(text="you.", start_ms=99_440, end_ms=99_900),
                    WordTiming(text="Said", start_ms=100_600, end_ms=101_020),
                    WordTiming(text="Tom.", start_ms=101_020, end_ms=101_400),
                ],
            ),
            TranscriptUtterance(
                text="This green duck is for you too. Said Holly.",
                start_ms=102_200,
                end_ms=109_380,
                words=[
                    WordTiming(text="This", start_ms=102_200, end_ms=102_940),
                    WordTiming(text="green", start_ms=102_940, end_ms=103_500),
                    WordTiming(text="duck", start_ms=103_500, end_ms=104_280),
                    WordTiming(text="is", start_ms=104_280, end_ms=105_120),
                    WordTiming(text="for", start_ms=105_120, end_ms=105_420),
                    WordTiming(text="you", start_ms=105_420, end_ms=105_820),
                    WordTiming(text="too.", start_ms=105_820, end_ms=106_460),
                    WordTiming(text="Said", start_ms=107_600, end_ms=108_160),
                    WordTiming(text="Holly.", start_ms=108_160, end_ms=109_380),
                ],
            ),
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="‘This yellow duck is for you,’ said Tom.",
                normalized_text="this yellow duck is for you said tom",
                start_ms=97_700,
                end_ms=102_500,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=20,
            ),
            SubtitleSpan(
                text="This green duck is for you, too,’ said Holly.",
                normalized_text="this green duck is for you too said holly",
                start_ms=102_500,
                end_ms=109_466,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=21,
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=110_367,
            video_duration_ms=110_318,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertEqual(segments[0].source_word_range, [0, 7])
        self.assertEqual(segments[1].source_word_range, [8, 16])
        self.assertEqual(segments[0].text, "‘This yellow duck is for you,’ said Tom.")
        self.assertEqual(segments[1].text, "This green duck is for you, too,’ said Holly.")
        self.assertEqual(summary["matched_segments"], 2)

    def test_srt_short_span_can_backtrack_to_early_boundary_words(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Here comes everyone. Here comes one boy.",
                start_ms=15_240,
                end_ms=24_100,
                words=[
                    WordTiming(text="Here", start_ms=15_240, end_ms=15_580),
                    WordTiming(text="comes", start_ms=15_580, end_ms=16_200),
                    WordTiming(text="everyone.", start_ms=16_200, end_ms=20_140),
                    WordTiming(text="Here", start_ms=21_480, end_ms=22_480),
                    WordTiming(text="comes", start_ms=22_480, end_ms=22_940),
                    WordTiming(text="one", start_ms=22_940, end_ms=23_560),
                    WordTiming(text="boy.", start_ms=23_560, end_ms=24_100),
                ],
            )
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="here comes ...",
                normalized_text="here comes",
                start_ms=17_700,
                end_ms=18_566,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=5,
            ),
            SubtitleSpan(
                text="everyone!",
                normalized_text="everyone",
                start_ms=19_733,
                end_ms=20_800,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=6,
            ),
            SubtitleSpan(
                text="Here comes one boy,",
                normalized_text="here comes one boy",
                start_ms=22_066,
                end_ms=24_566,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=7,
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=70_082,
            video_duration_ms=70_066,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 3)
        self.assertEqual(segments[0].source_word_range, [0, 1])
        self.assertEqual(segments[1].source_word_range, [2, 2])
        self.assertEqual(segments[2].source_word_range, [3, 6])
        self.assertGreater(segments[0].duration_ms, 500)
        self.assertEqual(summary["matched_segments"], 3)

    def test_relaxes_srt_end_boundary_when_gap_allows(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Miss Millers in the playground.",
                start_ms=55_940,
                end_ms=58_580,
                words=[
                    WordTiming(text="Miss", start_ms=55_940, end_ms=56_920),
                    WordTiming(text="Millers", start_ms=56_920, end_ms=57_540),
                    WordTiming(text="in", start_ms=57_540, end_ms=57_940),
                    WordTiming(text="the", start_ms=57_940, end_ms=58_120),
                    WordTiming(text="playground.", start_ms=58_120, end_ms=58_580),
                ],
            ),
            TranscriptUtterance(
                text="She's already there.",
                start_ms=59_760,
                end_ms=61_540,
                words=[
                    WordTiming(text="She's", start_ms=59_760, end_ms=60_440),
                    WordTiming(text="already", start_ms=60_440, end_ms=60_960),
                    WordTiming(text="there.", start_ms=60_960, end_ms=61_540),
                ],
            ),
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="Miss Miller’s in the playground.",
                normalized_text="miss miller's in the playground",
                start_ms=56_666,
                end_ms=59_233,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=16,
            ),
            SubtitleSpan(
                text="She’s already there.",
                normalized_text="she's already there",
                start_ms=60_066,
                end_ms=61_866,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=17,
            ),
        ]

        segments, summary, _ = build_segments_from_subtitles(
            sequence_no=1,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=70_082,
            video_duration_ms=70_066,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertEqual(segments[0].source_word_range, [0, 4])
        self.assertGreaterEqual(segments[0].end_ms, 59_200)
        self.assertEqual(summary["matched_segments"], 2)

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
        self.assertIn("edge_word_low_confidence", segments[1].quality_flags)
        self.assertIn("word_duration_outlier", segments[1].quality_flags)
        self.assertIn("targeted_subtitle_timing_repair", segments[1].quality_flags)
        self.assertNotIn("neighbor_boundary_drift", segments[0].quality_flags)

    def test_repairs_to_subtitle_timing_when_asr_boundary_words_are_wrong(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="Then, the big penguin comes back with her friends.",
                start_ms=102_320,
                end_ms=105_980,
                words=[
                    WordTiming(text="Then,", start_ms=102_320, end_ms=102_540, confidence=0.99),
                    WordTiming(text="the", start_ms=102_960, end_ms=103_240, confidence=0.99),
                    WordTiming(text="big", start_ms=103_240, end_ms=103_600, confidence=0.98),
                    WordTiming(text="penguin", start_ms=103_600, end_ms=104_400, confidence=0.99),
                    WordTiming(text="comes", start_ms=104_400, end_ms=104_760, confidence=0.99),
                    WordTiming(text="back", start_ms=104_760, end_ms=105_180, confidence=1.00),
                    WordTiming(text="with", start_ms=105_180, end_ms=105_440, confidence=1.00),
                    WordTiming(text="her", start_ms=105_440, end_ms=105_640, confidence=0.99),
                    WordTiming(text="friends.", start_ms=105_640, end_ms=105_980, confidence=1.00),
                ],
            ),
            TranscriptUtterance(
                text="Hello, Michelle.",
                start_ms=106_780,
                end_ms=108_220,
                words=[
                    WordTiming(text="Hello,", start_ms=106_780, end_ms=107_320, confidence=0.89),
                    WordTiming(text="Michelle.", start_ms=107_800, end_ms=108_220, confidence=0.94),
                ],
            ),
            TranscriptUtterance(
                text="Do you like my snowman? Says Michelle.",
                start_ms=108_220,
                end_ms=112_180,
                words=[
                    WordTiming(text="Do", start_ms=108_220, end_ms=108_500, confidence=0.05),
                    WordTiming(text="you", start_ms=108_500, end_ms=109_640, confidence=0.98),
                    WordTiming(text="like", start_ms=109_640, end_ms=109_940, confidence=0.99),
                    WordTiming(text="my", start_ms=109_940, end_ms=110_180, confidence=0.97),
                    WordTiming(text="snowman?", start_ms=110_180, end_ms=110_960, confidence=0.85),
                    WordTiming(text="Says", start_ms=111_480, end_ms=111_820, confidence=0.24),
                    WordTiming(text="Michelle.", start_ms=111_820, end_ms=112_180, confidence=0.06),
                ],
            ),
            TranscriptUtterance(
                text="It's great. They say.",
                start_ms=113_060,
                end_ms=115_900,
                words=[
                    WordTiming(text="It's", start_ms=113_060, end_ms=113_640, confidence=0.84),
                    WordTiming(text="great.", start_ms=113_640, end_ms=114_040, confidence=0.98),
                    WordTiming(text="They", start_ms=115_220, end_ms=115_580, confidence=0.98),
                    WordTiming(text="say.", start_ms=115_580, end_ms=115_900, confidence=1.00),
                ],
            ),
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="Then the big penguin comes back with her friends.",
                normalized_text="then the big penguin comes back with her friends",
                start_ms=102_200,
                end_ms=106_633,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=26,
            ),
            SubtitleSpan(
                text="Hello, Nishal.",
                normalized_text="hello nishal",
                start_ms=106_633,
                end_ms=109_233,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=27,
            ),
            SubtitleSpan(
                text="“Do you like my snowman?” says Nishal.",
                normalized_text="do you like my snowman says nishal",
                start_ms=109_233,
                end_ms=112_800,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=28,
            ),
            SubtitleSpan(
                text="“It’s great!” they say.",
                normalized_text="it's great they say",
                start_ms=112_800,
                end_ms=116_366,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=29,
            ),
        ]

        segments, _, _ = build_segments_from_subtitles(
            sequence_no=5,
            subtitle_spans=subtitle_spans,
            utterances=utterances,
            audio_duration_ms=116_366,
            video_duration_ms=116_366,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 4)
        self.assertEqual(segments[0].source_word_range, [0, 8])
        self.assertNotIn("targeted_subtitle_timing_repair", segments[0].quality_flags)
        self.assertEqual(segments[1].start_ms, 106_634)
        self.assertEqual(segments[1].end_ms, 109_232)
        self.assertIn("targeted_subtitle_timing_repair", segments[1].quality_flags)
        self.assertEqual(segments[2].start_ms, 109_234)
        self.assertEqual(segments[2].end_ms, 112_799)
        self.assertIn("targeted_subtitle_timing_repair", segments[2].quality_flags)
        self.assertEqual(segments[3].start_ms, 112_800)
        self.assertNotIn("targeted_subtitle_timing_repair", segments[3].quality_flags)

    def test_relaxed_srt_boundaries_do_not_leave_adjacent_segments_overlapping(self) -> None:
        utterances = [
            TranscriptUtterance(
                text="I’m afloat in a boat",
                start_ms=10_700,
                end_ms=13_800,
                words=[
                    WordTiming(text="I’m", start_ms=10_700, end_ms=11_300, confidence=0.98),
                    WordTiming(text="afloat", start_ms=11_300, end_ms=12_200, confidence=0.98),
                    WordTiming(text="in", start_ms=12_200, end_ms=12_500, confidence=0.98),
                    WordTiming(text="a", start_ms=12_500, end_ms=12_700, confidence=0.98),
                    WordTiming(text="boat", start_ms=12_700, end_ms=13_800, confidence=0.98),
                ],
            ),
            TranscriptUtterance(
                text="on the wide wide sea",
                start_ms=13_569,
                end_ms=17_300,
                words=[
                    WordTiming(text="on", start_ms=13_569, end_ms=13_900, confidence=0.98),
                    WordTiming(text="the", start_ms=13_900, end_ms=14_200, confidence=0.98),
                    WordTiming(text="wide", start_ms=14_200, end_ms=15_100, confidence=0.98),
                    WordTiming(text="wide", start_ms=15_100, end_ms=16_100, confidence=0.98),
                    WordTiming(text="sea", start_ms=16_100, end_ms=17_300, confidence=0.98),
                ],
            ),
        ]
        subtitle_spans = [
            SubtitleSpan(
                text="I’m afloat in a boat",
                normalized_text="i’m afloat in a boat",
                start_ms=10_700,
                end_ms=13_800,
                confidence=1.0,
                frame_count=1,
                source="srt",
                raw_index=1,
            ),
            SubtitleSpan(
                text="on the wide, wide sea.",
                normalized_text="on the wide wide sea",
                start_ms=14_200,
                end_ms=17_300,
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
            audio_duration_ms=17_300,
            video_duration_ms=17_300,
            segmentation_config=SegmentationConfig(),
            subtitle_config=SubtitleConfig(),
        )

        self.assertEqual(len(segments), 2)
        self.assertLess(segments[0].end_ms, segments[1].start_ms)


if __name__ == "__main__":
    unittest.main()
