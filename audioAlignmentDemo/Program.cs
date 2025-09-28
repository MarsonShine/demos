using AudioAlignmentDemo.Core;
using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Configuration;
using AudioAlignmentDemo.Services;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            DisplayWelcomeMessage();

            // 解析命令行参数
            var (config, shouldExit) = await CommandLineConfigurator.ParseArgumentsAsync(args);
            
            if (shouldExit)
            {
                return config == null ? 1 : 0; // 显示帮助时返回0，错误时返回1
            }

            if (config == null)
            {
                // 如果没有参数，启动交互模式
                return await RunInteractiveModeAsync();
            }

            // 获取输入文件列表
            var parsedArgs = ParseArguments(args);
            var inputFiles = CommandLineConfigurator.GetInputFiles(parsedArgs);
            var isBatchMode = CommandLineConfigurator.IsBatchMode(parsedArgs);

            if (inputFiles.Count == 0)
            {
                Console.WriteLine("❌ 没有找到要处理的音频文件");
                Console.WriteLine("💡 请使用 --input 或 --input-dir 参数指定输入文件");
                return 1;
            }

            DisplayProcessingInfo(config, inputFiles);

            if (inputFiles.Count == 1)
            {
                // 单文件处理
                await ProcessSingleFileAsync(inputFiles[0], config);
            }
            else
            {
                // 批量处理
                var batchProcessor = new BatchProcessingService();
                await batchProcessor.ProcessBatchAsync(inputFiles, config, parallel: isBatchMode);
            }

            DisplaySuccessMessage(config);
            return 0;
        }
        catch (Exception ex)
        {
            DisplayErrorMessage(ex);
            return 1;
        }
        finally
        {
            if (args.Length == 0) // 只在交互模式下等待按键
            {
                Console.WriteLine();
                Console.WriteLine("按任意键退出...");
                Console.ReadKey();
            }
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>();
        
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            
            if (arg.StartsWith("--"))
            {
                var key = arg.Substring(2);
                
                if (key == "debug" || key == "batch")
                {
                    result[key] = "true";
                    continue;
                }
                
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    var value = args[i + 1];
                    
                    if (key == "input")
                    {
                        var inputFiles = new List<string> { value };
                        i++;
                        
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
                        i++;
                    }
                }
            }
        }
        
        return result;
    }

    private static async Task<int> RunInteractiveModeAsync()
    {
        Console.WriteLine("🎯 交互模式");
        Console.WriteLine("=========");
        Console.WriteLine();
        Console.WriteLine("请选择操作:");
        Console.WriteLine("1. 处理单个音频文件");
        Console.WriteLine("2. 批量处理音频文件");
        Console.WriteLine("3. 使用配置预设");
        Console.WriteLine("4. 创建自定义配置");
        Console.WriteLine("5. 生成示例配置文件");
        Console.WriteLine("6. 显示帮助");
        Console.Write("请选择 (1-6): ");

        var choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                return await ProcessSingleFileInteractiveAsync();
            case "2":
                return await ProcessBatchInteractiveAsync();
            case "3":
                return await UsePresetConfigAsync();
            case "4":
                return await CreateCustomConfigAsync();
            case "5":
                CommandLineConfigurator.GenerateSampleConfigFile();
                return 0;
            case "6":
                await CommandLineConfigurator.ParseArgumentsAsync(new[] { "--help" });
                return 0;
            default:
                Console.WriteLine("无效选择");
                return 1;
        }
    }

    private static async Task<int> ProcessSingleFileInteractiveAsync()
    {
        Console.Write("请输入音频文件路径: ");
        var inputFile = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
        {
            Console.WriteLine("❌ 文件不存在");
            return 1;
        }

        var config = ConfigurationManager.Presets.Balanced;
        config.InputAudioPath = inputFile;
        
        await ProcessSingleFileAsync(inputFile, config);
        return 0;
    }

    private static async Task<int> ProcessBatchInteractiveAsync()
    {
        Console.Write("请输入音频文件目录路径: ");
        var inputDir = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(inputDir) || !Directory.Exists(inputDir))
        {
            Console.WriteLine("❌ 目录不存在");
            return 1;
        }

        var audioExtensions = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg" };
        var files = Directory.GetFiles(inputDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => audioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        if (files.Count == 0)
        {
            Console.WriteLine("❌ 目录中没有找到音频文件");
            return 1;
        }

        var config = ConfigurationManager.Presets.FastBatch;
        var batchProcessor = new BatchProcessingService();
        await batchProcessor.ProcessBatchAsync(files, config);
        
        return 0;
    }

    private static async Task<int> UsePresetConfigAsync()
    {
        var presets = ConfigurationManager.Presets.GetAllPresets();
        
        Console.WriteLine("可用的配置预设:");
        var presetList = presets.ToList();
        for (int i = 0; i < presetList.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {presetList[i].Key}");
        }

        Console.Write($"请选择预设 (1-{presetList.Count}): ");
        if (int.TryParse(Console.ReadLine(), out int choice) && choice >= 1 && choice <= presetList.Count)
        {
            var selectedPreset = presetList[choice - 1];
            ConfigurationManager.DisplayConfig(selectedPreset.Value, $"预设: {selectedPreset.Key}");
            
            Console.Write("请输入音频文件路径: ");
            var inputFile = Console.ReadLine();
            
            if (!string.IsNullOrWhiteSpace(inputFile) && File.Exists(inputFile))
            {
                await ProcessSingleFileAsync(inputFile, selectedPreset.Value);
                return 0;
            }
        }

        Console.WriteLine("❌ 无效选择或文件不存在");
        return 1;
    }

    private static async Task<int> CreateCustomConfigAsync()
    {
        var config = ConfigurationManager.CreateInteractiveConfig();
        
        Console.WriteLine("是否保存此配置? (y/N): ");
        if (Console.ReadLine()?.ToLower() == "y")
        {
            Console.Write("请输入配置文件名 (默认: custom_config.json): ");
            var configFileName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(configFileName))
                configFileName = "custom_config.json";
            
            ConfigurationManager.SaveConfig(config, configFileName);
        }

        Console.Write("请输入音频文件路径: ");
        var inputFile = Console.ReadLine();
        
        if (!string.IsNullOrWhiteSpace(inputFile) && File.Exists(inputFile))
        {
            await ProcessSingleFileAsync(inputFile, config);
            return 0;
        }

        Console.WriteLine("❌ 文件不存在");
        return 1;
    }

    private static async Task ProcessSingleFileAsync(string inputFile, SplitterConfig config)
    {
        config.InputAudioPath = inputFile;
        
        var splitter = new AudioSplitter();
        await splitter.ProcessAsync(config);
    }

    private static void DisplayWelcomeMessage()
    {
        Console.WriteLine("🎤 音频句子自动切割系统 (专业版)");
        Console.WriteLine("===============================");
        Console.WriteLine("📝 功能: 将包含多个句子的音频文件自动切割成独立的句子音频文件");
        Console.WriteLine("🎯 支持: 单文件处理、批量处理、配置预设、命令行操作");
        Console.WriteLine();
        Console.WriteLine("🎵 特色功能:");
        Console.WriteLine("   ✨ 智能音质保持: 输出文件与输入文件格式和音质完全一致");
        Console.WriteLine("   🎯 双模式处理: Whisper识别用WAV，切割用原始格式");
        Console.WriteLine("   🛠️ FFmpeg直接切割: 使用流复制技术，无损切割原始文件");
        Console.WriteLine("   🔧 智能时间校正: 自动修复Whisper识别时长不准确的问题");
        Console.WriteLine("   📦 批量处理: 支持并行处理大量音频文件");
        Console.WriteLine("   ⚙️ 配置预设: 内置多种优化预设，适应不同场景");
        Console.WriteLine();
    }

    private static void DisplayProcessingInfo(SplitterConfig config, List<string> inputFiles)
    {
        Console.WriteLine("🚀 开始处理...");
        Console.WriteLine($"📂 输入文件数量: {inputFiles.Count}");
        
        if (inputFiles.Count <= 5)
        {
            foreach (var file in inputFiles)
            {
                Console.WriteLine($"   📄 {Path.GetFileName(file)}");
            }
        }
        else
        {
            for (int i = 0; i < 3; i++)
            {
                Console.WriteLine($"   📄 {Path.GetFileName(inputFiles[i])}");
            }
            Console.WriteLine($"   ... 还有 {inputFiles.Count - 3} 个文件");
        }

        Console.WriteLine($"📄 支持格式: {config.GetSupportedFormatsString()}");
        Console.WriteLine($"🎵 处理策略: 双模式处理 (Whisper识别用WAV，切割保持原始格式)");
        Console.WriteLine($"🎨 音质策略: {config.GetAudioQualityDescription()}");
        Console.WriteLine($"🎯 输出格式: 与输入格式一致 (保持原始音质)");
        Console.WriteLine($"📏 边界填充: {config.SentenceBoundaryPadding}s");
        Console.WriteLine($"🔧 时间校正: {(config.EnableTimeCorrection ? "启用" : "禁用")} (阈值: {config.TimeCorrectionThreshold:F2}s)");
        Console.WriteLine();
    }

    private static void DisplaySuccessMessage(SplitterConfig config)
    {
        Console.WriteLine();
        Console.WriteLine("🎉 处理完成！");
        Console.WriteLine($"📂 请查看输出目录中的结果文件");
        Console.WriteLine();
        Console.WriteLine("💡 如果需要调整参数，可以:");
        Console.WriteLine("   - 使用 --config 参数指定配置文件");
        Console.WriteLine("   - 使用内置预设配置 (--help 查看详情)");
        Console.WriteLine("   - 运行交互模式进行自定义配置");
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
        
        // 在调试模式下显示完整堆栈跟踪
        if (Environment.GetEnvironmentVariable("DEBUG") == "1")
        {
            Console.WriteLine($"详细信息: {ex}");
        }

        Console.WriteLine();
        Console.WriteLine("💡 获取帮助:");
        Console.WriteLine("   - 运行程序不带参数启动交互模式");
        Console.WriteLine("   - 使用 --help 查看详细使用说明");
        Console.WriteLine("   - 检查输入文件是否存在且格式支持");
    }
}