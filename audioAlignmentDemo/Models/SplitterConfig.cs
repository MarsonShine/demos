namespace AudioAlignmentDemo.Models;

/// <summary>
/// 音频分割器配置类
/// 包含所有分割参数的配置选项
/// </summary>
public class SplitterConfig
{
    // 基本配置
    public string InputAudioPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "output_segments";
    public string Language { get; set; } = "zh";
    public string ModelSize { get; set; } = "tiny"; // tiny, base, small, medium, large

    // ?? 音频格式和品质控制
    /// <summary>
    /// 支持的音频格式列表
    /// 自动检测: WAV, MP3, M4A, WMA, AAC, FLAC, OGG
    /// </summary>
    public string[] SupportedFormats { get; } = { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg" };

    /// <summary>
    /// 音频品质策略
    /// "HighQuality": 保持或提升原始音频品质 (推荐用于音乐、高品质语音)
    /// "Balanced": 平衡品质和文件大小 (通用选择)
    /// "Whisper": 优化用于Whisper处理 (16kHz单声道，文件最小)
    /// </summary>
    public string AudioQualityStrategy { get; set; } = "HighQuality";

    /// <summary>
    /// 音频转换质量 (1-100, 100为最高质量)
    /// HighQuality策略: 建议90-100
    /// Balanced策略: 建议60-80  
    /// Whisper策略: 建议40-60
    /// </summary>
    public int AudioConversionQuality { get; set; } = 100;

    /// <summary>
    /// 强制目标采样率 (Hz)
    /// 0: 自动选择最佳采样率
    /// >0: 强制指定采样率 (如: 44100, 48000)
    /// </summary>
    public int ForceSampleRate { get; set; } = 0;

    /// <summary>
    /// 强制目标位深度 (bits)
    /// 0: 自动选择最佳位深度
    /// >0: 强制指定位深度 (如: 16, 24, 32)
    /// </summary>
    public int ForceBitDepth { get; set; } = 0;

    /// <summary>
    /// 强制目标声道数
    /// 0: 自动选择最佳声道数
    /// 1: 强制单声道
    /// 2: 强制立体声
    /// </summary>
    public int ForceChannels { get; set; } = 0;

    /// <summary>
    /// 启用FFmpeg备用转换 - 当NAudio无法处理时使用FFmpeg
    /// </summary>
    public bool EnableFFmpegFallback { get; set; } = true;

    /// <summary>
    /// 保留原始音频文件 - 转换后不删除原文件
    /// </summary>
    public bool KeepOriginalAudio { get; set; } = true;

    // 时长控制参数
    public double MaxSegmentDuration { get; set; } = 30.0;
    public double MinSegmentDuration { get; set; } = 1.0;

    // ?? 切割精度调整参数
    /// <summary>
    /// 句子边界扩展时间（秒）- 向前扩展多少时间来避免切断单词
    /// 建议值: 0.1-0.5秒
    /// </summary>
    public double SentenceBoundaryPadding { get; set; } = 0.2;

    /// <summary>
    /// 时间分配方式: "proportional"(按字符比例) 或 "equal"(平均分配)
    /// proportional: 根据句子长度按比例分配时间 (更准确但可能不均匀)
    /// equal: 平均分配时间 (更均匀但可能不准确)
    /// </summary>
    public string TimeAllocationMode { get; set; } = "proportional";

    /// <summary>
    /// 最小句子字符数 - 太短的"句子"会被合并到前一个句子
    /// 建议值: 5-15个字符
    /// </summary>
    public int MinSentenceCharacters { get; set; } = 8;

    /// <summary>
    /// 句子结束符后的静音检测时间（秒）- 在句子结束符后增加多少时间来捕获完整的句子
    /// 建议值: 0.05-0.3秒
    /// </summary>
    public double SilencePaddingAfterPunctuation { get; set; } = 0.15;

    /// <summary>
    /// 启用智能边界调整 - 自动调整切割点以避免在单词中间切割
    /// </summary>
    public bool EnableSmartBoundaryAdjustment { get; set; } = true;

    /// <summary>
    /// 调试模式 - 显示详细的时间分配信息
    /// </summary>
    public bool DebugMode { get; set; } = false;

    // ?? 时间校正参数 (新增)
    /// <summary>
    /// 启用智能时间校正 - 自动校正Whisper识别时长与实际音频时长的差异
    /// 解决识别时长过短导致辅音截断的问题
    /// </summary>
    public bool EnableTimeCorrection { get; set; } = true;

    /// <summary>
    /// 时间差异阈值（秒）- 超过此阈值才触发时间校正
    /// 建议值: 0.05-0.2秒
    /// </summary>
    public double TimeCorrectionThreshold { get; set; } = 0.1;

    /// <summary>
    /// 最大扩展时间（秒）- 单个段落最多扩展的时间
    /// 用于防止过度扩展，建议值: 0.2-1.0秒
    /// </summary>
    public double MaxExtensionTime { get; set; } = 0.5;

    // 高级参数
    /// <summary>
    /// Whisper识别的最小段落长度（秒）- Whisper倾向于生成的最小音频段
    /// 建议值: 1.0-3.0秒
    /// </summary>
    public double WhisperMinSegmentLength { get; set; } = 1.5;

    /// <summary>
    /// 单词边界检测模式
    /// "strict": 严格按标点符号切割
    /// "smart": 智能检测单词边界，避免切断单词
    /// "balanced": 平衡模式，优先标点符号但考虑单词完整性
    /// </summary>
    public string WordBoundaryMode { get; set; } = "smart";

    // ?? 语气词和特殊情况处理参数 (新增)
    /// <summary>
    /// 语气词扩展时间（秒）- 为语气词（如Ha ha!, Oh!, Wow!）添加额外的时间
    /// 语气词通常有延长音和自然停顿，需要更多时间
    /// 建议值: 0.2-0.6秒
    /// </summary>
    public double InterjectionPadding { get; set; } = 0.4;

    /// <summary>
    /// 短句特殊处理模式
    /// "extend": 为短句添加更多时间缓冲
    /// "merge": 将短句与相邻句子合并
    /// "preserve": 保持原始时间分配
    /// </summary>
    public string ShortSentenceMode { get; set; } = "extend";

    /// <summary>
    /// 重复词汇检测 - 检测如"Ha ha", "Yo yo"等重复模式
    /// 这类词汇通常需要更多的音频时间
    /// </summary>
    public bool EnableRepeatedWordDetection { get; set; } = true;

    /// <summary>
    /// 语调变化缓冲时间（秒）- 为感叹句、疑问句等添加额外时间
    /// 这些句子通常有语调变化，发音时间更长
    /// 建议值: 0.1-0.3秒
    /// </summary>
    public double IntonationBuffer { get; set; } = 0.2;

    /// <summary>
    /// 动态时间调整系数 - 根据句子特征动态调整时间分配
    /// 1.0 = 不调整, 1.2 = 增加20%, 0.8 = 减少20%
    /// 建议值: 1.1-1.3
    /// </summary>
    public double DynamicTimeAdjustmentFactor { get; set; } = 1.15;

    // 🆕 分割策略选择
    /// <summary>
    /// 分割策略名称
    /// "sentence": 基于句子结尾符号分割 (默认)
    /// "length": 基于文本长度分割
    /// "auto": 自动选择最适合的策略
    /// "custom": 使用自定义策略 (需要注册)
    /// </summary>
    public string SplitStrategy { get; set; } = "sentence";
    
    /// <summary>
    /// 是否启用分割条件预检查
    /// 在进行音频处理前先检查是否真的需要分割
    /// </summary>
    public bool EnableSplitPrecheck { get; set; } = true;
    
    /// <summary>
    /// 跳过不必要分割的阈值
    /// 当识别结果段数少于此值时，跳过音频分割处理
    /// </summary>
    public int SkipSplitThreshold { get; set; } = 2;

    /// <summary>
    /// 获取支持的格式字符串用于显示
    /// </summary>
    public string GetSupportedFormatsString()
    {
        return string.Join(", ", SupportedFormats.Select(f => f.ToUpper().TrimStart('.')));
    }

    /// <summary>
    /// 获取音频品质策略说明
    /// </summary>
    public string GetAudioQualityDescription()
    {
        return AudioQualityStrategy switch
        {
            "HighQuality" => "高品质 (保持原始音质，适合音乐和高品质语音)",
            "Balanced" => "平衡模式 (品质与文件大小平衡)",
            "Whisper" => "Whisper优化 (16kHz单声道，最小文件)",
            _ => "未知策略"
        };
    }
}