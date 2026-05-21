from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from js_subtitle_converter.converter import (
    ConversionBatchResult,
    convert_js_file_to_srt,
    convert_js_files_in_batch,
)


class JsSubtitleConverterTests(unittest.TestCase):
    def test_convert_js_file_to_srt_pairs_word_and_time_entries(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "Unit 1 Story time.js"
            source_path.write_text(
                """
var subtitleJson = {
"wordArr": [
    "Hello， world！",
    "How are you？"
],
"timeArr": [
    "000000",
    "001500",
    "001600",
    "003000"
]
};
""".strip(),
                encoding="utf-8",
            )

            result = convert_js_file_to_srt(source_path)

            self.assertEqual(result.output_srt, source_path.with_suffix(".srt"))
            self.assertTrue(result.output_srt.exists())
            self.assertEqual(
                result.output_srt.read_text(encoding="utf-8"),
                "1\n00:00:00,000 --> 00:00:01,500\nHello, world!\n\n"
                "2\n00:00:01,600 --> 00:00:03,000\nHow are you?\n",
            )

    def test_convert_js_file_to_srt_removes_irrelevant_files_after_success(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source_dir = Path(temp_dir) / "lesson"
            source_dir.mkdir(parents=True)
            source_path = source_dir / "Unit 1 Story time.js"
            source_path.write_text(
                """
var subtitleJson = {
"wordArr": ["Hello， world！"],
"timeArr": ["000000", "001500"]
};
""".strip(),
                encoding="utf-8",
            )
            (source_dir / "clip.mp4").write_text("video", encoding="utf-8")
            (source_dir / "bg.mp3").write_text("audio", encoding="utf-8")
            (source_dir / "note.txt").write_text("temp", encoding="utf-8")

            result = convert_js_file_to_srt(source_path)

            self.assertTrue((source_dir / "clip.mp4").exists())
            self.assertTrue((source_dir / "bg.mp3").exists())
            self.assertTrue(result.output_srt.exists())
            self.assertFalse(source_path.exists())
            self.assertFalse((source_dir / "note.txt").exists())
            self.assertFalse((source_dir / ".js_to_srt").exists())

    def test_convert_js_file_to_srt_can_resume_completed_output(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "Unit 1 Story time.js"
            source_path.write_text(
                """
var subtitleJson = {
"wordArr": ["Hello."],
"timeArr": ["000000", "001500"]
};
""".strip(),
                encoding="utf-8",
            )

            first_result = convert_js_file_to_srt(source_path, cleanup=False)
            second_result = convert_js_file_to_srt(source_path, resume=True)

            self.assertFalse(first_result.skipped)
            self.assertTrue(second_result.skipped)
            self.assertEqual(second_result.entry_count, 1)
            self.assertEqual(second_result.output_srt, source_path.with_suffix(".srt"))

    def test_convert_js_file_to_srt_can_disable_cleanup(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "Unit 1 Story time.js"
            source_path.write_text(
                """
var subtitleJson = {
"wordArr": ["Hello."],
"timeArr": ["000000", "001500"]
};
""".strip(),
                encoding="utf-8",
            )

            result = convert_js_file_to_srt(source_path, cleanup=False)

            self.assertTrue(source_path.exists())
            self.assertTrue(result.progress_path.exists())

    def test_convert_js_files_in_batch_writes_resume_progress(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            input_root = Path(temp_dir) / "input"
            first_dir = input_root / "A"
            second_dir = input_root / "B"
            first_dir.mkdir(parents=True)
            second_dir.mkdir(parents=True)
            first_source = first_dir / "A.js"
            second_source = second_dir / "B.js"
            js_content = """
var subtitleJson = {
"wordArr": ["One line."],
"timeArr": ["000000", "001000"]
};
""".strip()
            first_source.write_text(js_content, encoding="utf-8")
            second_source.write_text(js_content, encoding="utf-8")

            first_result = convert_js_files_in_batch(input_root=input_root, cleanup=False)

            self.assertEqual(len(first_result.converted), 2)
            self.assertEqual(first_result.summary_path, input_root / ".js_to_srt" / "batch_summary.json")
            self.assertTrue(first_result.progress_path.exists())
            progress_payload = json.loads(first_result.progress_path.read_text(encoding="utf-8"))
            self.assertEqual(progress_payload["status"], "completed")
            self.assertEqual(progress_payload["completed_items"], 2)

            second_result = convert_js_files_in_batch(input_root=input_root, resume=True, cleanup=False)

            self.assertEqual(second_result.converted, [])
            self.assertEqual(len(second_result.skipped), 2)

    def test_batch_accepts_explicit_js_files(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            input_root = Path(temp_dir)
            first_source = input_root / "A.js"
            second_source = input_root / "B.js"
            js_content = """
var subtitleJson = {
"wordArr": ["One line."],
"timeArr": ["000000", "001000"]
};
""".strip()
            first_source.write_text(js_content, encoding="utf-8")
            second_source.write_text(js_content, encoding="utf-8")

            result = convert_js_files_in_batch(js_files=[second_source, first_source])

            self.assertEqual([item.source_js.name for item in result.converted], ["A.js", "B.js"])
            self.assertEqual(result.progress_path, input_root / ".js_to_srt" / "batch_progress.json")


class JsSubtitleConverterBatchResultTests(unittest.TestCase):
    def test_batch_result_defaults(self) -> None:
        result = ConversionBatchResult(converted=[], skipped=[], progress_path=Path("progress.json"), summary_path=Path("summary.json"))

        self.assertEqual(result.converted, [])
        self.assertEqual(result.skipped, [])


if __name__ == "__main__":
    unittest.main()