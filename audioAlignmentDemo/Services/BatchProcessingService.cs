using AudioAlignmentDemo.Core;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 批量音频处理服务
/// 支持处理多个音频文件和并行处理
/// </summary>
public class BatchProcessingService
{
    private readonly AudioSplitter _audioSplitter;

    public BatchProcessingService()
    {
        _audioSplitter = new AudioSplitter();
    }

    /// <summary>
    /// 批量处理音频文件
    /// </summary>
    public async Task ProcessBatchAsync(List<string> inputFiles, SplitterConfig baseConfig, bool parallel = false)
    {
        if (inputFiles.Count == 0)
        {
            Console.WriteLine("?? 没有找到要处理的音频文件");
            return;
        }

        Console.WriteLine($"?? 开始批量处理 {inputFiles.Count} 个音频文件...");
        Console.WriteLine($"?? 处理模式: {(parallel ? "并行处理" : "顺序处理")}");
        Console.WriteLine();

        var totalStartTime = DateTime.Now;
        var results = new List<BatchProcessResult>();

        if (parallel)
        {
            await ProcessFilesInParallelAsync(inputFiles, baseConfig, results);
        }
        else
        {
            await ProcessFilesSequentiallyAsync(inputFiles, baseConfig, results);
        }

        var totalTime = DateTime.Now - totalStartTime;
        GenerateBatchReport(results, totalTime);
    }

    private async Task ProcessFilesSequentiallyAsync(List<string> inputFiles, SplitterConfig baseConfig, List<BatchProcessResult> results)
    {
        for (int i = 0; i < inputFiles.Count; i++)
        {
            var inputFile = inputFiles[i];
            Console.WriteLine($"?? 处理文件 {i + 1}/{inputFiles.Count}: {Path.GetFileName(inputFile)}");
            
            var result = await ProcessSingleFileAsync(inputFile, baseConfig);
            results.Add(result);

            Console.WriteLine();
            Console.WriteLine("─".PadRight(80, '─'));
            Console.WriteLine();
        }
    }

