using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Configuration;

/// <summary>
/// �����в���������
/// ֧��ͨ�������в����������ļ������ô������
/// </summary>
public class CommandLineConfigurator
{
    /// <summary>
    /// ���������в�������������
    /// </summary>
    public static async Task<(SplitterConfig? config, bool shouldExit)> ParseArgumentsAsync(string[] args)
    {
        // �򻯵������н���
        var parsedArgs = ParseSimpleArguments(args);

        if (parsedArgs.ContainsKey("help") || args.Contains("--help") || args.Contains("-h"))
        {
            ShowUsageHelp();
            return (null, true);
        }

        if (parsedArgs.ContainsKey("error"))
        {
            Console.WriteLine($"? �����в�������: {parsedArgs["error"]}");
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
                
                // ����ѡ��
                if (key == "debug" || key == "batch" || key == "help")
                {
                    result[key] = "true";
                    continue;
                }
                
                // ��Ҫֵ��ѡ��
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    var value = args[i + 1];
                    
                    // ���⴦���������ļ�
                    if (key == "input")
                    {
                        var inputFiles = new List<string> { value };
                        i++; // �����Ѵ����ֵ
                        
                        // ������ȡ����ļ�ֱ��������һ��ѡ��
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
                        i++; // �����Ѵ����ֵ
                    }
                }
                else
                {
                    result["error"] = $"ѡ�� --{key} ��Ҫһ��ֵ";
                    return result;
                }
            }
            else if (arg.StartsWith("-") && arg.Length == 2)
            {
                // ��ѡ���
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
                    i++; // �����Ѵ����ֵ
                }
                else
                {
                    result["error"] = $"ѡ�� -{shortKey} ��Ҫһ��ֵ";
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

        // �������в�����ȡֵ
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

        // ����ṩ�������ļ�������ز�����Ĭ��ֵ
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
            Console.WriteLine($"?? �޷���ȡ�����ļ� '{configPath}': {ex.Message}");
            Console.WriteLine("ʹ��Ĭ�����ü���...");
            return baseConfig;
        }
    }

    private static void ShowUsageHelp()
    {
        Console.WriteLine();
        Console.WriteLine("?? ��Ƶ�����Զ��и��");
        Console.WriteLine("======================");
        Console.WriteLine();
        Console.WriteLine("?? �����÷�:");
        Console.WriteLine("  # �������ļ�");
        Console.WriteLine("  dotnet run -- --input audio.mp3");
        Console.WriteLine();
        Console.WriteLine("  # �������ļ�");
        Console.WriteLine("  dotnet run -- --input file1.mp3 file2.wav file3.m4a");
        Console.WriteLine();
        Console.WriteLine("  # ��������Ŀ¼");
        Console.WriteLine("  dotnet run -- --input-dir ./audio_files/");
        Console.WriteLine();
        Console.WriteLine("  # ʹ�������ļ�");
        Console.WriteLine("  dotnet run -- --input audio.mp3 --config settings.json");
        Console.WriteLine();
        Console.WriteLine("?? ���ò���:");
        Console.WriteLine("  -i, --input <files>        ������Ƶ�ļ�(֧�ֶ��)");
        Console.WriteLine("  -d, --input-dir <dir>      ����Ŀ¼·��");
        Console.WriteLine("  -o, --output <dir>         ���Ŀ¼ (Ĭ��: output_sentences)");
        Console.WriteLine("  -l, --language <code>      ���Դ��� (Ĭ��: en)");
        Console.WriteLine("  -m, --model <size>         ģ�ʹ�С (Ĭ��: tiny)");
        Console.WriteLine("  -q, --quality <strategy>   ��ƵƷ�ʲ��� (Ĭ��: HighQuality)");
        Console.WriteLine("  -c, --config <file>        �����ļ�·��");
        Console.WriteLine("  --debug                    ���õ���ģʽ");
        Console.WriteLine("  --batch                    ��������ģʽ");
        Console.WriteLine();
        Console.WriteLine("?? �߼�����:");
        Console.WriteLine("  -p, --padding <seconds>    ���ӱ߽����ʱ�� (Ĭ��: 0.4)");
        Console.WriteLine("  -t, --threshold <seconds>  ʱ��У����ֵ (Ĭ��: 0.1)");
        Console.WriteLine();
        Console.WriteLine("?? ʾ��:");
        Console.WriteLine("  # ������Ӣ�ﴦ��");
        Console.WriteLine("  dotnet run -- -i audio.mp3 -l en -q HighQuality --debug");
        Console.WriteLine();
        Console.WriteLine("  # �������Ĵ���");
        Console.WriteLine("  dotnet run -- -d ./chinese_audio/ -l zh -m base --batch");
        Console.WriteLine();
    }

    /// <summary>
    /// ��ȡ�����ļ��б�
    /// </summary>
    public static List<string> GetInputFiles(Dictionary<string, string> args)
    {
        var files = new List<string>();

        // �� --input ������ȡ�ļ�
        if (args.TryGetValue("input", out string? inputValue))
        {
            var inputFiles = inputValue.Split('|');
            files.AddRange(inputFiles.Where(File.Exists));
        }

        // �� --input-dir ������ȡĿ¼�е��ļ�
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
    /// �Ƿ�Ϊ��������ģʽ
    /// </summary>
    public static bool IsBatchMode(Dictionary<string, string> args)
    {
        return args.ContainsKey("batch");
    }

    /// <summary>
    /// ����ʾ�������ļ�
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
        Console.WriteLine($"?? ʾ�������ļ�������: {path}");
    }
}