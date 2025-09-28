using NAudio.Wave;
using System.Text.RegularExpressions;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 音频切割服务
/// 负责将音频文件按照分割点切割成多个独立的音频文件
/// </summary>
public class AudioSplittingService
{
    /// <summary>
    /// 切割音频文件
    /// </summary>
    public async Task SplitAudioFilesAsync(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("?? 开始切割音频文件...");
        
        // ?? 获取源文件信息
        var sourceExtension = Path.GetExtension(sourceAudio).ToLowerInvariant();
        var isOriginalFormat = !sourceAudio.Contains("processed.wav");
        
        Console.WriteLine($"?? 源文件格式: {sourceExtension.ToUpper().TrimStart('.')}");
        Console.WriteLine($"?? 输出格式: {(isOriginalFormat ? sourceExtension.ToUpper().TrimStart('.') : "WAV")} (保持原始音质)");

        // 根据源文件格式选择切割方法
        if (isOriginalFormat && sourceExtension != ".wav")
        {
            // ?? 使用FFmpeg直接切割原始格式文件 (保持原始音质)
            await SplitAudioWithFFmpegAsync(sourceAudio, segments, config);
        }
        else
        {
            // ?? 使用NAudio切割WAV文件
            await SplitWavAudioWithNAudioAsync(sourceAudio, segments, config);
        }

        Console.WriteLine($"\n?? 音频切割完成！共生成 {segments.Count} 个句子音频文件");
    }

