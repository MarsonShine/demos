using AudioAlignmentDemo.Interfaces;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Strategies;

/// <summary>
/// 基于文本长度的分割策略
/// 当文本超过指定长度时进行分割
/// </summary>
public class LengthBasedSplitStrategy : ISplitConditionStrategy
{
    public string Name => "LengthBased";
    public string Description => "基于文本长度进行分割，适用于长段落文本";

    public bool ShouldSplit(string text, SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // 检查文本长度是否超过阈值
        int maxLength = config.MinSentenceCharacters * 8; // 可以配置为动态参数
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

        // 按比例分配时间
        double totalDuration = originalSegment.Duration;
        double currentTime = originalSegment.StartTime;
        int totalCharacters = chunks.Sum(c => c.Length);

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            double proportion = (double)chunk.Length / totalCharacters;
            double duration = totalDuration * proportion;
            
            // 确保最后一个块的结束时间正确
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
            Console.WriteLine($"?? [DEBUG] 长度分割: 原文本{text.Length}字符 -> {result.Count}个片段");
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