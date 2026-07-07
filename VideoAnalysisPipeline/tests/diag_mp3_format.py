"""
Investigate why Azure Speech SDK rejects certain MP3 files with SPXERR_INVALID_HEADER.

Tests different ffmpeg encoding parameters to find the root cause.
"""

from __future__ import annotations

import json
import os
import subprocess
import struct
import sys
import tempfile
import wave
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO_ROOT))

CONFIG_PATH = REPO_ROOT / "pipeline_config.json"


def _load_credentials() -> tuple[str, str, str]:
    key = ""
    region = ""
    language = "en-US"
    if CONFIG_PATH.exists():
        data = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        azure_data = data.get("azure_speech", {})
        key = str(azure_data.get("subscription_key", ""))
        region = str(azure_data.get("region", ""))
        language = str(azure_data.get("language", "en-US"))
    env_key = os.getenv("AZURE_SPEECH_KEY")
    env_region = os.getenv("AZURE_SPEECH_REGION")
    env_language = os.getenv("AZURE_SPEECH_LANGUAGE")
    if env_key:
        key = env_key
    if env_region:
        region = env_region
    if env_language:
        language = env_language
    return key.strip(), region.strip(), language.strip()


def _make_silent_wav(sample_rate: int = 16000) -> bytes:
    """Generate 0.5s of 16-bit mono silent WAV."""
    duration = 0.5
    num_samples = int(sample_rate * duration)
    buf = __import__("io").BytesIO()
    with wave.open(buf, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(struct.pack(f"<{num_samples}h", *([0] * num_samples)))
    return buf.getvalue()


def _make_non_silent_wav(sample_rate: int = 16000) -> bytes:
    """Generate 0.5s of 16-bit mono WAV with a simple 440 Hz tone."""
    import math
    duration = 0.5
    num_samples = int(sample_rate * duration)
    amplitude = 16000
    samples = [
        int(amplitude * math.sin(2 * math.pi * 440 * i / sample_rate))
        for i in range(num_samples)
    ]
    buf = __import__("io").BytesIO()
    with wave.open(buf, "wb") as wf:
        wf.setnchannels(1)
        wf.setsampwidth(2)
        wf.setframerate(sample_rate)
        wf.writeframes(struct.pack(f"<{num_samples}h", *samples))
    return buf.getvalue()


def _run_ffmpeg(wav_bytes: bytes, output_path: str, extra_args: list[str] | None = None) -> bool:
    """Convert WAV bytes to a file via ffmpeg. Returns True on success."""
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(wav_bytes)
        wav_path = tmp.name

    cmd = [
        "ffmpeg", "-y", "-hide_banner", "-loglevel", "error",
        "-i", wav_path,
        "-vn",
    ]
    if extra_args:
        cmd.extend(extra_args)
    cmd.append(output_path)

    try:
        result = subprocess.run(cmd, capture_output=True, text=True)
        return result.returncode == 0
    finally:
        Path(wav_path).unlink(missing_ok=True)


def _test_recognize(audio_path: str, key: str, region: str, language: str, label: str) -> tuple[bool, str]:
    """Try recognize_once with the given audio file. Returns (ok, detail)."""
    import azure.cognitiveservices.speech as speechsdk

    speech_config = speechsdk.SpeechConfig(subscription=key, region=region)
    speech_config.speech_recognition_language = language
    audio_config = speechsdk.audio.AudioConfig(filename=audio_path)

    try:
        recognizer = speechsdk.SpeechRecognizer(
            speech_config=speech_config,
            audio_config=audio_config,
        )
        result = recognizer.recognize_once()
        if result.reason == speechsdk.ResultReason.Canceled:
            cancellation = speechsdk.CancellationDetails(result)
            return False, f"Canceled: {cancellation.error_details}"
        return True, str(result.reason)
    except RuntimeError as exc:
        msg = str(exc)
        if "0xa" in msg or "INVALID_HEADER" in msg:
            return False, "SPXERR_INVALID_HEADER"
        return False, msg
    except Exception as exc:
        return False, str(exc)


def main() -> None:
    key, region, language = _load_credentials()
    if not key or key.startswith("YOUR_"):
        print("❌ Azure Speech key not configured.")
        sys.exit(1)

    print("=" * 70)
    print("Azure Speech MP3 Compatibility Diagnostic")
    print("=" * 70)

    # --- Baseline: raw WAV works ---
    print("\n[Baseline] Raw WAV file")
    wav_bytes = _make_silent_wav()
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(wav_bytes)
        wav_path = tmp.name
    ok, detail = _test_recognize(wav_path, key, region, language, "raw WAV (16kHz mono)")
    Path(wav_path).unlink(missing_ok=True)
    print(f"  {'✅' if ok else '❌'} {detail}")

    # --- Baseline: non-silent WAV ---
    print("\n[Baseline] Non-silent WAV (440Hz tone)")
    tone_wav = _make_non_silent_wav()
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        tmp.write(tone_wav)
        tone_path = tmp.name
    ok, detail = _test_recognize(tone_path, key, region, language, "tone WAV (16kHz mono)")
    Path(tone_path).unlink(missing_ok=True)
    print(f"  {'✅' if ok else '❌'} {detail}")

    # --- Test various MP3 encoding options ---
    test_dir = Path(tempfile.gettempdir()) / "azure_speech_mp3_test"
    test_dir.mkdir(parents=True, exist_ok=True)

    test_cases = [
        # (label, extra_ffmpeg_args)
        ("MP3: libmp3lame 44100Hz mono 192k (pipeline default)", [
            "-ac", "1", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", "192k",
        ]),
        ("MP3: libmp3lame 16000Hz mono 192k", [
            "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "192k",
        ]),
        ("MP3: libmp3lame 16000Hz mono 128k", [
            "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "128k",
        ]),
        ("MP3: libmp3lame 16000Hz mono 64k", [
            "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "64k",
        ]),
        ("MP3: libmp3lame 44100Hz stereo 192k", [
            "-ac", "2", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", "192k",
        ]),
        ("MP3: libmp3lame with joint stereo", [
            "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "128k",
            "-joint_stereo", "0",
        ]),
        ("MP3: mpeg2 layer3 16000Hz mono", [
            "-ac", "1", "-ar", "16000", "-c:a", "mp3", "-b:a", "128k",
        ]),
        ("WAV: PCM 44100Hz mono (same sample rate as pipeline)", [
            "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le",
        ]),
        ("WAV: PCM 44100Hz mono with .mp3 extension (test extension parsing)", [
            "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le",
        ]),
        ("WAV: PCM 16000Hz mono (optimal for ASR)", [
            "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le",
        ]),
    ]

    print(f"\n{'─' * 70}")
    print(f"{'Test':<58} {'Result'}")
    print(f"{'─' * 70}")

    results: list[tuple[str, bool, str]] = []

    for idx, (label, args) in enumerate(test_cases):
        # Use .wav extension for WAV codec, .mp3 for MP3 codec
        codec = "wav" if any("pcm" in str(a).lower() for a in args) else "mp3"
        # Special case: WAV content with .mp3 extension
        if "mp3 extension" in label:
            ext = ".mp3"
        else:
            ext = ".wav" if codec == "wav" else ".mp3"

        out_path = str(test_dir / f"test_{idx}{ext}")
        if not _run_ffmpeg(wav_bytes, out_path, args):
            results.append((label, False, "ffmpeg conversion failed"))
            continue

        ok, detail = _test_recognize(out_path, key, region, language, label)
        results.append((label, ok, detail))
        Path(out_path).unlink(missing_ok=True)

    # Print results
    for label, ok, detail in results:
        status = "✅" if ok else "❌"
        print(f"  {status} {label:<55} {detail}")

    # --- Test with non-silent audio ---
    print(f"\n{'─' * 70}")
    print("Non-silent MP3 tests (tone audio)")
    print(f"{'─' * 70}")

    non_silent_tests = [
        ("MP3 (tone): libmp3lame 44100Hz mono 192k", [
            "-ac", "1", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", "192k",
        ]),
        ("MP3 (tone): libmp3lame 16000Hz mono 128k", [
            "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "128k",
        ]),
    ]

    for idx, (label, args) in enumerate(non_silent_tests):
        out_path = str(test_dir / f"tone_{idx}.mp3")
        if not _run_ffmpeg(tone_wav, out_path, args):
            print(f"  ❌ {label}: ffmpeg conversion failed")
            continue
        ok, detail = _test_recognize(out_path, key, region, language, label)
        Path(out_path).unlink(missing_ok=True)
        print(f"  {'✅' if ok else '❌'} {label}: {detail}")

    # Cleanup
    try:
        test_dir.rmdir()
    except OSError:
        pass

    # Summary
    print(f"\n{'═' * 70}")
    failures = [(l, d) for l, ok, d in results if not ok]
    successes = [(l, d) for l, ok, d in results if ok]

    if failures:
        print(f"❌ {len(failures)} test(s) FAILED:")
        for label, detail in failures:
            print(f"   - {label}: {detail}")
    else:
        print("All tests passed.")

    print(f"\n✅ {len(successes)} test(s) passed out of {len(results)} total.")
    print(f"{'═' * 70}")


if __name__ == "__main__":
    main()
