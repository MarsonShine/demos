"""Contract tests for the Python-to-desktop JSONL event stream.

These use :mod:`unittest` so ``python -m unittest discover`` runs them in a
fresh engine.  Pytest also collects ``unittest.TestCase`` classes, so no test
runner-specific dependency is required.
"""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from uuid import uuid4

from video_analysis_pipeline.cli import main as cli_main
from video_analysis_pipeline.desktop_events import EventWriter, SCHEMA_VERSION, _sanitize_error
from video_analysis_pipeline.pipeline import ProcessedItem, process_batch


def _read_events(event_file: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in event_file.read_text(encoding="utf-8").splitlines() if line]


def _make_valid_input_item(directory: Path) -> None:
    directory.mkdir(parents=True, exist_ok=True)
    (directory / "clip.mp4").write_bytes(b"fake")
    (directory / "clip.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nHello", encoding="utf-8")


class DesktopEventWriterTests(unittest.TestCase):
    def test_event_contract_writes_complete_json_lines(self) -> None:
        run_id = str(uuid4())
        with tempfile.TemporaryDirectory() as tmp_dir:
            event_file = Path(tmp_dir) / "events.jsonl"
            writer = EventWriter(event_file)
            writer.run_started(run_id, 3, "/test/input", "/test/output", {"version": "1"})
            writer.item_started(run_id, 3, 0, 1, "video_01")
            writer.stage_changed(run_id, 3, 0, 1, "video_01", "extract-cover", "running")
            writer.item_completed(run_id, 3, 1, 1, "video_01")
            writer.run_completed(run_id, 3, 3)
            writer.close()

            events = _read_events(event_file)

        self.assertEqual([event["event"] for event in events], [
            "run_started",
            "item_started",
            "stage_changed",
            "item_completed",
            "run_completed",
        ])
        for event in events:
            self.assertEqual(event["schema_version"], SCHEMA_VERSION)
            self.assertEqual(event["run_id"], run_id)
            self.assertIn("timestamp_utc", event)
        self.assertEqual(events[0]["preset"], {"version": "1"})
        self.assertEqual(events[2]["details"], {})
        self.assertEqual(events[-1]["status"], "completed")

    def test_run_failed_event_contains_context_and_sanitizes_secret(self) -> None:
        run_id = str(uuid4())
        with tempfile.TemporaryDirectory() as tmp_dir:
            event_file = Path(tmp_dir) / "events.jsonl"
            writer = EventWriter(event_file)
            writer.run_failed(
                run_id=run_id,
                total_items=5,
                completed_items=2,
                item_index=3,
                relative_dir="video_03",
                error_summary="Something went wrong with api_key=sk-abc123secret",
                failure_phase="item",
            )
            writer.close()
            event = _read_events(event_file)[0]

        self.assertEqual(event["event"], "run_failed")
        self.assertEqual(event["item_index"], 3)
        self.assertEqual(event["relative_dir"], "video_03")
        self.assertEqual(event["failure_phase"], "item")
        self.assertIn("api_key=***REDACTED***", str(event["error_summary"]))
        self.assertNotIn("sk-abc123secret", str(event["error_summary"]))

    def test_event_file_is_flushed_per_line(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            event_file = Path(tmp_dir) / "events.jsonl"
            writer = EventWriter(event_file)
            writer.run_started(run_id="run", total_items=1, input_root="/i", output_root="/o")

            content = event_file.read_text(encoding="utf-8")
            self.assertIn("run_started", content)
            self.assertTrue(content.endswith("\n"))
            self.assertEqual(len(_read_events(event_file)), 1)
            writer.close()

    def test_sanitize_error_handles_common_secret_patterns(self) -> None:
        self.assertEqual(_sanitize_error("api-key=12345"), "api-key=***REDACTED***")
        self.assertEqual(_sanitize_error("subscription_key=abcdef"), "subscription_key=***REDACTED***")
        self.assertEqual(_sanitize_error("sk-1234567890123456"), "sk-***REDACTED***")
        self.assertEqual(_sanitize_error("File not found"), "File not found")


class BatchFailureEventTests(unittest.TestCase):
    def test_initialization_failure_emits_terminal_event(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            _make_valid_input_item(input_root / "lesson-01")
            output_root = root / "output-file"
            output_root.write_text("not a directory", encoding="utf-8")
            event_file = root / "events.jsonl"
            run_id = str(uuid4())

            with self.assertRaises(FileExistsError):
                process_batch(
                    input_root=input_root,
                    output_root=output_root,
                    source_name=None,
                    config=object(),
                    generate_overview=False,
                    event_file=event_file,
                    run_id=run_id,
                )

            events = _read_events(event_file)

        self.assertEqual([event["event"] for event in events], ["run_started", "run_failed"])
        self.assertEqual(events[-1]["failure_phase"], "initialize")
        self.assertIsNone(events[-1]["item_index"])
        self.assertIsNone(events[-1]["relative_dir"])

    def test_discovery_failure_emits_run_failed_before_run_started(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            broken_item = input_root / "broken"
            broken_item.mkdir(parents=True)
            (broken_item / "clip.mp4").write_bytes(b"fake")
            event_file = root / "events.jsonl"
            run_id = str(uuid4())

            with self.assertRaisesRegex(RuntimeError, "exactly one MP4"):
                process_batch(
                    input_root=input_root,
                    output_root=root / "output",
                    source_name=None,
                    config=object(),
                    generate_overview=False,
                    event_file=event_file,
                    run_id=run_id,
                )

            events = _read_events(event_file)

        self.assertEqual(len(events), 1)
        failure = events[0]
        self.assertEqual(failure["event"], "run_failed")
        self.assertEqual(failure["run_id"], run_id)
        self.assertEqual(failure["total_items"], 0)
        self.assertIsNone(failure["item_index"])
        self.assertIsNone(failure["relative_dir"])
        self.assertEqual(failure["failure_phase"], "discover")

    def test_item_failure_emits_active_item_context(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            item_dir = input_root / "lesson-01"
            item_dir.mkdir(parents=True)
            (item_dir / "clip.mp4").write_bytes(b"fake")
            (item_dir / "clip.srt").write_text("1\n00:00:00,000 --> 00:00:01,000\nHello", encoding="utf-8")
            event_file = root / "events.jsonl"
            run_id = str(uuid4())

            with patch(
                "video_analysis_pipeline.pipeline.process_single_video",
                side_effect=RuntimeError("api_key=should-not-leak"),
            ):
                with self.assertRaisesRegex(RuntimeError, "should-not-leak"):
                    process_batch(
                        input_root=input_root,
                        output_root=root / "output",
                        source_name=None,
                        config=object(),
                        generate_overview=False,
                        event_file=event_file,
                        run_id=run_id,
                    )

            events = _read_events(event_file)
            batch_progress = json.loads((root / "output" / "batch_progress.json").read_text(encoding="utf-8"))

        self.assertEqual([event["event"] for event in events], ["run_started", "item_started", "run_failed"])
        failure = events[-1]
        self.assertEqual(failure["item_index"], 1)
        self.assertEqual(failure["relative_dir"], "lesson-01")
        self.assertEqual(failure["failure_phase"], "item")
        self.assertIn("api_key=***REDACTED***", str(failure["error_summary"]))
        self.assertEqual(batch_progress["status"], "failed")
        self.assertEqual(batch_progress["items"][0]["status"], "failed")

    def test_finalization_failure_has_no_stale_item_context(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            item_dir = input_root / "lesson-01"
            _make_valid_input_item(item_dir)
            event_file = root / "events.jsonl"
            run_id = str(uuid4())
            processed = ProcessedItem(
                sequence_no=1,
                source_mp4=item_dir / "clip.mp4",
                output_dir=root / "output" / "lesson-01",
                workbook_path=None,
                review_page_path=None,
                segments=[],
            )

            with patch("video_analysis_pipeline.pipeline.process_single_video", return_value=processed), patch(
                "video_analysis_pipeline.pipeline.export_workbook",
                side_effect=RuntimeError("workbook write failed"),
            ):
                with self.assertRaisesRegex(RuntimeError, "workbook write failed"):
                    process_batch(
                        input_root=input_root,
                        output_root=root / "output",
                        source_name=None,
                        config=object(),
                        workbook_output=root / "output" / "dubbing.result.xlsx",
                        event_file=event_file,
                        run_id=run_id,
                    )

            events = _read_events(event_file)

        self.assertEqual([event["event"] for event in events], [
            "run_started",
            "item_started",
            "item_completed",
            "run_failed",
        ])
        failure = events[-1]
        self.assertEqual(failure["failure_phase"], "finalize")
        self.assertEqual(failure["completed_items"], 1)
        self.assertIsNone(failure["item_index"])
        self.assertIsNone(failure["relative_dir"])

    def test_cli_setup_failure_emits_fallback_event(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            event_file = root / "events.jsonl"
            run_id = str(uuid4())
            exit_code = cli_main(
                [
                    "batch",
                    "--config",
                    str(root / "missing-config.json"),
                    "--input-root",
                    str(root / "input"),
                    "--output-root",
                    str(root / "output"),
                    "--event-file",
                    str(event_file),
                    "--run-id",
                    run_id,
                ]
            )
            events = _read_events(event_file)

        self.assertEqual(exit_code, 1)
        self.assertEqual(len(events), 1)
        self.assertEqual(events[0]["event"], "run_failed")
        self.assertEqual(events[0]["failure_phase"], "cli")
        self.assertEqual(events[0]["run_id"], run_id)

    def test_cli_does_not_duplicate_pipeline_failure_event(self) -> None:
        with tempfile.TemporaryDirectory() as tmp_dir:
            root = Path(tmp_dir)
            input_root = root / "input"
            broken_item = input_root / "broken"
            broken_item.mkdir(parents=True)
            (broken_item / "clip.mp4").write_bytes(b"fake")
            event_file = root / "events.jsonl"
            run_id = str(uuid4())
            config_path = Path(__file__).resolve().parents[1] / "pipeline_config.json"

            exit_code = cli_main(
                [
                    "batch",
                    "--config",
                    str(config_path),
                    "--input-root",
                    str(input_root),
                    "--output-root",
                    str(root / "output"),
                    "--event-file",
                    str(event_file),
                    "--run-id",
                    run_id,
                ]
            )
            events = _read_events(event_file)

        self.assertEqual(exit_code, 1)
        self.assertEqual([event["event"] for event in events], ["run_failed"])
        self.assertEqual(events[0]["failure_phase"], "discover")


if __name__ == "__main__":
    unittest.main()
