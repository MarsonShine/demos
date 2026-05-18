from __future__ import annotations

import unittest
from unittest.mock import Mock, patch

from video_analysis_pipeline.azure_openai_summary import generate_video_summary
from video_analysis_pipeline.config import AzureOpenAIConfig


class AzureOpenAiSummaryTests(unittest.TestCase):
    @patch("video_analysis_pipeline.azure_openai_summary.requests.post")
    def test_generate_video_summary_calls_chat_completions_once(self, mock_post: Mock) -> None:
        mock_response = Mock()
        mock_response.json.return_value = {
            "choices": [
                {
                    "message": {
                        "content": "丹打开神秘盒子，蹦出一个调皮的杰克玩偶!"
                    }
                }
            ]
        }
        mock_response.ok = True
        mock_post.return_value = mock_response

        summary = generate_video_summary(
            title="Dan's Box",
            text_blocks=["Dan opens the box.", "A jack pops out."],
            config=AzureOpenAIConfig(
                endpoint="https://example.openai.azure.com",
                api_key="test-key",
                deployment="gpt-5.4-mini",
            ),
        )

        self.assertEqual(summary, "丹打开神秘盒子，蹦出一个调皮的杰克玩偶!")
        self.assertEqual(mock_post.call_count, 1)
        request_body = mock_post.call_args.kwargs["json"]
        self.assertEqual(request_body["max_completion_tokens"], 120)
        self.assertEqual(request_body["temperature"], 0.2)
        self.assertIn("完整字幕内容", request_body["messages"][1]["content"])

    @patch("video_analysis_pipeline.azure_openai_summary.requests.post")
    def test_generate_video_summary_surfaces_azure_error_details(self, mock_post: Mock) -> None:
        mock_response = Mock()
        mock_response.ok = False
        mock_response.status_code = 400
        mock_response.json.return_value = {"error": {"message": "Unsupported parameter: max_tokens"}}
        mock_response.text = ""
        mock_post.return_value = mock_response

        with self.assertRaisesRegex(ValueError, "Unsupported parameter: max_tokens"):
            generate_video_summary(
                title="Dan's Box",
                text_blocks=["Dan opens the box."],
                config=AzureOpenAIConfig(
                    endpoint="https://example.openai.azure.com",
                    api_key="test-key",
                    deployment="gpt-5.4-mini",
                ),
            )


if __name__ == "__main__":
    unittest.main()
