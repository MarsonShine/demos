namespace AudioAlignmentDemo.Models;

/// <summary>
/// ��Ƶ����Ϣ
/// ����ÿ����Ƶ�ε�ʱ����Ϣ���ı�����
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
/// ������Ϣ
/// �����ı�������ʱ�����
/// </summary>
public class SentenceInfo
{
    public string Text { get; set; } = "";
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public int CharacterLength { get; set; }
}

/// <summary>
/// ���ӷ������
/// ���������������������ʱ��
/// </summary>
public class SentenceAnalysis
{
    public SentenceInfo Sentence { get; set; } = new();
    public List<string> Characteristics { get; set; } = new();
    public double ExtraTimeNeeded { get; set; }
}