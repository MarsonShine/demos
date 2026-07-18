# Android 视频 Seek 与 GOP 优化记录

## 1. 背景

`VideoAnalysisPipeline` 导出的 MP4 视频用于 Android 原生播放器播放。旧版导出视频虽然能够正常播放，但在 Android `VideoView` / `MediaPlayer` 中调用 `seekTo()` 跳转到指定时间时，会出现定位不准确、跳转到目标时间前后较远位置的问题。

需求方提供的参考格式如下：

- 封装：MPEG-4（Sony PSP）
- 视频编码：H.264/AVC Main@L3.1
- 熵编码：CABAC
- 参考帧：3
- 分辨率：1280×720
- 帧率：25 fps
- 视频码率：约 1000 kbps
- 音频编码：AAC-LC
- 音频码率：128 kbps
- 采样率：44.1 kHz
- 声道：双声道

除编码格式外，最重要的要求是提高关键帧密度，避免旧视频约 10 秒才出现一个关键帧。

## 2. 旧方案存在的问题

旧版流水线使用 `libx264` 重新编码视频，但没有显式设置以下参数：

- GOP 长度
- 最大关键帧间隔
- 最小关键帧间隔
- 场景切换关键帧策略
- H.264 Profile
- H.264 Level
- H.264 参考帧数量
- MP4 Sony PSP 封装标记

libx264 默认最大关键帧间隔通常为 250 帧。在输出帧率为 25 fps 时：

```text
250 帧 ÷ 25 帧/秒 = 10 秒
```

因此旧视频可能约 10 秒才有一个关键帧。实际检查旧版成品时，第一条视频的关键帧时间为：

```text
0.00
7.76
17.76
27.76
37.76
```

除第一个场景切换关键帧外，后续关键帧基本保持约 10 秒间隔。

### 2.1 为什么这会影响 Android seek

H.264 视频中的普通 P 帧和 B 帧依赖其他帧才能解码，播放器无法随意从任意 P/B 帧开始播放。关键帧（通常为 IDR/I 帧）可以作为独立解码起点。

Android `VideoView` 底层使用 `MediaPlayer`。播放器执行 `seekTo()` 时，通常需要先寻找目标时间附近的同步帧，也就是关键帧，再从该位置开始解码。

例如关键帧位于：

```text
0 秒、10 秒、20 秒、30 秒
```

当业务请求跳转到 16 秒时，播放器只能先定位到附近的 10 秒或 20 秒关键帧。具体行为取决于播放器和 seek 模式，因此可能产生数秒偏差。

这不是 MP4 文件损坏，也不是 Android 时间计算错误，而是视频码流中可供播放器定位的关键帧过于稀疏。

## 3. GOP 是什么

GOP 是 Group of Pictures，即一组连续视频帧。一个典型 GOP 由一个关键帧开始，后面跟随多个 P 帧和 B 帧：

```text
I B B P B B P ... I B B P ...
^                   ^
关键帧              下一个关键帧
```

GOP 长度决定两个关键帧之间最多包含多少帧。

对于 25 fps 视频：

| GOP 帧数 | 关键帧间隔 |
|---:|---:|
| 250 | 10 秒 |
| 125 | 5 秒 |
| 50 | 2 秒 |
| 25 | 1 秒 |

本项目将 GOP 设置为 25 帧，因此每 1 秒产生一个关键帧。

## 4. 为什么缩短 GOP 能改善 seek

缩短 GOP 不会改变 `seekTo()` 的基本工作原理，但会为播放器提供更多可定位的解码起点。

优化前：

```text
关键帧：0-----------10-----------20-----------30
目标点：                 16
```

优化后：

```text
关键帧：0-1-2-3-4-5-6-7-8-9-10-11-12-13-14-15-16-17...
目标点：                                  16
```

当关键帧每 1 秒出现一次时：

- 如果播放器选择离目标最近的关键帧，理论最大偏差约为 0.5 秒。
- 如果播放器只能选择目标之前的关键帧，最大偏差约为 1 秒。
- 旧版约 10 秒 GOP 的对应偏差可能达到约 5 秒或 10 秒。

