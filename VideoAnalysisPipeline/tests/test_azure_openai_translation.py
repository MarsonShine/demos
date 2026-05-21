from __future__ import annotations

import unittest
from unittest.mock import Mock, patch

from video_analysis_pipeline.azure_openai_translation import translate_segments_for_education
from video_analysis_pipeline.config import AzureOpenAIConfig
from video_analysis_pipeline.models import Segment


class AzureOpenAiTranslationTests(unittest.TestCase):
    @patch("video_analysis_pipeline.azure_openai_translation.requests.post")
    def test_translate_segments_for_education_populates_translated_text(self, mock_post: Mock) -> None:
        mock_response = Mock()
        mock_response.ok = True
        mock_response.json.return_value = {
            "choices": [
                {
                    "message": {
                        "content": (
                            '{"translations":['
                            '{"segment_no":1,"translated_text":"丹发现了一个大箱子。"},'
                            '{"segment_no":2,"translated_text":"他正四处找小龙。"}'
                            ']}'
                        )
                    }
                }
            ]
        }
        mock_post.return_value = mock_response

        segments = [
            Segment(sequence_no=1, segment_no=1, text="Dan finds a big box.", start_ms=0, end_ms=1_000),
            Segment(sequence_no=1, segment_no=2, text="He is looking for dragons.", start_ms=1_000, end_ms=2_000),
        ]

        translated_segments = translate_segments_for_education(
            segments=segments,
            config=AzureOpenAIConfig(
                endpoint="https://example.openai.azure.com",
                api_key="test-key",
                deployment="gpt-5.4-mini",
            ),
        )

        self.assertEqual(translated_segments[0].translated_text, "丹发现了一个大箱子。")
        self.assertEqual(translated_segments[1].translated_text, "他正四处找小龙。")
        request_body = mock_post.call_args.kwargs["json"]
        self.assertIn("translate_segment_texts", request_body["messages"][1]["content"])
        self.assertIn("教学/教育场景", request_body["messages"][0]["content"])
        self.assertGreaterEqual(request_body["max_completion_tokens"], 160)

    @patch("video_analysis_pipeline.azure_openai_translation.requests.post")
    def test_translate_segments_for_education_raises_on_missing_segment(self, mock_post: Mock) -> None:
        mock_response = Mock()
        mock_response.ok = True
        mock_response.json.return_value = {
            "choices": [
                {
                    "message": {
                        "content": '{"translations":[{"segment_no":1,"translated_text":"丹发现了一个大箱子。"}]}'
                    }
                }
            ]
        }
        mock_post.return_value = mock_response

        with self.assertRaisesRegex(ValueError, "missing segments: 2"):
            translate_segments_for_education(
                segments=[
                    Segment(sequence_no=1, segment_no=1, text="Dan finds a big box.", start_ms=0, end_ms=1_000),
                    Segment(sequence_no=1, segment_no=2, text="He is looking for dragons.", start_ms=1_000, end_ms=2_000),
                ],
                config=AzureOpenAIConfig(
                    endpoint="https://example.openai.azure.com",
                    api_key="test-key",
                    deployment="gpt-5.4-mini",
                ),
            )


if __name__ == "__main__":
    unittest.main()