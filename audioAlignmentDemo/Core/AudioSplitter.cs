using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Services;

namespace AudioAlignmentDemo.Core;

/// <summary>
/// 音频分割器主类
/// 协调各个服务完成音频分割任务
/// </summary>
public class AudioSplitter
{
    private readonly AudioConversionService _conversionService;
    private readonly SpeechRecognitionService _recognitionService;
    private readonly SentenceAnalysisService _analysisService;
    private readonly AudioSplittingService _splittingService;
    private readonly ReportGenerationService _reportService;

    public AudioSplitter()
    {
        _conversionService = new AudioConversionService();
        _recognitionService = new SpeechRecognitionService();
        _analysisService = new SentenceAnalysisService();
        _splittingService = new AudioSplittingService();
        _reportService = new ReportGenerationService();
    }

    /// <summary>
    /// 执行音频分割流程
    /// </summary>
    public async Task ProcessAsync(SplitterConfig config)
    {
        var startTime = DateTime.Now;
        
        try
        {
            Console.WriteLine("🎵 开始音频分割流程...\n");
            
            // 1. 验证输入文件
            ValidateInputFile(config);

            // 2. 准备输出目录
            PrepareOutputDirectory(config);

            // 3. 音频格式转换 (准备给Whisper识别)
            Console.WriteLine("🔄 步骤 1/6: 音频格式转换");
            string processedAudio = await _conversionService.ConvertToWhisperFormatAsync(
                config.InputAudioPath, config.OutputDirectory);

            // 4. 语音识别与时间对齐
            Console.WriteLine("\n🎤 步骤 2/6: 语音识别与时间对齐");
            var segments = await _recognitionService.PerformAlignmentAsync(processedAudio, config);

            // 🆕 5. 早期分割条件检查 - 避免不必要的处理
            Console.WriteLine("\n🔍 步骤 3/6: 分割条件预检查");
            bool shouldProceedWithSplitting = await PreCheckSplittingConditions(segments, config);
            
            if (!shouldProceedWithSplitting)
            {
                Console.WriteLine("⚠ 检测到内容不需要分割，跳过后续处理步骤");
                Console.WriteLine("💾 保存单个完整音频文件...");
                
                // 复制原始音频到输出目录
                await SaveSingleAudioFile(config);
                
                // 生成简化报告
                GenerateSkippedProcessingReport(segments, DateTime.Now - startTime, config);
                
                // 清理临时文件
                CleanupTemporaryFiles(processedAudio, config);
                
                Console.WriteLine($"\n✅ 处理完成，总耗时: {(DateTime.Now - startTime).TotalSeconds:F1} 秒");
                Console.WriteLine($"📂 查看 '{config.OutputDirectory}' 目录中的结果文件");
                return;
            }

            // 6. 句子分析和分割优化
            Console.WriteLine("\n📝 步骤 4/6: 句子分析和分割优化");
            var optimizedSegments = _analysisService.OptimizeSegments(segments, config);

            // 7. 音频文件切割 (使用原始音频文件进行切割)
            Console.WriteLine("\n✂️ 步骤 5/6: 音频文件切割");
            await _splittingService.SplitAudioFilesAsync(config.InputAudioPath, optimizedSegments, config);

            // 8. 生成处理报告
            Console.WriteLine("\n📊 步骤 6/6: 生成处理报告");
            _reportService.GenerateReport(optimizedSegments, config);
            
            // 9. 生成性能分析报告
            var processingTime = DateTime.Now - startTime;
            _reportService.GeneratePerformanceReport(optimizedSegments, processingTime, config.OutputDirectory);

            // 10. 清理临时文件
            CleanupTemporaryFiles(processedAudio, config);
            
            Console.WriteLine($"\n🎉 处理完成，总耗时: {(DateTime.Now - startTime).TotalSeconds:F1} 秒");
            Console.WriteLine($"📂 查看 '{config.OutputDirectory}' 目录中的结果文件");
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            Console.WriteLine($"\n❌ 处理失败 (耗时: {processingTime.TotalSeconds:F1} 秒)");
            throw new AudioSplitterException($"音频分割失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 预检查分割条件，避免不必要的处理
    /// </summary>
    private async Task<bool> PreCheckSplittingConditions(List<AudioSegment> segments, SplitterConfig config)
    {
        if (!config.EnableSplitPrecheck)
        {
            Console.WriteLine("📋 分割预检查已禁用，继续完整处理流程");
            return true;
        }

        // 检查识别段数
        if (segments.Count < config.SkipSplitThreshold)
        {
            Console.WriteLine($"📊 识别结果: {segments.Count} 个段落 (阈值: {config.SkipSplitThreshold})");
            
            // 进一步检查文本内容是否需要分割
            var strategyManager = new SplitStrategyManager();
            bool needsSplitting = false;
            
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment.Text))
                    continue;
                    
                var strategy = config.SplitStrategy.ToLower() == "auto" 
                    ? strategyManager.SelectBestStrategy(segment.Text, config)
                    : strategyManager.GetStrategy(config.SplitStrategy);
                    
                if (strategy.ShouldSplit(segment.Text, config))
                {
                    Console.WriteLine($"✓ 段落需要分割: \"{segment.Text.Substring(0, Math.Min(50, segment.Text.Length))}...\"");
                    needsSplitting = true;
                    break;
                }
            }
            
            if (!needsSplitting)
            {
                Console.WriteLine("🚫 所有段落都不满足分割条件");
                return false;
            }
        }
        
        Console.WriteLine("✅ 检测到需要分割的内容，继续处理");
        return true;
    }

