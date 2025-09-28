using System.Text;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// ���ӷ������Ż�����
/// ������������������Ż��ָ���ʱ�����
/// </summary>
public class SentenceAnalysisService
{
    /// <summary>
    /// �Ż���Ƶ�ָ��
    /// </summary>
    public List<AudioSegment> OptimizeSegments(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("���ݱ������Ż���Ƶ�ָ��...");

        var optimized = new List<AudioSegment>();
        var currentSentenceParts = new List<AudioSegment>();

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            // ��鵱ǰsegment�Ƿ�������ӽ�������
            var sentences = SplitTextBySentenceEnding(segment.Text, segment, config);
            
            foreach (var sentence in sentences)
            {
                currentSentenceParts.Add(sentence);
                
                // �����������Ծ��ӽ������Ž�β���ʹ���һ�������ľ��Ӷ�
                if (IsNaturalBreakPoint(sentence.Text))
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

    private List<AudioSegment> SplitTextBySentenceEnding(string text, AudioSegment originalSegment, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        var sentences = ExtractSentencesWithPositions(text, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] �����ı�: \"{text}\"");
            Console.WriteLine($"?? [DEBUG] ��⵽ {sentences.Count} ������Ƭ��");
        }
        
        // ���ֻ��һ�����ӣ�ֱ�ӷ���ԭsegment
        if (sentences.Count <= 1)
        {
            if (config.DebugMode)
                Console.WriteLine($"?? [DEBUG] �����ӣ�ֱ�ӷ���ԭʼ����");
            return new List<AudioSegment> { originalSegment };
        }
        
        // ����ʱ�����
        var timeAllocatedSegments = AllocateTimeToSentences(sentences, originalSegment, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] ʱ�������:");
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
            
            // ����Ƿ��Ǿ��ӽ�����
            if (IsSentenceEndingChar(text[i]))
            {
                var sentenceText = currentSentence.ToString().Trim();
                
                // Ӧ����С�ַ�������
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
                // ̫�̵ľ��Ӽ����ۻ�
            }
        }
        
        // ����ʣ�ಿ��
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
                // ̫�̵�β���ı��ϲ������һ������
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
        
        // ����ÿ�����ӵ���������������Ķ���ʱ��
        var sentenceAnalysis = AnalyzeSentenceCharacteristics(sentences, config);
        var totalExtraTime = sentenceAnalysis.Sum(a => a.ExtraTimeNeeded);
        
        // Ԥ���߽����ʱ��������������ʱ��
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
        
        // ��������ѡ�����ʱ����䷽ʽ
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
        
        // Ӧ�ö�̬ʱ�����
        duration *= config.DynamicTimeAdjustmentFactor;
        
        // Ӧ�����ܱ߽����
        ApplySmartBoundaryAdjustment(config, analysis, sentence, ref currentTime, ref duration, index);
        
        // ȷ�����һ�����ӵĽ���ʱ����ȷ
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
            // ���ӿ�ʼǰ�����
            if (index > 0)
            {
                currentTime += config.SentenceBoundaryPadding / 2;
            }
            
            // Ӧ�þ��������Ķ���ʱ��
            duration += analysis.ExtraTimeNeeded;
            
            // �����ź�����
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
            
            // ���û���������������Ϊ��ͨ����
            if (analysis.Characteristics.Count == 0)
            {
                analysis.Characteristics.Add("��ͨ����");
            }
            
            analyses.Add(analysis);
        }
        
        return analyses;
    }

    private void AnalyzeInterjections(SentenceAnalysis analysis, string lowerText, SplitterConfig config)
    {
        if (IsInterjection(lowerText))
        {
            analysis.Characteristics.Add("������");
            analysis.ExtraTimeNeeded += config.InterjectionPadding;
        }
    }

    private void AnalyzeRepeatedWords(SentenceAnalysis analysis, string lowerText, SplitterConfig config)
    {
        if (config.EnableRepeatedWordDetection && HasRepeatedWords(lowerText))
        {
            analysis.Characteristics.Add("�ظ��ʻ�");
            analysis.ExtraTimeNeeded += config.InterjectionPadding * 0.7;
        }
    }

    private void AnalyzeIntonationChanges(SentenceAnalysis analysis, string text, SplitterConfig config)
    {
        if (text.EndsWith("!") || text.EndsWith("?") || text.EndsWith("��") || text.EndsWith("��"))
        {
            analysis.Characteristics.Add("����仯");
            analysis.ExtraTimeNeeded += config.IntonationBuffer;
        }
    }

    private void AnalyzeShortSentences(SentenceAnalysis analysis, string text, SplitterConfig config)
    {
        if (text.Length < config.MinSentenceCharacters * 2)
        {
            analysis.Characteristics.Add("�̾�");
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
            analysis.Characteristics.Add("ͣ�ٴ�");
            analysis.ExtraTimeNeeded += config.SilencePaddingAfterPunctuation;
        }
    }

    private bool IsInterjection(string text)
    {
        // �����������ʺ͸�̾��ģʽ
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
        // ����ظ��ʻ�ģʽ����"ha ha", "yo yo", "no no"��
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 0; i < words.Length - 1; i++)
        {
            if (words[i].Equals(words[i + 1], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        
        // ��ⳣ�����ظ�ģʽ
        var repeatedPatterns = new[]
        {
            "ha ha", "ho ho", "he he", "hi hi",
            "yo yo", "no no", "oh oh", "ah ah"
        };
        
        return repeatedPatterns.Any(pattern => text.Contains(pattern));
    }

    private bool ContainsPauseWords(string text)
    {
        // �����ܵ���ͣ�ٵĴʻ�
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
        // ����ȷ�ľ��ӽ������ż��
        var breakPoints = new[] { ".", "!", "?", ";", "��", "��", "��", "��" };
        var trimmedText = text.Trim();
        
        // ����ı��Ƿ��Ծ��ӽ������Ž�β
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }

    private bool IsSentenceEndingChar(char c)
    {
        return c == '.' || c == '!' || c == '?' || c == ';' || 
               c == '��' || c == '��' || c == '��' || c == '��';
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

    private void LogTimeAllocationDebugInfo(SplitterConfig config, double totalDuration, double availableDuration, 
        double reservedPadding, double totalExtraTime, List<SentenceAnalysis> sentenceAnalysis)
    {
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] ����ʱ��������:");
            Console.WriteLine($"?? [DEBUG] ʱ��������: {config.TimeAllocationMode}");
            Console.WriteLine($"?? [DEBUG] ��ʱ��: {totalDuration:F3}s, ����ʱ��: {availableDuration:F3}s");
            Console.WriteLine($"?? [DEBUG] Ԥ�����: {reservedPadding:F3}s (�������⴦��: {totalExtraTime:F3}s)");
            
            for (int j = 0; j < sentenceAnalysis.Count; j++)
            {
                var analysis = sentenceAnalysis[j];
                Console.WriteLine($"?? [DEBUG] ����{j+1}����: {string.Join(", ", analysis.Characteristics)} (+{analysis.ExtraTimeNeeded:F3}s)");
            }
        }
    }

    private void LogSegmentDebugInfo(SplitterConfig config, AudioSegment segment, SentenceAnalysis analysis, int index)
    {
        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] ���� {index+1}: \"{segment.Text}\"");
            Console.WriteLine($"?? [DEBUG]   ʱ��: [{segment.StartTime:F3}s-{segment.EndTime:F3}s] ({segment.Duration:F3}s)");
            Console.WriteLine($"?? [DEBUG]   ����: {string.Join(", ", analysis.Characteristics)}");
        }
    }
}