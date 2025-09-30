using AudioAlignmentDemo.Core;
using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Configuration;

namespace AudioAlignmentDemo.Library;

/// <summary>
/// 音频分割器库接口
/// 提供简单易用的API供其他项目调用
/// </summary>
public class AudioSplitterLibrary
{
    private readonly AudioSplitter _audioSplitter;
    private readonly SplitterConfig _baseConfig;

    /// <summary>
    /// 使用默认平衡配置初始化
    /// </summary>
    public AudioSplitterLibrary() : this(ConfigurationManager.Presets.Balanced)
    {
    }

    /// <summary>
    /// 使用指定配置初始化
    /// </summary>
    /// <param name="config">基础配置，每次调用会创建副本以确保线程安全</param>
    public AudioSplitterLibrary(SplitterConfig config)
    {
        _audioSplitter = new AudioSplitter();
        _baseConfig = CloneConfig(config); // 创建基础配置的副本
    }

    /// <summary>
    /// 处理单个音频文件 (使用实例配置)
    /// </summary>
    /// <param name="inputPath">输入音频文件路径</param>
    /// <param name="outputDirectory">输出目录路径</param>
    /// <returns>处理结果</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(string inputPath, string outputDirectory = "output_segments")
    {
        // 创建配置副本，避免修改基础配置
        var config = CloneConfig(_baseConfig);
        config.InputAudioPath = inputPath;
        config.OutputDirectory = outputDirectory;

        return await ProcessAudioFileAsync(config);
    }