因此，引入并固定 GOP 能显著改善 Android seek 定位准确度。

需要注意：缩短 GOP 改善的是“基于关键帧的快速 seek”，并不等于逐帧精确 seek。如果业务必须准确显示任意一帧，Android 端仍需从目标之前的关键帧开始解码到目标帧，或使用支持精确 seek 的播放方案。

## 5. 实现方案

流水线新增以下命令行参数：

| 参数 | 推荐值 | 作用 |
|---|---:|---|
| `--video-mp4-muxer` | `psp` | 写入 MPEG-4 Sony PSP / `MSNV` 封装标记 |
| `--video-h264-profile` | `main` | 使用 H.264 Main Profile |
| `--video-h264-level` | `3.1` | 使用 H.264 Level 3.1 |
| `--video-keyframe-interval-seconds` | `1` | 将最大关键帧间隔设为 1 秒 |
| `--video-reference-frames` | `3` | 将 H.264 参考帧数量设为 3 |

当帧率为 25 fps、关键帧间隔为 1 秒时，流水线向 FFmpeg 应用的核心参数等价于：

```text
-profile:v main
-level:v 3.1
-coder:v cabac
-refs 3
-x264-params b-pyramid=none
-force_key_frames expr:gte(t,n_forced*1)
-g 25
-keyint_min 25
-sc_threshold 0
-f psp
```

各参数的含义：

- `-g 25`：最大 GOP 长度为 25 帧。
- `-keyint_min 25`：固定最小关键帧间隔。
- `-sc_threshold 0`：关闭自动场景切换关键帧，获得稳定的固定 GOP。
- `-force_key_frames expr:gte(t,n_forced*1)`：按时间确保每 1 秒生成关键帧。
- `-profile:v main`：匹配参考视频的 Main Profile。
- `-level:v 3.1`：匹配参考视频的 Level 3.1。
- `-coder:v cabac`：显式启用 CABAC。
- `-refs 3`：指定 3 个参考帧。
- `-x264-params b-pyramid=none`：禁止 B-pyramid 增加额外参考帧。如果只设置 `-refs 3`，SPS 可能实际报告 4 个参考帧。
- `-f psp`：使用 PSP MP4 muxer，使 MP4 的 major brand 为 `MSNV`。

相关实现位置：

- `video_analysis_pipeline/cli.py`：命令行参数
- `video_analysis_pipeline/config.py`：配置字段与校验
- `video_analysis_pipeline/media.py`：FFmpeg 参数构造与视频转码

## 6. 推荐导出命令

首次按新编码参数完整重导时，建议使用新输出目录：

```powershell
py run_pipeline.py batch `
  --input-root "E:\repositories\demos\VideoAnalysisPipeline\files\深圳5A" `
  --output-root "E:\repositories\demos\VideoAnalysisPipeline\output\深圳5A_android" `
  --video-target-size-ratio 1000 `
  --video-audio-bitrate-kbps 128 `
  --video-x264-preset veryfast `
  --video-mp4-muxer psp `
  --video-h264-profile main `
  --video-h264-level 3.1 `
  --video-keyframe-interval-seconds 1 `
  --video-reference-frames 3 `
  --video-frame-size 1280x720 `
  --video-fps 25 `
  --video-audio-sample-rate-hz 44100 `
  --video-audio-channels 2 `
  --video-audio-bit-depth 32 `
  --audio-target-size-ratio 128 `
  --final-output mod
