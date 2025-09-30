using AudioAlignmentDemo.Models;
using System.Text.Json;

namespace AudioAlignmentDemo.Configuration;

/// <summary>
/// �����ļ�������
/// ֧�ּ��ء�����͹���ͬ������Ԥ��
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
    /// ���ļ���������
    /// </summary>
    public static SplitterConfig LoadConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"�����ļ�������: {configPath}");
        }

        try
        {
            var jsonContent = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<SplitterConfig>(jsonContent, JsonOptions);
            
            if (config == null)
            {
                throw new InvalidOperationException("�����ļ�������Ч");
            }

            Console.WriteLine($"? �Ѽ��������ļ�: {configPath}");
            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"�����ļ���ʽ����: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// �������õ��ļ�
    /// </summary>
    public static void SaveConfig(SplitterConfig config, string configPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(configPath, json);
            
            Console.WriteLine($"? �����ѱ��浽: {configPath}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"�޷����������ļ�: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ��������Ԥ��
    /// </summary>
    public static class Presets
    {
        /// <summary>
        /// �߾���ģʽ - �ʺ���Ҫ���ݵľ�ϸ�и�
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
        /// ��������ģʽ - �ʺϴ����ļ��Ŀ��ٴ���
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
        /// ƽ��ģʽ - �ھ��Ⱥ��ٶ�֮��ƽ��
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
        /// �����Ż�ģʽ - ������������Ż�
        /// </summary>
        public static SplitterConfig ChineseOptimized => new()
        {
            Language = "zh",
            ModelSize = "small",
            AudioQualityStrategy = "HighQuality",
            SentenceBoundaryPadding = 0.4,
            TimeAllocationMode = "proportional",
            MinSentenceCharacters = 3, // �����ַ�����
            SilencePaddingAfterPunctuation = 0.3,
            EnableSmartBoundaryAdjustment = true,
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.1,
            MaxExtensionTime = 0.6,
            InterjectionPadding = 0.2, // ���������ʽ϶�
            IntonationBuffer = 0.25, // ��������仯�ϴ�
            DynamicTimeAdjustmentFactor = 1.4,
            DebugMode = false
        };

        /// <summary>
        /// Ӣ�ĶԻ�ģʽ - �ʺ�Ӣ�ĶԻ��ͷ�̸
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
            EnableRepeatedWordDetection = true, // �Ի��г����ظ�
            EnableTimeCorrection = true,
            TimeCorrectionThreshold = 0.08,
            MaxExtensionTime = 0.5,
            InterjectionPadding = 0.15, // "Oh", "Wow", "Yeah"��
            IntonationBuffer = 0.1,
            DynamicTimeAdjustmentFactor = 1.1,
            DebugMode = false
        };

        /// <summary>
        /// ��ȡ����Ԥ��
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
    /// ����ʽ���ô�����
    /// </summary>
    public static SplitterConfig CreateInteractiveConfig()
    {
        Console.WriteLine("?? ����ʽ���ô�����");
        Console.WriteLine("=================");
        Console.WriteLine();

        var config = new SplitterConfig();

        // ����ѡ��
        Console.WriteLine("1. ѡ������:");
        Console.WriteLine("   1) Ӣ�� (en)");
        Console.WriteLine("   2) ���� (zh)");
        Console.WriteLine("   3) ���� (ja)");
        Console.WriteLine("   4) ���� (�Զ���)");
        Console.Write("   ��ѡ�� (Ĭ��: 1): ");
        
        var languageChoice = Console.ReadLine();
        config.Language = languageChoice switch
        {
            "2" => "zh",
            "3" => "ja",
            "4" => PromptForValue("���������Դ���", "en"),
            _ => "en"
        };

        // ģ�ʹ�С
        Console.WriteLine("\n2. ѡ��Whisperģ�ʹ�С:");
        Console.WriteLine("   1) tiny (��죬����һ��)");
        Console.WriteLine("   2) base (ƽ��)");
        Console.WriteLine("   3) small (�Ϻþ���)");
        Console.WriteLine("   4) medium (�߾��ȣ�����)");
        Console.WriteLine("   5) large (��߾��ȣ�����)");
        Console.Write("   ��ѡ�� (Ĭ��: 1): ");

        var modelChoice = Console.ReadLine();
        config.ModelSize = modelChoice switch
        {
            "2" => "base",
            "3" => "small",
            "4" => "medium",
            "5" => "large",
            _ => "tiny"
        };

        // Ʒ�ʲ���
        Console.WriteLine("\n3. ѡ����ƵƷ�ʲ���:");
        Console.WriteLine("   1) ��Ʒ�� (����ԭʼ����)");
        Console.WriteLine("   2) ƽ��ģʽ (Ʒ�����Сƽ��)");
        Console.WriteLine("   3) Whisper�Ż� (��С�ļ�)");
        Console.Write("   ��ѡ�� (Ĭ��: 1): ");

        var qualityChoice = Console.ReadLine();
        config.AudioQualityStrategy = qualityChoice switch
        {
            "2" => "Balanced",
            "3" => "Whisper",
            _ => "HighQuality"
        };

        // �߼�����
        Console.WriteLine("\n4. �Ƿ����ø߼�����? (y/N): ");
        if (Console.ReadLine()?.ToLower() == "y")
        {
            config.SentenceBoundaryPadding = PromptForDouble("���ӱ߽����ʱ�� (��)", 0.4);
            config.TimeCorrectionThreshold = PromptForDouble("ʱ��У����ֵ (��)", 0.1);
            config.MinSentenceCharacters = PromptForInt("��С�����ַ���", 5);
            
            Console.Write("���õ���ģʽ? (y/N): ");
            config.DebugMode = Console.ReadLine()?.ToLower() == "y";
        }

        Console.WriteLine("\n? ���ô�����ɣ�");
        return config;
    }

    private static string PromptForValue(string prompt, string defaultValue)
    {
        Console.Write($"{prompt} (Ĭ��: {defaultValue}): ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
    }

    private static double PromptForDouble(string prompt, double defaultValue)
    {
        Console.Write($"{prompt} (Ĭ��: {defaultValue}): ");
        var input = Console.ReadLine();
        return double.TryParse(input, out var result) ? result : defaultValue;
    }

    private static int PromptForInt(string prompt, int defaultValue)
    {
        Console.Write($"{prompt} (Ĭ��: {defaultValue}): ");
        var input = Console.ReadLine();
        return int.TryParse(input, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// ��ʾ������Ϣ
    /// </summary>
    public static void DisplayConfig(SplitterConfig config, string title = "��ǰ����")
    {
        Console.WriteLine($"?? {title}");
        Console.WriteLine("=" + new string('=', title.Length + 3));
        Console.WriteLine($"����: {config.Language}");
        Console.WriteLine($"ģ��: {config.ModelSize}");
        Console.WriteLine($"Ʒ�ʲ���: {config.AudioQualityStrategy}");
        Console.WriteLine($"�߽����: {config.SentenceBoundaryPadding}s");
        Console.WriteLine($"ʱ��У��: {(config.EnableTimeCorrection ? "����" : "����")} (��ֵ: {config.TimeCorrectionThreshold}s)");
        Console.WriteLine($"����ģʽ: {(config.DebugMode ? "����" : "����")}");
        Console.WriteLine();
    }
}