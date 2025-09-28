using System.Text.Json;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// �������ɷ���
/// �������ɴ������ı����ͳ����Ϣ
/// </summary>
public class ReportGenerationService
{
    /// <summary>
    /// ���ɴ���������
    /// </summary>
    public void GenerateReport(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("?? ���ɽ������...");

        var report = CreateReportData(segments, config);
        
        // ����JSON����
        var jsonPath = GenerateJsonReport(report, config.OutputDirectory);
        
        // �����ı��嵥
        var textListPath = GenerateTextList(segments, config.OutputDirectory);
        
        // ��ʾ������Ϣ
        DisplayReportInfo(jsonPath, textListPath, segments);
        
        // ��ʾͳ����Ϣ
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
               $"    ?? �ַ���: {segment.Text.Length}, ����: {CountWords(segment.Text)}";
    }

    private void DisplayReportInfo(string jsonPath, string textListPath, List<AudioSegment> segments)
    {
        Console.WriteLine($"?? ����������:");
        Console.WriteLine($"   ?? JSON����: {jsonPath}");
        Console.WriteLine($"   ?? �����嵥: {textListPath}");
        Console.WriteLine($"   ?? ������Ƶ: {segments.Count} ���ļ�");
    }

    private void DisplayStatistics(List<AudioSegment> segments)
    {
        Console.WriteLine($"\n?? ����ͳ��:");
        Console.WriteLine($"   �ܾ�����: {segments.Count}");
        Console.WriteLine($"   ��ʱ��: {segments.Sum(s => s.Duration):F2} ��");
        Console.WriteLine($"   ƽ���䳤: {segments.Average(s => s.Duration):F2} ��");
        Console.WriteLine($"   ��̾���: {segments.Min(s => s.Duration):F2} ��");
        Console.WriteLine($"   �����: {segments.Max(s => s.Duration):F2} ��");
        
        // ����ͳ����Ϣ
        DisplayDetailedStatistics(segments);
    }

    private void DisplayDetailedStatistics(List<AudioSegment> segments)
    {
        var totalCharacters = segments.Sum(s => s.Text.Length);
        var totalWords = segments.Sum(s => CountWords(s.Text));
        var avgCharactersPerSecond = totalCharacters / segments.Sum(s => s.Duration);
        var avgWordsPerMinute = (totalWords / segments.Sum(s => s.Duration)) * 60;

        Console.WriteLine($"\n?? ��ϸͳ��:");
        Console.WriteLine($"   ���ַ���: {totalCharacters:N0}");
        Console.WriteLine($"   �ܴ���: {totalWords:N0}");
        Console.WriteLine($"   ƽ���ַ�/��: {avgCharactersPerSecond:F1}");
        Console.WriteLine($"   ƽ������/����: {avgWordsPerMinute:F1}");
        
        // ʱ���ֲ�ͳ��
        DisplayDurationDistribution(segments);
    }

    private void DisplayDurationDistribution(List<AudioSegment> segments)
    {
        var durationRanges = new[]
        {
            (min: 0.0, max: 2.0, label: "0-2��"),
            (min: 2.0, max: 5.0, label: "2-5��"),
            (min: 5.0, max: 10.0, label: "5-10��"),
            (min: 10.0, max: double.MaxValue, label: "10������")
        };

        Console.WriteLine($"\n?? ʱ���ֲ�:");
        foreach (var range in durationRanges)
        {
            var count = segments.Count(s => s.Duration >= range.min && s.Duration < range.max);
            var percentage = (double)count / segments.Count * 100;
            Console.WriteLine($"   {range.label}: {count} �� ({percentage:F1}%)");
        }
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
            
        return text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// ���ɼ򻯵�CSV����
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
        Console.WriteLine($"   ?? CSV����: {csvPath}");
    }

    /// <summary>
    /// �������ܷ�������
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
                    ? $"�����ٶ���ʵʱ�� {processingSpeed:F1}x" 
                    : $"�����ٶ���ʵʱ�� {processingSpeed:F2}x (����ʵʱ)"
            }
        };

        var performancePath = Path.Combine(outputDirectory, "performance_report.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(performancePath, JsonSerializer.Serialize(performanceReport, jsonOptions));

        Console.WriteLine($"\n? ����ͳ��:");
        Console.WriteLine($"   ����ʱ��: {FormatTimeSpan(processingTime)}");
        Console.WriteLine($"   ��Ƶ��ʱ��: {totalDuration:F1} ��");
        Console.WriteLine($"   �����ٶ�: {processingSpeed:F1}x ʵʱ�ٶ�");
        Console.WriteLine($"   ?? ���ܱ���: {performancePath}");
    }

    private string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalMinutes < 1)
        {
            return $"{timeSpan.TotalSeconds:F1} ��";
        }
        else if (timeSpan.TotalHours < 1)
        {
            return $"{timeSpan.Minutes} �� {timeSpan.Seconds} ��";
        }
        else
        {
            return $"{(int)timeSpan.TotalHours} Сʱ {timeSpan.Minutes} �� {timeSpan.Seconds} ��";
        }
    }
}