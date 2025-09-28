# 音频句子自动切割工具 - 使用指南

## ?? 概述

这是一个强大的音频处理工具，可以将包含多个句子的音频文件自动切割成独立的句子音频文件。支持多种使用方式：命令行操作、交互模式、批量处理和作为类库调用。

## ?? 快速开始

### 1. 命令行使用

#### 处理单个文件
```bash
# 使用默认设置处理音频文件
dotnet run -- --input audio.mp3

# 指定输出目录
dotnet run -- --input audio.mp3 --output my_output

# 使用中文语言模型
dotnet run -- --input chinese_audio.wav --language zh --model small
```

#### 批量处理
```bash
# 处理多个文件
dotnet run -- --input file1.mp3 file2.wav file3.m4a

# 处理整个目录
dotnet run -- --input-dir ./audio_files/ --batch

# 使用配置文件
dotnet run -- --input-dir ./audio/ --config my_settings.json
```

#### 高级参数
```bash
# 高精度处理
dotnet run -- --input audio.mp3 --model base --padding 0.5 --threshold 0.05 --debug

# 快速批量处理
dotnet run -- --input-dir ./batch/ --model tiny --quality Balanced --batch
```

### 2. 交互模式

直接运行程序不带任何参数，会启动交互模式：

```bash
dotnet run
```

然后按照提示选择操作：
- 处理单个音频文件
- 批量处理音频文件  
- 使用配置预设
- 创建自定义配置
- 生成示例配置文件

### 3. 配置文件

创建 JSON 配置文件来保存常用设置：

```json
{
  "Language": "en",
  "ModelSize": "base", 
  "AudioQualityStrategy": "HighQuality",
  "SentenceBoundaryPadding": 0.4,
  "EnableTimeCorrection": true,
  "TimeCorrectionThreshold": 0.1,
  "DebugMode": true
}
```

使用配置文件：
```bash
dotnet run -- --input audio.mp3 --config settings.json
```

## ?? 作为类库使用

在你的 C# 项目中引用这个库：

### 基本使用

```csharp
using AudioAlignmentDemo.Library;

// 创建分割器实例
var splitter = new AudioSplitterLibrary();

// 处理单个文件 (使用默认配置)
var result = await splitter.ProcessAudioFileAsync("input.mp3", "output_dir");

if (result.Success)
{
    Console.WriteLine($"成功生成 {result.SegmentCount} 个音频片段");
    foreach (var file in result.GeneratedFiles)
    {
        Console.WriteLine($"生成文件: {file}");
    }
}
else
{
    Console.WriteLine($"处理失败: {result.Error}");
}
```

### 自定义配置

```csharp
using AudioAlignmentDemo.Library;
using AudioAlignmentDemo.Configuration;

// 使用预设配置
var config = ConfigurationManager.Presets.HighPrecision;
config.InputAudioPath = "my_audio.mp3";
config.OutputDirectory = "my_output";

var splitter = new AudioSplitterLibrary();
var result = await splitter.ProcessAudioFileAsync(config);
```

### 批量处理

```csharp
using AudioAlignmentDemo.Library;

var splitter = new AudioSplitterLibrary();

// 从目录查找音频文件
var audioFiles = AudioSplitterLibrary.FindAudioFiles("./audio_directory/");

// 批量处理
var batchResult = await splitter.ProcessAudioFilesAsync(
    audioFiles, 
    "batch_output",
    "balanced" // 使用平衡预设
);

Console.WriteLine($"处理了 {batchResult.InputFiles.Count} 个文件");
Console.WriteLine($"成功: {batchResult.SuccessfulFiles}, 失败: {batchResult.FailedFiles}");
Console.WriteLine($"总共生成 {batchResult.TotalSegments} 个音频片段");
```

### 验证和查找文件

```csharp
// 检查文件是否支持
if (AudioSplitterLibrary.IsAudioFileSupported("test.mp3"))
{
    Console.WriteLine("文件格式支持");
}

// 获取可用预设
var presets = AudioSplitterLibrary.GetAvailablePresets();
Console.WriteLine("可用预设: " + string.Join(", ", presets));

// 查找音频文件
var audioFiles = AudioSplitterLibrary.FindAudioFiles("./music/", includeSubdirectories: true);
Console.WriteLine($"找到 {audioFiles.Count} 个音频文件");
```

## ?? 配置预设

