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
    /// 执行音频分割处理
    /// </summary>
    public async Task ProcessAsync(SplitterConfig config)
    {
        var startTime = DateTime.Now;
        
        try
        {
            Console.WriteLine("?? 开始音频分割处理流程...\n");
            
            // 1. 验证输入文件
            ValidateInputFile(config);

            // 2. 准备输出目录
            PrepareOutputDirectory(config);

            // 3. 音频格式转换 (仅用于Whisper识别)
            Console.WriteLine("?? 步骤 1/6: 音频格式转换");
            string processedAudio = await _conversionService.ConvertToWhisperFormatAsync(
                config.InputAudioPath, config.OutputDirectory);

            // 4. 语音识别和时间对齐
            Console.WriteLine("\n?? 步骤 2/6: 语音识别和时间对齐");
            var segments = await _recognitionService.PerformAlignmentAsync(processedAudio, config);

            // 5. 句子分析和分割点优化
            Console.WriteLine("\n?? 步骤 3/6: 句子分析和分割点优化");
            var optimizedSegments = _analysisService.OptimizeSegments(segments, config);

            // 6. 音频文件切割 (使用原始音频文件保持音质)
            Console.WriteLine("\n?? 步骤 4/6: 音频文件切割");
            await _splittingService.SplitAudioFilesAsync(config.InputAudioPath, optimizedSegments, config);

            // 7. 生成结果报告
            Console.WriteLine("\n?? 步骤 5/6: 生成结果报告");
            _reportService.GenerateReport(optimizedSegments, config);
            
            // 8. 生成性能报告
            Console.WriteLine("\n?? 步骤 6/6: 性能分析");
            var processingTime = DateTime.Now - startTime;
            _reportService.GeneratePerformanceReport(optimizedSegments, processingTime, config.OutputDirectory);

            // 9. 清理临时文件
            CleanupTemporaryFiles(processedAudio, config);
            
            Console.WriteLine($"\n?? 处理完成！总耗时: {(DateTime.Now - startTime).TotalSeconds:F1} 秒");
            Console.WriteLine($"?? 请查看 '{config.OutputDirectory}' 目录中的结果文件");
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            Console.WriteLine($"\n? 处理失败 (耗时: {processingTime.TotalSeconds:F1} 秒)");
            throw new AudioSplitterException($"音频分割处理失败: {ex.Message}", ex);
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