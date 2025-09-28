using System.Text.Json;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 报告生成服务
/// 负责生成处理结果的报告和统计信息
/// </summary>
public class ReportGenerationService
{
    /// <summary>
    /// 生成处理结果报告
    /// </summary>
    public void GenerateReport(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("?? 生成结果报告...");

        var report = CreateReportData(segments, config);
        
        // 生成JSON报告
        var jsonPath = GenerateJsonReport(report, config.OutputDirectory);
        
        // 生成文本清单
        var textListPath = GenerateTextList(segments, config.OutputDirectory);
        
        // 显示报告信息
        DisplayReportInfo(jsonPath, textListPath, segments);
        
        // 显示统计信息
        DisplayStatistics(segments);
    }

    private object CreateReportData(List<AudioSegment> segments, SplitterConfig config)
    {
        return new
        {
            ProcessingInfo = new
            {
                ProcessedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                InputFile = config.InputAudioPath,
                OutputDirectory = config.OutputDirectory,
                ModelSize = config.ModelSize,
                Language = config.Language
            },
            QualitySettings = new
            {
                AudioQualityStrategy = config.AudioQualityStrategy,
                AudioConversionQuality = config.AudioConversionQuality,
                TimeCorrection = config.EnableTimeCorrection,
                TimeCorrectionThreshold = config.TimeCorrectionThreshold
            },
            SegmentationSettings = new
            {
                SentenceBoundaryPadding = config.SentenceBoundaryPadding,
                MinSentenceCharacters = config.MinSentenceCharacters,
                SilencePaddingAfterPunctuation = config.SilencePaddingAfterPunctuation,
                TimeAllocationMode = config.TimeAllocationMode,
                WordBoundaryMode = config.WordBoundaryMode
            },
            Results = new
            {
                TotalSegments = segments.Count,
                TotalDuration = Math.Round(segments.Sum(s => s.Duration), 2),
                AverageDuration = Math.Round(segments.Average(s => s.Duration), 2),
                MinDuration = Math.Round(segments.Min(s => s.Duration), 2),
                MaxDuration = Math.Round(segments.Max(s => s.Duration), 2)
            },
            Segments = segments.Select((s, i) => new
            {
                Index = i + 1,
                StartTime = Math.Round(s.StartTime, 3),
                EndTime = Math.Round(s.EndTime, 3),
                Duration = Math.Round(s.Duration, 3),
                Text = s.Text,
                OutputFileName = s.OutputFileName,
                CharacterCount = s.Text.Length,
                WordCount = CountWords(s.Text)
            }).ToArray()
        };
    }

