using Whisper.net;
using NAudio.Wave;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 语音识别和对齐服务
/// 使用Whisper模型进行语音识别并校正时间对齐
/// </summary>
public class SpeechRecognitionService
{
    /// <summary>
    /// 执行语音识别和时间对齐
    /// </summary>
    public async Task<List<AudioSegment>> PerformAlignmentAsync(string audioPath, SplitterConfig config)
    {
        Console.WriteLine("执行语音识别和对齐...");

        var segments = new List<AudioSegment>();

        try
        {
            // 验证音频文件格式
            ValidateAudioFile(audioPath);

            // 获取或下载模型
            var modelPath = await GetOrDownloadModelAsync(config.ModelSize);

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage(config.Language)
                .WithThreads(Environment.ProcessorCount)
                .Build();

            await using var fileStream = File.OpenRead(audioPath);

            Console.WriteLine("开始语音识别...");
            
            // ?? 获取音频文件实际时长用于校正
            double actualAudioDuration = GetActualAudioDuration(audioPath);

            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                // result is SegmentData, process it directly
                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    var audioSegment = new AudioSegment
                    {
                        StartTime = result.Start.TotalSeconds,  // ? 使用 TotalSeconds 而不是 TotalMilliseconds
                        EndTime = result.End.TotalSeconds,     // ? 使用 TotalSeconds 而不是 TotalMilliseconds
                        Text = result.Text.Trim(),
                        Duration = (result.End - result.Start).TotalSeconds // ? 使用 TotalSeconds
                    };

                    segments.Add(audioSegment);
                    Console.WriteLine($"识别: [{audioSegment.StartTime:F2}s-{audioSegment.EndTime:F2}s] {audioSegment.Text}");
                }
            }

