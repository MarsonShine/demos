using System.Text;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 句子分析和优化服务
/// 负责分析句子特征、优化分割点和时间分配
/// </summary>
public class SentenceAnalysisService
{
    /// <summary>
    /// 优化音频分割点
    /// </summary>
    public List<AudioSegment> OptimizeSegments(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("根据标点符号优化音频分割点...");

        var optimized = new List<AudioSegment>();
        var currentSentenceParts = new List<AudioSegment>();

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            // 检查当前segment是否包含句子结束符号
            var sentences = SplitTextBySentenceEnding(segment.Text, segment, config);
            
            foreach (var sentence in sentences)
            {
                currentSentenceParts.Add(sentence);
                
                // 如果这个部分以句子结束符号结尾，就创建一个完整的句子段
                if (IsNaturalBreakPoint(sentence.Text))
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

    private List<AudioSegment> SplitTextBySentenceEnding(string text, AudioSegment originalSegment, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        var sentences = ExtractSentencesWithPositions(text, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] 分析文本: \"{text}\"");
            Console.WriteLine($"?? [DEBUG] 检测到 {sentences.Count} 个句子片段");
        }
        
        // 如果只有一个句子，直接返回原segment
        if (sentences.Count <= 1)
        {
            if (config.DebugMode)
                Console.WriteLine($"?? [DEBUG] 单句子，直接返回原始段落");
            return new List<AudioSegment> { originalSegment };
        }
        
        // 智能时间分配
        var timeAllocatedSegments = AllocateTimeToSentences(sentences, originalSegment, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] 时间分配结果:");
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
        
        LogTimeAllocationDebugInfo(config, totalDuration, availableDuration, reservedPadding, totalExtraTime, sentenceAnalysis);
        
        for (int i = 0; i < sentences.Count; i++)
        {
            var segment = CreateTimeAllocatedSegment(sentences, original, config, sentenceAnalysis, availableDuration, ref currentTime, i);
            result.Add(segment);
            
            LogSegmentDebugInfo(config, segment, sentenceAnalysis[i], i);
        }
        
        return result;
    }

    private AudioSegment CreateTimeAllocatedSegment(List<SentenceInfo> sentences, AudioSegment original, SplitterConfig config, 
        List<SentenceAnalysis> sentenceAnalysis, double availableDuration, ref double currentTime, int index)
    {
        var sentence = sentences[index];
        var analysis = sentenceAnalysis[index];
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
        ApplySmartBoundaryAdjustment(config, analysis, sentence, ref currentTime, ref duration, index);
        
        // 确保最后一个句子的结束时间正确
        double endTime = currentTime + duration;
        if (index == sentences.Count - 1)
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
        
        currentTime = endTime;
        return segment;
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
            
            AnalyzeInterjections(analysis, lowerText, config);
            AnalyzeRepeatedWords(analysis, lowerText, config);
            AnalyzeIntonationChanges(analysis, text, config);
            AnalyzeShortSentences(analysis, text, config);
            AnalyzePauseWords(analysis, lowerText, config);
            
            // 如果没有特殊特征，标记为普通句子
            if (analysis.Characteristics.Count == 0)
            {
                analysis.Characteristics.Add("普通句子");
            }
            
            analyses.Add(analysis);
        }
        
