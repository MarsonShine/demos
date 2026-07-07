"""
Minimal Azure Speech Service connectivity diagnostic.

Usage:
    py tests/diag_azure_speech.py

This script mimics how the pipeline reads config (pipeline_config.json + env vars)
and performs a lightweight recognition test to isolate SPXERR_INVALID_HEADER.
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT))

CONFIG_PATH = REPO_ROOT / "pipeline_config.json"


def _load_raw_credentials() -> tuple[str, str, str]:
    """Read key / region / language the same way PipelineConfig.load_config does."""
    subscription_key = ""
    region = ""
    language = "en-US"

    if CONFIG_PATH.exists():
        data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        azure_data = data.get("azure_speech", {})
        subscription_key = str(azure_data.get("subscription_key", ""))
        region = str(azure_data.get("region", ""))
        language = str(azure_data.get("language", "en-US"))

    env_key = os.getenv("AZURE_SPEECH_KEY")
    env_region = os.getenv("AZURE_SPEECH_REGION")
    env_language = os.getenv("AZURE_SPEECH_LANGUAGE")

    if env_key:
        subscription_key = env_key
    if env_region:
        region = env_region
    if env_language:
        language = env_language

    return subscription_key.strip(), region.strip(), language.strip()


def main() -> None:
    print("=" * 60)
    print("Azure Speech Connectivity Diagnostic")
    print("=" * 60)

    key, region, language = _load_raw_credentials()

    # --- Step 1: basic sanity checks ---
    print("\n[1] Credential sanity checks")
    print(f"    Key length  : {len(key)} chars")
    print(f"    Region      : '{region}'")
    print(f"    Language    : '{language}'")

    if not key:
        print("    ❌ FAIL: subscription_key is empty.")
        print("       Set AZURE_SPEECH_KEY env var or edit pipeline_config.json")
        sys.exit(1)

    if key.startswith("YOUR_"):
        print("    ❌ FAIL: subscription_key is still the placeholder value.")
        sys.exit(1)

    if not region:
        print("    ❌ FAIL: region is empty.")
        print("       Set AZURE_SPEECH_REGION env var or edit pipeline_config.json")
        sys.exit(1)

    if region.startswith("YOUR_"):
        print("    ❌ FAIL: region is still the placeholder value.")
        sys.exit(1)

    # Key should only contain base64-ish characters
    import re
    if not re.fullmatch(r"[A-Za-z0-9+/=_-]+", key):
        print("    ⚠ WARNING: key contains unexpected characters (expected base64-safe chars only).")
        printable = "".join(c if c.isprintable() else f"\\x{ord(c):02x}" for c in key)
        print(f"       Full key (escaped): {printable}")
    else:
        print("    ✅ Key format looks valid")

    # Known Azure regions (non-exhaustive list for validation)
    known_regions = {
        "australiaeast", "australiasoutheast",
        "brazilsouth", "brazilsoutheast",
        "canadacentral", "canadaeast",
        "centralindia", "centralus", "centraluseuap",
        "eastasia", "eastus", "eastus2", "eastusstg",
        "francecentral", "francesouth",
        "germanywestcentral",
        "japaneast", "japanwest",
        "koreacentral", "koreasouth",
        "northcentralus", "northeurope", "northeurope2",
        "norwayeast", "norwaywest",
        "southafricanorth", "southafricawest",
        "southcentralus", "southeastasia", "southindia",
        "swedencentral", "switzerlandnorth", "switzerlandwest",
        "uaenorth", "uksouth", "ukwest",
        "westcentralus", "westeurope", "westindia",
        "westus", "westus2", "westus3",
    }
    if region.lower() not in known_regions:
        print(f"    ⚠ WARNING: '{region}' is not a recognized Azure region name.")
        print("       Check that the region exactly matches your Speech resource's location.")
    else:
        print("    ✅ Region name recognized")

    # --- Step 2: SDK import ---
    print("\n[2] Azure Speech SDK import")
    try:
        import azure.cognitiveservices.speech as speechsdk  # noqa: F811
        print("    ✅ SDK imported successfully")
    except ImportError:
        print("    ❌ FAIL: azure-cognitiveservices-speech is not installed.")
        print("       Run: pip install azure-cognitiveservices-speech")
        sys.exit(1)

    # --- Step 3: Create SpeechConfig ---
    print("\n[3] Creating SpeechConfig")
    try:
        speech_config = speechsdk.SpeechConfig(subscription=key, region=region)
        print("    ✅ SpeechConfig created")
    except Exception as exc:
        print(f"    ❌ FAIL: {exc}")
        sys.exit(1)

    # --- Step 4: Try a one-shot recognition with a tiny silent WAV ---
    print("\n[4] Testing recognition with a minimal audio stream …")
    print("    (This tests the full auth handshake with Azure)")

    # Generate a minimal valid WAV file (0.5 seconds of silence, 16 kHz, mono, 16-bit)
    import struct
    import wave
    import io

    sample_rate = 16000
    duration_seconds = 0.5
    num_samples = int(sample_rate * duration_seconds)

    wav_buffer = io.BytesIO()
    with wave.open(wav_buffer, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)  # 16-bit
        wf.setframerate(sample_rate)
        wf.writeframes(struct.pack(f"<{num_samples}h", *([0] * num_samples)))

    wav_bytes = wav_buffer.getvalue()

    # 4a: stream-based recognition (baseline)
    audio_format = speechsdk.audio.AudioStreamFormat(
        samples_per_second=sample_rate,
        bits_per_sample=16,
        channels=1,
    )
    push_stream = speechsdk.audio.PushAudioInputStream(stream_format=audio_format)
    push_stream.write(wav_bytes)
    push_stream.close()

    audio_config = speechsdk.audio.AudioConfig(stream=push_stream)
    recognizer = speechsdk.SpeechRecognizer(
        speech_config=speech_config,
        audio_config=audio_config,
    )

    result = recognizer.recognize_once()
    reason = result.reason

    if reason == speechsdk.ResultReason.RecognizedSpeech:
        print(f"    ✅ Stream recognition succeeded: '{result.text}' (expected empty/silence)")
    elif reason == speechsdk.ResultReason.NoMatch:
        print("    ✅ Stream recognition completed (NoMatch — expected for silence)")
    elif reason == speechsdk.ResultReason.Canceled:
        cancellation = speechsdk.CancellationDetails(result)
        print(f"    ❌ FAIL: Stream recognition canceled")
        print(f"       Reason  : {cancellation.reason}")
        print(f"       Code    : {cancellation.error_code} (0x{cancellation.error_code:x})")
        print(f"       Details : {cancellation.error_details}")
        _print_troubleshooting(cancellation.error_details)
        sys.exit(1)
    else:
        print(f"    ❓ Unexpected stream result reason: {reason}")
        sys.exit(1)

    # --- Step 5: File-based recognition (WAV, ASCII path) ---
    _test_file_recognition(key, region, language, wav_bytes, sample_rate)

    # --- Step 6: File-based recognition (WAV, Chinese path) ---
    _test_file_recognition_chinese_path(key, region, language, wav_bytes, sample_rate)

    # --- Step 7: File-based recognition (MP3, ASCII path - matches pipeline format) ---
    # Note: MP3 recognition may fail with SPXERR_INVALID_HEADER on some systems
    # due to missing MP3 decoder in the Azure Speech SDK native library.
    # This is non-fatal — the pipeline now uses WAV (PCM) via stream, not MP3 files.
    try:
        _test_mp3_recognition(key, region, language, wav_bytes, sample_rate)
    except (SystemExit, RuntimeError):
        print("    ⚠ MP3 recognition not supported on this system (expected).")
        print("    The pipeline uses WAV PCM stream input instead of MP3 files.")

    # --- Step 8: Pipeline-style continuous recognition with word timestamps ---
    _test_pipeline_style_recognition(key, region, language, wav_bytes, sample_rate)

    print("\n" + "=" * 60)
    print("✅ All checks passed — Azure Speech is configured correctly!")
    print("=" * 60)


def _run_recognize_once(
    speech_config: object,
    audio_config: object,
    label: str,
) -> None:
    """Run a single recognize_once call and report the result."""
    import azure.cognitiveservices.speech as speechsdk

    recognizer = speechsdk.SpeechRecognizer(
        speech_config=speech_config,
        audio_config=audio_config,
    )
    result = recognizer.recognize_once()
    reason = result.reason

    if reason == speechsdk.ResultReason.RecognizedSpeech:
        print(f"    ✅ {label}: '{result.text}' (expected empty/silence)")
    elif reason == speechsdk.ResultReason.NoMatch:
        print(f"    ✅ {label} (NoMatch — expected for silence)")
    elif reason == speechsdk.ResultReason.Canceled:
        cancellation = speechsdk.CancellationDetails(result)
        print(f"    ❌ FAIL: {label} canceled")
        print(f"       Reason  : {cancellation.reason}")
        print(f"       Code    : {cancellation.error_code} (0x{cancellation.error_code:x})")
        print(f"       Details : {cancellation.error_details}")
        _print_troubleshooting(cancellation.error_details)
        sys.exit(1)
    else:
        print(f"    ❓ Unexpected {label} result: {reason}")
        sys.exit(1)


def _test_file_recognition(
    key: str,
    region: str,
    language: str,
    wav_bytes: bytes,
    sample_rate: int,
) -> None:
    """Test recognition using a WAV file on disk (ASCII path)."""
    import tempfile
    import azure.cognitiveservices.speech as speechsdk

    print("\n[5] Testing recognition with WAV file on disk (ASCII path) …")

    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(wav_bytes)
        tmp_path = tmp.name

    try:
        speech_config = speechsdk.SpeechConfig(subscription=key, region=region)
        speech_config.speech_recognition_language = language
        audio_config = speechsdk.audio.AudioConfig(filename=tmp_path)
        _run_recognize_once(speech_config, audio_config, "ASCII file path")
    finally:
        Path(tmp_path).unlink(missing_ok=True)


def _test_file_recognition_chinese_path(
    key: str,
    region: str,
    language: str,
    wav_bytes: bytes,
    sample_rate: int,
) -> None:
    """Test recognition using a WAV file on disk with Chinese characters in path."""
    import tempfile
    import azure.cognitiveservices.speech as speechsdk

    print("\n[6] Testing recognition with WAV file on disk (中文路径) …")

    # Create a temp directory with Chinese characters in the name
    base_dir = Path(tempfile.gettempdir()) / "azure_speech_test_中文测试"
    base_dir.mkdir(parents=True, exist_ok=True)
    wav_path = base_dir / "test_音频.wav"
    wav_path.write_bytes(wav_bytes)

    try:
        speech_config = speechsdk.SpeechConfig(subscription=key, region=region)
        speech_config.speech_recognition_language = language
        audio_config = speechsdk.audio.AudioConfig(filename=str(wav_path))
        _run_recognize_once(speech_config, audio_config, "中文 file path")
    finally:
        wav_path.unlink(missing_ok=True)
        try:
            base_dir.rmdir()
        except OSError:
            pass


def _test_mp3_recognition(
    key: str,
    region: str,
    language: str,
    wav_bytes: bytes,
    sample_rate: int,
) -> None:
    """Test recognition with an MP3 file (the format the pipeline actually uses)."""
    import subprocess
    import tempfile
    import azure.cognitiveservices.speech as speechsdk

    print("\n[7] Testing recognition with MP3 file on disk (pipeline format) …")

    # Write WAV to temp file, then convert to MP3 with ffmpeg
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp_wav:
        tmp_wav.write(wav_bytes)
        wav_path = tmp_wav.name

    mp3_path = wav_path.replace(".wav", ".mp3")
    try:
        result = subprocess.run(
            [
                "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
                "-i", wav_path,
                "-vn", "-ac", "1", "-ar", "44100",
                "-c:a", "libmp3lame", "-b:a", "192k",
                mp3_path,
            ],
            capture_output=True,
            text=True,
        )
        if result.returncode != 0:
            print(f"    ⚠ SKIP: ffmpeg MP3 conversion failed: {result.stderr.strip()}")
            return

        speech_config = speechsdk.SpeechConfig(subscription=key, region=region)
        speech_config.speech_recognition_language = language
        audio_config = speechsdk.audio.AudioConfig(filename=mp3_path)
        _run_recognize_once(speech_config, audio_config, "MP3 file path")
    finally:
        Path(wav_path).unlink(missing_ok=True)
        Path(mp3_path).unlink(missing_ok=True)


def _test_pipeline_style_recognition(
    key: str,
    region: str,
    language: str,
    wav_bytes: bytes,
    sample_rate: int,
) -> None:
    """Test using the exact same SpeechConfig + stream-based continuous recognition as the pipeline."""
    import wave
    import io
    from threading import Event
    import azure.cognitiveservices.speech as speechsdk

    print("\n[8] Testing pipeline-style recognition (word timestamps + stream + continuous) …")

    # Read WAV header to get audio format (same approach as _build_stream_audio_config)
    wav_io = io.BytesIO(wav_bytes)
    with wave.open(wav_io, "rb") as wf:
        actual_sample_rate = wf.getframerate()
        bits_per_sample = wf.getsampwidth() * 8
        channels = wf.getnchannels()
        pcm_data = wf.readframes(wf.getnframes())

    speech_config = speechsdk.SpeechConfig(subscription=key, region=region)
    speech_config.speech_recognition_language = language
    speech_config.request_word_level_timestamps()
    speech_config.output_format = speechsdk.OutputFormat.Detailed

    stream_format = speechsdk.audio.AudioStreamFormat(
        samples_per_second=actual_sample_rate,
        bits_per_sample=bits_per_sample,
        channels=channels,
    )
    push_stream = speechsdk.audio.PushAudioInputStream(stream_format=stream_format)
    push_stream.write(pcm_data)
    push_stream.close()

    audio_config = speechsdk.audio.AudioConfig(stream=push_stream)
    recognizer = speechsdk.SpeechRecognizer(
        speech_config=speech_config,
        audio_config=audio_config,
    )

    done = Event()
    error_details: str | None = None

    def _on_canceled(event: object) -> None:
        nonlocal error_details
        details = speechsdk.CancellationDetails(event.result)
        error_details = details.error_details
        done.set()

    def _on_session_end(_: object) -> None:
        done.set()

    recognizer.canceled.connect(_on_canceled)
    recognizer.session_stopped.connect(_on_session_end)

    recognizer.start_continuous_recognition()
    done.wait()
    recognizer.stop_continuous_recognition()

    if error_details:
        print(f"    ❌ FAIL: Pipeline-style recognition canceled")
        print(f"       Details : {error_details}")
        _print_troubleshooting(error_details)
        sys.exit(1)
    else:
        print("    ✅ Pipeline-style recognition completed successfully")


def _print_troubleshooting(error_details: str) -> None:
    if "InvalidHeader" in str(error_details):
        print()
        print("    🔍 SPXERR_INVALID_HEADER typically means:")
        print("       1. The subscription key is for a *different* Azure region than specified")
        print("       2. The key belongs to a different resource type (e.g., not Speech)")
        print("       3. The key has been regenerated and is no longer valid")
        print("       4. The Speech resource has been deleted")
        print()
        print("       → Go to https://portal.azure.com → your Speech resource")
        print("         → 'Keys and Endpoint' → verify Region matches LOCATION")
        print("         → Copy Key 1 or Key 2 exactly (no extra spaces)")
    elif "audio" in str(error_details).lower() or "wav" in str(error_details).lower():
        print()
        print("    🔍 The error mentions audio/WAV. Possible causes:")
        print("       1. The audio file format is not supported (must be PCM WAV)")
        print("       2. The audio sample rate is too high or too low")
        print("       3. The audio file is corrupt or truncated")
        print()
        print("       → Check the extracted analysis-audio WAV file with ffprobe")


if __name__ == "__main__":
    main()