            // ?? 智能时间校正：处理 Whisper 识别时长与实际音频时长不匹配的问题
            if (segments.Count > 0 && actualAudioDuration > 0 && config.EnableTimeCorrection)
            {
                PerformTimeCorrection(segments, actualAudioDuration, config);
            }
        }
        catch (Whisper.net.Wave.CorruptedWaveException ex)
        {
            return await HandleCorruptedWaveFile(audioPath, config, ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"语音识别失败: {ex.Message}", ex);
        }

        Console.WriteLine($"识别完成，共 {segments.Count} 个片段");
        return segments;
    }

    private double GetActualAudioDuration(string audioPath)
    {
        try
        {
            using var audioReader = new AudioFileReader(audioPath);
            var duration = audioReader.TotalTime.TotalSeconds;
            Console.WriteLine($"?? 音频文件实际时长: {duration:F3}秒");
            return duration;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? 无法获取音频实际时长: {ex.Message}");
            return 0;
        }
    }

    private void PerformTimeCorrection(List<AudioSegment> segments, double actualAudioDuration, SplitterConfig config)
    {
        var whisperTotalDuration = segments.Max(s => s.EndTime);
        var timeDifference = actualAudioDuration - whisperTotalDuration;
        
        Console.WriteLine($"?? 时长对比分析:");
        Console.WriteLine($"   Whisper识别时长: {whisperTotalDuration:F3}秒");
        Console.WriteLine($"   音频实际时长: {actualAudioDuration:F3}秒");
        Console.WriteLine($"   时长差异: {timeDifference:F3}秒");

        // 如果差异超过配置的阈值，进行智能校正
        if (Math.Abs(timeDifference) > config.TimeCorrectionThreshold)
        {
            Console.WriteLine($"?? 检测到明显时长差异 (>{config.TimeCorrectionThreshold:F3}s)，开始智能校正...");
            
            if (timeDifference > 0)
            {
                ApplyExtensionCorrection(segments, actualAudioDuration, timeDifference, config);
            }
            else
            {
                ApplyCompressionCorrection(segments, actualAudioDuration, whisperTotalDuration);
            }
            
            ValidateCorrectionResults(segments, actualAudioDuration, config);
        }
        else
        {
            Console.WriteLine($"   ? 时长匹配良好，无需校正 (差异 ≤ {config.TimeCorrectionThreshold:F3}s)");
        }
    }

    private void ApplyExtensionCorrection(List<AudioSegment> segments, double actualAudioDuration, double timeDifference, SplitterConfig config)
    {
        // 实际音频比Whisper识别的长，扩展最后一个段落
        var lastSegment = segments[^1];
        var originalEnd = lastSegment.EndTime;
        
        // ?? 策略1: 按比例扩展最后一个段落
        var extensionTime = Math.Min(timeDifference, config.MaxExtensionTime);
        lastSegment.EndTime = Math.Min(actualAudioDuration, originalEnd + extensionTime);
        lastSegment.Duration = lastSegment.EndTime - lastSegment.StartTime;
        
        Console.WriteLine($"   ? 扩展最后段落: {originalEnd:F3}s → {lastSegment.EndTime:F3}s (+{extensionTime:F3}s)");
        
        // ?? 策略2: 如果还有剩余差异，按比例调整所有段落
        var remainingDifference = actualAudioDuration - lastSegment.EndTime;
        if (remainingDifference > config.TimeCorrectionThreshold)
        {
            ApplyScaleCorrection(segments, actualAudioDuration, segments.Max(s => s.EndTime), config);
        }
    }

    private void ApplyCompressionCorrection(List<AudioSegment> segments, double actualAudioDuration, double whisperTotalDuration)
    {
        // Whisper识别的比实际音频长（不常见但可能发生）
        var compressionFactor = actualAudioDuration / whisperTotalDuration;
        Console.WriteLine($"   ?? 应用时间压缩因子: {compressionFactor:F4}");
        
        foreach (var segment in segments)
        {
            segment.StartTime *= compressionFactor;
            segment.EndTime *= compressionFactor;
            segment.Duration = segment.EndTime - segment.StartTime;
        }
        
        Console.WriteLine($"   ? 时间压缩校正完成");
    }

    private void ApplyScaleCorrection(List<AudioSegment> segments, double actualAudioDuration, double currentTotalDuration, SplitterConfig config)
    {
        var scaleFactor = actualAudioDuration / currentTotalDuration;
        Console.WriteLine($"   ?? 应用时间缩放因子: {scaleFactor:F4}");
        
        foreach (var segment in segments)
        {
            var segmentOriginalStart = segment.StartTime;
            var segmentOriginalEnd = segment.EndTime;
            
            segment.StartTime *= scaleFactor;
            segment.EndTime *= scaleFactor;
            segment.Duration = segment.EndTime - segment.StartTime;
            
            if (config.DebugMode)
            {
                Console.WriteLine($"     段落校正: [{segmentOriginalStart:F3}-{segmentOriginalEnd:F3}] → [{segment.StartTime:F3}-{segment.EndTime:F3}]");
            }
        }
        
        Console.WriteLine($"   ? 时间缩放校正完成");
    }

    private void ValidateCorrectionResults(List<AudioSegment> segments, double actualAudioDuration, SplitterConfig config)
    {
        var correctedTotalDuration = segments.Max(s => s.EndTime);
        var finalDifference = Math.Abs(actualAudioDuration - correctedTotalDuration);
        
        Console.WriteLine($"?? 校正结果:");
        Console.WriteLine($"   校正后时长: {correctedTotalDuration:F3}秒");
        Console.WriteLine($"   剩余差异: {finalDifference:F3}秒");
        
        if (finalDifference <= config.TimeCorrectionThreshold)
        {
            Console.WriteLine($"   ? 校正成功！时长匹配良好");
        }
        else
        {
            Console.WriteLine($"   ?? 仍有差异，但已明显改善");
        }
    }

    private void ValidateAudioFile(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        Console.WriteLine($"处理音频文件: {audioPath}");
        Console.WriteLine($"格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
        Console.WriteLine($"时长: {reader.TotalTime.TotalSeconds:F2}秒");
        Console.WriteLine($"编码: {reader.WaveFormat.Encoding}");
        
        // 检查文件是否为空
        if (reader.TotalTime.TotalSeconds < 0.1)
        {
            throw new InvalidOperationException("音频文件时长过短或为空");
        }
        
        // 检查是否为支持的格式
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm && 
            reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            Console.WriteLine($"警告: 音频编码格式 {reader.WaveFormat.Encoding} 可能需要转换");
        }
    }

    private async Task<string> GetOrDownloadModelAsync(string modelSize)
    {
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = Path.Combine("models", modelFileName);

        if (File.Exists(modelPath))
        {
            Console.WriteLine($"使用现有模型: {modelPath}");
            return modelPath;
        }

        // 创建模型目录
        Directory.CreateDirectory("models");

        Console.WriteLine($"首次运行，正在下载模型 {modelSize}...");
        Console.WriteLine("请稍等，模型较大可能需要几分钟时间...");

        // 使用Whisper.net的内置下载功能
        using var httpClient = new HttpClient();
        var modelUrl = GetModelDownloadUrl(modelSize);

        var response = await httpClient.GetAsync(modelUrl);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(modelPath);
        await response.Content.CopyToAsync(fileStream);

        Console.WriteLine($"模型下载完成: {modelPath}");
        return modelPath;
    }

    private string GetModelDownloadUrl(string modelSize)
    {
        var baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
        return $"{baseUrl}/ggml-{modelSize}.bin";
    }

    private async Task<List<AudioSegment>> HandleCorruptedWaveFile(string audioPath, SplitterConfig config, Exception originalException)
    {
        Console.WriteLine($"WAV文件格式错误: {originalException.Message}");
        Console.WriteLine("尝试重新处理音频文件...");
        
        // 尝试重新转换音频文件
        var backupPath = audioPath + ".fixed.wav";
        
        // 这里需要引用音频转换服务，但为了避免循环依赖，我们暂时简化处理
        // 在实际应用中，应该通过依赖注入来处理
        
        // 递归调用，但要防止无限递归
        if (!audioPath.Contains(".fixed.wav"))
        {
            // 这里应该调用AudioConversionService来修复文件
            // var conversionService = new AudioConversionService();
            // conversionService.ConvertWavToOptimalFormat(audioPath, backupPath);
            
            var backupSegments = await PerformAlignmentAsync(backupPath, config);
            
            // 清理备份文件
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            
            return backupSegments;
        }
        else
        {
            throw new InvalidOperationException($"无法处理音频文件格式，即使在重新转换后: {originalException.Message}", originalException);
        }
    }
}