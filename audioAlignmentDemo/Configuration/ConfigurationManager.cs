using AudioAlignmentDemo.Models;
using System.Text.Json;

namespace AudioAlignmentDemo.Configuration;

/// <summary>
/// 配置文件管理器
/// 支持加载、保存和管理不同的配置预设
/// </summary>
public class ConfigurationManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    public static SplitterConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configPath}");
        }

        try
        {
            var jsonContent = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<SplitterConfig>(jsonContent, JsonOptions);
            
            if (config == null)
            {
                throw new InvalidOperationException("配置文件内容无效");
            }

            Console.WriteLine($"? 已加载配置文件: {configPath}");
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"配置文件格式错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    public static void SaveConfig(SplitterConfig config, string configPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(configPath, json);
            
            Console.WriteLine($"? 配置已保存到: {configPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法保存配置文件: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 创建配置预设
    /// </summary>
    public static class Presets
    {
        /// <summary>
        /// 高精度模式 - 适合重要内容的精细切割
        /// </summary>
        public static SplitterConfig HighPrecision => new()
        {
            ModelSize = "base",
            AudioQualityStrategy = "HighQuality",
            SentenceBoundaryPadding = 0.5,
            TimeAllocationMode = "proportional",
            MinSentenceCharacters = 8,
            SilencePaddingAfterPunctuation = 0.4,
            EnableSmartBoundaryAdjustment = true,
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.05,
            MaxExtensionTime = 0.8,
            InterjectionPadding = 0.15,
            IntonationBuffer = 0.2,
            DynamicTimeAdjustmentFactor = 1.3,
            DebugMode = true
        };

        /// <summary>
        /// 快速批量模式 - 适合大量文件的快速处理
        /// </summary>
        public static SplitterConfig FastBatch => new()
        {
            ModelSize = "tiny",
            AudioQualityStrategy = "Balanced",
            SentenceBoundaryPadding = 0.2,
            TimeAllocationMode = "equal",
            MinSentenceCharacters = 5,
            SilencePaddingAfterPunctuation = 0.1,
            EnableSmartBoundaryAdjustment = false,
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.2,
            MaxExtensionTime = 0.3,
            InterjectionPadding = 0.05,
            IntonationBuffer = 0.05,
            DynamicTimeAdjustmentFactor = 1.0,
            DebugMode = false
        };

        /// <summary>
        /// 平衡模式 - 在精度和速度之间平衡
        /// </summary>
        public static SplitterConfig Balanced => new()
        {
            ModelSize = "small",
            AudioQualityStrategy = "HighQuality",
            SentenceBoundaryPadding = 0.18,
            TimeAllocationMode = "proportional",
            MinSentenceCharacters = 2,
            SilencePaddingAfterPunctuation = 0.05,
            EnableSmartBoundaryAdjustment = true,
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.1,
            MaxExtensionTime = 0.5,
            InterjectionPadding = 0.1,
            IntonationBuffer = 0.25,
            DynamicTimeAdjustmentFactor = 1.18,
            DebugMode = false
        };

        /// <summary>
        /// 中文优化模式 - 针对中文语音优化
        /// </summary>
        public static SplitterConfig ChineseOptimized => new()
        {
            Language = "zh",
            ModelSize = "small",
            AudioQualityStrategy = "HighQuality",
            SentenceBoundaryPadding = 0.4,
            TimeAllocationMode = "proportional",
            MinSentenceCharacters = 3, // 中文字符较少
            SilencePaddingAfterPunctuation = 0.3,
            EnableSmartBoundaryAdjustment = true,
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.1,
            MaxExtensionTime = 0.6,
            InterjectionPadding = 0.2, // 中文语气词较多
            IntonationBuffer = 0.25, // 中文语调变化较大
            DynamicTimeAdjustmentFactor = 1.4,
            DebugMode = false
        };

        /// <summary>
        /// 英文对话模式 - 适合英文对话和访谈
        /// </summary>
        public static SplitterConfig EnglishDialogue => new()
        {
            Language = "en",
            ModelSize = "base",
            AudioQualityStrategy = "HighQuality",
            SentenceBoundaryPadding = 0.3,
            TimeAllocationMode = "proportional",
            MinSentenceCharacters = 5,
            SilencePaddingAfterPunctuation = 0.2,
            EnableSmartBoundaryAdjustment = true,
            EnableRepeatedWordDetection = true, // 对话中常有重复
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.08,
            MaxExtensionTime = 0.5,
            InterjectionPadding = 0.15, // "Oh", "Wow", "Yeah"等
            IntonationBuffer = 0.1,
            DynamicTimeAdjustmentFactor = 1.1,
            DebugMode = false
        };

        /// <summary>
        /// 获取所有预设
        /// </summary>
        public static Dictionary<string, SplitterConfig> GetAllPresets()
        {
            return new Dictionary<string, SplitterConfig>
            {
                ["high-precision"] = HighPrecision,
                ["fast-batch"] = FastBatch,
                ["balanced"] = Balanced,
                ["chinese"] = ChineseOptimized,
                ["english-dialogue"] = EnglishDialogue
            };
        }
    }

    /// <summary>
    /// 交互式配置创建器
    /// </summary>
    public static SplitterConfig CreateInteractiveConfig()
    {
        Console.WriteLine("?? 交互式配置创建器");
        Console.WriteLine("=================");
        Console.WriteLine();

        var config = new SplitterConfig();

        // 语言选择
        Console.WriteLine("1. 选择语言:");
        Console.WriteLine("   1) 英文 (en)");
        Console.WriteLine("   2) 中文 (zh)");
        Console.WriteLine("   3) 日文 (ja)");
        Console.WriteLine("   4) 其他 (自定义)");
        Console.Write("   请选择 (默认: 1): ");
        
        var languageChoice = Console.ReadLine();
        config.Language = languageChoice switch
        {
            "2" => "zh",
            "3" => "ja",
            "4" => PromptForValue("请输入语言代码", "en"),
            _ => "en"
        };

        // 模型大小
        Console.WriteLine("\n2. 选择Whisper模型大小:");
        Console.WriteLine("   1) tiny (最快，精度一般)");
        Console.WriteLine("   2) base (平衡)");
        Console.WriteLine("   3) small (较好精度)");
        Console.WriteLine("   4) medium (高精度，较慢)");
        Console.WriteLine("   5) large (最高精度，最慢)");
        Console.Write("   请选择 (默认: 1): ");

        var modelChoice = Console.ReadLine();
        config.ModelSize = modelChoice switch
        {
            "2" => "base",
            "3" => "small",
            "4" => "medium",
            "5" => "large",
            _ => "tiny"
        };

        // 品质策略
        Console.WriteLine("\n3. 选择音频品质策略:");
        Console.WriteLine("   1) 高品质 (保持原始音质)");
        Console.WriteLine("   2) 平衡模式 (品质与大小平衡)");
        Console.WriteLine("   3) Whisper优化 (最小文件)");
        Console.Write("   请选择 (默认: 1): ");

        var qualityChoice = Console.ReadLine();
        config.AudioQualityStrategy = qualityChoice switch
        {
            "2" => "Balanced",
            "3" => "Whisper",
            _ => "HighQuality"
        };

        // 高级设置
        Console.WriteLine("\n4. 是否配置高级参数? (y/N): ");
        if (Console.ReadLine()?.ToLower() == "y")
        {
            config.SentenceBoundaryPadding = PromptForDouble("句子边界填充时间 (秒)", 0.4);
            config.TimeCorrectionThreshold = PromptForDouble("时间校正阈值 (秒)", 0.1);
            config.MinSentenceCharacters = PromptForInt("最小句子字符数", 5);
            
            Console.Write("启用调试模式? (y/N): ");
            config.DebugMode = Console.ReadLine()?.ToLower() == "y";
        }

        Console.WriteLine("\n? 配置创建完成！");
        return config;
    }

    private static string PromptForValue(string prompt, string defaultValue)
    {
        Console.Write($"{prompt} (默认: {defaultValue}): ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
    }

    private static double PromptForDouble(string prompt, double defaultValue)
    {
        Console.Write($"{prompt} (默认: {defaultValue}): ");
        var input = Console.ReadLine();
        return double.TryParse(input, out var result) ? result : defaultValue;
    }

    private static int PromptForInt(string prompt, int defaultValue)
    {
        Console.Write($"{prompt} (默认: {defaultValue}): ");
        var input = Console.ReadLine();
        return int.TryParse(input, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// 显示配置信息
    /// </summary>
    public static void DisplayConfig(SplitterConfig config, string title = "当前配置")
    {
        Console.WriteLine($"?? {title}");
        Console.WriteLine("=" + new string('=', title.Length + 3));
        Console.WriteLine($"语言: {config.Language}");
        Console.WriteLine($"模型: {config.ModelSize}");
        Console.WriteLine($"品质策略: {config.AudioQualityStrategy}");
        Console.WriteLine($"边界填充: {config.SentenceBoundaryPadding}s");
        Console.WriteLine($"时间校正: {(config.EnableTimeCorrection ? "启用" : "禁用")} (阈值: {config.TimeCorrectionThreshold}s)");
        Console.WriteLine($"调试模式: {(config.DebugMode ? "启用" : "禁用")}");
        Console.WriteLine();
    }
}