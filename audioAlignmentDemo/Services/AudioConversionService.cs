using NAudio.Wave;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 音频格式转换服务
/// 负责将各种格式的音频文件转换为Whisper兼容的格式
/// </summary>
public class AudioConversionService
{
    /// <summary>
    /// 将音频文件转换为Whisper兼容的WAV格式
    /// </summary>
    public async Task<string> ConvertToWhisperFormatAsync(string inputPath, string outputDirectory)
    {
        var inputExtension = Path.GetExtension(inputPath).ToLowerInvariant();
        var outputPath = Path.Combine(outputDirectory, "processed.wav");
        
        Console.WriteLine($"?? 检测音频格式: {inputExtension.ToUpper().TrimStart('.')}");
        
        // 支持的音频格式检查
        var supportedFormats = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
        
        if (!supportedFormats.Contains(inputExtension))
        {
            var supportedList = string.Join(", ", supportedFormats.Select(f => f.ToUpper().TrimStart('.')));
            throw new NotSupportedException($"? 不支持的音频格式: {inputExtension.ToUpper().TrimStart('.')}\n? 支持的格式: {supportedList}");
        }

        try
        {
            if (inputExtension == ".wav")
            {
                Console.WriteLine("?? 检测到WAV格式，进行智能优化处理...");
                ConvertWavToOptimalFormat(inputPath, outputPath);
            }
            else
            {
                Console.WriteLine($"?? 转换 {inputExtension.ToUpper().TrimStart('.')} 格式到高品质WAV...");
                await ConvertToWavAsync(inputPath, outputPath);
            }
            
            // 验证转换结果
            ValidateConversionResult(outputPath);
            
            return outputPath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"音频格式转换失败: {ex.Message}", ex);
        }
    }

    private async Task ConvertToWavAsync(string inputPath, string outputPath)
    {
        try
        {
            using var reader = new AudioFileReader(inputPath);
            
            DisplayOriginalFormatInfo(reader);
            
            var targetFormat = DetermineTargetFormat(reader.WaveFormat, out string conversionStrategy);
            
            Console.WriteLine($"?? 转换策略: {conversionStrategy}");
            Console.WriteLine($"?? 目标格式: {targetFormat.SampleRate}Hz, {targetFormat.BitsPerSample}位, {targetFormat.Channels}声道 PCM");
            
            // ?? 使用高品质重采样设置
            using var resampler = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 100 // ?? 最高质量重采样 (0-100)
            };
            
            // ?? 生成高品质WAV文件
            WaveFileWriter.CreateWaveFile(outputPath, resampler);
            
            Console.WriteLine($"? 高品质转换完成: {outputPath}");
            
            // ?? 详细验证转换结果
            ValidateConvertedFile(outputPath, reader.WaveFormat, conversionStrategy);
        }
        catch (Exception ex)
        {
            // 回退策略
            Console.WriteLine($"?? 高品质转换失败: {ex.Message}");
            await TryFallbackConversion(inputPath, outputPath);
        }
    }

    private void ConvertWavToOptimalFormat(string inputPath, string outputPath)
    {
        Console.WriteLine("?? WAV格式智能优化...");

        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            using var reader = new AudioFileReader(inputPath);
            
            Console.WriteLine($"?? 原始WAV格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
            Console.WriteLine($"?? 原始编码: {reader.WaveFormat.Encoding}");

            var targetFormat = DetermineWavOptimizationFormat(reader.WaveFormat, out string optimizationStrategy);

            Console.WriteLine($"?? 优化策略: {optimizationStrategy}");
            Console.WriteLine($"?? 目标格式: {targetFormat.SampleRate}Hz, {targetFormat.Channels}通道, {targetFormat.BitsPerSample}位");

            // 检查是否需要实际转换
            if (reader.WaveFormat.Equals(targetFormat))
            {
                Console.WriteLine("?? 格式已经是最优，直接复制文件...");
                File.Copy(inputPath, outputPath, true);
            }
            else
            {
                // ?? 使用最高品质重采样进行转换
                using var resampler = new MediaFoundationResampler(reader, targetFormat)
                {
                    ResamplerQuality = 100 // ?? 最高质量重采样
                };

                WaveFileWriter.CreateWaveFile(outputPath, resampler);
            }

            Console.WriteLine($"? WAV格式优化完成: {outputPath}");
            
            ValidateConvertedFile(outputPath, reader.WaveFormat, optimizationStrategy);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"WAV格式优化失败: {ex.Message}", ex);
        }
    }

    private WaveFormat DetermineTargetFormat(WaveFormat originalFormat, out string strategy)
    {
        // 保持原始采样率，但至少16kHz用于Whisper兼容性
        int targetSampleRate = Math.Max(originalFormat.SampleRate, 16000);
        
        // 如果原始音频是高采样率，保持较高的品质
        if (originalFormat.SampleRate >= 44100)
        {
            strategy = "高品质保持";
            return new WaveFormat(
                originalFormat.SampleRate, // 保持原始采样率
                24, // 提升到24位获得更好的动态范围
                Math.Min(originalFormat.Channels, 2) // 最多保持立体声
            );
        }
        else if (originalFormat.SampleRate >= 22050)
        {
            strategy = "品质提升";
            return new WaveFormat(
                Math.Max(originalFormat.SampleRate, 44100), // 提升到CD品质
                24, 
                Math.Min(originalFormat.Channels, 2)
            );
        }
        else
        {
            strategy = "标准品质";
            return new WaveFormat(
                Math.Max(originalFormat.SampleRate, 22050), // 至少22kHz
                24, // 24位而不是16位
                1 // 单声道用于Whisper
            );
        }
    }

    private WaveFormat DetermineWavOptimizationFormat(WaveFormat originalFormat, out string strategy)
    {
        if (originalFormat.SampleRate >= 44100 && originalFormat.BitsPerSample >= 16)
        {
            // 已经是高品质WAV，只需要确保PCM格式
            if (originalFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                strategy = "格式保持优化";
                return new WaveFormat(
                    originalFormat.SampleRate,
                    Math.Max(originalFormat.BitsPerSample, 24), // 至少24位
                    originalFormat.Channels
                );
            }
            else
            {
                strategy = "高品质PCM转换";
                return new WaveFormat(
                    originalFormat.SampleRate,
                    24, // 24位PCM
                    originalFormat.Channels
                );
            }
        }
        else
        {
            strategy = "品质提升优化";
            return new WaveFormat(
                Math.Max(originalFormat.SampleRate, 44100), // 至少44.1kHz
                24, // 24位
                Math.Max(originalFormat.Channels, 1) // 至少单声道
            );
        }
    }

    private void DisplayOriginalFormatInfo(AudioFileReader reader)
    {
        Console.WriteLine($"?? 原始格式信息:");
        Console.WriteLine($"   采样率: {reader.WaveFormat.SampleRate}Hz");
        Console.WriteLine($"   声道数: {reader.WaveFormat.Channels}");
        Console.WriteLine($"   位深度: {reader.WaveFormat.BitsPerSample}位");
        Console.WriteLine($"   编码: {reader.WaveFormat.Encoding}");
        Console.WriteLine($"   时长: {reader.TotalTime.TotalSeconds:F2}秒");
    }

    private void ValidateConversionResult(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("转换后的音频文件不存在");
        }

        var fileSize = new FileInfo(outputPath).Length;
        if (fileSize < 1024) // 小于1KB可能有问题
        {
            throw new InvalidOperationException($"转换后的音频文件过小 ({fileSize} bytes)，可能转换失败");
        }

        Console.WriteLine($"? 音频转换完成，文件大小: {fileSize / 1024:F1} KB");
    }

    private void ValidateConvertedFile(string filePath, WaveFormat originalFormat, string conversionStrategy)
    {
        try
        {
            using var reader = new WaveFileReader(filePath);
            
            Console.WriteLine($"?? 转换结果验证 ({conversionStrategy}):");
            Console.WriteLine($"   转换后格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
            Console.WriteLine($"   转换后编码: {reader.WaveFormat.Encoding}");
            Console.WriteLine($"   时长: {reader.TotalTime.TotalSeconds:F2}秒");
            Console.WriteLine($"   文件大小: {new FileInfo(filePath).Length / 1024:F2} KB");
            
            // ?? 音质分析
            var qualityScore = CalculateQualityScore(originalFormat, reader.WaveFormat);
            Console.WriteLine($"   ?? 品质评分: {qualityScore}/100 ({GetQualityDescription(qualityScore)})");
            
            if (qualityScore < 70)
            {
                Console.WriteLine($"   ?? 警告: 音质可能有明显下降，建议检查转换参数");
            }
            else if (qualityScore >= 90)
            {
                Console.WriteLine($"   ? 优秀: 音质保持良好或有提升");
            }
            
            // ? Whisper兼容性检查
            bool whisperCompatible = reader.WaveFormat.SampleRate >= 16000 && 
                                   reader.WaveFormat.Channels <= 2 && 
                                   reader.WaveFormat.BitsPerSample >= 16;
            
            Console.WriteLine($"   ?? Whisper兼容性: {(whisperCompatible ? "? 兼容" : "? 需要进一步转换")}");
            Console.WriteLine("   ? 音频文件验证完成");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"转换后文件验证失败: {ex.Message}", ex);
        }
    }

    private async Task TryFallbackConversion(string inputPath, string outputPath)
    {
        Console.WriteLine($"?? 回退到标准Whisper格式转换...");
        
        try
        {
            await ConvertToStandardWhisperFormat(inputPath, outputPath);
        }
        catch
        {
            Console.WriteLine($"?? 尝试FFmpeg备用转换方法...");
            await TryFFmpegConversion(inputPath, outputPath);
        }
    }

    private async Task ConvertToStandardWhisperFormat(string inputPath, string outputPath)
    {
        using var reader = new AudioFileReader(inputPath);
        
        // 标准Whisper格式：16kHz, 16位, 单声道
        var targetFormat = new WaveFormat(16000, 16, 1);
        
        Console.WriteLine($"?? 标准Whisper格式转换: {targetFormat.SampleRate}Hz, {targetFormat.BitsPerSample}位, {targetFormat.Channels}声道");
        
        using var resampler = new MediaFoundationResampler(reader, targetFormat)
        {
            ResamplerQuality = 60 // 标准质量重采样
        };
        
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
        
        Console.WriteLine($"? 标准格式转换完成: {outputPath}");
        ValidateConvertedFile(outputPath, reader.WaveFormat, "标准Whisper格式");
    }

    private async Task TryFFmpegConversion(string inputPath, string outputPath)
    {
        try
        {
            Console.WriteLine("??? 尝试使用FFmpeg进行高品质转换...");
            
            var ffmpegArgs = $"-i \"{inputPath}\" " +
                           $"-acodec pcm_s24le " +     // 24位PCM编码
                           $"-ar 44100 " +             // CD品质采样率
                           $"-ac 2 " +                 // 立体声
                           $"-af \"aformat=sample_fmts=s24:sample_rates=44100\" " + // 音频过滤器
                           $"-y \"{outputPath}\"";
            
            var success = await RunFFmpegCommand(ffmpegArgs);
            
            if (success)
            {
                Console.WriteLine("? FFmpeg高品质转换成功");
                ValidateConvertedFile(outputPath, null, "FFmpeg高品质");
            }
            else
            {
                await TryStandardFFmpegConversion(inputPath, outputPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? FFmpeg转换失败: {ex.Message}");
            await TryStandardFFmpegConversion(inputPath, outputPath);
        }
    }

    private async Task TryStandardFFmpegConversion(string inputPath, string outputPath)
    {
        try
        {
            Console.WriteLine("??? 使用FFmpeg标准参数转换...");
            
            var ffmpegArgs = $"-i \"{inputPath}\" -ar 22050 -ac 2 -sample_fmt s16 -y \"{outputPath}\"";
            var success = await RunFFmpegCommand(ffmpegArgs);

            if (success)
            {
                Console.WriteLine("? FFmpeg标准转换成功");
                ValidateConvertedFile(outputPath, null, "FFmpeg标准");
            }
            else
            {
                throw new InvalidOperationException("所有转换方法都失败了");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? 所有FFmpeg转换方法都失败了: {ex.Message}");
            Console.WriteLine("?? 建议:");
            Console.WriteLine("   1. 确保FFmpeg已正确安装并添加到PATH环境变量");
            Console.WriteLine("   2. 检查音频文件是否损坏");
            Console.WriteLine("   3. 尝试使用其他音频转换工具预处理文件");
            Console.WriteLine("   4. 确保有足够的磁盘空间");
            
            throw new InvalidOperationException($"无法转换音频格式。所有转换方法都失败。最后错误: {ex.Message}");
        }
    }

    private async Task<bool> RunFFmpegCommand(string arguments)
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

        return process.ExitCode == 0;
    }

    private int CalculateQualityScore(WaveFormat original, WaveFormat converted)
    {
        int score = 100;
        
        // 采样率评分 (40分)
        var sampleRateRatio = (double)converted.SampleRate / original.SampleRate;
        if (sampleRateRatio >= 1.0)
        {
            score += 0; // 保持或提升，不扣分
        }
        else if (sampleRateRatio >= 0.75)
        {
            score -= 10; // 轻微下降
        }
        else if (sampleRateRatio >= 0.5)
        {
            score -= 25; // 明显下降
        }
        else
        {
            score -= 40; // 严重下降
        }
        
        // 位深度评分 (30分)
        var bitDepthRatio = (double)converted.BitsPerSample / original.BitsPerSample;
        if (bitDepthRatio >= 1.0)
        {
            score += 0; // 保持或提升
        }
        else if (bitDepthRatio >= 0.75)
        {
            score -= 10; // 轻微下降
        }
        else
        {
            score -= 30; // 明显下降
        }
        
        // 声道评分 (20分)
        if (converted.Channels >= original.Channels)
        {
            score += 0; // 保持或提升
        }
        else if (original.Channels == 2 && converted.Channels == 1)
        {
            score -= 15; // 立体声变单声道
        }
        else
        {
            score -= 20; // 其他声道减少
        }
        
        // 编码格式评分 (10分)
        if (converted.Encoding == WaveFormatEncoding.Pcm || converted.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            score += 0; // 无损格式
        }
        else
        {
            score -= 10; // 可能有损格式
        }
        
        return Math.Max(0, Math.Min(100, score));
    }

    private string GetQualityDescription(int score)
    {
        return score switch
        {
            >= 95 => "卓越",
            >= 90 => "优秀", 
            >= 80 => "良好",
            >= 70 => "可接受",
            >= 60 => "一般",
            >= 50 => "较差",
            _ => "很差"
        };
    }
}