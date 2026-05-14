from __future__ import annotations

import unittest

from video_analysis_pipeline.timecode import format_timestamp, natural_sort_key, ticks_to_milliseconds


class TimecodeTests(unittest.TestCase):
    def test_format_timestamp(self) -> None:
        self.assertEqual(format_timestamp(1_234), "00:00:01.234")
        self.assertEqual(format_timestamp(3_661_009), "01:01:01.009")

    def test_ticks_to_milliseconds(self) -> None:
        self.assertEqual(ticks_to_milliseconds(10_000), 1)
        self.assertEqual(ticks_to_milliseconds(25_000_000), 2500)

    def test_natural_sort_key(self) -> None:
        values = ["10", "2", "1"]
        self.assertEqual(sorted(values, key=natural_sort_key), ["1", "2", "10"])


if __name__ == "__main__":
    unittest.main()
