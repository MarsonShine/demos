"""Tests for desktop preflight validation and stable input snapshots.

The module is intentionally ``unittest`` based so it is runnable in the
packaged Python engine without pytest, while remaining pytest-compatible.
"""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from video_analysis_pipeline.desktop_preflight import run_preflight


def _write_valid_item(directory: Path, name: str = "clip", with_mp3: bool = False) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    (directory / f"{name}.mp4").write_bytes(b"video-bytes")
    (directory / f"{name}.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nHello", encoding="utf-8")
    if with_mp3:
        (directory / f"{name}.mp3").write_bytes(b"audio-bytes")


class DesktopPreflightTests(unittest.TestCase):
    def test_empty_directory_returns_written_failure_result(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "empty"
            input_root.mkdir()
            result_file = root / "result.json"

            result = run_preflight(input_root, result_file)

            self.assertFalse(result["passed"])
            self.assertEqual(result["total_items"], 0)
            self.assertEqual(result["snapshot_sha256"], None)
            self.assertEqual(result["errors"][0]["type"], "no_candidates_found")
            self.assertEqual(json.loads(result_file.read_text(encoding="utf-8")), result)

    def test_missing_input_root_returns_atomic_failure_result(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            result_file = root / "nested" / "result.json"
            result = run_preflight(root / "missing", result_file)

            self.assertFalse(result["passed"])
            self.assertTrue(any(error["type"] == "input_root_missing" for error in result["errors"]))
            self.assertEqual(json.loads(result_file.read_text(encoding="utf-8")), result)
            self.assertEqual(list(result_file.parent.glob(".tmp-*")), [])

    def test_valid_structure_has_stable_snapshot_and_posix_relative_paths(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            _write_valid_item(input_root / "season" / "lesson-10")
            _write_valid_item(input_root / "season" / "lesson-2")
            result_file = root / "result.json"

            first = run_preflight(input_root, result_file)
            second = run_preflight(input_root, result_file)

        self.assertTrue(first["passed"])
        self.assertEqual(first["total_items"], 2)
        self.assertEqual(first["snapshot_sha256"], second["snapshot_sha256"])
        self.assertRegex(str(first["snapshot_sha256"]), r"^[0-9a-f]{64}$")
        self.assertEqual([item["relative_dir"] for item in first["items"]], ["season/lesson-2", "season/lesson-10"])
        self.assertTrue(all("\\" not in str(item["relative_dir"]) for item in first["items"]))

    def test_snapshot_changes_when_input_content_changes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            item_dir = input_root / "lesson"
            _write_valid_item(item_dir)
            result_file = root / "result.json"

            first = run_preflight(input_root, result_file)
            (item_dir / "clip.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nChanged", encoding="utf-8")
            second = run_preflight(input_root, result_file)

        self.assertTrue(first["passed"])
        self.assertTrue(second["passed"])
        self.assertNotEqual(first["snapshot_sha256"], second["snapshot_sha256"])

    def test_optional_mp3_is_captured_in_snapshot(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            _write_valid_item(input_root / "lesson", with_mp3=True)

            result = run_preflight(input_root, root / "result.json")

        self.assertTrue(result["passed"])
        item = result["items"][0]
        self.assertIsNotNone(item["source_mp3"])
        self.assertEqual(item["mp3_size_bytes"], len(b"audio-bytes"))
        self.assertIsNotNone(result["snapshot_sha256"])

    def test_reports_all_structural_errors_and_keeps_valid_items(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"

            missing_srt = input_root / "01-missing-srt"
            missing_srt.mkdir(parents=True)
            (missing_srt / "clip.mp4").write_bytes(b"video")

            many_mp4 = input_root / "02-many-mp4"
            many_mp4.mkdir()
            (many_mp4 / "a.mp4").write_bytes(b"a")
            (many_mp4 / "b.mp4").write_bytes(b"b")
            (many_mp4 / "clip.srt").write_text("text", encoding="utf-8")

            many_mp3 = input_root / "03-many-mp3"
            _write_valid_item(many_mp3)
            (many_mp3 / "a.mp3").write_bytes(b"a")
            (many_mp3 / "b.mp3").write_bytes(b"b")

            _write_valid_item(input_root / "04-valid")
            result_file = root / "result.json"
            result = run_preflight(input_root, result_file)

            written = json.loads(result_file.read_text(encoding="utf-8"))

        self.assertFalse(result["passed"])
        self.assertEqual(result["total_items"], 1)
        self.assertEqual(result["items"][0]["relative_dir"], "04-valid")
        self.assertEqual([error["relative_dir"] for error in result["errors"]], [
            "01-missing-srt",
            "02-many-mp4",
            "03-many-mp3",
        ])
        self.assertTrue(all(error["type"] == "invalid_directory_structure" for error in result["errors"]))
        self.assertEqual(result["snapshot_sha256"], None)
        self.assertEqual(written, result)


if __name__ == "__main__":
    unittest.main()
