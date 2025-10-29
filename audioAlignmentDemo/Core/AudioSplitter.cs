using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Services;

namespace AudioAlignmentDemo.Core;

/// <summary>
/// 音频分割器主类
/// 协调各个服务完成音频分割任务
/// </summary>
public class AudioSplitter
{
    private readonly AudioConversionService _conversionService;
    private readonly SpeechRecognitionService _recognitionService;
    private readonly SentenceAnalysisService _analysisService;
    private readonly AudioSplittingService _splittingService;
    private readonly ReportGenerationService _reportService;

    public AudioSplitter()
    {
        _conversionService = new AudioConversionService();
        _recognitionService = new SpeechRecognitionService();
        _analysisService = new SentenceAnalysisService();
        _splittingService = new AudioSplittingService();
        _reportService = new ReportGenerationService();
    }

    /// <summary>
    /// 执行音频分割流程
    /// </summary>
    public async Task ProcessAsync(SplitterConfig config)
    {
        var startTime = DateTime.Now;
        
        try
        {
            Console.WriteLine("🎵 开始音频分割流程...\n");
            
            // 1. 验证输入文件
            ValidateInputFile(config);

            // 2. 准备输出目录
            PrepareOutputDirectory(config);

            // 3. 音频格式转换 (准备给Whisper识别)
            Console.WriteLine("🔄 步骤 1/6: 音频格式转换");
            string processedAudio = await _conversionService.ConvertToWhisperFormatAsync(
                config.InputAudioPath, config.OutputDirectory);

            // 4. 语音识别与时间对齐 (或使用预设文本内容)
            Console.WriteLine("\n🎤 步骤 2/6: 语音识别与时间对齐");
            List<AudioSegment> segments;
            
            if (config.UsePresetTextContent)
            {
                Console.WriteLine("🎯 检测到预设文本内容，跳过语音识别步骤");
                Console.WriteLine($"📝 使用预设文本: \"{config.PresetTextContent!.Substring(0, Math.Min(100, config.PresetTextContent.Length))}...\"");
                
                // 使用预设文本内容创建音频段
                segments = await CreateSegmentsFromPresetText(processedAudio, config);
            }
            else
            {
                // 标准语音识别流程
                segments = await _recognitionService.PerformAlignmentAsync(processedAudio, config);
            }

            // 🆕 5. 早期分割条件检查 - 避免不必要的处理
            Console.WriteLine("\n🔍 步骤 3/6: 分割条件预检查");
            bool shouldProceedWithSplitting = await PreCheckSplittingConditions(segments, config);
            
            if (!shouldProceedWithSplitting)
            {
                Console.WriteLine("⚠ 检测到内容不需要分割，跳过后续处理步骤");
                Console.WriteLine("💾 保存单个完整音频文件...");
                
                // 复制原始音频到输出目录
                await SaveSingleAudioFile(config);
                
                // 生成简化报告
                GenerateSkippedProcessingReport(segments, DateTime.Now - startTime, config);
                
                // 清理临时文件
                CleanupTemporaryFiles(processedAudio, config);
                
                Console.WriteLine($"\n✅ 处理完成，总耗时: {(DateTime.Now - startTime).TotalSeconds:F1} 秒");
                Console.WriteLine($"📂 查看 '{config.OutputDirectory}' 目录中的结果文件");
                return;
            }

            // 6. 句子分析和分割优化
            Console.WriteLine("\n📝 步骤 4/6: 句子分析和分割优化");
            var optimizedSegments = _analysisService.OptimizeSegments(segments, config);

            // 7. 音频文件切割 (使用原始音频文件进行切割)
            Console.WriteLine("\n✂️ 步骤 5/6: 音频文件切割");
            await _splittingService.SplitAudioFilesAsync(config.InputAudioPath, optimizedSegments, config);

            //// 8. 生成处理报告
            //Console.WriteLine("\n📊 步骤 6/6: 生成处理报告");
            //_reportService.GenerateReport(optimizedSegments, config);
            
            //// 9. 生成性能分析报告
            //var processingTime = DateTime.Now - startTime;
            //_reportService.GeneratePerformanceReport(optimizedSegments, processingTime, config.OutputDirectory);

            // 10. 清理临时文件
            CleanupTemporaryFiles(processedAudio, config);
            
            Console.WriteLine($"\n🎉 处理完成，总耗时: {(DateTime.Now - startTime).TotalSeconds:F1} 秒");
            Console.WriteLine($"📂 查看 '{config.OutputDirectory}' 目录中的结果文件");
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            Console.WriteLine($"\n❌ 处理失败 (耗时: {processingTime.TotalSeconds:F1} 秒)");
            throw new AudioSplitterException($"音频分割失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 预检查分割条件，避免不必要的处理
    /// </summary>
    private async Task<bool> PreCheckSplittingConditions(List<AudioSegment> segments, SplitterConfig config)
    {
        if (!config.EnableSplitPrecheck)
        {
            Console.WriteLine("📋 分割预检查已禁用，继续完整处理流程");
            return true;
        }

        // 检查识别段数
        if (segments.Count < config.SkipSplitThreshold)
        {
            Console.WriteLine($"📊 识别结果: {segments.Count} 个段落 (阈值: {config.SkipSplitThreshold})");
            
            // 进一步检查文本内容是否需要分割
            var strategyManager = new SplitStrategyManager();
            bool needsSplitting = false;
            
            foreach (var segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment.Text))
                    continue;
                    
                var strategy = config.SplitStrategy.ToLower() == "auto" 
                    ? strategyManager.SelectBestStrategy(segment.Text, config)
                    : strategyManager.GetStrategy(config.SplitStrategy);
                    
                if (strategy.ShouldSplit(segment.Text, config))
                {
                    Console.WriteLine($"✓ 段落需要分割: \"{segment.Text.Substring(0, Math.Min(50, segment.Text.Length))}...\"");
                    needsSplitting = true;
                    break;
                }
            }
            
            if (!needsSplitting)
            {
                Console.WriteLine("🚫 所有段落都不满足分割条件");
                return false;
            }
        }
        
        Console.WriteLine("✅ 检测到需要分割的内容，继续处理");
        return true;
    }

    /// <summary>
    /// 保存单个音频文件（当不需要分割时）
    /// </summary>
    private async Task SaveSingleAudioFile(SplitterConfig config)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(config.InputAudioPath);
            var extension = Path.GetExtension(config.InputAudioPath);
            var outputPath = Path.Combine(config.OutputDirectory, $"complete_audio{extension}");
            
            File.Copy(config.InputAudioPath, outputPath, true);
            Console.WriteLine($"📁 已保存完整音频: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 保存完整音频文件时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成跳过处理的简化报告
    /// </summary>
    private void GenerateSkippedProcessingReport(List<AudioSegment> segments, TimeSpan processingTime, SplitterConfig config)
    {
        try
        {
            var reportPath = Path.Combine(config.OutputDirectory, "processing_report.json");
            var report = new
            {
                Status = "Skipped",
                Reason = "Content does not require splitting",
                ProcessingTime = processingTime.TotalSeconds,
                OriginalSegments = segments.Count,
                SplitStrategy = config.SplitStrategy,
                InputFile = config.InputAudioPath,
                OutputDirectory = config.OutputDirectory,
                Timestamp = DateTime.Now
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            File.WriteAllText(reportPath, json);
            Console.WriteLine($"📋 已生成处理报告: {reportPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 生成报告时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 从预设文本内容创建音频段
    /// 结合Whisper时间对齐和预设文本内容，提供精确的分割结果
    /// </summary>
    private async Task<List<AudioSegment>> CreateSegmentsFromPresetText(string processedAudio, SplitterConfig config)
    {
        try
        {
            Console.WriteLine("🎯 使用预设文本内容模式");
            Console.WriteLine($"📝 预设文本: \"{config.PresetTextContent!.Substring(0, Math.Min(100, config.PresetTextContent.Length))}...\"");
            
            // 1. 首先使用Whisper获取精确的时间对齐信息
            Console.WriteLine("🎤 步骤 1/3: 使用Whisper获取时间对齐信息");
            var whisperSegments = await _recognitionService.PerformAlignmentAsync(processedAudio, config);
            
            if (whisperSegments == null || whisperSegments.Count == 0)
            {
                throw new AudioSplitterException("Whisper识别失败，无法获取时间对齐信息");
            }
            
            Console.WriteLine($"✅ Whisper识别完成，获得 {whisperSegments.Count} 个时间段");
            
            // 2. 使用预设文本内容校正识别结果
            Console.WriteLine("🔧 步骤 2/3: 使用预设文本校正识别结果");
            var correctedSegments = CorrectSegmentsWithPresetText(whisperSegments, config.PresetTextContent, config);
            
            Console.WriteLine($"✅ 文本校正完成，生成 {correctedSegments.Count} 个校正段落");
            
            // 3. 显示校正结果对比
            DisplayCorrectionResults(whisperSegments, correctedSegments, config);
            
            return correctedSegments;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 创建预设文本音频段失败: {ex.Message}");
            throw new AudioSplitterException($"无法从预设文本创建音频段: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 使用预设文本内容校正Whisper识别结果
    /// 保持时间信息，但使用准确的文本内容
    /// </summary>
    private List<AudioSegment> CorrectSegmentsWithPresetText(List<AudioSegment> whisperSegments, string presetText, SplitterConfig config)
    {
        try
        {
            // 方案选择基于配置
            return config.PresetTextMode.ToLower() switch
            {
                "replace" => ReplaceWithPresetText(whisperSegments, presetText, config),
                "merge" => MergeWithPresetText(whisperSegments, presetText, config),
                "fallback" => UsePresetTextAsFallback(whisperSegments, presetText, config),
                _ => ReplaceWithPresetText(whisperSegments, presetText, config)
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 文本校正失败，使用原始Whisper结果: {ex.Message}");
            return whisperSegments;
        }
    }

    /// <summary>
    /// 完全替换模式：使用预设文本替换识别文本，但保持时间信息
    /// </summary>
    private List<AudioSegment> ReplaceWithPresetText(List<AudioSegment> whisperSegments, string presetText, SplitterConfig config)
    {
        if (whisperSegments.Count == 0)
        {
            return new List<AudioSegment>();
        }

        // 如果只有一个Whisper段落，直接替换文本内容
        if (whisperSegments.Count == 1)
        {
            var singleSegment = whisperSegments[0];
            Console.WriteLine($"🔄 单段落替换: 保持时间 [{singleSegment.StartTime:F2}s-{singleSegment.EndTime:F2}s]");
            
            return new List<AudioSegment>
            {
                new AudioSegment
                {
                    StartTime = singleSegment.StartTime,
                    EndTime = singleSegment.EndTime,
                    Duration = singleSegment.Duration,
                    Text = presetText.Trim()
                }
            };
        }

        // 多段落情况：尝试智能映射
        return SmartTextMapping(whisperSegments, presetText, config);
    }

    /// <summary>
    /// 智能文本映射：将预设文本智能分配到Whisper时间段
    /// </summary>
    private List<AudioSegment> SmartTextMapping(List<AudioSegment> whisperSegments, string presetText, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        
        // 简单策略：按字符比例分配文本到时间段
        var words = presetText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        int wordsPerSegment = Math.Max(1, words.Count / whisperSegments.Count);
        
        Console.WriteLine($"📊 智能映射: {words.Count} 个单词 → {whisperSegments.Count} 个时间段 (约 {wordsPerSegment} 词/段)");
        
        int wordIndex = 0;
        for (int i = 0; i < whisperSegments.Count; i++)
        {
            var segment = whisperSegments[i];
            var segmentWords = new List<string>();
            
            // 为当前段落分配单词
            int wordsForThisSegment = (i == whisperSegments.Count - 1) 
                ? words.Count - wordIndex  // 最后一段获得所有剩余单词
                : wordsPerSegment;
                
            for (int j = 0; j < wordsForThisSegment && wordIndex < words.Count; j++)
            {
                segmentWords.Add(words[wordIndex]);
                wordIndex++;
            }
            
            if (segmentWords.Count > 0)
            {
                var segmentText = string.Join(" ", segmentWords);
                result.Add(new AudioSegment
                {
                    StartTime = segment.StartTime,
                    EndTime = segment.EndTime,
                    Duration = segment.Duration,
                    Text = segmentText
                });
                
                if (config.DebugMode)
                {
                    Console.WriteLine($"🔍 [DEBUG] 段落 {i+1}: [{segment.StartTime:F2}s-{segment.EndTime:F2}s] \"{segmentText}\"");
                }
            }
        }
        
        return result;
    }

    /// <summary>
    /// 合并模式：尝试合并Whisper识别结果和预设文本 (实验性)
    /// </summary>
    private List<AudioSegment> MergeWithPresetText(List<AudioSegment> whisperSegments, string presetText, SplitterConfig config)
    {
        Console.WriteLine("🧪 使用实验性合并模式");
        
        // 简单实现：使用预设文本的标点符号，但尝试保持Whisper的单词
        var result = new List<AudioSegment>();
        
        // 获取预设文本的句子分割
        var presetSentences = SplitTextIntoSentences(presetText);
        Console.WriteLine($"📝 预设文本包含 {presetSentences.Count} 个句子");
        
        if (presetSentences.Count <= whisperSegments.Count)
        {
            // 预设句子数量不超过Whisper段落，可以一一对应
            for (int i = 0; i < Math.Min(presetSentences.Count, whisperSegments.Count); i++)
            {
                var segment = whisperSegments[i];
                result.Add(new AudioSegment
                {
                    StartTime = segment.StartTime,
                    EndTime = segment.EndTime,
                    Duration = segment.Duration,
                    Text = presetSentences[i].Trim()
                });
            }
        }
        else
        {
            // 预设句子更多，需要重新分配时间
            return SmartTextMapping(whisperSegments, presetText, config);
        }
        
        return result;
    }

    /// <summary>
    /// 后备模式：仅在识别质量低时使用预设文本
    /// </summary>
    private List<AudioSegment> UsePresetTextAsFallback(List<AudioSegment> whisperSegments, string presetText, SplitterConfig config)
    {
        // 简单的质量评估：检查标点符号比例
        var whisperText = string.Join(" ", whisperSegments.Select(s => s.Text));
        var punctuationCount = whisperText.Count(c => ".,!?;。！？；".Contains(c));
        var punctuationRatio = (double)punctuationCount / Math.Max(1, whisperText.Length);
        
        Console.WriteLine($"📊 识别质量评估: 标点符号比例 {punctuationRatio:P}");
        
        if (punctuationRatio < 0.02) // 标点符号比例低于2%，认为质量不好
        {
            Console.WriteLine("⚠ 识别质量较低，使用预设文本");
            return ReplaceWithPresetText(whisperSegments, presetText, config);
        }
        else
        {
            Console.WriteLine("✅ 识别质量良好，保持原始结果");
            return whisperSegments;
        }
    }

    /// <summary>
    /// 将文本按句子分割
    /// </summary>
    private List<string> SplitTextIntoSentences(string text)
    {
        var sentences = new List<string>();
        var currentSentence = "";
        
        foreach (char c in text)
        {
            currentSentence += c;
            
            if (".,!?;。！？；".Contains(c))
            {
                var sentence = currentSentence.Trim();
                if (!string.IsNullOrEmpty(sentence))
                {
                    sentences.Add(sentence);
                }
                currentSentence = "";
            }
        }
        
        // 处理剩余部分
        if (!string.IsNullOrWhiteSpace(currentSentence))
        {
            sentences.Add(currentSentence.Trim());
        }
        
        return sentences;
    }

    /// <summary>
    /// 显示校正结果对比
    /// </summary>
    private void DisplayCorrectionResults(List<AudioSegment> original, List<AudioSegment> corrected, SplitterConfig config)
    {
        if (!config.DebugMode) return;
        
        Console.WriteLine("\n🔍 [DEBUG] 文本校正结果对比:");
        Console.WriteLine("============================================================");
        
        var maxCount = Math.Max(original.Count, corrected.Count);
        
        for (int i = 0; i < maxCount; i++)
        {
            Console.WriteLine($"段落 {i + 1}:");
            
            if (i < original.Count)
            {
                var orig = original[i];
                Console.WriteLine($"  原始: [{orig.StartTime:F2}s-{orig.EndTime:F2}s] \"{orig.Text}\"");
            }
            else
            {
                Console.WriteLine($"  原始: (无)");
            }
            
            if (i < corrected.Count)
            {
                var corr = corrected[i];
                Console.WriteLine($"  校正: [{corr.StartTime:F2}s-{corr.EndTime:F2}s] \"{corr.Text}\"");
            }
            else
            {
                Console.WriteLine($"  校正: (无)");
            }
            
            Console.WriteLine();
        }
        
        Console.WriteLine("============================================================");
    }

    private void ValidateInputFile(SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.InputAudioPath))
        {
            throw new ArgumentException("输入音频文件路径不能为空", nameof(config.InputAudioPath));
        }

        if (!File.Exists(config.InputAudioPath))
        {
            throw new FileNotFoundException($"音频文件不存在: {config.InputAudioPath}");
        }

        var fileInfo = new FileInfo(config.InputAudioPath);
        if (fileInfo.Length == 0)
        {
            throw new ArgumentException("音频文件为空", nameof(config.InputAudioPath));
        }

        Console.WriteLine($"✅ 输入文件验证通过: {config.InputAudioPath} ({fileInfo.Length / 1024:F1} KB)");
    }

    private void PrepareOutputDirectory(SplitterConfig config)
    {
        try
        {
            if (Directory.Exists(config.OutputDirectory))
            {
                Console.WriteLine($"📁 输出目录已存在: {config.OutputDirectory}");
            }
            else
            {
                Directory.CreateDirectory(config.OutputDirectory);
                Console.WriteLine($"📁 创建输出目录: {config.OutputDirectory}");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"无法创建输出目录 '{config.OutputDirectory}': {ex.Message}", ex);
        }
    }

    private void CleanupTemporaryFiles(string processedAudio, SplitterConfig config)
    {
        try
        {
            if (File.Exists(processedAudio))
            {
                if (config.KeepOriginalAudio)
                {
                    Console.WriteLine($"📁 保留临时转换文件: {processedAudio}");
                }
                else
                {
                    File.Delete(processedAudio);
                    Console.WriteLine($"🗑️ 清理临时文件: {processedAudio}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 清理临时文件时出现问题: {ex.Message}");
            // 不抛出异常，因为这不影响主要功能
        }
    }
}

/// <summary>
/// 音频分割器专用异常类
/// </summary>
public class AudioSplitterException : Exception
{
    public AudioSplitterException(string message) : base(message) { }
    public AudioSplitterException(string message, Exception innerException) : base(message, innerException) { }
}