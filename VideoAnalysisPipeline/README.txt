当前用法：
py -3.12 -m pip install -r requirements.txt
 
py run_pipeline.py single --source-mp4 "/Your/Input/source.mp4" --source-srt "/Your/Input/source.srt" --output-dir "/Your/Output"

批量用法：
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output"

MOD最终产物用法：
py run_pipeline.py batch --input-root "C:\video-analysis\input" --output-root "C:\video-analysis\output" --final-output mod

batch 会递归扫描 input-root；每个要处理的目录必须只有一个 mp4 和一个 srt，输出目录会按输入目录结构镜像生成。
当使用 --final-output mod 时，最终产物会整理为：
- output 根目录下生成 movie_dubbing.xlsx
- 每个视频目录改为 dubbing\<序号>（序号与xlsx第一张sheet“序号”列一致）
- 自动清理中间文件（如 batch_progress.json、batch_summary.json、manifest.json、progress.json、review.html、segments.csv、segments.json、subtitle_spans.json）

如果要切回 Azure：

 py run_pipeline.py single --asr-provider azure-speech --source-mp4 F:\Your\Input\source.mp4 --output-dir 
F:\Your\Output\1