    private async Task ProcessFilesInParallelAsync(List<string> inputFiles, SplitterConfig baseConfig, List<BatchProcessResult> results)
    {
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount); // 限制并发数
        var tasks = inputFiles.Select(async (inputFile, index) =>
        {
            await semaphore.WaitAsync();
            try
            {
                Console.WriteLine($"?? 开始处理: {Path.GetFileName(inputFile)}");
                var result = await ProcessSingleFileAsync(inputFile, baseConfig);
                lock (results)
                {
                    results.Add(result);
                }
                Console.WriteLine($"? 完成处理: {Path.GetFileName(inputFile)}");
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<BatchProcessResult> ProcessSingleFileAsync(string inputFile, SplitterConfig baseConfig)
    {
        var startTime = DateTime.Now;
        var result = new BatchProcessResult
        {
            InputFile = inputFile,
            FileName = Path.GetFileName(inputFile),
            StartTime = startTime
        };

        try
        {
            // 创建针对该文件的配置
            var config = CreateFileSpecificConfig(baseConfig, inputFile);

            // 处理音频文件
            await _audioSplitter.ProcessAsync(config);

            result.Success = true;
            result.OutputDirectory = config.OutputDirectory;
            result.ProcessingTime = DateTime.Now - startTime;

            // 统计生成的文件
            if (Directory.Exists(config.OutputDirectory))
            {
                var outputFiles = Directory.GetFiles(config.OutputDirectory, "sentence_*.*")
                    .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                    .ToArray();
                result.GeneratedFiles = outputFiles.Length;
            }

            Console.WriteLine($"? {result.FileName} 处理成功，生成 {result.GeneratedFiles} 个音频片段");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.ProcessingTime = DateTime.Now - startTime;
            
            Console.WriteLine($"? {result.FileName} 处理失败: {ex.Message}");
        }

        return result;
    }

    private SplitterConfig CreateFileSpecificConfig(SplitterConfig baseConfig, string inputFile)
    {
        // 创建配置的深拷贝
        var json = System.Text.Json.JsonSerializer.Serialize(baseConfig);
        var config = System.Text.Json.JsonSerializer.Deserialize<SplitterConfig>(json)!;

        // 设置文件特定的路径
        config.InputAudioPath = inputFile;
        
        // 为每个文件创建独立的输出目录
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);
        var sanitizedFileName = SanitizeDirectoryName(fileNameWithoutExt);
        config.OutputDirectory = Path.Combine(baseConfig.OutputDirectory, sanitizedFileName);

        return config;
    }

    private string SanitizeDirectoryName(string name)
    {
        // 移除或替换不安全的目录名字符
        var invalidChars = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).ToArray();
        var sanitized = name;
        
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        // 限制长度
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized.Trim('_', ' ', '.');
    }

    private void GenerateBatchReport(List<BatchProcessResult> results, TimeSpan totalTime)
    {
        Console.WriteLine("?? 批量处理完成报告");
        Console.WriteLine("==================");
        Console.WriteLine();

        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        var totalFiles = successful + failed;
        var totalSegments = results.Where(r => r.Success).Sum(r => r.GeneratedFiles);

        Console.WriteLine($"?? 总体统计:");
        Console.WriteLine($"   处理文件总数: {totalFiles}");
        Console.WriteLine($"   成功处理: {successful} 个");
        Console.WriteLine($"   处理失败: {failed} 个");
        Console.WriteLine($"   成功率: {(double)successful / totalFiles * 100:F1}%");
        Console.WriteLine($"   生成音频片段: {totalSegments} 个");
        Console.WriteLine($"   总处理时间: {FormatTimeSpan(totalTime)}");
        Console.WriteLine($"   平均每文件时间: {FormatTimeSpan(TimeSpan.FromSeconds(totalTime.TotalSeconds / totalFiles))}");
        Console.WriteLine();

        if (successful > 0)
        {
            Console.WriteLine("? 处理成功的文件:");
            foreach (var result in results.Where(r => r.Success).OrderBy(r => r.FileName))
            {
                Console.WriteLine($"   ?? {result.FileName} → {result.GeneratedFiles} 个片段 ({FormatTimeSpan(result.ProcessingTime)})");
            }
            Console.WriteLine();
        }

        if (failed > 0)
        {
            Console.WriteLine("? 处理失败的文件:");
            foreach (var result in results.Where(r => !r.Success).OrderBy(r => r.FileName))
            {
                Console.WriteLine($"   ?? {result.FileName}: {result.Error}");
            }
            Console.WriteLine();
        }

        // 生成详细的批量报告文件
        GenerateDetailedBatchReport(results, totalTime);
    }

    private void GenerateDetailedBatchReport(List<BatchProcessResult> results, TimeSpan totalTime)
    {
        var reportPath = Path.Combine(results.First().OutputDirectory ?? ".", "..", "batch_report.json");
        
        var report = new
        {
            ProcessedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            TotalProcessingTime = totalTime.ToString(),
            Summary = new
            {
                TotalFiles = results.Count,
                SuccessfulFiles = results.Count(r => r.Success),
                FailedFiles = results.Count(r => !r.Success),
                TotalSegmentsGenerated = results.Where(r => r.Success).Sum(r => r.GeneratedFiles)
            },
            Results = results.Select(r => new
            {
                r.FileName,
                r.InputFile,
                r.Success,
                r.GeneratedFiles,
                ProcessingTimeSeconds = r.ProcessingTime.TotalSeconds,
                r.Error,
                r.OutputDirectory
            }).ToArray()
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            File.WriteAllText(reportPath, json);
            Console.WriteLine($"?? 详细批量报告已生成: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? 无法生成批量报告: {ex.Message}");
        }
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 1)
        {
            return $"{timeSpan.TotalSeconds:F1}秒";
        }
        else if (timeSpan.TotalHours < 1)
        {
            return $"{timeSpan.Minutes}分{timeSpan.Seconds}秒";
        }
        else
        {
            return $"{(int)timeSpan.TotalHours}时{timeSpan.Minutes}分{timeSpan.Seconds}秒";
        }
    }
}

/// <summary>
/// 批量处理结果
/// </summary>
public class BatchProcessResult
{
    public string InputFile { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int GeneratedFiles { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime StartTime { get; set; }
    public string? OutputDirectory { get; set; }
}