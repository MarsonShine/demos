using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Services;
using AudioAlignmentDemo.Interfaces;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// ���ӷ������Ż�����
/// ������������������Ż��ָ���ʱ�����
/// </summary>
public class SentenceAnalysisService
{
    private readonly SplitStrategyManager _strategyManager;

    public SentenceAnalysisService()
    {
        _strategyManager = new SplitStrategyManager();
    }

    /// <summary>
    /// �Ż���Ƶ�ָ��
    /// </summary>
    public List<AudioSegment> OptimizeSegments(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("�������ò����Ż���Ƶ�ָ��...");
        
        // Ԥ��飺���������Ԥ����Ҷ����Ѿ����٣����ܲ���Ҫ��һ���ָ�
        if (config.EnableSplitPrecheck && segments.Count < config.SkipSplitThreshold)
        {
            Console.WriteLine($"? Ԥ��飺ʶ����ֻ�� {segments.Count} �����䣬����Ƿ���Ҫ�ָ�...");
            
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
                Console.WriteLine("? Ԥ��飺���ݲ�����ָ�����������ԭʼ����ṹ");
                return segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToList();
            }
        }

        var optimized = new List<AudioSegment>();
        var currentSentenceParts = new List<AudioSegment>();

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            // ʹ�ò���ģʽ���зָ�
            var strategy = GetSplitStrategy(segment.Text, config);
            var splitSegments = strategy.SplitText(segment.Text, segment, config);
            
            if (config.DebugMode)
            {
                Console.WriteLine($"?? [DEBUG] ʹ�ò���: {strategy.Name} - {strategy.Description}");
                Console.WriteLine($"?? [DEBUG] �ָ���: {splitSegments.Count} ��Ƭ��");
            }
            
            foreach (var splitSegment in splitSegments)
            {
                currentSentenceParts.Add(splitSegment);
                
                // �����������Ծ��ӽ������Ž�β���ʹ���һ�������ľ��Ӷ�
                if (IsNaturalBreakPoint(splitSegment.Text))
                {
                    ProcessCompleteSentence(currentSentenceParts, optimized, config);
                    currentSentenceParts.Clear();
                }
            }
        }

        // �������ʣ��Ĳ���
        ProcessFinalSegments(currentSentenceParts, optimized, config);

        DisplayOptimizationResults(optimized);
        
        return optimized;
    }

    /// <summary>
    /// ��ȡ�ָ����
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
    /// ע���Զ���ָ����
    /// </summary>
    public void RegisterCustomStrategy(string name, ISplitConditionStrategy strategy)
    {
        _strategyManager.RegisterStrategy(name, strategy);
    }

    /// <summary>
    /// ��ȡ���п��õĲ�����Ϣ
    /// </summary>
    public Dictionary<string, string> GetAvailableStrategies()
    {
        return _strategyManager.GetAllStrategies()
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Description);
    }

    private bool IsNaturalBreakPoint(string text)
    {
        // ����ȷ�ľ��ӽ������ż��
        var breakPoints = new[] { ".", "!", "?", ";", "��", "��", "��", "��" };
        var trimmedText = text.Trim();
        
        // ����ı��Ƿ��Ծ��ӽ������Ž�β
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }

    private void ProcessCompleteSentence(List<AudioSegment> currentSentenceParts, List<AudioSegment> optimized, SplitterConfig config)
    {
        if (currentSentenceParts.Count > 0)
        {
            var completeSentence = CombineSegments(currentSentenceParts);
            
            // ���ʱ���Ƿ����Ҫ��
            if (completeSentence.Duration >= config.MinSegmentDuration)
            {
                optimized.Add(completeSentence);
                Console.WriteLine($"? �������Ӷ� {optimized.Count}: [{completeSentence.StartTime:F2}s-{completeSentence.EndTime:F2}s] ({completeSentence.Duration:F2}s)");
                Console.WriteLine($"   ����: \"{completeSentence.Text}\"");
            }
            else
            {
                // ̫�̵ľ�����ǰһ���ϲ�
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
                Console.WriteLine($"? �������ն� {optimized.Count}: [{finalSegment.StartTime:F2}s-{finalSegment.EndTime:F2}s]");
                Console.WriteLine($"   ����: \"{finalSegment.Text}\"");
            }
            else if (optimized.Count > 0)
            {
                // ��ǰһ���κϲ�
                var lastSegment = optimized[^1];
                var mergedSegment = MergeSegments(lastSegment, finalSegment);
                optimized[^1] = mergedSegment;
                Console.WriteLine($"?? �ϲ����ն�: \"{mergedSegment.Text}\"");
            }
            else
            {
                optimized.Add(finalSegment);
                Console.WriteLine($"? ����Ψһ���ն�: \"{finalSegment.Text}\"");
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
            Console.WriteLine($"?? �ϲ��̾��ӵ��� {optimized.Count}: \"{mergedSegment.Text}\"");
        }
        else
        {
            optimized.Add(completeSentence);
            Console.WriteLine($"? �����̾��Ӷ� {optimized.Count}: \"{completeSentence.Text}\"");
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
        Console.WriteLine($"\n?? �Ż���ɣ������� {optimized.Count} ��������Ƶ��:");
        for (int i = 0; i < optimized.Count; i++)
        {
            var seg = optimized[i];
            Console.WriteLine($"   {i + 1}. [{seg.StartTime:F2}s-{seg.EndTime:F2}s] ({seg.Duration:F2}s) \"{seg.Text}\"");
        }
    }
}