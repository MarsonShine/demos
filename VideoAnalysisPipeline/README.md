# VideoAnalysisPipeline

Initial .NET CLI scaffold for replacing the manual video dubbing segment workflow.

## What is implemented

1. Input a full mp4 file.
2. Probe media metadata with `ffprobe`.
3. Extract a normalized mono wav track with `ffmpeg`.
4. Detect silence windows with `ffmpeg silencedetect`.
5. Export analysis results to:
   - `analysis.json`
   - `segments.csv`
6. Support transcript enrichment through:
   - imported transcript JSON
   - OpenAI-compatible transcription when explicitly configured

## Current output model

Each exported segment contains:

- `segment_id`
- `audio_start_seconds`
- `audio_end_seconds`
- `video_start_seconds`
- `video_end_seconds`
- `transcript`
- `confidence`
- `review_status`
- `source`

## Usage

```powershell
dotnet run --project src\VideoAnalysis.Cli -- analyze --input C:\path\input.mp4
```

Use an imported transcript:

```powershell
dotnet run --project src\VideoAnalysis.Cli -- analyze `
  --input C:\path\input.mp4 `
  --transcript-json C:\path\segments.json
```

Use OpenAI-compatible transcription:

```powershell
dotnet run --project src\VideoAnalysis.Cli -- analyze `
  --input C:\path\input.mp4 `
  --openai-api-key <key> `
  --openai-base-url https://api.openai.com/v1 `
  --openai-model whisper-1
```

## Notes

- Local processing is the default for probing, extraction, and silence detection.
- If cloud ASR is explicitly configured, the CLI prefers cloud transcription.
- When no transcript provider is configured, the tool still exports silence-derived segments and marks them as `MissingTranscript`.