    /// <summary>
    /// 处理单个音频文件 (使用预设文本内容)
    /// 适用于已知准确文本内容的场景，避免语音识别错误导致的分割问题
    /// </summary>
    /// <param name="inputPath">输入音频文件路径</param>
    /// <param name="presetTextContent">预设的准确文本内容</param>
    /// <param name="outputDirectory">输出目录路径</param>
    /// <returns>处理结果</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(
        string inputPath, 
        string presetTextContent, 
        string outputDirectory = "output_segments")
    {
        // 创建配置副本并设置预设文本
        var config = CloneConfig(_baseConfig);
        config.InputAudioPath = inputPath;
        config.OutputDirectory = outputDirectory;
        config.PresetTextContent = presetTextContent;

        // 启用预设文本模式的日志
        Console.WriteLine($"🎯 使用预设文本内容: \"{presetTextContent.Substring(0, Math.Min(50, presetTextContent.Length))}...\"");
        Console.WriteLine($"📋 预设文本模式: {config.PresetTextMode}");

        return await ProcessAudioFileAsync(config);
    }

    /// <summary>
    /// 处理单个音频文件 (使用临时配置覆盖)
    /// </summary>
    /// <param name="inputPath">输入音频文件路径</param>
    /// <param name="presetTextContent">预设的准确文本内容</param>
    /// <param name="outputDirectory">输出目录路径</param>
    /// <param name="configOverrides">临时配置覆盖（会合并到基础配置中）</param>
    /// <returns>处理结果</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(
        string inputPath, 
        string presetTextContent, 
        string outputDirectory,
        Action<SplitterConfig> configOverrides)
    {
        // 创建配置副本
        var config = CloneConfig(_baseConfig);
        config.InputAudioPath = inputPath;
        config.OutputDirectory = outputDirectory;
        config.PresetTextContent = presetTextContent;

        // 应用临时配置覆盖
        configOverrides?.Invoke(config);

        Console.WriteLine($"🎯 使用预设文本内容: \"{presetTextContent.Substring(0, Math.Min(50, presetTextContent.Length))}...\"");
        Console.WriteLine($"📋 预设文本模式: {config.PresetTextMode}");
        Console.WriteLine($"⚙️ 应用了自定义配置覆盖");

        return await ProcessAudioFileAsync(config);
    }

    /// <summary>
    /// 处理单个音频文件 (使用完全自定义配置)
    /// 注意：此配置会完全替代实例的基础配置
    /// </summary>
    /// <param name="config">完全自定义的配置</param>
    /// <returns>处理结果</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(SplitterConfig config)
    {
        // 为完全自定义配置也创建副本，确保线程安全
        var configCopy = CloneConfig(config);
        
        var startTime = DateTime.Now;
        var result = new AudioSplitResult
        {
            InputFile = configCopy.InputAudioPath,
            OutputDirectory = configCopy.OutputDirectory,
            StartTime = startTime
        };

        try
        {
            await _audioSplitter.ProcessAsync(configCopy);

            result.Success = true;
            result.ProcessingTime = DateTime.Now - startTime;
            
            // 统计生成的文件
            if (Directory.Exists(configCopy.OutputDirectory))
            {
                var outputFiles = Directory.GetFiles(configCopy.OutputDirectory, "sentence_*.*")
                    .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                    .ToArray();
                
                result.GeneratedFiles = outputFiles.ToList();
                result.SegmentCount = outputFiles.Length;

                // 读取报告文件获取更多信息
                var reportPath = Path.Combine(configCopy.OutputDirectory, "sentence_split_report.json");
                if (File.Exists(reportPath))
                {
                    result.ReportPath = reportPath;
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Exception = ex;
            result.ProcessingTime = DateTime.Now - startTime;
        }

        return result;
    }

    /// <summary>
    /// 批量处理音频文件
    /// </summary>
    /// <param name="inputFiles">输入文件列表</param>
    /// <param name="baseOutputDirectory">基础输出目录</param>
    /// <param name="preset">使用的预设配置名称（如果不想使用实例配置）</param>
    /// <returns>批量处理结果</returns>
    public async Task<BatchSplitResult> ProcessAudioFilesAsync(
        IEnumerable<string> inputFiles, 
        string baseOutputDirectory = "output_batch",
        string? preset = null)
    {
        // 选择配置：如果指定了预设则使用预设，否则使用实例配置
        var config = preset != null 
            ? GetPresetConfig(preset) 
            : CloneConfig(_baseConfig);
            
        config.OutputDirectory = baseOutputDirectory;

        var batchProcessor = new Services.BatchProcessingService();
        var inputFilesList = inputFiles.ToList();
        var startTime = DateTime.Now;

        var result = new BatchSplitResult
        {
            InputFiles = inputFilesList,
            BaseOutputDirectory = baseOutputDirectory,
            StartTime = startTime
        };

        try
        {
            // 批量处理
            await batchProcessor.ProcessBatchAsync(inputFilesList, config);

            result.Success = true;
            result.ProcessingTime = DateTime.Now - startTime;

            // 收集所有生成的文件
            result.Results = new List<AudioSplitResult>();
            foreach (var inputFile in inputFilesList)
            {
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);
                var fileOutputDir = Path.Combine(baseOutputDirectory, SanitizeDirectoryName(fileNameWithoutExt));
                
                if (Directory.Exists(fileOutputDir))
                {
                    var outputFiles = Directory.GetFiles(fileOutputDir, "sentence_*.*")
                        .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                        .ToList();
                    
                    result.Results.Add(new AudioSplitResult
                    {
                        InputFile = inputFile,
                        OutputDirectory = fileOutputDir,
                        Success = true,
                        GeneratedFiles = outputFiles,
                        SegmentCount = outputFiles.Count
                    });
                }
            }

            result.TotalSegments = result.Results.Sum(r => r.SegmentCount);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Exception = ex;
            result.ProcessingTime = DateTime.Now - startTime;
        }

        return result;
    }

    /// <summary>
    /// 获取当前实例使用的基础配置的只读副本
    /// </summary>
    /// <returns>配置副本</returns>
    public SplitterConfig GetCurrentConfig()
    {
        return CloneConfig(_baseConfig);
    }

    /// <summary>
    /// 更新实例的基础配置
    /// </summary>
    /// <param name="configUpdates">配置更新操作</param>
    public void UpdateBaseConfig(Action<SplitterConfig> configUpdates)
    {
        configUpdates?.Invoke(_baseConfig);
    }

    /// <summary>
    /// 重置为默认配置
    /// </summary>
    public void ResetToDefaultConfig()
    {
        var defaultConfig = ConfigurationManager.Presets.Balanced;
        _baseConfig.GetType().GetProperties().ToList().ForEach(prop =>
        {
            if (prop.CanWrite)
            {
                prop.SetValue(_baseConfig, prop.GetValue(defaultConfig));
            }
        });
    }

    /// <summary>
    /// 获取预设配置
    /// </summary>
    /// <param name="presetName">预设名称</param>
    /// <returns>配置对象</returns>
    public static SplitterConfig GetPresetConfig(string presetName)
    {
        var presets = ConfigurationManager.Presets.GetAllPresets();
        return presets.TryGetValue(presetName.ToLower(), out var preset) 
            ? preset 
            : ConfigurationManager.Presets.Balanced;
    }

    /// <summary>
    /// 获取所有可用的预设名称
    /// </summary>
    /// <returns>预设名称列表</returns>
    public static List<string> GetAvailablePresets()
    {
        return ConfigurationManager.Presets.GetAllPresets().Keys.ToList();
    }

    /// <summary>
    /// 验证音频文件是否支持
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持</returns>
    public static bool IsAudioFileSupported(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var supportedExtensions = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
        
        return supportedExtensions.Contains(extension);
    }

    /// <summary>
    /// 从目录中查找所有支持的音频文件
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="includeSubdirectories">是否包含子目录</param>
    /// <returns>音频文件列表</returns>
    public static List<string> FindAudioFiles(string directoryPath, bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directoryPath))
            return new List<string>();

        var supportedExtensions = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
    }

    private string SanitizeDirectoryName(string name)
    {
        var invalidChars = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).ToArray();
        var sanitized = name;
        
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized.Trim('_', ' ', '.');
    }

    /// <summary>
    /// 深拷贝配置对象 - 确保线程安全和配置隔离
    /// </summary>
    /// <param name="source">源配置</param>
    /// <returns>配置副本</returns>
    private static SplitterConfig CloneConfig(SplitterConfig source)
    {
        return new SplitterConfig
        {
            InputAudioPath = source.InputAudioPath,
            OutputDirectory = source.OutputDirectory,
            Language = source.Language,
            ModelSize = source.ModelSize,
            AudioQualityStrategy = source.AudioQualityStrategy,
            AudioConversionQuality = source.AudioConversionQuality,
            ForceSampleRate = source.ForceSampleRate,
            ForceBitDepth = source.ForceBitDepth,
            ForceChannels = source.ForceChannels,
            EnableFFmpegFallback = source.EnableFFmpegFallback,
            KeepOriginalAudio = source.KeepOriginalAudio,
            MaxSegmentDuration = source.MaxSegmentDuration,
            MinSegmentDuration = source.MinSegmentDuration,
            SentenceBoundaryPadding = source.SentenceBoundaryPadding,
            TimeAllocationMode = source.TimeAllocationMode,
            MinSentenceCharacters = source.MinSentenceCharacters,
            SilencePaddingAfterPunctuation = source.SilencePaddingAfterPunctuation,
            EnableSmartBoundaryAdjustment = source.EnableSmartBoundaryAdjustment,
            DebugMode = source.DebugMode,
            EnableTimeCorrection = source.EnableTimeCorrection,
            TimeCorrectionThreshold = source.TimeCorrectionThreshold,
            MaxExtensionTime = source.MaxExtensionTime,
            WhisperMinSegmentLength = source.WhisperMinSegmentLength,
            WordBoundaryMode = source.WordBoundaryMode,
            InterjectionPadding = source.InterjectionPadding,
            ShortSentenceMode = source.ShortSentenceMode,
            EnableRepeatedWordDetection = source.EnableRepeatedWordDetection,
            IntonationBuffer = source.IntonationBuffer,
            DynamicTimeAdjustmentFactor = source.DynamicTimeAdjustmentFactor,
            SplitStrategy = source.SplitStrategy,
            EnableSplitPrecheck = source.EnableSplitPrecheck,
            SkipSplitThreshold = source.SkipSplitThreshold,
            PresetTextContent = source.PresetTextContent,
            PresetTextMode = source.PresetTextMode
        };
    }
}

/// <summary>
/// 音频分割结果
/// </summary>
public class AudioSplitResult
{
    public string InputFile { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Exception? Exception { get; set; }
    public List<string> GeneratedFiles { get; set; } = new();
    public int SegmentCount { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime StartTime { get; set; }
    public string? ReportPath { get; set; }
}

/// <summary>
/// 批量分割结果
/// </summary>
public class BatchSplitResult
{
    public List<string> InputFiles { get; set; } = new();
    public string BaseOutputDirectory { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Exception? Exception { get; set; }
    public List<AudioSplitResult> Results { get; set; } = new();
    public int TotalSegments { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime StartTime { get; set; }

    public int SuccessfulFiles => Results.Count(r => r.Success);
    public int FailedFiles => Results.Count(r => !r.Success);
}