    private string GenerateJsonReport(object report, string outputDirectory)
    {
        var jsonPath = Path.Combine(outputDirectory, "sentence_split_report.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        
        var jsonContent = JsonSerializer.Serialize(report, jsonOptions);
        File.WriteAllText(jsonPath, jsonContent);
        
        return jsonPath;
    }

    private string GenerateTextList(List<AudioSegment> segments, string outputDirectory)
    {
        var textListPath = Path.Combine(outputDirectory, "sentence_list.txt");
        var textLines = segments.Select((s, i) => FormatSegmentLine(s, i + 1));
        File.WriteAllLines(textListPath, textLines);
        
        return textListPath;
    }

    private string FormatSegmentLine(AudioSegment segment, int index)
    {
        return $"{index:D2}. [{segment.StartTime:F2}s-{segment.EndTime:F2}s] ({segment.Duration:F2}s) {segment.OutputFileName}\n" +
               $"    ?? \"{segment.Text}\"\n" +
               $"    ?? 字符数: {segment.Text.Length}, 词数: {CountWords(segment.Text)}";
    }

    private void DisplayReportInfo(string jsonPath, string textListPath, List<AudioSegment> segments)
    {
        Console.WriteLine($"?? 报告已生成:");
        Console.WriteLine($"   ?? JSON报告: {jsonPath}");
        Console.WriteLine($"   ?? 句子清单: {textListPath}");
        Console.WriteLine($"   ?? 句子音频: {segments.Count} 个文件");
    }

    private void DisplayStatistics(List<AudioSegment> segments)
    {
        Console.WriteLine($"\n?? 处理统计:");
        Console.WriteLine($"   总句子数: {segments.Count}");
        Console.WriteLine($"   总时长: {segments.Sum(s => s.Duration):F2} 秒");
        Console.WriteLine($"   平均句长: {segments.Average(s => s.Duration):F2} 秒");
        Console.WriteLine($"   最短句子: {segments.Min(s => s.Duration):F2} 秒");
        Console.WriteLine($"   最长句子: {segments.Max(s => s.Duration):F2} 秒");
        
        // 额外统计信息
        DisplayDetailedStatistics(segments);
    }

    private void DisplayDetailedStatistics(List<AudioSegment> segments)
    {
        var totalCharacters = segments.Sum(s => s.Text.Length);
        var totalWords = segments.Sum(s => CountWords(s.Text));
        var avgCharactersPerSecond = totalCharacters / segments.Sum(s => s.Duration);
        var avgWordsPerMinute = (totalWords / segments.Sum(s => s.Duration)) * 60;

        Console.WriteLine($"\n?? 详细统计:");
        Console.WriteLine($"   总字符数: {totalCharacters:N0}");
        Console.WriteLine($"   总词数: {totalWords:N0}");
        Console.WriteLine($"   平均字符/秒: {avgCharactersPerSecond:F1}");
        Console.WriteLine($"   平均词数/分钟: {avgWordsPerMinute:F1}");
        
        // 时长分布统计
        DisplayDurationDistribution(segments);
    }

    private void DisplayDurationDistribution(List<AudioSegment> segments)
    {
        var durationRanges = new[]
        {
            (min: 0.0, max: 2.0, label: "0-2秒"),
            (min: 2.0, max: 5.0, label: "2-5秒"),
            (min: 5.0, max: 10.0, label: "5-10秒"),
            (min: 10.0, max: double.MaxValue, label: "10秒以上")
        };

        Console.WriteLine($"\n?? 时长分布:");
        foreach (var range in durationRanges)
        {
            var count = segments.Count(s => s.Duration >= range.min && s.Duration < range.max);
            var percentage = (double)count / segments.Count * 100;
            Console.WriteLine($"   {range.label}: {count} 个 ({percentage:F1}%)");
        }
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
            
        return text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// 生成简化的CSV报告
    /// </summary>
    public void GenerateCsvReport(List<AudioSegment> segments, string outputDirectory)
    {
        var csvPath = Path.Combine(outputDirectory, "segments.csv");
        var csvLines = new List<string>
        {
            "Index,StartTime,EndTime,Duration,Text,OutputFileName,CharacterCount,WordCount"
        };

        csvLines.AddRange(segments.Select((s, i) => 
            $"{i + 1},{s.StartTime:F3},{s.EndTime:F3},{s.Duration:F3}," +
            $"\"{s.Text.Replace("\"", "\"\"")}\",{s.OutputFileName}," +
            $"{s.Text.Length},{CountWords(s.Text)}"
        ));

        File.WriteAllLines(csvPath, csvLines);
        Console.WriteLine($"   ?? CSV报告: {csvPath}");
    }

    /// <summary>
    /// 生成性能分析报告
    /// </summary>
    public void GeneratePerformanceReport(List<AudioSegment> segments, TimeSpan processingTime, string outputDirectory)
    {
        var totalDuration = segments.Sum(s => s.Duration);
        var processingSpeed = totalDuration / processingTime.TotalSeconds;
        
        var performanceReport = new
        {
            ProcessingTime = new
            {
                TotalSeconds = processingTime.TotalSeconds,
                FormattedTime = FormatTimeSpan(processingTime)
            },
            Performance = new
            {
                AudioDurationSeconds = totalDuration,
                ProcessingSpeedRatio = processingSpeed,
                SegmentsPerSecond = segments.Count / processingTime.TotalSeconds,
                Description = processingSpeed > 1 
                    ? $"处理速度是实时的 {processingSpeed:F1}x" 
                    : $"处理速度是实时的 {processingSpeed:F2}x (慢于实时)"
            }
        };

        var performancePath = Path.Combine(outputDirectory, "performance_report.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(performancePath, JsonSerializer.Serialize(performanceReport, jsonOptions));

        Console.WriteLine($"\n? 性能统计:");
        Console.WriteLine($"   处理时间: {FormatTimeSpan(processingTime)}");
        Console.WriteLine($"   音频总时长: {totalDuration:F1} 秒");
        Console.WriteLine($"   处理速度: {processingSpeed:F1}x 实时速度");
        Console.WriteLine($"   ?? 性能报告: {performancePath}");
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 1)
        {
            return $"{timeSpan.TotalSeconds:F1} 秒";
        }
        else if (timeSpan.TotalHours < 1)
        {
            return $"{timeSpan.Minutes} 分 {timeSpan.Seconds} 秒";
        }
        else
        {
            return $"{(int)timeSpan.TotalHours} 小时 {timeSpan.Minutes} 分 {timeSpan.Seconds} 秒";
        }
    }
}