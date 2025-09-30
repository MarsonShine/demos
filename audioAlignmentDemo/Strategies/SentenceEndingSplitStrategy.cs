using System.Text;
using AudioAlignmentDemo.Interfaces;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Strategies;

/// <summary>
/// 基于句子结尾符号的分割策略
/// 按照句号、问号、感叹号等标点符号进行分割
/// </summary>
public class SentenceEndingSplitStrategy : ISplitConditionStrategy
{
    public string Name => "SentenceEnding";
    public string Description => "基于句子结尾符号（句号、问号、感叹号等）进行分割";

    public bool ShouldSplit(string text, SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // 检查文本长度是否足够分割
        if (text.Length < config.MinSentenceCharacters * 2)
            return false;

        // 检查是否包含句子结尾符号
        var endingChars = new[] { '.', '!', '?', ';', '。', '！', '？', '；' };
        bool hasEndingChars = endingChars.Any(c => text.Contains(c));

        if (!hasEndingChars)
            return false;

        // 统计句子结尾符号数量
        int endingCount = text.Count(c => endingChars.Contains(c));
        
        // 如果只有一个结尾符号且在文本末尾，可能不需要分割
        if (endingCount == 1 && endingChars.Contains(text.Trim().Last()))
        {
            return false; // 单句子，不需要分割
        }

        return endingCount > 1 || (endingCount == 1 && !endingChars.Contains(text.Trim().Last()));
    }

    public List<AudioSegment> SplitText(string text, AudioSegment originalSegment, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        var sentences = ExtractSentencesWithPositions(text, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"🔍 [DEBUG] 分析文本: \"{text}\"");
            Console.WriteLine($"🔍 [DEBUG] 检测到 {sentences.Count} 个句子片段");
        }
        
        // 如果只有一个句子，直接返回原segment
        if (sentences.Count <= 1)
        {
            if (config.DebugMode)
                Console.WriteLine($"🔍 [DEBUG] 单句子，直接返回原始段落");
            return new List<AudioSegment> { originalSegment };
        }
        
