using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Configuration;

/// <summary>
/// 命令行参数配置器
/// 支持通过命令行参数或配置文件来设置处理参数
/// </summary>
public class CommandLineConfigurator
{
    /// <summary>
    /// 解析命令行参数并创建配置
    /// </summary>
    public static async Task<(SplitterConfig? config, bool shouldExit)> ParseArgumentsAsync(string[] args)
    {
        // 简化的命令行解析
        var parsedArgs = ParseSimpleArguments(args);

        if (parsedArgs.ContainsKey("help") || args.Contains("--help") || args.Contains("-h"))
        {
            ShowUsageHelp();
            return (null, true);
        }

        if (parsedArgs.ContainsKey("error"))
        {
            Console.WriteLine($"? 命令行参数错误: {parsedArgs["error"]}");
            ShowUsageHelp();
            return (null, true);
        }

        var config = CreateConfigFromArguments(parsedArgs);
        return (config, false);
    }

    private static Dictionary<string, string> ParseSimpleArguments(string[] args)
    {
        var result = new Dictionary<string, string>();
        
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            if (arg.StartsWith("--"))
            {
                var key = arg.Substring(2);
                
                // 布尔选项
                if (key == "debug" || key == "batch" || key == "help")
                {
                    result[key] = "true";
                    continue;
                }
                
                // 需要值的选项
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    var value = args[i + 1];
                    
                    // 特殊处理多个输入文件
                    if (key == "input")
                    {
                        var inputFiles = new List<string> { value };
                        i++; // 跳过已处理的值
                        
                        // 继续读取多个文件直到遇到下一个选项
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            inputFiles.Add(args[i + 1]);
                            i++;
                        }
                        
                        result[key] = string.Join("|", inputFiles);
                    }
                    else
                    {
                        result[key] = value;
                        i++; // 跳过已处理的值
                    }
                }
                else
                {
                    result["error"] = $"选项 --{key} 需要一个值";
                    return result;
                }
            }
            else if (arg.StartsWith("-") && arg.Length == 2)
            {
                // 短选项处理
                var shortKey = arg.Substring(1);
                var longKey = MapShortToLongOption(shortKey);
                
                if (longKey == "help")
                {
                    result["help"] = "true";
                    continue;
                }
                
                if (longKey == "debug" || longKey == "batch")
                {
                    result[longKey] = "true";
                    continue;
                }
                
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    result[longKey] = args[i + 1];
                    i++; // 跳过已处理的值
                }
                else
                {
                    result["error"] = $"选项 -{shortKey} 需要一个值";
                    return result;
                }
            }
        }
        
        return result;
    }

    private static string MapShortToLongOption(string shortKey)
    {
        return shortKey switch
        {
            "i" => "input",
            "d" => "input-dir",
            "o" => "output",
            "l" => "language",
            "m" => "model",
            "q" => "quality",
            "c" => "config",
            "p" => "padding",
            "t" => "threshold",
            "b" => "batch",
            "h" => "help",
            _ => shortKey
        };
    }

    private static SplitterConfig CreateConfigFromArguments(Dictionary<string, string> args)
    {
        var config = new SplitterConfig();

        // 从命令行参数获取值
        if (args.TryGetValue("language", out string? language))
            config.Language = language;

        if (args.TryGetValue("model", out string? model))
            config.ModelSize = model;

        if (args.TryGetValue("quality", out string? quality))
            config.AudioQualityStrategy = quality;

        if (args.TryGetValue("output", out string? output))
            config.OutputDirectory = output;

        if (args.TryGetValue("padding", out string? paddingStr) && double.TryParse(paddingStr, out double padding))
            config.SentenceBoundaryPadding = padding;

        if (args.TryGetValue("threshold", out string? thresholdStr) && double.TryParse(thresholdStr, out double threshold))
            config.TimeCorrectionThreshold = threshold;

        if (args.TryGetValue("debug", out _))
            config.DebugMode = true;

        // 如果提供了配置文件，则加载并覆盖默认值
        if (args.TryGetValue("config", out string? configFile) && File.Exists(configFile))
        {
            config = LoadConfigFromFile(configFile, config);
        }

        return config;
    }

    private static SplitterConfig LoadConfigFromFile(string configPath, SplitterConfig baseConfig)
    {
        try
        {
            var jsonContent = File.ReadAllText(configPath);
            var fileConfig = System.Text.Json.JsonSerializer.Deserialize<SplitterConfig>(jsonContent, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
            });

            return fileConfig ?? baseConfig;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? 无法读取配置文件 '{configPath}': {ex.Message}");
            Console.WriteLine("使用默认配置继续...");
            return baseConfig;
        }
    }

    private static void ShowUsageHelp()
    {
        Console.WriteLine();
        Console.WriteLine("?? 音频句子自动切割工具");
        Console.WriteLine("======================");
        Console.WriteLine();
        Console.WriteLine("?? 基本用法:");
        Console.WriteLine("  # 处理单个文件");
        Console.WriteLine("  dotnet run -- --input audio.mp3");
        Console.WriteLine();
        Console.WriteLine("  # 处理多个文件");
        Console.WriteLine("  dotnet run -- --input file1.mp3 file2.wav file3.m4a");
        Console.WriteLine();
        Console.WriteLine("  # 处理整个目录");
        Console.WriteLine("  dotnet run -- --input-dir ./audio_files/");
        Console.WriteLine();
        Console.WriteLine("  # 使用配置文件");
        Console.WriteLine("  dotnet run -- --input audio.mp3 --config settings.json");
        Console.WriteLine();
        Console.WriteLine("?? 常用参数:");
        Console.WriteLine("  -i, --input <files>        输入音频文件(支持多个)");
        Console.WriteLine("  -d, --input-dir <dir>      输入目录路径");
        Console.WriteLine("  -o, --output <dir>         输出目录 (默认: output_sentences)");
        Console.WriteLine("  -l, --language <code>      语言代码 (默认: en)");
        Console.WriteLine("  -m, --model <size>         模型大小 (默认: tiny)");
        Console.WriteLine("  -q, --quality <strategy>   音频品质策略 (默认: HighQuality)");
        Console.WriteLine("  -c, --config <file>        配置文件路径");
        Console.WriteLine("  --debug                    启用调试模式");
        Console.WriteLine("  --batch                    批量处理模式");
        Console.WriteLine();
        Console.WriteLine("?? 高级参数:");
        Console.WriteLine("  -p, --padding <seconds>    句子边界填充时间 (默认: 0.4)");
        Console.WriteLine("  -t, --threshold <seconds>  时间校正阈值 (默认: 0.1)");
        Console.WriteLine();
        Console.WriteLine("?? 示例:");
        Console.WriteLine("  # 高质量英语处理");
        Console.WriteLine("  dotnet run -- -i audio.mp3 -l en -q HighQuality --debug");
        Console.WriteLine();
        Console.WriteLine("  # 批量中文处理");
        Console.WriteLine("  dotnet run -- -d ./chinese_audio/ -l zh -m base --batch");
        Console.WriteLine();
    }

    /// <summary>
    /// 获取输入文件列表
    /// </summary>
    public static List<string> GetInputFiles(Dictionary<string, string> args)
    {
        var files = new List<string>();

        // 从 --input 参数获取文件
        if (args.TryGetValue("input", out string? inputValue))
        {
            var inputFiles = inputValue.Split('|');
            files.AddRange(inputFiles.Where(File.Exists));
        }

        // 从 --input-dir 参数获取目录中的文件
        if (args.TryGetValue("input-dir", out string? inputDir) && Directory.Exists(inputDir))
        {
            var audioExtensions = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
            var dirFiles = Directory.GetFiles(inputDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            files.AddRange(dirFiles);
        }

        return files.Distinct().ToList();
    }

    /// <summary>
    /// 是否为批量处理模式
    /// </summary>
    public static bool IsBatchMode(Dictionary<string, string> args)
    {
        return args.ContainsKey("batch");
    }

    /// <summary>
    /// 生成示例配置文件
    /// </summary>
    public static void GenerateSampleConfigFile(string path = "sample_config.json")
    {
        var sampleConfig = new SplitterConfig
        {
            Language = "en",
            ModelSize = "tiny",
            AudioQualityStrategy = "HighQuality",
            SentenceBoundaryPadding = 0.4,
            TimeCorrectionThreshold = 0.1,
            DebugMode = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(sampleConfig, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        File.WriteAllText(path, json);
        Console.WriteLine($"?? 示例配置文件已生成: {path}");
    }
}