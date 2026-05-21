当前用法：
py -3.12 -m pip install -r requirements.txt
 
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output"
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output" --video-target-size-ratio 0.0833 --audio-target-size-ratio 0.4380
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output" --video-target-size-ratio 500 --video-audio-bitrate-kbps 64 --audio-target-size-ratio 64
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output" --video-target-size-ratio 64 --video-audio-bitrate-kbps 32 --audio-target-size-ratio 32

批量用法：
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output"
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output" --resume

MOD最终产物用法：
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output" --final-output mod

batch 会递归扫描 input-root；每个要处理的目录必须只有一个 mp4 和一个 srt，输出目录会按输入目录结构镜像生成。
--video-target-size-ratio 现在既支持数值比例，也支持显式视频码率（kbps）：
- 比例示例：0.0833
- 码率示例：64、128、500、64k、500kbps
- 对 02.mp4 来说，最小可用视频码率默认按 64 kbps 处理；如果你给了更低值，工具会自动钳到 64 kbps，并在必要时继续自动降分辨率。
--video-audio-bitrate-kbps 用来控制 02.mp4 内嵌 AAC 音轨码率；如果你想得到尽量小但仍可用的 02.mp4，建议设为 32。
--audio-target-size-ratio 现在也支持数值比例和显式 MP3 码率（kbps）：
- 比例示例：0.4380
- 码率示例：32、64、128、32k、128kbps
- 对 03.mp3 来说，最小可用背景音码率默认按 32 kbps 处理；如果你给了更低值，工具会自动钳到 32 kbps。
如果配置了 --video-target-size-ratio 的数值形式，则导出的 02.mp4 会按源文件大小比例反推目标码率并转码导出；当目标码率不足以支撑当前分辨率时，工具会保持 H.264/AAC 兼容输出，并自动降到更合适的标准分辨率（例如 480p -> 216p / 360p 一类），从而把文件继续压小，而不是单纯把最终体积抬高。
如果配置了 --video-target-size-ratio 的显式码率形式，则工具会直接把这个值当作视频码率目标，而不是再按文件大小占比反推。
当目标过小无法同时满足首选音频码率和最小可行视频码率时，工具会先自动下调音频码率；如果目标仍然过小，则退到最小可行音视频码率并继续自动降分辨率导出，因此最终文件会尽量靠近可用范围内的最小体积，而不会因为这个场景直接报错。
如果配置了 --audio-target-size-ratio 的数值形式，则导出的 03.mp3 会按源文件大小比例反推 MP3 码率；如果配置了显式码率形式，则直接使用该 MP3 码率；如果目标过小，会自动退到 32 kbps 而不是报错。
如果 batch 中途报错，重新执行同一条命令并追加 --resume，会根据 output 目录里的 batch_progress.json 跳过已完成项，从第一个未完成视频继续处理。
当使用 --final-output mod 时，最终产物会整理为：
- output 根目录下生成 movie_dubbing.xlsx
- 每个视频目录改为 dubbing\<序号>（序号与xlsx第一张sheet“序号”列一致）
- 自动清理中间文件（如 batch_progress.json、batch_summary.json、manifest.json、progress.json、review.html、segments.csv、segments.json、subtitle_spans.json）

Excel 中难度值如果未手工配置，会根据字幕内容复杂度自动计算为 1-5。
生成 Excel 时，第二张 sheet 会在“分视频文本”右边新增“分视频文本（中文）”列，内容由 gpt-5.4-mini 批量翻译为更适合教学/教育场景使用的自然中文；这些译文也会随 workbook 重建一起刷新。

如果要切回 Azure：

 py run_pipeline.py single --asr-provider azure-speech --source-mp4 F:\Your\Input\source.mp4 --output-dir 
F:\Your\Output\1

推荐的视频目标大小比例是 8.33%（即原视频的 1/12），音频目标大小比例是 43.80%（即原视频的 1/2.28）。这个比例更适合原始码率正常或偏高的视频；如果你只是想直接拿到尽可能小且还能用的输出，建议直接使用：--video-target-size-ratio 64 --video-audio-bitrate-kbps 32 --audio-target-size-ratio 32。如果目标压得很紧，工具会自动改用更低的标准分辨率来继续缩小文件，而不是只靠抬高码率保画质。如果不确定可以先试验一个视频，看看导出的视频和音频质量是否满足需求，再微调这两个比例或码率。
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output" --video-target-size-ratio 0.0833 --audio-target-size-ratio 0.4380 --skip summary

批量
py run_pipeline.py batch --input-root "/Your/Input" --output-root "/Your/Output" --video-target-size-ratio 0.1233 --audio-target-size-ratio 0.7380 --final-output mod --resume


