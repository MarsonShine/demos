using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Services;
using AudioAlignmentDemo.Interfaces;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 句子分析和优化服务
/// 负责分析句子特征、优化分割点和时间分配
/// </summary>
public class SentenceAnalysisService
{
    private readonly SplitStrategyManager _strategyManager;

    public SentenceAnalysisService()
    {
        _strategyManager = new SplitStrategyManager();
    }

    /// <summary>
    /// 优化音频分割点
    /// </summary>
    public List<AudioSegment> OptimizeSegments(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("根据配置策略优化音频分割点...");
        
        // 预检查：如果启用了预检查且段数已经很少，可能不需要进一步分割
        if (config.EnableSplitPrecheck && segments.Count < config.SkipSplitThreshold)
        {
            Console.WriteLine($"? 预检查：识别结果只有 {segments.Count} 个段落，检查是否需要分割...");
            
            bool needsSplitting = false;
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment.Text))
                    continue;
                    
                var strategy = GetSplitStrategy(segment.Text, config);
                if (strategy.ShouldSplit(segment.Text, config))
                {
                    needsSplitting = true;
                    break;
                }
            }
            
            if (!needsSplitting)
            {
                Console.WriteLine("? 预检查：内容不满足分割条件，保持原始段落结构");
                return segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();
            }
        }

        var optimized = new List<AudioSegment>();
        var currentSentenceParts = new List<AudioSegment>();

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            // 使用策略模式进行分割
            var strategy = GetSplitStrategy(segment.Text, config);
            var splitSegments = strategy.SplitText(segment.Text, segment, config);
            
            if (config.DebugMode)
            {
                Console.WriteLine($"?? [DEBUG] 使用策略: {strategy.Name} - {strategy.Description}");
                Console.WriteLine($"?? [DEBUG] 分割结果: {splitSegments.Count} 个片段");
            }
            
            foreach (var splitSegment in splitSegments)
            {
                currentSentenceParts.Add(splitSegment);
                
                // 如果这个部分以句子结束符号结尾，就创建一个完整的句子段
                if (IsNaturalBreakPoint(splitSegment.Text))
                {
                    ProcessCompleteSentence(currentSentenceParts, optimized, config);
                    currentSentenceParts.Clear();
                }
            }
        }

        // 处理最后剩余的部分
        ProcessFinalSegments(currentSentenceParts, optimized, config);

        DisplayOptimizationResults(optimized);
        
        return optimized;
    }

    /// <summary>
    /// 获取分割策略
    /// </summary>
    private ISplitConditionStrategy GetSplitStrategy(string text, SplitterConfig config)
    {
        return config.SplitStrategy.ToLower() switch
        {
            "auto" => _strategyManager.SelectBestStrategy(text, config),
            _ => _strategyManager.GetStrategy(config.SplitStrategy)
        };
    }

    /// <summary>
    /// 注册自定义分割策略
    /// </summary>
    public void RegisterCustomStrategy(string name, ISplitConditionStrategy strategy)
    {
        _strategyManager.RegisterStrategy(name, strategy);
    }

    /// <summary>
    /// 获取所有可用的策略信息
    /// </summary>
    public Dictionary<string, string> GetAvailableStrategies()
    {
        return _strategyManager.GetAllStrategies()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Description);
    }

    private bool IsNaturalBreakPoint(string text)
    {
        // 更精确的句子结束符号检测
        var breakPoints = new[] { ".", "!", "?", ";", "。", "！", "？", "；" };
        var trimmedText = text.Trim();
        
        // 检查文本是否以句子结束符号结尾
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }

    private void ProcessCompleteSentence(List<AudioSegment> currentSentenceParts, List<AudioSegment> optimized, SplitterConfig config)
    {
        if (currentSentenceParts.Count > 0)
        {
            var completeSentence = CombineSegments(currentSentenceParts);
            
            // 检查时长是否符合要求
            if (completeSentence.Duration >= config.MinSegmentDuration)
            {
                optimized.Add(completeSentence);
                Console.WriteLine($"? 创建句子段 {optimized.Count}: [{completeSentence.StartTime:F2}s-{completeSentence.EndTime:F2}s] ({completeSentence.Duration:F2}s)");
                Console.WriteLine($"   内容: \"{completeSentence.Text}\"");
            }
            else
            {
                // 太短的句子与前一个合并
                HandleShortSentence(completeSentence, optimized);
            }
        }
    }

    private void ProcessFinalSegments(List<AudioSegment> currentSentenceParts, List<AudioSegment> optimized, SplitterConfig config)
    {
        if (currentSentenceParts.Count > 0)
        {
            var finalSegment = CombineSegments(currentSentenceParts);
            
            if (finalSegment.Duration >= config.MinSegmentDuration)
            {
                optimized.Add(finalSegment);
                Console.WriteLine($"? 创建最终段 {optimized.Count}: [{finalSegment.StartTime:F2}s-{finalSegment.EndTime:F2}s]");
                Console.WriteLine($"   内容: \"{finalSegment.Text}\"");
            }
            else if (optimized.Count > 0)
            {
                // 与前一个段合并
                var lastSegment = optimized[^1];
                var mergedSegment = MergeSegments(lastSegment, finalSegment);
                optimized[^1] = mergedSegment;
                Console.WriteLine($"?? 合并最终段: \"{mergedSegment.Text}\"");
            }
            else
            {
                optimized.Add(finalSegment);
                Console.WriteLine($"? 创建唯一最终段: \"{finalSegment.Text}\"");
            }
        }
    }

    private void HandleShortSentence(AudioSegment completeSentence, List<AudioSegment> optimized)
    {
        if (optimized.Count > 0)
        {
            var lastSegment = optimized[^1];
            var mergedSegment = MergeSegments(lastSegment, completeSentence);
            optimized[^1] = mergedSegment;
            Console.WriteLine($"?? 合并短句子到段 {optimized.Count}: \"{mergedSegment.Text}\"");
        }
        else
        {
            optimized.Add(completeSentence);
            Console.WriteLine($"? 创建短句子段 {optimized.Count}: \"{completeSentence.Text}\"");
        }
    }

    private AudioSegment CombineSegments(List<AudioSegment> segments)
    {
        if (segments.Count == 0)
            throw new ArgumentException("segments cannot be empty");

        if (segments.Count == 1)
            return segments[0];

        return new AudioSegment
        {
            StartTime = segments[0].StartTime,
            EndTime = segments[^1].EndTime,
            Duration = segments[^1].EndTime - segments[0].StartTime,
            Text = string.Join(" ", segments.Select(s => s.Text.Trim())).Trim()
        };
    }

    private AudioSegment MergeSegments(AudioSegment segment1, AudioSegment segment2)
    {
        return new AudioSegment
        {
            StartTime = segment1.StartTime,
            EndTime = segment2.EndTime,
            Duration = segment2.EndTime - segment1.StartTime,
            Text = (segment1.Text + " " + segment2.Text).Trim()
        };
    }

    private void DisplayOptimizationResults(List<AudioSegment> optimized)
    {
        Console.WriteLine($"\n?? 优化完成！共创建 {optimized.Count} 个句子音频段:");
        for (int i = 0; i < optimized.Count; i++)
        {
            var seg = optimized[i];
            Console.WriteLine($"   {i + 1}. [{seg.StartTime:F2}s-{seg.EndTime:F2}s] ({seg.Duration:F2}s) \"{seg.Text}\"");
        }
    }
}