工具内置了多种预设配置，适应不同使用场景：

### 1. high-precision (高精度模式)
- 适合：重要内容的精细切割
- 特点：使用 base 模型，高边界填充，详细调试信息

### 2. fast-batch (快速批量模式)  
- 适合：大量文件的快速处理
- 特点：使用 tiny 模型，较少填充，关闭调试

### 3. balanced (平衡模式)
- 适合：日常使用，在精度和速度间平衡
- 特点：使用 small 模型，适中参数

### 4. chinese (中文优化模式)
- 适合：中文语音内容
- 特点：针对中文语言特点优化参数

### 5. english-dialogue (英文对话模式)
- 适合：英文对话和访谈
- 特点：针对对话特点优化，处理语气词

## ??? 主要参数说明

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `--input` | string[] | - | 输入音频文件路径 (必需) |
| `--input-dir` | string | - | 输入目录路径 |
| `--output` | string | output_sentences | 输出目录路径 |
| `--language` | string | en | 语言代码 (en/zh/ja等) |
| `--model` | string | tiny | Whisper模型 (tiny/base/small/medium/large) |
| `--quality` | string | HighQuality | 音频品质策略 |
| `--padding` | double | 0.4 | 句子边界填充时间(秒) |
| `--threshold` | double | 0.1 | 时间校正阈值(秒) |
| `--config` | string | - | 配置文件路径 |
| `--batch` | bool | false | 启用并行批量模式 |
| `--debug` | bool | false | 启用调试模式 |

## ?? 支持的音频格式

- WAV (.wav) - 无损音频格式
- MP3 (.mp3) - 常见压缩格式  
- M4A (.m4a) - Apple 音频格式
- WMA (.wma) - Windows Media 音频
- AAC (.aac) - 高级音频编码
- FLAC (.flac) - 无损压缩格式
- OGG (.ogg) - 开源音频格式
- MP4 (.mp4) - 视频容器中的音频

## ?? 使用建议

### 1. 选择合适的模型
- `tiny`: 最快，适合快速批量处理
- `base`: 平衡选择，日常使用推荐
- `small`: 更好精度，适合重要内容
- `medium/large`: 最高精度，处理时间较长

### 2. 调整边界填充
- 如果发现单词被切断，增加 `SentenceBoundaryPadding`
- 对于语速较快的内容，建议使用 0.5-0.8 秒
- 对于语速较慢的内容，0.2-0.4 秒通常足够

### 3. 时间校正
- 默认启用时间校正功能，解决识别时长不准确问题
- 如果发现结尾被截断，可以降低 `TimeCorrectionThreshold`
- 可以增加 `MaxExtensionTime` 来允许更多扩展

### 4. 批量处理优化
- 使用 `--batch` 参数启用并行处理
- 大量文件时建议使用 `fast-batch` 预设
- 可以根据 CPU 核心数调整并发数

## ?? 常见问题

### 1. 模型下载失败
- 确保网络连接正常
- 可以手动下载模型文件到 `models` 目录
- 首次使用需要下载对应的 Whisper 模型

### 2. FFmpeg 相关错误
- 确保系统已安装 FFmpeg
- 将 FFmpeg 添加到系统 PATH 环境变量
- 对于 WAV 文件，会优先使用 NAudio 处理

### 3. 音频格式不支持
- 检查文件扩展名是否在支持列表中
- 可以先用其他工具转换为支持的格式
- 使用 `IsAudioFileSupported` 方法验证

### 4. 内存不足
- 处理大文件时可能需要更多内存
- 可以尝试使用更小的模型 (tiny/base)
- 批量处理时调整并发数量

## ?? 输出文件

处理完成后会生成以下文件：
- `sentence_XX_time_text.{format}` - 切割的音频片段
- `sentence_split_report.json` - 详细处理报告
- `sentence_list.txt` - 句子清单
- `performance_report.json` - 性能分析报告 (如果启用)

## ?? 更新和扩展

这个工具采用模块化设计，可以轻松扩展：
- 添加新的音频格式支持
- 扩展语言识别能力
- 增加新的分析算法
- 集成其他语音识别引擎

## ?? 技术支持

如果遇到问题或需要新功能：
1. 检查本文档的常见问题部分
2. 使用 `--debug` 参数获取详细日志
3. 确认输入文件格式和路径正确
4. 检查系统依赖 (FFmpeg, .NET 9) 是否正确安装