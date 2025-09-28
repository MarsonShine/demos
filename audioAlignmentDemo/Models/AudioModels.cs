namespace AudioAlignmentDemo.Models;

/// <summary>
/// 音频段信息
/// 包含每个音频段的时间信息和文本内容
/// </summary>
public class AudioSegment
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double Duration { get; set; }
    public string Text { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string OutputPath { get; set; } = "";
}

/// <summary>
/// 句子信息
/// 用于文本分析和时间分配
/// </summary>
public class SentenceInfo
{
    public string Text { get; set; } = "";
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public int CharacterLength { get; set; }
}

/// <summary>
/// 句子分析结果
/// 包含句子特征和所需额外时间
/// </summary>
public class SentenceAnalysis
{
    public SentenceInfo Sentence { get; set; } = new();
    public List<string> Characteristics { get; set; } = new();
    public double ExtraTimeNeeded { get; set; }
}