        return analyses;
    }

    private void AnalyzeInterjections(SentenceAnalysis analysis, string lowerText, SplitterConfig config)
    {
        if (IsInterjection(lowerText))
        {
            analysis.Characteristics.Add("语气词");
            analysis.ExtraTimeNeeded += config.InterjectionPadding;
        }
    }

    private void AnalyzeRepeatedWords(SentenceAnalysis analysis, string lowerText, SplitterConfig config)
    {
        if (config.EnableRepeatedWordDetection && HasRepeatedWords(lowerText))
        {
            analysis.Characteristics.Add("重复词汇");
            analysis.ExtraTimeNeeded += config.InterjectionPadding * 0.7;
        }
    }

    private void AnalyzeIntonationChanges(SentenceAnalysis analysis, string text, SplitterConfig config)
    {
        if (text.EndsWith("!") || text.EndsWith("?") || text.EndsWith("！") || text.EndsWith("？"))
        {
            analysis.Characteristics.Add("语调变化");
            analysis.ExtraTimeNeeded += config.IntonationBuffer;
        }
    }

    private void AnalyzeShortSentences(SentenceAnalysis analysis, string text, SplitterConfig config)
    {
        if (text.Length < config.MinSentenceCharacters * 2)
        {
            analysis.Characteristics.Add("短句");
            if (config.ShortSentenceMode == "extend")
            {
                analysis.ExtraTimeNeeded += config.SentenceBoundaryPadding;
            }
        }
    }

    private void AnalyzePauseWords(SentenceAnalysis analysis, string lowerText, SplitterConfig config)
    {
        if (ContainsPauseWords(lowerText))
        {
            analysis.Characteristics.Add("停顿词");
            analysis.ExtraTimeNeeded += config.SilencePaddingAfterPunctuation;
        }
    }

    private bool IsInterjection(string text)
    {
        // 常见的语气词和感叹词模式
        var interjectionPatterns = new[]
        {
            "ha ha", "haha", "ah ha", "aha",
            "oh", "oh!", "ooh", "wow", "wow!",
            "hey", "hey!", "hi", "hello",
            "um", "uh", "er", "hmm",
            "yay", "yeah", "yes!", "no!",
            "oops", "whoops", "huh", "eh",
            "yo", "yoyo", "yo yo"
        };
        
        return interjectionPatterns.Any(pattern => 
            text.Contains(pattern) || 
            text.StartsWith(pattern + " ") || 
            text.EndsWith(" " + pattern) ||
            text == pattern
        );
    }

    private bool HasRepeatedWords(string text)
    {
        // 检测重复词汇模式，如"ha ha", "yo yo", "no no"等
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 0; i < words.Length - 1; i++)
        {
            if (words[i].Equals(words[i + 1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        // 检测常见的重复模式
        var repeatedPatterns = new[]
        {
            "ha ha", "ho ho", "he he", "hi hi",
            "yo yo", "no no", "oh oh", "ah ah"
        };
        
        return repeatedPatterns.Any(pattern => text.Contains(pattern));
    }

    private bool ContainsPauseWords(string text)
    {
        // 检测可能导致停顿的词汇
        var pauseWords = new[]
        {
            "well", "so", "and", "but", "however",
            "actually", "really", "like", "you know",
            "i mean", "basically", "obviously"
        };
        
        return pauseWords.Any(word => 
            text.StartsWith(word + " ") || 
            text.Contains(" " + word + " ") ||
            text.EndsWith(" " + word)
        );
    }

    private bool IsNaturalBreakPoint(string text)
    {
        // 更精确的句子结束符号检测
        var breakPoints = new[] { ".", "!", "?", ";", "。", "！", "？", "；" };
        var trimmedText = text.Trim();
        
        // 检查文本是否以句子结束符号结尾
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }

    private bool IsSentenceEndingChar(char c)
    {
        return c == '.' || c == '!' || c == '?' || c == ';' || 
               c == '。' || c == '！' || c == '？' || c == '；';
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

    private void LogTimeAllocationDebugInfo(SplitterConfig config, double totalDuration, double availableDuration, 
        double reservedPadding, double totalExtraTime, List<SentenceAnalysis> sentenceAnalysis)
    {
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] 智能时间分配分析:");
            Console.WriteLine($"?? [DEBUG] 时间分配策略: {config.TimeAllocationMode}");
            Console.WriteLine($"?? [DEBUG] 总时长: {totalDuration:F3}s, 可用时长: {availableDuration:F3}s");
            Console.WriteLine($"?? [DEBUG] 预留填充: {reservedPadding:F3}s (包含特殊处理: {totalExtraTime:F3}s)");
            
            for (int j = 0; j < sentenceAnalysis.Count; j++)
            {
                var analysis = sentenceAnalysis[j];
                Console.WriteLine($"?? [DEBUG] 句子{j+1}特征: {string.Join(", ", analysis.Characteristics)} (+{analysis.ExtraTimeNeeded:F3}s)");
            }
        }
    }

    private void LogSegmentDebugInfo(SplitterConfig config, AudioSegment segment, SentenceAnalysis analysis, int index)
    {
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] 句子 {index+1}: \"{segment.Text}\"");
            Console.WriteLine($"?? [DEBUG]   时间: [{segment.StartTime:F3}s-{segment.EndTime:F3}s] ({segment.Duration:F3}s)");
            Console.WriteLine($"?? [DEBUG]   特征: {string.Join(", ", analysis.Characteristics)}");
        }
    }
}