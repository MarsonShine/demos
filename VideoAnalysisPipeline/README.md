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
   - sheet 2: segment rows with English text plus a teaching-friendly Chinese translation column
4. Use Azure OpenAI Chat Completions during workbook generation to create a concise Chinese overview and teaching-friendly Chinese translations for each segment.
5. Cache Demucs BGM outputs under `.video_pipeline_cache\bgm` to avoid repeated separation work.
6. Write `batch_progress.json` and `batch_summary.json` during batch runs.
7. Support final MOD packaging in batch mode via `--final-output mod`:
   - root workbook: `movie_dubbing.xlsx`
   - item folders: `dubbing/<sequence_no>`
   - remove intermediate JSON/HTML/CSV files after packaging
8. Support target-size export controls for generated assets:
   - `02.mp4` can be transcoded from a source-size ratio or an explicit video bitrate, and can also pin frame size, frame rate, and embedded AAC export settings
   - `03.mp3` can derive its MP3 bitrate from a source-size ratio or use an explicit MP3 bitrate
9. When `overview.difficulty` is left blank, sheet-1 difficulty is auto-scored from subtitle complexity on a `1-6` scale.

## Execution modes

The default commands keep the full automated pipeline:

```powershell
py run_pipeline.py single --source-mp4 <mp4> --output-dir <output>
py run_pipeline.py batch --input-root <input> --output-root <output>
py run_pipeline.py batch --input-root <input> --output-root <output> --final-output mod
py run_pipeline.py batch --input-root <input> --output-root <output> --video-target-size-ratio 0.0833 --audio-target-size-ratio 0.4380
py run_pipeline.py single --source-mp4 <mp4> --output-dir <output> --video-target-size-ratio 1000 --video-audio-bitrate-kbps 128 --audio-target-size-ratio 128
py run_pipeline.py single --source-mp4 <mp4> --output-dir <output> --video-target-size-ratio 2000 --video-audio-bitrate-kbps 128 --video-frame-size 1280x720 --video-fps 25 --video-audio-sample-rate-hz 44100 --video-audio-channels 2 --video-audio-bit-depth 32
```

You can now split the workflow when you do **not** want to rerun everything:

```powershell
py run_pipeline.py single-segments --source-mp4 <mp4> --output-dir <output>
py run_pipeline.py batch-segments --input-root <input> --output-root <output>
py run_pipeline.py single-overview --output-dir <existing-output>
py run_pipeline.py batch-overview --output-root <existing-output-root>
```

- `single-segments` / `batch-segments`: only generate segmentation assets and review resources.
- `single-overview` / `batch-overview`: rebuild the overview workbook and refresh segment Chinese translations from existing outputs without rerunning segmentation, ASR, or BGM extraction.
- `batch-overview` also accepts `--input-root` for command-line compatibility with the normal `batch` form, but it only reads existing outputs from `--output-root`.
- `batch --final-output mod`: emit MOD-ready assets under `dubbing/<sequence_no>`, write root workbook `movie_dubbing.xlsx`, and clean intermediate artifacts.
- `--video-target-size-ratio`: accepts either a numeric ratio or an explicit video bitrate in kbps, such as `64`, `500`, `64k`, or `500kbps`.
- `--video-audio-bitrate-kbps`: controls the embedded AAC audio bitrate inside `02.mp4`.
- `--video-frame-size`: sets an explicit `02.mp4` output frame size such as `1280x720`.
- `--video-fps`: sets an explicit `02.mp4` frame rate such as `25`.
- `--video-audio-sample-rate-hz`, `--video-audio-channels`, `--video-audio-bit-depth`: control embedded AAC export settings for `02.mp4`; `32`-bit maps to AAC `fltp` export.
- `--audio-target-size-ratio`: accepts either a numeric ratio or an explicit MP3 bitrate in kbps, such as `32`, `64`, `128`, `32k`, or `128kbps`.
- Smallest generally usable baseline: `02.mp4` video `64 kbps` + embedded audio `32 kbps`, and `03.mp3` at `32 kbps`.
- Jianying-style 720p example: video `2000 kbps`, AAC `128 kbps`, frame size `1280x720`, frame rate `25`, sample rate `44100`, stereo `2`, and `32`-bit AAC export.

## JS Subtitle DB To SRT

For subtitle JS files like `subtitleDB.js`, use the standalone converter package:

```powershell
py -m js_subtitle_converter single --source-js <subtitle-js>
py -m js_subtitle_converter single --source-js <subtitle-js> --resume
py -m js_subtitle_converter batch --input-root <folder> --resume
py -m js_subtitle_converter batch --source-js <a.js> --source-js <b.js>
py -m js_subtitle_converter batch --input-root <folder> --resume --no-cleanup
```

- The converter reads `wordArr` and `timeArr`, pairs `wordArr[n]` with `timeArr[2n]` and `timeArr[2n+1]`, and writes a sibling `.srt` file beside the source JS file.
- Chinese punctuation inside `wordArr` is normalized to English punctuation before export.
- By default, successful conversion cleans the folder and keeps only `mp4/srt/mp3` files; this removes source `js` and `.js_to_srt` progress files.
- Progress JSON files are written under a sibling `.js_to_srt` folder, and batch resume state is stored in `.js_to_srt/batch_progress.json`. If you need resume files preserved, run with `--no-cleanup`.

## Azure OpenAI overview

- Provider: Azure OpenAI Chat Completions
- Default deployment: `gpt-5.4-mini`
- Call pattern: one overview request per video, plus batched segment-translation requests whenever a workbook is generated

Set these environment variables at runtime when overview generation or workbook translation is needed:

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://<resource>.openai.azure.com"
$env:AZURE_OPENAI_API_KEY = "<key>"
$env:AZURE_OPENAI_DEPLOYMENT = "gpt-5.4-mini"
```

## Documentation

See `USAGE.txt` for the full command reference and configuration details.

Maintenance rule:

- Any change to config fields, CLI arguments, or output structure must update `README.md`, `README.txt`, and `USAGE.txt` in the same change.
