from __future__ import annotations

import json
from pathlib import Path
from threading import Event

from video_analysis_pipeline.config import AzureSpeechConfig
from video_analysis_pipeline.models import TranscriptUtterance, WordTiming
from video_analysis_pipeline.timecode import ticks_to_milliseconds


class AzureSpeechTranscriber:
    def __init__(self, config: AzureSpeechConfig) -> None:
        self._config = config

    def transcribe(self, audio_path: Path) -> list[TranscriptUtterance]:
        self._config.validate()

        try:
            import azure.cognitiveservices.speech as speechsdk
        except ImportError as exc:
            raise RuntimeError(
                "azure-cognitiveservices-speech is not installed. "
                "Run: py -m pip install -r requirements.txt"
            ) from exc

        speech_config = speechsdk.SpeechConfig(
            subscription=self._config.subscription_key,
            region=self._config.region,
        )
        speech_config.speech_recognition_language = self._config.language
        speech_config.request_word_level_timestamps()
        speech_config.output_format = speechsdk.OutputFormat.Detailed

        audio_config = speechsdk.audio.AudioConfig(filename=str(audio_path))
        recognizer = speechsdk.SpeechRecognizer(
            speech_config=speech_config,
            audio_config=audio_config,
        )

        utterances: list[TranscriptUtterance] = []
        done = Event()
        cancellation_message: str | None = None

        def handle_recognized(event: object) -> None:
            result = event.result
            if result.reason != speechsdk.ResultReason.RecognizedSpeech:
                return
            if not result.text.strip():
                return

            payload = result.properties.get(speechsdk.PropertyId.SpeechServiceResponse_JsonResult)
            utterances.append(self._parse_json_result(payload, fallback_text=result.text))

        def handle_session_end(_: object) -> None:
            done.set()

        def handle_canceled(event: object) -> None:
            nonlocal cancellation_message
            details = speechsdk.CancellationDetails(event.result)
            reason = getattr(details.reason, "name", str(details.reason))
            error_details = details.error_details or "No error details were returned by Azure Speech."
            cancellation_message = f"Azure Speech canceled recognition: {reason}. {error_details}"
            done.set()

        recognizer.recognized.connect(handle_recognized)
        recognizer.session_stopped.connect(handle_session_end)
        recognizer.canceled.connect(handle_canceled)

        recognizer.start_continuous_recognition()
        done.wait()
        recognizer.stop_continuous_recognition()

        if cancellation_message:
            raise RuntimeError(cancellation_message)

        utterances.sort(key=lambda item: (item.start_ms, item.end_ms))

        if not utterances:
            raise RuntimeError("Azure Speech returned no recognized speech segments.")

        return utterances

    def _parse_json_result(self, payload: str | None, fallback_text: str) -> TranscriptUtterance:
        data = json.loads(payload) if payload else {}
        nbest = data.get("NBest") or []
        best = nbest[0] if nbest else {}

        words: list[WordTiming] = []
        for word_data in best.get("Words", []):
            start_ms = ticks_to_milliseconds(int(word_data.get("Offset", 0)))
            duration_ms = ticks_to_milliseconds(int(word_data.get("Duration", 0)))
            words.append(
                WordTiming(
                    text=str(word_data.get("Word", "")).strip(),
                    start_ms=start_ms,
                    end_ms=start_ms + duration_ms,
                )
            )

        start_ms = ticks_to_milliseconds(int(data.get("Offset", 0)))
        duration_ms = ticks_to_milliseconds(int(data.get("Duration", 0)))
        end_ms = start_ms + duration_ms

        if words:
            start_ms = words[0].start_ms
            end_ms = words[-1].end_ms

        display_text = str(best.get("Display") or data.get("DisplayText") or fallback_text).strip()
        confidence = float(best["Confidence"]) if "Confidence" in best else None

        return TranscriptUtterance(
            text=display_text,
            start_ms=start_ms,
            end_ms=end_ms,
            confidence=confidence,
            words=words,
            raw_json=data or None,
        )