```

首次重导不要添加 `--resume`。`--resume` 会跳过已经标记为完成的条目，使旧视频无法应用新的编码参数。只有在使用完全相同参数的批处理意外中断后，才应添加 `--resume` 继续执行。

## 7. 实际验证结果

以 `dubbing\1\02.mp4` 为例：

| 属性 | 旧版 | 新版 |
|---|---|---|
| H.264 Profile | High@L3.1 | Main@L3.1 |
| MP4 major brand | `isom` | `MSNV` |
| CABAC | 开启 | 开启 |
| SPS 参考帧 | 4 | 3 |
| 关键帧 | 0、7.76、17.76、27.76、37.76 秒 | 0、1、2、3、4、5……秒 |
| 视频码率 | 约 862 kbps | 约 907 kbps |
| 文件大小 | 5,600,535 字节 | 5,853,619 字节 |

新视频的关键帧已经严格按 1 秒分布。

`01.mp4` 是从已优化的完整视频中移除音轨得到的静音视频。它保留相同的 H.264 码流和关键帧分布，因此同样是每 1 秒一个关键帧；不过重新封装后的 container major brand 仍可能显示为普通 `isom`。这不影响关键帧 seek。

## 8. 为什么新视频文件会变大

关键帧是完整或接近完整的画面，通常明显大于依赖前后帧的 P/B 帧。

旧版约 10 秒一个关键帧，新版每 1 秒一个关键帧，单位时间内关键帧数量大约增加到原来的 10 倍。虽然编码器仍会尽量遵守目标码率，但文件大小和实际平均码率可能有所上升。

此外还有以下压缩效率变化：

- 从 High Profile 调整到 Main Profile。
- 将参考帧固定为 3。
- 为了保证 SPS 精确报告 3 个参考帧而关闭 B-pyramid 参考。

这些设置优先保证 Android 兼容性、seek 准确度和参考格式一致性，而不是追求最高压缩率。

实测第一条视频从约 5.60 MB 增加到约 5.85 MB，增长约 4.5%，属于预期范围。

## 9. 如何验收

### 9.1 检查视频和音频参数

```powershell
ffprobe -v error `
  -show_entries stream=index,codec_name,profile,level,width,height,r_frame_rate,bit_rate,sample_rate,channels,channel_layout `
  -show_entries format_tags=major_brand,compatible_brands `
  -of json `
  "E:\repositories\demos\VideoAnalysisPipeline\output\深圳5A_android\dubbing\1\02.mp4"
```

预期结果包括：

- 视频 `codec_name` 为 `h264`
- `profile` 为 `Main`
- `level` 为 `31`
- 分辨率为 `1280x720`
- 帧率为 `25/1`
- 音频为 `aac` / `LC`
- 采样率为 `44100`
- 声道数为 `2`
- `major_brand` 为 `MSNV`

### 9.2 检查关键帧时间

```powershell
ffprobe -v error `
  -select_streams v:0 `
  -skip_frame nokey `
  -show_entries frame=pts_time `
  -of csv=p=0 `
  "E:\repositories\demos\VideoAnalysisPipeline\output\深圳5A_android\dubbing\1\02.mp4"
```

预期输出：

```text
0.000000
1.000000
2.000000
3.000000
4.000000
...
```

Windows 文件属性页通常只显示“MP4、H.264、AAC、分辨率、帧率”等基础信息，不显示 GOP、关键帧时间、CABAC 或 SPS 参考帧数量。因此不能仅通过 Windows 属性页判断本次优化是否生效。

### 9.3 Android 端验收

建议在视频的不整秒时间点测试，例如：

```text
1.3 秒、5.7 秒、16.4 秒、30.8 秒
```

重点确认：

- 多次 seek 后播放位置是否稳定。
- 定位偏差是否控制在约 0.5～1 秒内。
- seek 后是否能够快速出画。
- `01.mp4` 和 `02.mp4` 是否都符合业务端实际使用场景。

## 10. 结论

旧方案的问题不是“H.264 不能在 Android 播放”，而是 H.264 默认 GOP 太长，导致 Android 播放器缺少足够密集的同步帧作为 seek 起点。

本方案通过将 GOP 从最多约 250 帧缩短到固定 25 帧，在 25 fps 下实现每 1 秒一个关键帧，并补齐 Main@L3.1、CABAC、3 参考帧和 Sony PSP MP4 封装。代价是文件体积略有增加，但能够显著改善 Android `VideoView` / `MediaPlayer` 的 seek 定位准确度。
