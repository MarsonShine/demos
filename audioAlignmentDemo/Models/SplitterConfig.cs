namespace AudioAlignmentDemo.Models;

/// <summary>
/// ��Ƶ�ָ���������
/// �������зָ����������ѡ��
/// </summary>
public class SplitterConfig
{
    // ��������
    public string InputAudioPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "output_segments";
    public string Language { get; set; } = "zh";
    public string ModelSize { get; set; } = "tiny"; // tiny, base, small, medium, large

    // ?? ��Ƶ��ʽ��Ʒ�ʿ���
    /// <summary>
    /// ֧�ֵ���Ƶ��ʽ�б�
    /// �Զ����: WAV, MP3, M4A, WMA, AAC, FLAC, OGG
    /// </summary>
    public string[] SupportedFormats { get; } = { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg" };

    /// <summary>
    /// ��ƵƷ�ʲ���
    /// "HighQuality": ���ֻ�����ԭʼ��ƵƷ�� (�Ƽ��������֡���Ʒ������)
    /// "Balanced": ƽ��Ʒ�ʺ��ļ���С (ͨ��ѡ��)
    /// "Whisper": �Ż�����Whisper���� (16kHz���������ļ���С)
    /// </summary>
    public string AudioQualityStrategy { get; set; } = "HighQuality";

    /// <summary>
    /// ��Ƶת������ (1-100, 100Ϊ�������)
    /// HighQuality����: ����90-100
    /// Balanced����: ����60-80  
    /// Whisper����: ����40-60
    /// </summary>
    public int AudioConversionQuality { get; set; } = 100;

    /// <summary>
    /// ǿ��Ŀ������� (Hz)
    /// 0: �Զ�ѡ����Ѳ�����
    /// >0: ǿ��ָ�������� (��: 44100, 48000)
    /// </summary>
    public int ForceSampleRate { get; set; } = 0;

    /// <summary>
    /// ǿ��Ŀ��λ��� (bits)
    /// 0: �Զ�ѡ�����λ���
    /// >0: ǿ��ָ��λ��� (��: 16, 24, 32)
    /// </summary>
    public int ForceBitDepth { get; set; } = 0;

    /// <summary>
    /// ǿ��Ŀ��������
    /// 0: �Զ�ѡ�����������
    /// 1: ǿ�Ƶ�����
    /// 2: ǿ��������
    /// </summary>
    public int ForceChannels { get; set; } = 0;

    /// <summary>
    /// ����FFmpeg����ת�� - ��NAudio�޷�����ʱʹ��FFmpeg
    /// </summary>
    public bool EnableFFmpegFallback { get; set; } = true;

    /// <summary>
    /// ����ԭʼ��Ƶ�ļ� - ת����ɾ��ԭ�ļ�
    /// </summary>
    public bool KeepOriginalAudio { get; set; } = true;

    // ʱ�����Ʋ���
    public double MaxSegmentDuration { get; set; } = 30.0;
    public double MinSegmentDuration { get; set; } = 1.0;

    // ?? �и�ȵ�������
    /// <summary>
    /// ���ӱ߽���չʱ�䣨�룩- ��ǰ��չ����ʱ���������жϵ���
    /// ����ֵ: 0.1-0.5��
    /// </summary>
    public double SentenceBoundaryPadding { get; set; } = 0.2;

    /// <summary>
    /// ʱ����䷽ʽ: "proportional"(���ַ�����) �� "equal"(ƽ������)
    /// proportional: ���ݾ��ӳ��Ȱ���������ʱ�� (��׼ȷ�����ܲ�����)
    /// equal: ƽ������ʱ�� (�����ȵ����ܲ�׼ȷ)
    /// </summary>
    public string TimeAllocationMode { get; set; } = "proportional";

    /// <summary>
    /// ��С�����ַ��� - ̫�̵�"����"�ᱻ�ϲ���ǰһ������
    /// ����ֵ: 5-15���ַ�
    /// </summary>
    public int MinSentenceCharacters { get; set; } = 8;

    /// <summary>
    /// ���ӽ�������ľ������ʱ�䣨�룩- �ھ��ӽ����������Ӷ���ʱ�������������ľ���
    /// ����ֵ: 0.05-0.3��
    /// </summary>
    public double SilencePaddingAfterPunctuation { get; set; } = 0.15;

    /// <summary>
    /// �������ܱ߽���� - �Զ������и���Ա����ڵ����м��и�
    /// </summary>
    public bool EnableSmartBoundaryAdjustment { get; set; } = true;

    /// <summary>
    /// ����ģʽ - ��ʾ��ϸ��ʱ�������Ϣ
    /// </summary>
    public bool DebugMode { get; set; } = false;

    // ?? ʱ��У������ (����)
    /// <summary>
    /// ��������ʱ��У�� - �Զ�У��Whisperʶ��ʱ����ʵ����Ƶʱ���Ĳ���
    /// ���ʶ��ʱ�����̵��¸����ضϵ�����
    /// </summary>
    public bool EnableTimeCorrection { get; set; } = true;

    /// <summary>
    /// ʱ�������ֵ���룩- ��������ֵ�Ŵ���ʱ��У��
    /// ����ֵ: 0.05-0.2��
    /// </summary>
    public double TimeCorrectionThreshold { get; set; } = 0.1;

    /// <summary>
    /// �����չʱ�䣨�룩- �������������չ��ʱ��
    /// ���ڷ�ֹ������չ������ֵ: 0.2-1.0��
    /// </summary>
    public double MaxExtensionTime { get; set; } = 0.5;

    // �߼�����
    /// <summary>
    /// Whisperʶ�����С���䳤�ȣ��룩- Whisper���������ɵ���С��Ƶ��
    /// ����ֵ: 1.0-3.0��
    /// </summary>
    public double WhisperMinSegmentLength { get; set; } = 1.5;

    /// <summary>
    /// ���ʱ߽���ģʽ
    /// "strict": �ϸ񰴱������и�
    /// "smart": ���ܼ�ⵥ�ʱ߽磬�����жϵ���
    /// "balanced": ƽ��ģʽ�����ȱ����ŵ����ǵ���������
    /// </summary>
    public string WordBoundaryMode { get; set; } = "smart";

    // ?? �����ʺ��������������� (����)
    /// <summary>
    /// ��������չʱ�䣨�룩- Ϊ�����ʣ���Ha ha!, Oh!, Wow!����Ӷ����ʱ��
    /// ������ͨ�����ӳ�������Ȼͣ�٣���Ҫ����ʱ��
    /// ����ֵ: 0.2-0.6��
    /// </summary>
    public double InterjectionPadding { get; set; } = 0.4;

    /// <summary>
    /// �̾����⴦��ģʽ
    /// "extend": Ϊ�̾���Ӹ���ʱ�仺��
    /// "merge": ���̾������ھ��Ӻϲ�
    /// "preserve": ����ԭʼʱ�����
    /// </summary>
    public string ShortSentenceMode { get; set; } = "extend";

    /// <summary>
    /// �ظ��ʻ��� - �����"Ha ha", "Yo yo"���ظ�ģʽ
    /// ����ʻ�ͨ����Ҫ�������Ƶʱ��
    /// </summary>
    public bool EnableRepeatedWordDetection { get; set; } = true;

    /// <summary>
    /// ����仯����ʱ�䣨�룩- Ϊ��̾�䡢���ʾ����Ӷ���ʱ��
    /// ��Щ����ͨ��������仯������ʱ�����
    /// ����ֵ: 0.1-0.3��
    /// </summary>
    public double IntonationBuffer { get; set; } = 0.2;

    /// <summary>
    /// ��̬ʱ�����ϵ�� - ���ݾ���������̬����ʱ�����
    /// 1.0 = ������, 1.2 = ����20%, 0.8 = ����20%
    /// ����ֵ: 1.1-1.3
    /// </summary>
    public double DynamicTimeAdjustmentFactor { get; set; } = 1.2;

    /// <summary>
    /// ��ȡ֧�ֵĸ�ʽ�ַ���������ʾ
    /// </summary>
    public string GetSupportedFormatsString()
    {
        return string.Join(", ", SupportedFormats.Select(f => f.ToUpper().TrimStart('.')));
    }

    /// <summary>
    /// ��ȡ��ƵƷ�ʲ���˵��
    /// </summary>
    public string GetAudioQualityDescription()
    {
        return AudioQualityStrategy switch
        {
            "HighQuality" => "��Ʒ�� (����ԭʼ���ʣ��ʺ����ֺ͸�Ʒ������)",
            "Balanced" => "ƽ��ģʽ (Ʒ�����ļ���Сƽ��)",
            "Whisper" => "Whisper�Ż� (16kHz����������С�ļ�)",
            _ => "δ֪����"
        };
    }
}