    private async Task SplitAudioWithFFmpegAsync(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("??? 使用FFmpeg进行原始格式切割 (保持最佳音质)...");
        
        var sourceExtension = Path.GetExtension(sourceAudio);
        
        // ?? 获取源文件音频信息
        await DisplaySourceAudioInfoAsync(sourceAudio);

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            await ProcessSegmentWithFFmpeg(sourceAudio, segment, sourceExtension, config, i, segments.Count);
        }
    }

    private async Task ProcessSegmentWithFFmpeg(string sourceAudio, AudioSegment segment, string sourceExtension, 
        SplitterConfig config, int segmentIndex, int totalSegments)
    {
        // 创建输出文件名 (保持原始格式)
        var cleanText = CleanTextForFilename(segment.Text);
        var outputFileName = $"sentence_{segmentIndex + 1:D2}_{segment.StartTime:F1}s-{segment.EndTime:F1}s_{cleanText}{sourceExtension}";
        var outputPath = Path.Combine(config.OutputDirectory, outputFileName);

        try
        {
            Console.WriteLine($"\n?? 切割句子 {segmentIndex + 1}/{totalSegments}:");
            Console.WriteLine($"   时间: {segment.StartTime:F2}s - {segment.EndTime:F2}s ({segment.Duration:F2}s)");
            Console.WriteLine($"   内容: \"{segment.Text}\"");
            Console.WriteLine($"   文件: {outputFileName}");

            // ?? 使用FFmpeg高品质切割参数
            var ffmpegArgs = BuildFFmpegCutCommand(sourceAudio, segment, outputPath);
            Console.WriteLine($"   ??? FFmpeg命令: ffmpeg {ffmpegArgs}");

            var success = await ExecuteFFmpegCommand(ffmpegArgs);

            if (success)
            {
                // 更新段信息
                segment.OutputFileName = outputFileName;
                segment.OutputPath = outputPath;

                // 验证生成的文件
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"   ? 已生成: {fileInfo.Length / 1024:F1} KB");
                
                // ?? 验证音频时长 (使用FFprobe)
                await ValidateFFmpegOutputAsync(outputPath, segment.Duration);
            }
            else
            {
                Console.WriteLine($"   ? FFmpeg切割失败");
                
                // ?? 回退到重编码模式
                Console.WriteLine($"   ?? 尝试重编码模式...");
                await SplitWithReencodingAsync(sourceAudio, segment, outputPath, sourceExtension);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ? 切割片段 {segmentIndex + 1} 时出错: {ex.Message}");
        }
    }

    private string BuildFFmpegCutCommand(string sourceAudio, AudioSegment segment, string outputPath)
    {
        return $"-i \"{sourceAudio}\" " +
               $"-ss {segment.StartTime:F3} " +                    // 开始时间 (精确到毫秒)
               $"-t {segment.Duration:F3} " +                      // 持续时间
               $"-c copy " +                                       // ?? 流复制，不重新编码 (保持原始音质)
               $"-avoid_negative_ts make_zero " +                  // 避免负时间戳
               $"-y \"{outputPath}\"";                             // 覆盖输出文件
    }

    private async Task SplitWithReencodingAsync(string sourceAudio, AudioSegment segment, string outputPath, string sourceExtension)
    {
        // ?? 根据格式选择最佳重编码参数
        string codecArgs = GetReencodingParameters(sourceExtension);
        
        var ffmpegArgs = $"-i \"{sourceAudio}\" " +
                       $"-ss {segment.StartTime:F3} " +
                       $"-t {segment.Duration:F3} " +
                       $"{codecArgs} " +
                       $"-y \"{outputPath}\"";

        Console.WriteLine($"   ??? 重编码命令: ffmpeg {ffmpegArgs}");

        var success = await ExecuteFFmpegCommand(ffmpegArgs);

        if (success)
        {
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"   ? 重编码成功: {fileInfo.Length / 1024:F1} KB");
        }
        else
        {
            Console.WriteLine($"   ? 重编码也失败");
        }
    }

    private string GetReencodingParameters(string sourceExtension)
    {
        return sourceExtension.ToLowerInvariant() switch
        {
            ".mp3" => "-c:a libmp3lame -b:a 320k",          // 320kbps MP3
            ".aac" => "-c:a aac -b:a 256k",                 // 256kbps AAC
            ".flac" => "-c:a flac",                         // 无损FLAC
            ".ogg" => "-c:a libvorbis -q:a 8",             // 高质量Ogg
            _ => "-c:a libmp3lame -b:a 320k"               // 默认高质量MP3
        };
    }

    private async Task<bool> ExecuteFFmpegCommand(string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        
        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"   FFmpeg错误: {error}");
        }

        return process.ExitCode == 0;
    }

    private async Task DisplaySourceAudioInfoAsync(string audioPath)
    {
        try
        {
            // 使用FFprobe获取音频信息
            var ffprobeArgs = $"-v quiet -print_format json -show_format -show_streams \"{audioPath}\"";
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = ffprobeArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                ParseAndDisplayAudioInfo(output, audioPath);
            }
            else
            {
                Console.WriteLine($"?? 源音频: {Path.GetFileName(audioPath)} (无法获取详细信息)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? 源音频: {Path.GetFileName(audioPath)} (信息获取失败: {ex.Message})");
        }
    }

    private void ParseAndDisplayAudioInfo(string jsonOutput, string audioPath)
    {
        // 简单解析JSON信息
        if (jsonOutput.Contains("codec_name"))
        {
            Console.WriteLine($"?? 源音频信息: {Path.GetFileName(audioPath)}");
            
            // 提取基本信息
            ExtractAndDisplayAudioProperty(jsonOutput, "sample_rate", "采样率", "Hz");
            ExtractAndDisplayAudioProperty(jsonOutput, "channels", "声道数", "");
            ExtractAndDisplayBitrate(jsonOutput);
            ExtractAndDisplayDuration(jsonOutput);
        }
    }

    private void ExtractAndDisplayAudioProperty(string json, string propertyName, string displayName, string unit)
    {
        if (json.Contains($"\"{propertyName}\""))
        {
            var value = ExtractJsonValue(json, propertyName);
            if (!string.IsNullOrEmpty(value))
            {
                Console.WriteLine($"   {displayName}: {value}{unit}");
            }
        }
    }

    private void ExtractAndDisplayBitrate(string json)
    {
        if (json.Contains("\"bit_rate\""))
        {
            var bitrate = ExtractJsonValue(json, "bit_rate");
            if (!string.IsNullOrEmpty(bitrate) && int.TryParse(bitrate, out int bitrateValue))
            {
                Console.WriteLine($"   比特率: {bitrateValue / 1000}kbps");
            }
        }
    }

    private void ExtractAndDisplayDuration(string json)
    {
        if (json.Contains("\"duration\""))
        {
            var duration = ExtractJsonValue(json, "duration");
            if (!string.IsNullOrEmpty(duration) && double.TryParse(duration, out double durationValue))
            {
                Console.WriteLine($"   时长: {durationValue:F2}秒");
            }
        }
    }

    private string ExtractJsonValue(string json, string key)
    {
        try
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"?([^,\"\\}}]+)\"?";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private async Task ValidateFFmpegOutputAsync(string outputPath, double expectedDuration)
    {
        try
        {
            // 使用FFprobe验证输出文件时长
            var ffprobeArgs = $"-v quiet -show_entries format=duration -of csv:p=0 \"{outputPath}\"";
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = ffprobeArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output.Trim()))
            {
                ValidateDuration(output.Trim(), expectedDuration);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ?? 无法验证输出文件时长: {ex.Message}");
        }
    }

    private void ValidateDuration(string durationString, double expectedDuration)
    {
        if (double.TryParse(durationString, out double actualDuration))
        {
            Console.WriteLine($"   ?? 实际时长: {actualDuration:F2}s (期望: {expectedDuration:F2}s)");
            
            var difference = Math.Abs(actualDuration - expectedDuration);
            if (difference > 0.1)
            {
                Console.WriteLine($"   ?? 时长差异: {difference:F2}s");
            }
            else
            {
                Console.WriteLine($"   ? 时长匹配");
            }
        }
    }

    private async Task SplitWavAudioWithNAudioAsync(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("?? 使用NAudio进行WAV格式切割...");
        
        using var reader = new AudioFileReader(sourceAudio);
        var format = reader.WaveFormat;

        Console.WriteLine($"源音频格式: {format.SampleRate}Hz, {format.Channels}通道, {format.BitsPerSample}位");
        Console.WriteLine($"源音频时长: {reader.TotalTime.TotalSeconds:F2}秒");

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            await ProcessSegmentWithNAudio(reader, segment, format, config, i, segments.Count);
        }
    }

    private async Task ProcessSegmentWithNAudio(AudioFileReader reader, AudioSegment segment, WaveFormat format, 
        SplitterConfig config, int segmentIndex, int totalSegments)
    {
        // 创建更清晰的文件名
        var cleanText = CleanTextForFilename(segment.Text);
        var outputFileName = $"sentence_{segmentIndex + 1:D2}_{segment.StartTime:F1}s-{segment.EndTime:F1}s_{cleanText}.wav";
        var outputPath = Path.Combine(config.OutputDirectory, outputFileName);

        try
        {
            Console.WriteLine($"\n?? 切割句子 {segmentIndex + 1}/{totalSegments}:");
            Console.WriteLine($"   时间: {segment.StartTime:F2}s - {segment.EndTime:F2}s ({segment.Duration:F2}s)");
            Console.WriteLine($"   内容: \"{segment.Text}\"");
            Console.WriteLine($"   文件: {outputFileName}");

            // 计算精确的采样位置
            var (startByte, totalBytes) = CalculateSamplePositions(segment, format);
            
            // 设置读取位置
            reader.Position = startByte;

            // 创建输出文件
            using var writer = new WaveFileWriter(outputPath, format);
            
            // 复制音频数据
            await CopyAudioData(reader, writer, totalBytes, format);

            // 更新段信息
            segment.OutputFileName = outputFileName;
            segment.OutputPath = outputPath;

            // 验证生成的文件
            ValidateGeneratedFile(outputPath, segment);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ? 切割片段 {segmentIndex + 1} 时出错: {ex.Message}");
        }
    }

    private (long startByte, long totalBytes) CalculateSamplePositions(AudioSegment segment, WaveFormat format)
    {
        var startSample = (long)(segment.StartTime * format.SampleRate);
        var endSample = (long)(segment.EndTime * format.SampleRate);
        var sampleCount = endSample - startSample;

        // 计算字节位置
        var bytesPerSample = format.BitsPerSample / 8 * format.Channels;
        var startByte = startSample * bytesPerSample;
        var totalBytes = sampleCount * bytesPerSample;

        Console.WriteLine($"   采样范围: {startSample} - {endSample} ({sampleCount} samples)");
        Console.WriteLine($"   字节范围: {startByte} - {startByte + totalBytes} ({totalBytes} bytes)");

        return (startByte, totalBytes);
    }

    private async Task CopyAudioData(AudioFileReader reader, WaveFileWriter writer, long totalBytes, WaveFormat format)
    {
        // 使用较小的缓冲区以获得更好的精度
        var bufferSize = Math.Min(format.AverageBytesPerSecond / 4, (int)totalBytes);
        var buffer = new byte[bufferSize];
        var bytesRead = 0L;

        while (bytesRead < totalBytes)
        {
            var bytesToRead = (int)Math.Min(buffer.Length, totalBytes - bytesRead);
            var actualBytesRead = reader.Read(buffer, 0, bytesToRead);

            if (actualBytesRead == 0) 
            {
                Console.WriteLine($"   ?? 提前结束读取，已读取 {bytesRead} / {totalBytes} 字节");
                break;
            }

            writer.Write(buffer, 0, actualBytesRead);
            bytesRead += actualBytesRead;
        }
    }

    private void ValidateGeneratedFile(string outputPath, AudioSegment segment)
    {
        var fileInfo = new FileInfo(outputPath);
        Console.WriteLine($"   ? 已生成: {fileInfo.Length / 1024:F1} KB");

        // 验证音频文件时长
        using var verifyReader = new AudioFileReader(outputPath);
        var actualDuration = verifyReader.TotalTime.TotalSeconds;
        Console.WriteLine($"   ?? 实际时长: {actualDuration:F2}s (期望: {segment.Duration:F2}s)");
        
        if (Math.Abs(actualDuration - segment.Duration) > 0.1)
        {
            Console.WriteLine($"   ?? 时长差异较大: {Math.Abs(actualDuration - segment.Duration):F2}s");
        }
    }

    private string CleanTextForFilename(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";

        // 取前20个字符，移除不安全的文件名字符
        var cleaned = text.Length > 20 ? text.Substring(0, 20) : text;
        
        // 移除或替换不安全的字符
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            cleaned = cleaned.Replace(c, '_');
        }
        
        // 移除标点符号并替换空格
        cleaned = cleaned.Replace(" ", "_")
                        .Replace(".", "")
                        .Replace("!", "")
                        .Replace("?", "")
                        .Replace(";", "")
                        .Replace(",", "")
                        .Replace("'", "")
                        .Replace("\"", "");
        
        return string.IsNullOrWhiteSpace(cleaned) ? "segment" : cleaned;
    }
}