# VideoAnalysisPipeline

Python pipeline for dubbing-prep asset generation.

## Current capabilities

1. Generate per-video output assets from MP4 + optional SRT:
   - `01.jpg` cover
   - `01.mp4` muted video
   - `02.mp4` full video copy
   - `03.mp3` BGM-only audio
   - `subtitle_spans.json`
   - `segments.json`
   - `segments.csv`
   - `review.html`
   - `progress.json`
2. Align subtitle spans with ASR timings and export editable review data.
3. Generate workbook outputs with:
   - sheet 1: overview rows
   - sheet 2: segment rows
4. Use Azure OpenAI Chat Completions once per video to create a concise Chinese overview.
5. Cache Demucs BGM outputs under `.video_pipeline_cache\bgm` to avoid repeated separation work.
6. Write `batch_progress.json` and `batch_summary.json` during batch runs.
7. Support final MOD packaging in batch mode via `--final-output mod`:
   - root workbook: `movie_dubbing.xlsx`
   - item folders: `dubbing/<sequence_no>`
   - remove intermediate JSON/HTML/CSV files after packaging
8. Support target-size export controls for generated assets:
   - `02.mp4` can be transcoded to a target size in KB
   - `03.mp3` can derive its MP3 bitrate from a target size in KB
9. When `overview.difficulty` is left blank, sheet-1 difficulty is auto-scored from subtitle complexity on a `1-6` scale.

## Execution modes

The default commands keep the full automated pipeline:

```powershell
py run_pipeline.py single --source-mp4 <mp4> --output-dir <output>
py run_pipeline.py batch --input-root <input> --output-root <output>
py run_pipeline.py batch --input-root <input> --output-root <output> --final-output mod
py run_pipeline.py batch --input-root <input> --output-root <output> --video-target-size-kb 9149 --audio-target-size-kb 955
```

You can now split the workflow when you do **not** want to rerun everything:

```powershell
py run_pipeline.py single-segments --source-mp4 <mp4> --output-dir <output>
py run_pipeline.py batch-segments --input-root <input> --output-root <output>
py run_pipeline.py single-overview --output-dir <existing-output>
py run_pipeline.py batch-overview --output-root <existing-output-root>
```

- `single-segments` / `batch-segments`: only generate segmentation assets and review resources.
- `single-overview` / `batch-overview`: rebuild the overview workbook from existing outputs without rerunning segmentation, ASR, or BGM extraction.
- `batch-overview` also accepts `--input-root` for command-line compatibility with the normal `batch` form, but it only reads existing outputs from `--output-root`.
- `batch --final-output mod`: emit MOD-ready assets under `dubbing/<sequence_no>`, write root workbook `movie_dubbing.xlsx`, and clean intermediate artifacts.
- `--video-target-size-kb`: derive a target video bitrate for exported `02.mp4` from the requested size.
- `--audio-target-size-kb`: derive a target MP3 bitrate for exported `03.mp3` from the requested size.

## Azure OpenAI overview

- Provider: Azure OpenAI Chat Completions
- Default deployment: `gpt-5.4-mini`
- Call pattern: once per video, using the full subtitle corpus

Set these environment variables at runtime when overview generation is needed:

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://<resource>.openai.azure.com"
$env:AZURE_OPENAI_API_KEY = "<key>"
$env:AZURE_OPENAI_DEPLOYMENT = "gpt-5.4-mini"
```

## Documentation

See `USAGE.txt` for the full command reference and configuration details.

Maintenance rule:

- Any change to config fields, CLI arguments, or output structure must update `README.md`, `README.txt`, and `USAGE.txt` in the same change.
