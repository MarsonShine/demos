using AudioAlignmentDemo.Core;
using AudioAlignmentDemo.Models;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            DisplayWelcomeMessage();

            var splitter = new AudioSplitter();
            var config = CreateConfiguration();

            DisplayProcessingInfo(config);

            await splitter.ProcessAsync(config);
            
            DisplaySuccessMessage(config);
        }
        catch (Exception ex)
        {
            DisplayErrorMessage(ex);
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

    private static void DisplayWelcomeMessage()
    {
        Console.WriteLine("🎤 音频句子自动切割系统 (保持原始音质版)");
        Console.WriteLine("===================================");
        Console.WriteLine("📝 功能: 将包含多个句子的音频文件自动切割成独立的句子音频文件");
        Console.WriteLine("🎯 示例: \"This is Marson! He's a bit naughty, but he is not a bad bird.\"");
        Console.WriteLine("   将被切割成:");
        Console.WriteLine("   📁 sentence_01_xxx_This_is_Marson.mp3 (保持原始MP3格式)");
        Console.WriteLine("   📁 sentence_02_xxx_Hes_a_bit_naughty.mp3 (保持原始音质)");
        Console.WriteLine();
        Console.WriteLine("🎵 特色功能:");
        Console.WriteLine("   ✨ 智能音质保持: 输出文件与输入文件格式和音质完全一致");
        Console.WriteLine("   🎯 双模式处理: Whisper识别用WAV，切割用原始格式");
        Console.WriteLine("   🛠️ FFmpeg直接切割: 使用流复制技术，无损切割原始文件");
        Console.WriteLine("   🔧 智能时间校正: 自动修复Whisper识别时长不准确的问题");
        Console.WriteLine();
    }

    private static SplitterConfig CreateConfiguration()
    {
        return new SplitterConfig
        {
            // 基本配置
            InputAudioPath = "be64c3b9-662c-47cf-8faa-3b663e8aaa0e.mp3",
            OutputDirectory = "output_sentences",
            Language = "en", 
            ModelSize = "tiny",

            // 🎵 音频品质配置
            AudioQualityStrategy = "HighQuality",
            AudioConversionQuality = 100,
            ForceSampleRate = 0,
            ForceBitDepth = 0,
            ForceChannels = 0,
            KeepOriginalAudio = true,

            // ⚙️ 精度调整参数 - 针对切断单词问题优化
            SentenceBoundaryPadding = 0.4,
            TimeAllocationMode = "proportional",
            MinSentenceCharacters = 5,
            SilencePaddingAfterPunctuation = 0.3,
            EnableSmartBoundaryAdjustment = true,
            WordBoundaryMode = "smart",

            // 🔧 时间校正参数 - 解决辅音截断问题
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.1,
            MaxExtensionTime = 0.5,

            // 🎭 语气词和特殊情况处理参数
            InterjectionPadding = 0.08,
            ShortSentenceMode = "extend",
            EnableRepeatedWordDetection = true,
            IntonationBuffer = 0.1,
            DynamicTimeAdjustmentFactor = 1,

            DebugMode = true,

            // 时长控制
            MaxSegmentDuration = 30.0,
            MinSegmentDuration = 1.0,
            WhisperMinSegmentLength = 2.0
        };
    }

    private static void DisplayProcessingInfo(SplitterConfig config)
    {
        Console.WriteLine("🚀 开始处理...");
        Console.WriteLine($"📂 输入文件: {config.InputAudioPath}");
        Console.WriteLine($"📄 支持格式: {config.GetSupportedFormatsString()}");
        Console.WriteLine($"🎵 处理策略: 双模式处理 (Whisper识别用WAV，切割保持原始格式)");
        Console.WriteLine($"🎨 音质策略: {config.GetAudioQualityDescription()}");
        Console.WriteLine($"🎯 输出格式: 与输入格式一致 (保持原始音质)");
        Console.WriteLine($"📏 边界填充: {config.SentenceBoundaryPadding}s");
        Console.WriteLine($"🎭 语气词填充: {config.InterjectionPadding}s");
        Console.WriteLine($"🎵 语调缓冲: {config.IntonationBuffer}s");
        Console.WriteLine($"📊 动态调整: {config.DynamicTimeAdjustmentFactor}x");
        Console.WriteLine($"📝 最小字符: {config.MinSentenceCharacters}");
        Console.WriteLine($"🔇 标点静音: {config.SilencePaddingAfterPunctuation}s");
        Console.WriteLine($"🔧 时间校正: {(config.EnableTimeCorrection ? "启用" : "禁用")} (阈值: {config.TimeCorrectionThreshold:F2}s)");
        Console.WriteLine();
    }

    private static void DisplaySuccessMessage(SplitterConfig config)
    {
        Console.WriteLine();
        Console.WriteLine("🎉 处理完成！");
        Console.WriteLine($"📂 请查看 '{config.OutputDirectory}' 目录中的句子音频文件");
        Console.WriteLine();
        Console.WriteLine("💡 如果仍有切割问题，请调整参数:");
        Console.WriteLine($"   - 增加 SentenceBoundaryPadding (当前 {config.SentenceBoundaryPadding}s)");
        Console.WriteLine($"   - 增加 SilencePaddingAfterPunctuation (当前 {config.SilencePaddingAfterPunctuation}s)");
        Console.WriteLine($"   - 增加 MinSentenceCharacters (当前 {config.MinSentenceCharacters})");
        Console.WriteLine($"   - 调整 TimeCorrectionThreshold (当前 {config.TimeCorrectionThreshold}s)");
    }

    private static void DisplayErrorMessage(Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine("❌ 处理出错:");
        Console.WriteLine($"错误: {ex.Message}");
        
        if (ex.InnerException != null)
        {
            Console.WriteLine($"内部错误: {ex.InnerException.Message}");
        }
        
        Console.WriteLine($"详细信息: {ex}");
    }
}