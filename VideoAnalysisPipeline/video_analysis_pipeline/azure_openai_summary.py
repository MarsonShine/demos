from __future__ import annotations

import re
from typing import Iterable

import requests

from video_analysis_pipeline.config import AzureOpenAIConfig


def generate_video_summary(
    title: str,
    text_blocks: Iterable[str],
    config: AzureOpenAIConfig,
) -> str:
    config.validate()

    cleaned_blocks = [block.strip() for block in text_blocks if block and block.strip()]
    if not cleaned_blocks:
        raise ValueError("At least one text block is required to generate a video summary.")

    joined_text = "\n".join(cleaned_blocks)
    if len(joined_text) > config.max_input_chars:
        joined_text = joined_text[: config.max_input_chars].rstrip()

    endpoint = config.endpoint.rstrip("/")
    request_body = {
        "messages": [
            {
                "role": "system",
                "content": (
                    "你是配音资源整理助手。请根据整段字幕内容，输出一条简洁、自然、适合少儿英语绘本视频的中文概览。"
                    "要求：只输出一句中文简介；不要分点；不要引号；不要复述所有细节；默认生成普适性的内容概览；"
                    "控制在 18 到 40 个汉字之间。"
                ),
            },
            {
                "role": "user",
                "content": f"标题：{title}\n请基于下面的完整字幕内容生成一条中文视频简介：\n{joined_text}",
            },
        ],
        "temperature": config.temperature,
        _completion_token_field_name(config.api_version): config.max_output_tokens,
    }
    response = requests.post(
        f"{endpoint}/openai/deployments/{config.deployment}/chat/completions",
        params={"api-version": config.api_version},
        headers={
            "Content-Type": "application/json",
            "api-key": config.api_key,
        },
        json=request_body,
        timeout=90,
    )
    if not response.ok:
        raise ValueError(_build_azure_error_message(response))

    payload = response.json()
    choices = payload.get("choices") or []
    if not choices:
        raise ValueError("Azure OpenAI returned no choices for the summary request.")

    message = choices[0].get("message") or {}
    content = str(message.get("content") or "").strip()
    if not content:
        raise ValueError("Azure OpenAI returned an empty summary.")

    return " ".join(content.split())


def _completion_token_field_name(api_version: str) -> str:
    matched = re.match(r"^(\d{4})-(\d{2})-(\d{2})", api_version.strip())
    if not matched:
        return "max_completion_tokens"
    version_key = int("".join(matched.groups()))
    if version_key >= 20240215:
        return "max_completion_tokens"
    return "max_tokens"


def _build_azure_error_message(response: requests.Response) -> str:
    detail = ""
    try:
        payload = response.json()
    except ValueError:
        payload = None

    if isinstance(payload, dict):
        error_payload = payload.get("error")
        if isinstance(error_payload, dict):
            detail = str(error_payload.get("message") or error_payload.get("code") or "").strip()
        elif payload:
            detail = str(payload).strip()
    if not detail:
        detail = response.text.strip()

    if detail:
        return f"Azure OpenAI request failed ({response.status_code}): {detail}"
    return f"Azure OpenAI request failed ({response.status_code})."
