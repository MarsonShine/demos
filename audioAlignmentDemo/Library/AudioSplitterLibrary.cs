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

    public AudioSplitterLibrary()
    {
        _audioSplitter = new AudioSplitter();
    }

    /// <summary>
    /// 处理单个音频文件 (使用默认配置)
    /// </summary>
    /// <param name="inputPath">输入音频文件路径</param>
    /// <param name="outputDirectory">输出目录路径</param>
    /// <returns>处理结果</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(string inputPath, string outputDirectory = "output_segments")
    {
        var config = ConfigurationManager.Presets.Balanced;
        config.InputAudioPath = inputPath;
        config.OutputDirectory = outputDirectory;

        return await ProcessAudioFileAsync(config);
    }

    /// <summary>
    /// 处理单个音频文件 (使用自定义配置)
    /// </summary>
    /// <param name="config">分割配置</param>
    /// <returns>处理结果</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(SplitterConfig config)
    {
        var startTime = DateTime.Now;
        var result = new AudioSplitResult
        {
            InputFile = config.InputAudioPath,
            OutputDirectory = config.OutputDirectory,
            StartTime = startTime
        };

        try
        {
            await _audioSplitter.ProcessAsync(config);

            result.Success = true;
            result.ProcessingTime = DateTime.Now - startTime;
            
            // 统计生成的文件
            if (Directory.Exists(config.OutputDirectory))
            {
                var outputFiles = Directory.GetFiles(config.OutputDirectory, "sentence_*.*")
                    .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                    .ToArray();
                
                result.GeneratedFiles = outputFiles.ToList();
                result.SegmentCount = outputFiles.Length;

                // 读取报告文件获取更多信息
                var reportPath = Path.Combine(config.OutputDirectory, "sentence_split_report.json");
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
    /// <param name="preset">使用的预设配置</param>
    /// <returns>批量处理结果</returns>
    public async Task<BatchSplitResult> ProcessAudioFilesAsync(
        IEnumerable<string> inputFiles, 
        string baseOutputDirectory = "output_batch",
        string preset = "balanced")
    {
        var config = GetPresetConfig(preset);
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
            // 这里我们需要修改BatchProcessingService来返回结果
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