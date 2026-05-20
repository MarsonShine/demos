当前用法：
py -3.12 -m pip install -r requirements.txt
 
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output"
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output" --video-target-size-ratio 0.0833 --audio-target-size-ratio 0.4380

批量用法：
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output"
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output" --resume

MOD最终产物用法：
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output" --final-output mod

batch 会递归扫描 input-root；每个要处理的目录必须只有一个 mp4 和一个 srt，输出目录会按输入目录结构镜像生成。
如果配置了 --video-target-size-ratio，则导出的 02.mp4 会按源文件大小比例反推目标码率并转码导出；同时现在内建了 auto 质量保护，会结合原视频本身码率和分辨率自动抬高过低的目标码率。也就是说，原视频本身已经比较小、比较糊时，不会再机械地继续按同一比例压缩到看不清。
当目标过小无法同时满足这个 auto 质量下限和音频码率时，工具会先自动下调音频码率，必要时退到最小可行码率继续导出，因此最终文件可能略大于目标值，但不会因为这个场景直接报错。
如果配置了 --audio-target-size-ratio，则导出的 03.mp3 会按源文件大小比例反推 MP3 码率；如果目标过小，会自动退到 32 kbps 而不是报错。
如果 batch 中途报错，重新执行同一条命令并追加 --resume，会根据 output 目录里的 batch_progress.json 跳过已完成项，从第一个未完成视频继续处理。
当使用 --final-output mod 时，最终产物会整理为：
- output 根目录下生成 movie_dubbing.xlsx
- 每个视频目录改为 dubbing\<序号>（序号与xlsx第一张sheet“序号”列一致）
- 自动清理中间文件（如 batch_progress.json、batch_summary.json、manifest.json、progress.json、review.html、segments.csv、segments.json、subtitle_spans.json）

Excel 中难度值如果未手工配置，会根据字幕内容复杂度自动计算为 1-5。

如果要切回 Azure：

 py run_pipeline.py single --asr-provider azure-speech --source-mp4 F:\Your\Input\source.mp4 --output-dir 
F:\Your\Output\1

推荐的视频目标大小比例是 8.33%（即原视频的 1/12），音频目标大小比例是 43.80%（即原视频的 1/2.28）。这个比例更适合原始码率正常或偏高的视频；如果源视频本身已经比较小，内建的 auto 质量保护会自动放宽压缩，避免继续压糊。如果不确定可以先试验一个视频，看看导出的视频和音频质量是否满足需求，再微调这两个比例。
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output" --video-target-size-ratio 0.0833 --audio-target-size-ratio 0.4380 --skip summary

批量
py run_pipeline.py batch --input-root "/Your/Input" --output-root "/Your/Output" --video-target-size-ratio 0.1233 --audio-target-size-ratio 0.7380 --final-output mod --resume


