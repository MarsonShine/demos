from __future__ import annotations

import csv
import json
import html
from pathlib import Path
from typing import Iterable

from openpyxl import Workbook, load_workbook

from video_analysis_pipeline.models import Segment


HEADERS = ["序号", "分视频序号", "分视频文本", "起始时间", "结束时间"]


def write_json(path: Path, payload: object) -> Path:
    path.write_text(
        json.dumps(payload, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    return path


def export_csv(
    output_path: Path,
    rows: Iterable[tuple[int, int, str, str, str]],
) -> Path:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(HEADERS)
        writer.writerows(rows)
    return output_path


def export_workbook(
    output_path: Path,
    rows: Iterable[tuple[int, int, str, str, str]],
    template_path: Path | None = None,
) -> Path:
    if template_path and template_path.exists():
        workbook = load_workbook(template_path)
    else:
        workbook = Workbook()

    while len(workbook.worksheets) < 2:
        workbook.create_sheet()

    worksheet = workbook.worksheets[1]

    if worksheet.max_row > 1:
        worksheet.delete_rows(2, worksheet.max_row - 1)

    for column_index, header in enumerate(HEADERS, start=1):
        if worksheet.cell(row=1, column=column_index).value in (None, ""):
            worksheet.cell(row=1, column=column_index, value=header)

    row_index = 2
    for row in rows:
        for column_index, value in enumerate(row, start=1):
            worksheet.cell(row=row_index, column=column_index, value=value)
        row_index += 1

    output_path.parent.mkdir(parents=True, exist_ok=True)
    workbook.save(output_path)
    return output_path


def segments_to_rows(segments: list[Segment]) -> list[tuple[int, int, str, str, str]]:
    return [segment.to_excel_row() for segment in segments]


def export_review_page(
    output_path: Path,
    video_path: str,
    segments: list[Segment],
    title: str,
) -> Path:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    segments_payload = json.dumps([segment.to_json() for segment in segments], ensure_ascii=False).replace("</", "<\\/")
    title_text = html.escape(title)
    video_src = html.escape(video_path)

    output_path.write_text(
        f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{title_text}</title>
  <style>
    :root {{
      color-scheme: light dark;
      font-family: Arial, sans-serif;
    }}
    body {{
      margin: 0;
      padding: 16px;
      background: #111827;
      color: #f9fafb;
    }}
    .layout {{
      display: grid;
      grid-template-columns: minmax(320px, 1.3fr) minmax(360px, 1fr);
      gap: 16px;
    }}
    .panel {{
      background: #1f2937;
      border-radius: 12px;
      padding: 16px;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2);
    }}
    video {{
      width: 100%;
      max-height: 70vh;
      border-radius: 8px;
      background: #000;
    }}
    .toolbar {{
      margin-top: 12px;
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
      font-size: 14px;
    }}
    .segment-list {{
      display: grid;
      gap: 8px;
      max-height: 78vh;
      overflow: auto;
    }}
    .filter-row {{
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 12px;
      font-size: 14px;
    }}
    .segment-item {{
      border: 1px solid #374151;
      border-radius: 8px;
      padding: 10px 12px;
      cursor: pointer;
      background: #111827;
    }}
    .segment-item:hover {{
      border-color: #60a5fa;
    }}
    .segment-item.active {{
      border-color: #22c55e;
      background: #0f2d22;
    }}
    .segment-meta {{
      color: #93c5fd;
      font-size: 12px;
      margin-bottom: 4px;
    }}
    .segment-text {{
      font-size: 15px;
      line-height: 1.45;
      white-space: pre-wrap;
    }}
    .segment-flags {{
      margin-top: 6px;
      color: #fca5a5;
      font-size: 12px;
    }}
    button {{
      border: 0;
      border-radius: 6px;
      padding: 8px 12px;
      background: #2563eb;
      color: white;
      cursor: pointer;
    }}
    button.secondary {{
      background: #374151;
    }}
    @media (max-width: 980px) {{
      .layout {{
        grid-template-columns: 1fr;
      }}
      .segment-list {{
        max-height: none;
      }}
    }}
  </style>
</head>
<body>
  <div class="layout">
    <section class="panel">
      <h1 style="margin-top: 0;">配音时间校验页</h1>
      <video id="video" controls preload="metadata" src="{video_src}"></video>
      <div class="toolbar">
        <span id="status">点击右侧分段可跳转并播放。</span>
        <button id="replayButton" class="secondary" type="button">重播当前段</button>
      </div>
    </section>
    <section class="panel">
      <h2 style="margin-top: 0;">分段列表</h2>
      <label class="filter-row">
        <input id="onlyAnomalies" type="checkbox">
        <span>只看异常段</span>
      </label>
      <div id="segmentList" class="segment-list"></div>
    </section>
  </div>
  <script id="segments-data" type="application/json">{segments_payload}</script>
  <script>
    const segments = JSON.parse(document.getElementById('segments-data').textContent);
    const video = document.getElementById('video');
    const segmentList = document.getElementById('segmentList');
    const status = document.getElementById('status');
    const onlyAnomalies = document.getElementById('onlyAnomalies');
    const replayButton = document.getElementById('replayButton');
    let activeSegmentIndex = -1;
    let playbackEndSeconds = null;

    function formatRange(segment) {{
      return `${{segment.segment_no}} | ${{segment.start_timecode}} - ${{segment.end_timecode}}`;
    }}

    function renderSegments() {{
      segmentList.innerHTML = '';
      segments.forEach((segment, index) => {{
        const hasFlags = Array.isArray(segment.quality_flags) && segment.quality_flags.length > 0;
        if (onlyAnomalies.checked && !hasFlags) {{
          return;
        }}
        const item = document.createElement('div');
        item.className = 'segment-item';
        item.dataset.index = String(index);
        item.innerHTML = `
          <div class="segment-meta">${{formatRange(segment)}}</div>
          <div class="segment-text">${{escapeHtml(segment.text)}}</div>
          <div class="segment-flags">${{(segment.quality_flags || []).join(' | ')}}</div>
        `;
        item.addEventListener('click', () => playSegment(index));
        segmentList.appendChild(item);
      }});
    }}

    function escapeHtml(text) {{
      return text
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
    }}

    function setActiveSegment(index) {{
      activeSegmentIndex = index;
      [...segmentList.children].forEach((item, itemIndex) => {{
        item.classList.toggle('active', itemIndex === index);
      }});
      if (index >= 0) {{
        segmentList.children[index]?.scrollIntoView({{ block: 'nearest' }});
      }}
    }}

    function playSegment(index) {{
      const segment = segments[index];
      if (!segment) {{
        return;
      }}
      playbackEndSeconds = segment.end_ms / 1000;
      video.currentTime = segment.start_ms / 1000;
      setActiveSegment(index);
      status.textContent = `当前分段：${{segment.segment_no}} / ${{segment.text}}`;
      video.play().catch(() => {{
        status.textContent = '浏览器阻止了自动播放，请手动点击播放。';
      }});
    }}

    replayButton.addEventListener('click', () => {{
      if (activeSegmentIndex >= 0) {{
        playSegment(activeSegmentIndex);
      }}
    }});
    onlyAnomalies.addEventListener('change', () => renderSegments());

    video.addEventListener('timeupdate', () => {{
      if (playbackEndSeconds !== null && video.currentTime >= playbackEndSeconds) {{
        video.pause();
      }}
      const currentMs = video.currentTime * 1000;
      const index = segments.findIndex(segment => currentMs >= segment.start_ms && currentMs <= segment.end_ms);
      if (index >= 0 && index !== activeSegmentIndex) {{
        setActiveSegment(index);
      }}
    }});

    renderSegments();
  </script>
</body>
</html>
""",
        encoding="utf-8",
    )
    return output_path
