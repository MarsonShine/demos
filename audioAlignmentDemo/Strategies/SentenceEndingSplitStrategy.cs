using System.Text;
using AudioAlignmentDemo.Interfaces;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Strategies;

/// <summary>
/// ���ھ��ӽ�β���ŵķָ����
/// ���վ�š��ʺš���̾�ŵȱ����Ž��зָ�
/// </summary>
public class SentenceEndingSplitStrategy : ISplitConditionStrategy
{
    public string Name => "SentenceEnding";
    public string Description => "���ھ��ӽ�β���ţ���š��ʺš���̾�ŵȣ����зָ�";

    public bool ShouldSplit(string text, SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // ����ı������Ƿ��㹻�ָ�
        if (text.Length < config.MinSentenceCharacters * 2)
            return false;

        // ����Ƿ�������ӽ�β����
        var endingChars = new[] { '.', '!', '?', ';', '��', '��', '��', '��' };
        bool hasEndingChars = endingChars.Any(c => text.Contains(c));

        if (!hasEndingChars)
            return false;

        // ͳ�ƾ��ӽ�β��������
        int endingCount = text.Count(c => endingChars.Contains(c));
        
        // ���ֻ��һ����β���������ı�ĩβ�����ܲ���Ҫ�ָ�
        if (endingCount == 1 && endingChars.Contains(text.Trim().Last()))
        {
            return false; // �����ӣ�����Ҫ�ָ�
        }

        return endingCount > 1 || (endingCount == 1 && !endingChars.Contains(text.Trim().Last()));
    }

    public List<AudioSegment> SplitText(string text, AudioSegment originalSegment, SplitterConfig config)
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
        
        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var analysis = sentenceAnalysis[i];
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
            ApplySmartBoundaryAdjustment(config, analysis, sentence, ref currentTime, ref duration, i);
            
            // ȷ�����һ�����ӵĽ���ʱ����ȷ
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
        // ���︴��ԭ���ķ����߼�
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
            
            // �򻯵ķ����߼�
            if (text.EndsWith("!") || text.EndsWith("?") || text.EndsWith("��") || text.EndsWith("��"))
            {
                analysis.Characteristics.Add("����仯");
                analysis.ExtraTimeNeeded += config.IntonationBuffer;
            }
            
            if (text.Length < config.MinSentenceCharacters * 2)
            {
                analysis.Characteristics.Add("�̾�");
                if (config.ShortSentenceMode == "extend")
                {
                    analysis.ExtraTimeNeeded += config.SentenceBoundaryPadding;
                }
            }
            
            if (analysis.Characteristics.Count == 0)
            {
                analysis.Characteristics.Add("��ͨ����");
            }
            
            analyses.Add(analysis);
        }
        
        return analyses;
    }

    private bool IsSentenceEndingChar(char c)
    {
        return c == '.' || c == '!' || c == '?' || c == ';' || 
               c == '��' || c == '��' || c == '��' || c == '��';
    }

    private bool IsNaturalBreakPoint(string text)
    {
        var breakPoints = new[] { ".", "!", "?", ";", "��", "��", "��", "��" };
        var trimmedText = text.Trim();
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }
}