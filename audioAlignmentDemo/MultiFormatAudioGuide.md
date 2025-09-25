# ?? 多格式音频支持指南

您的音频切割系统现在支持多种音频格式！

## ? 支持的音频格式

| 格式 | 扩展名 | 描述 | 处理方式 |
|------|--------|------|----------|
| **WAV** | `.wav` | 无损音频格式 | 直接处理，格式优化 |
| **MP3** | `.mp3` | 常用压缩格式 | 自动转换为WAV |
| **M4A** | `.m4a` | Apple音频格式 | 自动转换为WAV |
| **WMA** | `.wma` | Windows Media音频 | 自动转换为WAV |
| **AAC** | `.aac` | 高效音频编码 | 自动转换为WAV |
| **FLAC** | `.flac` | 无损压缩格式 | 自动转换为WAV |
| **OGG** | `.ogg` | 开源音频格式 | 自动转换为WAV |
| **MP4** | `.mp4` | 视频文件(提取音频) | 自动转换为WAV |

## ?? 使用方法

### 1. 更新输入文件路径
```csharp
var config = new SplitterConfig
{
    InputAudioPath = "your_audio.mp3",  // ?? 支持各种格式
    // ... 其他配置
};
```

### 2. 常见文件名示例
```
? "speech.wav"     - WAV格式
? "podcast.mp3"    - MP3格式  
? "interview.m4a"  - M4A格式
? "meeting.wma"    - WMA格式
? "music.flac"     - FLAC格式
? "recording.aac"  - AAC格式
? "video.mp4"      - MP4格式(提取音频)
```

## ?? 转换过程

### 自动检测和转换
1. **格式检测**: 系统自动检测音频文件格式
2. **智能转换**: 根据格式选择最佳转换方法
3. **质量保证**: 转换为Whisper需要的16kHz单声道PCM格式
4. **格式验证**: 确保转换后的文件符合要求

### 处理流程
```
原始音频 → 格式检测 → 转换/优化 → Whisper处理 → 句子切割
```

## ? 转换方法

### 方法1: NAudio (默认)
- ? 内置支持，无需外部依赖
- ? 支持大部分常见格式
- ? 高质量转换

### 方法2: FFmpeg (备用)
- ?? 当NAudio无法处理时自动启用
- ?? 支持更多格式和编码
- ?? 需要安装FFmpeg

## ?? 输出示例

```
?? 检测音频格式: MP3
?? 原始格式信息:
   采样率: 44100Hz
   声道数: 2
   位深度: 16位
   编码: Mp3Frame
   时长: 45.67秒
?? 目标格式: 16kHz, 16位, 单声道 PCM
? 转换完成: output_sentences\processed.wav
? 音频格式验证通过
```

## ??? 故障排除

### 转换失败？
1. **检查文件格式**: 确保文件未损坏
2. **安装FFmpeg**: 下载并添加到PATH环境变量
3. **手动转换**: 使用在线工具转换为WAV格式

### FFmpeg安装
```bash
# Windows (使用Chocolatey)
choco install ffmpeg

# 或下载并添加到PATH
# https://ffmpeg.org/download.html
```

## ?? 最佳实践

### 推荐格式优先级
1. **WAV** - 最佳兼容性，无需转换
2. **FLAC** - 无损质量，较好支持
3. **MP3** - 常用格式，良好支持
4. **M4A/AAC** - Apple生态，较好支持

### 性能优化
- ?? WAV格式处理最快
- ?? 无损格式质量最好
- ?? 压缩格式文件更小

## ?? 配置参数

```csharp
// 音频相关配置
AudioConversionQuality = 60,      // 转换质量 (1-100)
EnableFFmpegFallback = true,      // 启用FFmpeg备用
```

现在您的系统可以处理几乎所有常见的音频格式了！??