    /// <summary>
    /// 保存单个音频文件（当不需要分割时）
    /// </summary>
    private async Task SaveSingleAudioFile(SplitterConfig config)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(config.InputAudioPath);
            var extension = Path.GetExtension(config.InputAudioPath);
            var outputPath = Path.Combine(config.OutputDirectory, $"complete_audio{extension}");
            
            File.Copy(config.InputAudioPath, outputPath, true);
            Console.WriteLine($"📁 已保存完整音频: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 保存完整音频文件时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成跳过处理的简化报告
    /// </summary>
    private void GenerateSkippedProcessingReport(List<AudioSegment> segments, TimeSpan processingTime, SplitterConfig config)
    {
        try
        {
            var reportPath = Path.Combine(config.OutputDirectory, "processing_report.json");
            var report = new
            {
                Status = "Skipped",
                Reason = "Content does not require splitting",
                ProcessingTime = processingTime.TotalSeconds,
                OriginalSegments = segments.Count,
                SplitStrategy = config.SplitStrategy,
                InputFile = config.InputAudioPath,
                OutputDirectory = config.OutputDirectory,
                Timestamp = DateTime.Now
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            File.WriteAllText(reportPath, json);
            Console.WriteLine($"📋 已生成处理报告: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 生成报告时出错: {ex.Message}");
        }
    }

    private void ValidateInputFile(SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.InputAudioPath))
        {
            throw new ArgumentException("输入音频文件路径不能为空", nameof(config.InputAudioPath));
        }

        if (!File.Exists(config.InputAudioPath))
        {
            throw new FileNotFoundException($"音频文件不存在: {config.InputAudioPath}");
        }

        var fileInfo = new FileInfo(config.InputAudioPath);
        if (fileInfo.Length == 0)
        {
            throw new ArgumentException("音频文件为空", nameof(config.InputAudioPath));
        }

        Console.WriteLine($"? 输入文件验证通过: {config.InputAudioPath} ({fileInfo.Length / 1024:F1} KB)");
    }

    private void PrepareOutputDirectory(SplitterConfig config)
    {
        try
        {
            if (Directory.Exists(config.OutputDirectory))
            {
                Console.WriteLine($"?? 输出目录已存在: {config.OutputDirectory}");
            }
            else
            {
                Directory.CreateDirectory(config.OutputDirectory);
                Console.WriteLine($"?? 创建输出目录: {config.OutputDirectory}");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法创建输出目录 '{config.OutputDirectory}': {ex.Message}", ex);
        }
    }

    private void CleanupTemporaryFiles(string processedAudio, SplitterConfig config)
    {
        try
        {
            if (File.Exists(processedAudio))
            {
                if (config.KeepOriginalAudio)
                {
                    Console.WriteLine($"?? 保留临时转换文件: {processedAudio}");
                }
                else
                {
                    File.Delete(processedAudio);
                    Console.WriteLine($"??? 清理临时文件: {processedAudio}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? 清理临时文件时出现问题: {ex.Message}");
            // 不抛出异常，因为这不影响主要功能
        }
    }
}

/// <summary>
/// 音频分割器专用异常类
/// </summary>
public class AudioSplitterException : Exception
{
    public AudioSplitterException(string message) : base(message) { }
    public AudioSplitterException(string message, Exception innerException) : base(message, innerException) { }
}