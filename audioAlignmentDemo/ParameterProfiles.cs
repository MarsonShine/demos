// ?? ��Ƶ�и�Ȳ�������ģ��
// �����и�Ч��ѡ����ʵĲ������

using System;

public static class ParameterProfiles
{
    /// <summary>
    /// ?? ��ȷģʽ - ����������������������׼����Ƶ
    /// �ŵ�: �и�ȸߣ������жϵ���
    /// ȱ��: ���ܲ���ʱ�������ȵ�Ƭ��
    /// </summary>
    public static SplitterConfig PrecisionMode => new SplitterConfig
    {
        // ��������
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_precision",
        Language = "en",
        ModelSize = "tiny",
        
        // ?? �����Ż�����
        SentenceBoundaryPadding = 0.3,        // ����߽���䣬�����ж�
        TimeAllocationMode = "proportional",   // ���ַ���������
        MinSentenceCharacters = 12,           // ���ϸ����С����
        SilencePaddingAfterPunctuation = 0.25, // ���������
        EnableSmartBoundaryAdjustment = true,
        WordBoundaryMode = "smart",
        
        // ���Ժ�ʱ��
        DebugMode = true,
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.8,
        WhisperMinSegmentLength = 2.0
    };

    /// <summary>
    /// ?? ƽ��ģʽ - �ھ��Ⱥ;�����֮��ȡƽ��
    /// �ŵ�: ʱ����Ծ��ȣ��и��׼ȷ
    /// ȱ��: ����ż���жϵ���
    /// </summary>
    public static SplitterConfig BalancedMode => new SplitterConfig
    {
        // ��������
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_balanced",
        Language = "en", 
        ModelSize = "tiny",
        
        // ?? ƽ�����
        SentenceBoundaryPadding = 0.2,        // ���еı߽����
        TimeAllocationMode = "proportional",   // ��������������
        MinSentenceCharacters = 8,            // �е���С����
        SilencePaddingAfterPunctuation = 0.15, // ���еľ���ʱ��
        EnableSmartBoundaryAdjustment = true,
        WordBoundaryMode = "balanced",
        
        // ���Ժ�ʱ��
        DebugMode = false,
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.5,
        WhisperMinSegmentLength = 1.5
    };

    /// <summary>
    /// ?? ����ģʽ - ���ȱ�֤ʱ������
    /// �ŵ�: ����Ƭ��ʱ���������
    /// ȱ��: �����ڲ����ʵĵط��и�
    /// </summary>
    public static SplitterConfig UniformMode => new SplitterConfig
    {
        // ��������
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_uniform",
        Language = "en",
        ModelSize = "tiny",
        
        // ?? ���Ȳ���
        SentenceBoundaryPadding = 0.1,        // ���ٱ߽����
        TimeAllocationMode = "equal",         // ƽ������ʱ��
        MinSentenceCharacters = 5,            // �Ͽ��ɵ���С����
        SilencePaddingAfterPunctuation = 0.1, // ���پ���ʱ��
        EnableSmartBoundaryAdjustment = false,
        WordBoundaryMode = "strict",
        
        // ���Ժ�ʱ��
        DebugMode = false,
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.3,
        WhisperMinSegmentLength = 1.0
    };

    /// <summary>
    /// ?? ����ģʽ - ���ڷ����͵����и�����
    /// ��ʾ������ϸ��Ϣ����������и����
    /// </summary>
    public static SplitterConfig DebugMode => new SplitterConfig
    {
        // ��������
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_debug",
        Language = "en",
        ModelSize = "tiny",
        
        // ?? ���Բ���
        SentenceBoundaryPadding = 0.25,       // ��ǰ������Ƽ�ֵ
        TimeAllocationMode = "proportional",
        MinSentenceCharacters = 10,
        SilencePaddingAfterPunctuation = 0.2,
        EnableSmartBoundaryAdjustment = true,
        WordBoundaryMode = "smart",
        
        // ?? ��ϸ����
        DebugMode = true,  // ��ʾ���е�����Ϣ
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.5,
        WhisperMinSegmentLength = 1.5
    };

    /// <summary>
    /// ?? ��ӡ���п��õĲ�������ģʽ
    /// </summary>
    public static void PrintAvailableModes()
    {
        Console.WriteLine("?? ���õĲ�������ģʽ:");
        Console.WriteLine();
        Console.WriteLine("1. ?? PrecisionMode - ��ȷģʽ (�����и�׼ȷ��)");
        Console.WriteLine("2. ?? BalancedMode - ƽ��ģʽ (ƽ��׼ȷ�Ժ;�����)"); 
        Console.WriteLine("3. ?? UniformMode - ����ģʽ (����ʱ������)");
        Console.WriteLine("4. ?? DebugMode - ����ģʽ (��ʾ��ϸ��Ϣ)");
        Console.WriteLine();
        Console.WriteLine("?? ʹ�÷���: ��Main�������滻config����");
        Console.WriteLine("   ����: var config = ParameterProfiles.PrecisionMode;");
        Console.WriteLine();
    }
}