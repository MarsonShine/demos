// ?? 音频切割精度参数配置模板
// 根据切割效果选择合适的参数组合

using System;

public static class ParameterProfiles
{
    /// <summary>
    /// ?? 精确模式 - 适用于语音清晰、发音标准的音频
    /// 优点: 切割精度高，很少切断单词
    /// 缺点: 可能产生时长不均匀的片段
    /// </summary>
    public static SplitterConfig PrecisionMode => new SplitterConfig
    {
        // 基本设置
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_precision",
        Language = "en",
        ModelSize = "tiny",
        
        // ?? 精度优化参数
        SentenceBoundaryPadding = 0.3,        // 更多边界填充，避免切断
        TimeAllocationMode = "proportional",   // 按字符比例分配
        MinSentenceCharacters = 12,           // 更严格的最小长度
        SilencePaddingAfterPunctuation = 0.25, // 更多标点后静音
        EnableSmartBoundaryAdjustment = true,
        WordBoundaryMode = "smart",
        
        // 调试和时长
        DebugMode = true,
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.8,
        WhisperMinSegmentLength = 2.0
    };

    /// <summary>
    /// ?? 平衡模式 - 在精度和均匀性之间取平衡
    /// 优点: 时长相对均匀，切割较准确
    /// 缺点: 可能偶尔切断单词
    /// </summary>
    public static SplitterConfig BalancedMode => new SplitterConfig
    {
        // 基本设置
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_balanced",
        Language = "en", 
        ModelSize = "tiny",
        
        // ?? 平衡参数
        SentenceBoundaryPadding = 0.2,        // 适中的边界填充
        TimeAllocationMode = "proportional",   // 按比例但有限制
        MinSentenceCharacters = 8,            // 中等最小长度
        SilencePaddingAfterPunctuation = 0.15, // 适中的静音时间
        EnableSmartBoundaryAdjustment = true,
        WordBoundaryMode = "balanced",
        
        // 调试和时长
        DebugMode = false,
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.5,
        WhisperMinSegmentLength = 1.5
    };

    /// <summary>
    /// ?? 均匀模式 - 优先保证时长均匀
    /// 优点: 所有片段时长基本相等
    /// 缺点: 可能在不合适的地方切割
    /// </summary>
    public static SplitterConfig UniformMode => new SplitterConfig
    {
        // 基本设置
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_uniform",
        Language = "en",
        ModelSize = "tiny",
        
        // ?? 均匀参数
        SentenceBoundaryPadding = 0.1,        // 较少边界填充
        TimeAllocationMode = "equal",         // 平均分配时间
        MinSentenceCharacters = 5,            // 较宽松的最小长度
        SilencePaddingAfterPunctuation = 0.1, // 较少静音时间
        EnableSmartBoundaryAdjustment = false,
        WordBoundaryMode = "strict",
        
        // 调试和时长
        DebugMode = false,
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.3,
        WhisperMinSegmentLength = 1.0
    };

    /// <summary>
    /// ?? 调试模式 - 用于分析和调试切割问题
    /// 显示所有详细信息，帮助理解切割过程
    /// </summary>
    public static SplitterConfig DebugMode => new SplitterConfig
    {
        // 基本设置
        InputAudioPath = "temp_align.wav",
        OutputDirectory = "output_sentences_debug",
        Language = "en",
        ModelSize = "tiny",
        
        // ?? 调试参数
        SentenceBoundaryPadding = 0.25,       // 当前问题的推荐值
        TimeAllocationMode = "proportional",
        MinSentenceCharacters = 10,
        SilencePaddingAfterPunctuation = 0.2,
        EnableSmartBoundaryAdjustment = true,
        WordBoundaryMode = "smart",
        
        // ?? 详细调试
        DebugMode = true,  // 显示所有调试信息
        MaxSegmentDuration = 30.0,
        MinSegmentDuration = 0.5,
        WhisperMinSegmentLength = 1.5
    };

    /// <summary>
    /// ?? 打印所有可用的参数配置模式
    /// </summary>
    public static void PrintAvailableModes()
    {
        Console.WriteLine("?? 可用的参数配置模式:");
        Console.WriteLine();
        Console.WriteLine("1. ?? PrecisionMode - 精确模式 (优先切割准确性)");
        Console.WriteLine("2. ?? BalancedMode - 平衡模式 (平衡准确性和均匀性)"); 
        Console.WriteLine("3. ?? UniformMode - 均匀模式 (优先时长均匀)");
        Console.WriteLine("4. ?? DebugMode - 调试模式 (显示详细信息)");
        Console.WriteLine();
        Console.WriteLine("?? 使用方法: 在Main方法中替换config变量");
        Console.WriteLine("   例如: var config = ParameterProfiles.PrecisionMode;");
        Console.WriteLine();
    }
}