using AudioAlignmentDemo.Interfaces;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Strategies;

/// <summary>
/// �����ı����ȵķָ����
/// ���ı�����ָ������ʱ���зָ�
/// </summary>
public class LengthBasedSplitStrategy : ISplitConditionStrategy
{
    public string Name => "LengthBased";
    public string Description => "�����ı����Ƚ��зָ�����ڳ������ı�";

    public bool ShouldSplit(string text, SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // ����ı������Ƿ񳬹���ֵ
        int maxLength = config.MinSentenceCharacters * 8; // ��������Ϊ��̬����
        return text.Length > maxLength;
    }

    public List<AudioSegment> SplitText(string text, AudioSegment originalSegment, SplitterConfig config)
    {
        if (!ShouldSplit(text, config))
        {
            return new List<AudioSegment> { originalSegment };
        }

        var result = new List<AudioSegment>();
        int maxChunkLength = config.MinSentenceCharacters * 6;
        var chunks = SplitTextIntoChunks(text, maxChunkLength);
        
        if (chunks.Count <= 1)
        {
            return new List<AudioSegment> { originalSegment };
        }

        // ����������ʱ��
        double totalDuration = originalSegment.Duration;
        double currentTime = originalSegment.StartTime;
        int totalCharacters = chunks.Sum(c => c.Length);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            double proportion = (double)chunk.Length / totalCharacters;
            double duration = totalDuration * proportion;
            
            // ȷ�����һ����Ľ���ʱ����ȷ
            double endTime = currentTime + duration;
            if (i == chunks.Count - 1)
            {
                endTime = originalSegment.EndTime;
                duration = endTime - currentTime;
            }

            result.Add(new AudioSegment
            {
                StartTime = currentTime,
                EndTime = endTime,
                Duration = duration,
                Text = chunk.Trim()
            });

            currentTime = endTime;
        }

        if (config.DebugMode)
        {
            Console.WriteLine($"?? [DEBUG] ���ȷָ�: ԭ�ı�{text.Length}�ַ� -> {result.Count}��Ƭ��");
        }

        return result;
    }

    private List<string> SplitTextIntoChunks(string text, int maxChunkLength)
    {
        var chunks = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new List<string>();
        int currentLength = 0;

        foreach (var word in words)
        {
            if (currentLength + word.Length + 1 > maxChunkLength && currentChunk.Count > 0)
            {
                chunks.Add(string.Join(" ", currentChunk));
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(word);
            currentLength += word.Length + 1; // +1 for space
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join(" ", currentChunk));
        }

        return chunks;
    }
}