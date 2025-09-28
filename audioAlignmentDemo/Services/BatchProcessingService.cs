using AudioAlignmentDemo.Core;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// ������Ƶ�������
/// ֧�ִ�������Ƶ�ļ��Ͳ��д���
/// </summary>
public class BatchProcessingService
{
    private readonly AudioSplitter _audioSplitter;

    public BatchProcessingService()
    {
        _audioSplitter = new AudioSplitter();
    }

    /// <summary>
    /// ����������Ƶ�ļ�
    /// </summary>
    public async Task ProcessBatchAsync(List<string> inputFiles, SplitterConfig baseConfig, bool parallel = false)
    {
        if (inputFiles.Count == 0)
        {
            Console.WriteLine("?? û���ҵ�Ҫ�������Ƶ�ļ�");
            return;
        }

        Console.WriteLine($"?? ��ʼ�������� {inputFiles.Count} ����Ƶ�ļ�...");
        Console.WriteLine($"?? ����ģʽ: {(parallel ? "���д���" : "˳����")}");
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
            Console.WriteLine($"?? �����ļ� {i + 1}/{inputFiles.Count}: {Path.GetFileName(inputFile)}");
            
            var result = await ProcessSingleFileAsync(inputFile, baseConfig);
            results.Add(result);

            Console.WriteLine();
            Console.WriteLine("��".PadRight(80, '��'));
            Console.WriteLine();
        }
    }

    private async Task ProcessFilesInParallelAsync(List<string> inputFiles, SplitterConfig baseConfig, List<BatchProcessResult> results)
    {
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount); // ���Ʋ�����
        var tasks = inputFiles.Select(async (inputFile, index) =>
        {
            await semaphore.WaitAsync();
            try
            {
                Console.WriteLine($"?? ��ʼ����: {Path.GetFileName(inputFile)}");
                var result = await ProcessSingleFileAsync(inputFile, baseConfig);
                lock (results)
                {
                    results.Add(result);
                }
                Console.WriteLine($"? ��ɴ���: {Path.GetFileName(inputFile)}");
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
            // ������Ը��ļ�������
            var config = CreateFileSpecificConfig(baseConfig, inputFile);

            // ������Ƶ�ļ�
            await _audioSplitter.ProcessAsync(config);

            result.Success = true;
            result.OutputDirectory = config.OutputDirectory;
            result.ProcessingTime = DateTime.Now - startTime;

            // ͳ�����ɵ��ļ�
            if (Directory.Exists(config.OutputDirectory))
            {
                var outputFiles = Directory.GetFiles(config.OutputDirectory, "sentence_*.*")
                    .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                    .ToArray();
                result.GeneratedFiles = outputFiles.Length;
            }

            Console.WriteLine($"? {result.FileName} ����ɹ������� {result.GeneratedFiles} ����ƵƬ��");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.ProcessingTime = DateTime.Now - startTime;
            
            Console.WriteLine($"? {result.FileName} ����ʧ��: {ex.Message}");
        }

        return result;
    }

    private SplitterConfig CreateFileSpecificConfig(SplitterConfig baseConfig, string inputFile)
    {
        // �������õ����
        var json = System.Text.Json.JsonSerializer.Serialize(baseConfig);
        var config = System.Text.Json.JsonSerializer.Deserialize<SplitterConfig>(json)!;

        // �����ļ��ض���·��
        config.InputAudioPath = inputFile;
        
        // Ϊÿ���ļ��������������Ŀ¼
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);
        var sanitizedFileName = SanitizeDirectoryName(fileNameWithoutExt);
        config.OutputDirectory = Path.Combine(baseConfig.OutputDirectory, sanitizedFileName);

        return config;
    }

    private string SanitizeDirectoryName(string name)
    {
        // �Ƴ����滻����ȫ��Ŀ¼���ַ�
        var invalidChars = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).ToArray();
        var sanitized = name;
        
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        // ���Ƴ���
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized.Trim('_', ' ', '.');
    }

    private void GenerateBatchReport(List<BatchProcessResult> results, TimeSpan totalTime)
    {
        Console.WriteLine("?? ����������ɱ���");
        Console.WriteLine("==================");
        Console.WriteLine();

        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        var totalFiles = successful + failed;
        var totalSegments = results.Where(r => r.Success).Sum(r => r.GeneratedFiles);

        Console.WriteLine($"?? ����ͳ��:");
        Console.WriteLine($"   �����ļ�����: {totalFiles}");
        Console.WriteLine($"   �ɹ�����: {successful} ��");
        Console.WriteLine($"   ����ʧ��: {failed} ��");
        Console.WriteLine($"   �ɹ���: {(double)successful / totalFiles * 100:F1}%");
        Console.WriteLine($"   ������ƵƬ��: {totalSegments} ��");
        Console.WriteLine($"   �ܴ���ʱ��: {FormatTimeSpan(totalTime)}");
        Console.WriteLine($"   ƽ��ÿ�ļ�ʱ��: {FormatTimeSpan(TimeSpan.FromSeconds(totalTime.TotalSeconds / totalFiles))}");
        Console.WriteLine();

        if (successful > 0)
        {
            Console.WriteLine("? ����ɹ����ļ�:");
            foreach (var result in results.Where(r => r.Success).OrderBy(r => r.FileName))
            {
                Console.WriteLine($"   ?? {result.FileName} �� {result.GeneratedFiles} ��Ƭ�� ({FormatTimeSpan(result.ProcessingTime)})");
            }
            Console.WriteLine();
        }

        if (failed > 0)
        {
            Console.WriteLine("? ����ʧ�ܵ��ļ�:");
            foreach (var result in results.Where(r => !r.Success).OrderBy(r => r.FileName))
            {
                Console.WriteLine($"   ?? {result.FileName}: {result.Error}");
            }
            Console.WriteLine();
        }

        // ������ϸ�����������ļ�
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
            Console.WriteLine($"?? ��ϸ��������������: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? �޷�������������: {ex.Message}");
        }
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 1)
        {
            return $"{timeSpan.TotalSeconds:F1}��";
        }
        else if (timeSpan.TotalHours < 1)
        {
            return $"{timeSpan.Minutes}��{timeSpan.Seconds}��";
        }
        else
        {
            return $"{(int)timeSpan.TotalHours}ʱ{timeSpan.Minutes}��{timeSpan.Seconds}��";
        }
    }
}

/// <summary>
/// ����������
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