        // 智能时间分配
        var timeAllocatedSegments = AllocateTimeToSentences(sentences, originalSegment, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"🔍 [DEBUG] 时间分配结果:");
            for (int i = 0; i < timeAllocatedSegments.Count; i++)
            {
                var seg = timeAllocatedSegments[i];
                Console.WriteLine($"   {i+1}. [{seg.StartTime:F3}s-{seg.EndTime:F3}s] ({seg.Duration:F3}s) \"{seg.Text}\"");
            }
        }
        
        return timeAllocatedSegments;
    }

    private List<SentenceInfo> ExtractSentencesWithPositions(string text, SplitterConfig config)
    {
        var sentences = new List<SentenceInfo>();
        var currentSentence = new StringBuilder();
        var startPos = 0;
        
        for (int i = 0; i < text.Length; i++)
        {
            currentSentence.Append(text[i]);
            
            // 检查是否是句子结束符
            if (IsSentenceEndingChar(text[i]))
            {
                var sentenceText = currentSentence.ToString().Trim();
                
                // 应用最小字符数过滤
                if (sentenceText.Length >= config.MinSentenceCharacters)
                {
                    sentences.Add(new SentenceInfo
                    {
                        Text = sentenceText,
                        StartPosition = startPos,
                        EndPosition = i,
                        CharacterLength = sentenceText.Length
                    });
                    
                    startPos = i + 1;
                    currentSentence.Clear();
                }
                // 太短的句子继续累积
            }
        }
        
        // 处理剩余部分
        ProcessRemainingSentence(sentences, currentSentence, startPos, text, config);
        
        return sentences;
    }

    private void ProcessRemainingSentence(List<SentenceInfo> sentences, StringBuilder currentSentence, int startPos, string text, SplitterConfig config)
    {
        if (currentSentence.Length > 0)
        {
            var remainingText = currentSentence.ToString().Trim();
            if (remainingText.Length >= config.MinSentenceCharacters)
            {
                sentences.Add(new SentenceInfo
                {
                    Text = remainingText,
                    StartPosition = startPos,
                    EndPosition = text.Length - 1,
                    CharacterLength = remainingText.Length
                });
            }
            else if (sentences.Count > 0)
            {
                // 太短的尾部文本合并到最后一个句子
                var lastSentence = sentences[^1];
                lastSentence.Text += " " + remainingText;
                lastSentence.EndPosition = text.Length - 1;
                lastSentence.CharacterLength = lastSentence.Text.Length;
            }
        }
    }

    private List<AudioSegment> AllocateTimeToSentences(List<SentenceInfo> sentences, AudioSegment original, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        
        if (sentences.Count == 0) return result;
        if (sentences.Count == 1)
        {
            return new List<AudioSegment> { original };
        }
        
        double totalDuration = original.Duration;
        double currentTime = original.StartTime;
        
        // 分析每个句子的特征，计算所需的额外时间
        var sentenceAnalysis = AnalyzeSentenceCharacteristics(sentences, config);
        var totalExtraTime = sentenceAnalysis.Sum(a => a.ExtraTimeNeeded);
        
        // 预留边界调整时间和特殊情况处理时间
        double reservedPadding = config.SentenceBoundaryPadding * sentences.Count + totalExtraTime;
        double availableDuration = Math.Max(totalDuration - reservedPadding, totalDuration * 0.7);
        
        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var analysis = sentenceAnalysis[i];
            double duration;
            
            // 根据配置选择基础时间分配方式
            if (config.TimeAllocationMode == "equal")
            {
                duration = availableDuration / sentences.Count;
            }
            else // proportional
            {
                int totalChars = sentences.Sum(s => s.CharacterLength);
                double proportion = (double)sentence.CharacterLength / totalChars;
                duration = availableDuration * proportion;
            }
            
            // 应用动态时间调整
            duration *= config.DynamicTimeAdjustmentFactor;
            
            // 应用智能边界填充
            ApplySmartBoundaryAdjustment(config, analysis, sentence, ref currentTime, ref duration, i);
            
            // 确保最后一个句子的结束时间正确
            double endTime = currentTime + duration;
            if (i == sentences.Count - 1)
            {
                endTime = original.EndTime;
                duration = endTime - currentTime;
            }
            
            var segment = new AudioSegment
            {
                StartTime = currentTime,
                EndTime = endTime,
                Duration = duration,
                Text = sentence.Text
            };
            
            result.Add(segment);
            currentTime = endTime;
        }
        
        return result;
    }

    private void ApplySmartBoundaryAdjustment(SplitterConfig config, SentenceAnalysis analysis, SentenceInfo sentence, 
        ref double currentTime, ref double duration, int index)
    {
        if (config.EnableSmartBoundaryAdjustment)
        {
            // 句子开始前的填充
            if (index > 0)
            {
                currentTime += config.SentenceBoundaryPadding / 2;
            }
            
            // 应用句子特征的额外时间
            duration += analysis.ExtraTimeNeeded;
            
            // 标点符号后的填充
            if (IsNaturalBreakPoint(sentence.Text))
            {
                duration += config.SilencePaddingAfterPunctuation;
            }
        }
    }

    private List<SentenceAnalysis> AnalyzeSentenceCharacteristics(List<SentenceInfo> sentences, SplitterConfig config)
    {
        // 这里复用原来的分析逻辑
        var analyses = new List<SentenceAnalysis>();
        
        foreach (var sentence in sentences)
        {
            var analysis = new SentenceAnalysis
            {
                Sentence = sentence,
                Characteristics = new List<string>(),
                ExtraTimeNeeded = 0.0
            };
            
            var text = sentence.Text.Trim();
            var lowerText = text.ToLowerInvariant();
            
            // 简化的分析逻辑
            if (text.EndsWith("!") || text.EndsWith("?") || text.EndsWith("！") || text.EndsWith("？"))
            {
                analysis.Characteristics.Add("语调变化");
                analysis.ExtraTimeNeeded += config.IntonationBuffer;
            }
            
            if (text.Length < config.MinSentenceCharacters * 2)
            {
                analysis.Characteristics.Add("短句");
                if (config.ShortSentenceMode == "extend")
                {
                    analysis.ExtraTimeNeeded += config.SentenceBoundaryPadding;
                }
            }
            
            if (analysis.Characteristics.Count == 0)
            {
                analysis.Characteristics.Add("普通句子");
            }
            
            analyses.Add(analysis);
        }
        
        return analyses;
    }

    private bool IsSentenceEndingChar(char c)
    {
        return c == '.' || c == '!' || c == '?' || c == ';' || 
               c == '。' || c == '！' || c == '？' || c == '；';
    }

    private bool IsNaturalBreakPoint(string text)
    {
        var breakPoints = new[] { ".", "!", "?", ";", "。", "！", "？", "；" };
        var trimmedText = text.Trim();
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }
}