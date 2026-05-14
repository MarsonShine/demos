from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from video_analysis_pipeline.subtitle_srt import discover_srt_path, parse_srt_file


class SubtitleSrtTests(unittest.TestCase):
    def test_parse_srt_file_builds_subtitle_spans(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            srt_path = Path(tmp_dir) / "sample.srt"
            srt_path.write_text(
                "\n".join(
                    [
                        "1",
                        "00:00:00,500 --> 00:00:02,000",
                        "<i>Hi, Bean Bean.</i>",
                        "School is over.",
                        "",
                        "2",
                        "00:00:03,100 --> 00:00:04,200",
                        "{\\an8}Let's go to the playground.",
                        "",
                    ]
                ),
                encoding="utf-8",
            )

            spans = parse_srt_file(srt_path)

            self.assertEqual(len(spans), 2)
            self.assertEqual(spans[0].text, "Hi, Bean Bean. School is over.")
            self.assertEqual(spans[0].normalized_text, "hi bean bean school is over")
            self.assertEqual(spans[0].start_ms, 500)
            self.assertEqual(spans[0].end_ms, 2000)
            self.assertEqual(spans[0].source, "srt")
            self.assertEqual(spans[0].raw_index, 1)
            self.assertEqual(spans[1].text, "Let's go to the playground.")

    def test_filters_leading_ai_generated_boilerplate(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            srt_path = Path(tmp_dir) / "sample.srt"
            srt_path.write_text(
                "\n".join(
                    [
                        "1",
                        "00:00:00,000 --> 00:01:18,166",
                        "AI 生成",
                        "",
                        "2",
                        "00:00:05,333 --> 00:00:09,166",
                        "Dan finds a big box.",
                        "",
                    ]
                ),
                encoding="utf-8",
            )

            spans = parse_srt_file(srt_path)

            self.assertEqual(len(spans), 1)
            self.assertEqual(spans[0].raw_index, 2)
            self.assertEqual(spans[0].text, "Dan finds a big box.")

    def test_discovers_same_stem_srt(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            temp_path = Path(tmp_dir)
            source_mp4 = temp_path / "02.mp4"
            source_mp4.write_bytes(b"fake")
            source_srt = temp_path / "02.srt"
            source_srt.write_text("", encoding="utf-8")

            resolved = discover_srt_path(source_mp4=source_mp4)

            self.assertEqual(resolved, source_srt)

    def test_raises_when_multiple_srt_candidates_exist(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            temp_path = Path(tmp_dir)
            source_mp4 = temp_path / "02.mp4"
            source_mp4.write_bytes(b"fake")
            (temp_path / "02.en.srt").write_text("", encoding="utf-8")
            (temp_path / "02.zh.srt").write_text("", encoding="utf-8")

            with self.assertRaises(RuntimeError):
                discover_srt_path(source_mp4=source_mp4)


if __name__ == "__main__":
    unittest.main()
