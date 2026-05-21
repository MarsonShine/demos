from __future__ import annotations

import json
import re
from collections.abc import Sequence

import requests

from video_analysis_pipeline.azure_openai_summary import _build_azure_error_message, _completion_token_field_name
from video_analysis_pipeline.config import AzureOpenAIConfig
from video_analysis_pipeline.models import Segment


_JSON_BLOCK_PATTERN = re.compile(r"```(?:json)?\s*(?P<payload>[\[{].*[\]}])\s*```", re.DOTALL)
_PROMPT_OVERHEAD_CHARS = 1_800


def translate_segments_for_education(
    segments: Sequence[Segment],
    config: AzureOpenAIConfig,
) -> list[Segment]:
    config.validate()

    pending_segments = [segment for segment in segments if segment.text.strip() and not _has_translation(segment)]
    if not pending_segments:
        return list(segments)

    translation_map: dict[int, str] = {}
    for batch in _build_translation_batches(pending_segments, config.max_input_chars):
        response_text = _request_segment_translations(batch, config)
        translation_map.update(_parse_translation_response(response_text, batch))

    for segment in segments:
        if not segment.text.strip():
            segment.translated_text = segment.translated_text or ""
            continue
        if _has_translation(segment):
            continue
        translated_text = translation_map.get(segment.segment_no, "").strip()
        if not translated_text:
            raise ValueError(f"Azure OpenAI did not return a translation for segment {segment.segment_no}.")
        segment.translated_text = translated_text
    return list(segments)


def _has_translation(segment: Segment) -> bool:
    return bool((segment.translated_text or "").strip())


def _build_translation_batches(
    segments: Sequence[Segment],
    max_input_chars: int,
) -> list[list[Segment]]:
    batch_limit = max(600, max_input_chars - _PROMPT_OVERHEAD_CHARS)
    batches: list[list[Segment]] = []
    current_batch: list[Segment] = []
    current_chars = 0

    for segment in segments:
        payload = json.dumps(
            {
                "segment_no": segment.segment_no,
                "text": segment.text,
            },
            ensure_ascii=False,
        )
        payload_length = len(payload) + 2
        if current_batch and current_chars + payload_length > batch_limit:
            batches.append(current_batch)
            current_batch = []
            current_chars = 0
        current_batch.append(segment)
        current_chars += payload_length

    if current_batch:
        batches.append(current_batch)
    return batches


def _request_segment_translations(batch: Sequence[Segment], config: AzureOpenAIConfig) -> str:
    endpoint = config.endpoint.rstrip("/")
    request_body = {
        "messages": [
            {
                "role": "system",
                "content": (
                    "你是英语教学视频分段字幕翻译智能体。"
                    "请把每条英文分段字幕翻译成地道、自然、适合教学/教育场景使用的简体中文。"
                    "要求：1. 忠实原意，不遗漏关键信息；2. 用词口语化但规范，适合老师、学生和家长阅读；"
                    "3. 遇到绘本或课堂口吻时，保留轻松自然的表达；4. 不要输出解释、注释、拼音或英文回译；"
                    "5. 严格只输出 JSON，对象结构必须是 {\"translations\":[{\"segment_no\":1,\"translated_text\":\"...\"}]}."
                ),
            },
            {
                "role": "user",
                "content": json.dumps(
                    {
                        "task": "translate_segment_texts",
                        "style": "teaching-friendly, natural simplified Chinese",
                        "segments": [
                            {
                                "segment_no": segment.segment_no,
                                "text": segment.text,
                            }
                            for segment in batch
                        ],
                    },
                    ensure_ascii=False,
                    indent=2,
                ),
            },
        ],
        "temperature": config.temperature,
        _completion_token_field_name(config.api_version): max(config.max_output_tokens, len(batch) * 80),
    }
    response = requests.post(
        f"{endpoint}/openai/deployments/{config.deployment}/chat/completions",
        params={"api-version": config.api_version},
        headers={
            "Content-Type": "application/json",
            "api-key": config.api_key,
        },
        json=request_body,
        timeout=120,
    )
    if not response.ok:
        raise ValueError(_build_azure_error_message(response))

    payload = response.json()
    choices = payload.get("choices") or []
    if not choices:
        raise ValueError("Azure OpenAI returned no choices for the segment translation request.")

    message = choices[0].get("message") or {}
    content = str(message.get("content") or "").strip()
    if not content:
        raise ValueError("Azure OpenAI returned an empty segment translation response.")
    return content


def _parse_translation_response(response_text: str, batch: Sequence[Segment]) -> dict[int, str]:
    payload = _load_json_payload(response_text)
    if isinstance(payload, dict):
        translation_entries = payload.get("translations")
    else:
        translation_entries = payload

    if not isinstance(translation_entries, list):
        raise ValueError("Azure OpenAI translation response must contain a translations array.")

    translations: dict[int, str] = {}
    for entry in translation_entries:
        if not isinstance(entry, dict):
            continue
        segment_no = int(entry.get("segment_no"))
        translated_text = str(entry.get("translated_text") or "").strip()
        if translated_text:
            translations[segment_no] = " ".join(translated_text.split())

    expected_segment_nos = {segment.segment_no for segment in batch}
    missing_segment_nos = expected_segment_nos - set(translations)
    if missing_segment_nos:
        missing_label = ", ".join(str(item) for item in sorted(missing_segment_nos))
        raise ValueError(f"Azure OpenAI translation response is missing segments: {missing_label}.")
    return translations


def _load_json_payload(response_text: str) -> object:
    trimmed = response_text.strip()
    match = _JSON_BLOCK_PATTERN.search(trimmed)
    if match:
        trimmed = match.group("payload").strip()

    try:
        return json.loads(trimmed)
    except json.JSONDecodeError:
        start_positions = [position for position in [trimmed.find("{"), trimmed.find("[")] if position >= 0]
        end_positions = [position for position in [trimmed.rfind("}"), trimmed.rfind("]")] if position >= 0]
        if not start_positions or not end_positions:
            raise ValueError("Azure OpenAI translation response did not contain valid JSON.") from None
        start = min(start_positions)
        end = max(end_positions) + 1
        try:
            return json.loads(trimmed[start:end])
        except json.JSONDecodeError as exc:
            raise ValueError("Azure OpenAI translation response did not contain valid JSON.") from exc