using Whisper.net;
using NAudio.Wave;
using System.Text.Json;
using System.Text;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("🎤 音频句子自动切割系统");
            Console.WriteLine("===================================");
            Console.WriteLine("📝 功能: 将包含多个句子的音频文件自动切割成独立的句子音频文件");
            Console.WriteLine("🎯 示例: \"This is Marson! He's a bit naughty, but he is not a bad bird.\"");
            Console.WriteLine("   将被切割成:");
            Console.WriteLine("   📁 sentence_01_xxx_This_is_Marson.wav");
            Console.WriteLine("   📁 sentence_02_xxx_Hes_a_bit_naughty.wav");
            Console.WriteLine();

            var splitter = new AudioSplitter();

            // 🎛️ 针对"切断单词"问题的优化配置
            var config = new SplitterConfig
            {
                // 基本配置
                InputAudioPath = "temp_align.wav",
                OutputDirectory = "output_sentences",
                Language = "en", 
                ModelSize = "tiny",

                // ⚙️ 精度调整参数 - 针对切断单词问题优化
                SentenceBoundaryPadding = 0.4,         // 📈 增加到0.4秒，给单词更多缓冲时间
                TimeAllocationMode = "proportional",    // 按字符比例分配
                MinSentenceCharacters = 5,             // 📈 增加到15字符，避免短片段
                SilencePaddingAfterPunctuation = 0.3,   // 📈 标点后0.3秒静音
                EnableSmartBoundaryAdjustment = true,   // 启用智能调整
                WordBoundaryMode = "smart",             // 智能边界检测
                DebugMode = true,                       // 🔍 显示详细信息

                // 时长控制
                MaxSegmentDuration = 30.0,
                MinSegmentDuration = 1.0,
                WhisperMinSegmentLength = 2.0
            };

            Console.WriteLine("🚀 开始处理...");
            Console.WriteLine($"📏 边界填充: {config.SentenceBoundaryPadding}s");
            Console.WriteLine($"📝 最小字符: {config.MinSentenceCharacters}");
            Console.WriteLine($"🔇 标点静音: {config.SilencePaddingAfterPunctuation}s");
            Console.WriteLine();

            await splitter.ProcessAsync(config);
            
            Console.WriteLine();
            Console.WriteLine("🎉 处理完成！");
            Console.WriteLine($"📂 请查看 '{config.OutputDirectory}' 目录中的句子音频文件");
            Console.WriteLine();
            Console.WriteLine("💡 如果仍有切割问题，请调整参数:");
            Console.WriteLine("   - 增加 SentenceBoundaryPadding (当前 0.4s)");
            Console.WriteLine("   - 增加 SilencePaddingAfterPunctuation (当前 0.3s)");
            Console.WriteLine("   - 增加 MinSentenceCharacters (当前 15)");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("❌ 处理出错:");
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine($"详细信息: {ex}");
        }
        
        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}

public class AudioSplitter
{
    public async Task ProcessAsync(SplitterConfig config)
    {
        // 1. 检查输入文件
        if (!File.Exists(config.InputAudioPath))
        {
            throw new FileNotFoundException($"音频文件不存在: {config.InputAudioPath}");
        }

        // 2. 准备输出目录
        Directory.CreateDirectory(config.OutputDirectory);

        // 3. 转换音频格式
        string processedAudio = Path.Combine(config.OutputDirectory, "processed.wav");
        ConvertToWhisperFormat(config.InputAudioPath, processedAudio);

        // 4. 使用Whisper进行语音识别和时间对齐
        var segments = await PerformAlignment(processedAudio, config);

        // 5. 优化分割点
        var optimizedSegments = OptimizeSegments(segments, config);

        // 6. 切割音频文件
        await SplitAudioFiles(processedAudio, optimizedSegments, config);

        // 7. 生成结果报告
        GenerateReport(optimizedSegments, config);

        // 8. 清理临时文件
        if (File.Exists(processedAudio))
            File.Delete(processedAudio);
    }

    private void ConvertToWhisperFormat(string inputPath, string outputPath)
    {
        Console.WriteLine("转换音频格式...");

        try
        {
            // 删除已存在的输出文件
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            using var reader = new AudioFileReader(inputPath);
            
            // Whisper需要16kHz单声道PCM格式
            var targetFormat = new WaveFormat(16000, 16, 1); // 16kHz, 16-bit, mono

            Console.WriteLine($"原始格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
            Console.WriteLine($"原始编码: {reader.WaveFormat.Encoding}");
            Console.WriteLine($"目标格式: {targetFormat.SampleRate}Hz, {targetFormat.Channels}通道, {targetFormat.BitsPerSample}位");

            // 强制重新采样和格式转换
            using var resampler = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 60 // 高质量重采样
            };

            // 使用标准的WAV文件写入方法
            WaveFileWriter.CreateWaveFile(outputPath, resampler);

            Console.WriteLine($"音频已转换: {outputPath}");
            
            // 验证输出文件
            ValidateConvertedFile(outputPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"音频格式转换失败: {ex.Message}", ex);
        }
    }

    private void ValidateConvertedFile(string filePath)
    {
        try
        {
            using var reader = new WaveFileReader(filePath);
            Console.WriteLine($"转换后格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
            Console.WriteLine($"转换后编码: {reader.WaveFormat.Encoding}");
            Console.WriteLine($"时长: {reader.TotalTime.TotalSeconds:F2}秒");
            Console.WriteLine($"文件大小: {new FileInfo(filePath).Length / 1024:F2} KB");
            
            // 验证格式是否符合Whisper要求
            if (reader.WaveFormat.SampleRate != 16000)
            {
                throw new InvalidOperationException($"采样率不正确: {reader.WaveFormat.SampleRate}Hz (期望 16000Hz)");
            }
            
            if (reader.WaveFormat.Channels != 1)
            {
                throw new InvalidOperationException($"声道数不正确: {reader.WaveFormat.Channels} (期望 1)");
            }
            
            if (reader.WaveFormat.BitsPerSample != 16)
            {
                throw new InvalidOperationException($"位深度不正确: {reader.WaveFormat.BitsPerSample}位 (期望 16位)");
            }
            
            if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
            {
                throw new InvalidOperationException($"编码格式不正确: {reader.WaveFormat.Encoding} (期望 PCM)");
            }
            
            Console.WriteLine("✓ 音频格式验证通过");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"转换后文件验证失败: {ex.Message}", ex);
        }
    }

    private async Task<List<AudioSegment>> PerformAlignment(string audioPath, SplitterConfig config)
    {
        Console.WriteLine("执行语音识别和对齐...");

        var segments = new List<AudioSegment>();

        try
        {
            // 验证音频文件格式
            ValidateAudioFile(audioPath);

            // 获取或下载模型
            var modelPath = await GetOrDownloadModel(config.ModelSize);

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage(config.Language)
                .WithThreads(Environment.ProcessorCount)
                .Build();

            await using var fileStream = File.OpenRead(audioPath);

            Console.WriteLine("开始语音识别...");
            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                // result is SegmentData, process it directly
                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    var audioSegment = new AudioSegment
                    {
                        StartTime = result.Start.TotalSeconds,
                        EndTime = result.End.TotalSeconds,
                        Text = result.Text.Trim(),
                        Duration = (result.End - result.Start).TotalSeconds
                    };

                    segments.Add(audioSegment);
                    Console.WriteLine($"识别: [{audioSegment.StartTime:F2}s-{audioSegment.EndTime:F2}s] {audioSegment.Text}");
                }
            }
        }
        catch (Whisper.net.Wave.CorruptedWaveException ex)
        {
            Console.WriteLine($"WAV文件格式错误: {ex.Message}");
            Console.WriteLine("尝试重新处理音频文件...");
            
            // 尝试重新转换音频文件
            var backupPath = audioPath + ".fixed.wav";
            ConvertToWhisperFormat(audioPath, backupPath);
            
            // 递归调用，但要防止无限递归
            if (!audioPath.Contains(".fixed.wav"))
            {
                var backupSegments = await PerformAlignment(backupPath, config);
                
                // 清理备份文件
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                
                return backupSegments;
            }
            else
            {
                throw new InvalidOperationException($"无法处理音频文件格式，即使在重新转换后: {ex.Message}", ex);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"语音识别失败: {ex.Message}", ex);
        }

        Console.WriteLine($"识别完成，共 {segments.Count} 个片段");
        return segments;
    }

    private void ValidateAudioFile(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        Console.WriteLine($"处理音频文件: {audioPath}");
        Console.WriteLine($"格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
        Console.WriteLine($"时长: {reader.TotalTime.TotalSeconds:F2}秒");
        Console.WriteLine($"编码: {reader.WaveFormat.Encoding}");
        
        // 检查文件是否为空
        if (reader.TotalTime.TotalSeconds < 0.1)
        {
            throw new InvalidOperationException("音频文件时长过短或为空");
        }
        
        // 检查是否为支持的格式
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm && 
            reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            Console.WriteLine($"警告: 音频编码格式 {reader.WaveFormat.Encoding} 可能需要转换");
        }
    }

    private async Task<string> GetOrDownloadModel(string modelSize)
    {
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = Path.Combine("models", modelFileName);

        if (File.Exists(modelPath))
        {
            Console.WriteLine($"使用现有模型: {modelPath}");
            return modelPath;
        }

        // 创建模型目录
        Directory.CreateDirectory("models");

        Console.WriteLine($"首次运行，正在下载模型 {modelSize}...");
        Console.WriteLine("请稍等，模型较大可能需要几分钟时间...");

        // 使用Whisper.net的内置下载功能
        using var httpClient = new HttpClient();
        var modelUrl = GetModelDownloadUrl(modelSize);

        var response = await httpClient.GetAsync(modelUrl);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(modelPath);
        await response.Content.CopyToAsync(fileStream);

        Console.WriteLine($"模型下载完成: {modelPath}");
        return modelPath;
    }

    private string GetModelDownloadUrl(string modelSize)
    {
        var baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
        return $"{baseUrl}/ggml-{modelSize}.bin";
    }

    private List<AudioSegment> OptimizeSegments(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("根据标点符号优化音频分割点...");

        var optimized = new List<AudioSegment>();
        var currentSentenceParts = new List<AudioSegment>();

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
                continue;

            // 检查当前segment是否包含句子结束符号
            var sentences = SplitTextBySentenceEnding(segment.Text, segment, config);
            
            foreach (var sentence in sentences)
            {
                currentSentenceParts.Add(sentence);
                
                // 如果这个部分以句子结束符号结尾，就创建一个完整的句子段
                if (IsNaturalBreakPoint(sentence.Text))
                {
                    if (currentSentenceParts.Count > 0)
                    {
                        var completeSentence = CombineSegments(currentSentenceParts);
                        
                        // 检查时长是否符合要求
                        if (completeSentence.Duration >= config.MinSegmentDuration)
                        {
                            optimized.Add(completeSentence);
                            Console.WriteLine($"✅ 创建句子段 {optimized.Count}: [{completeSentence.StartTime:F2}s-{completeSentence.EndTime:F2}s] ({completeSentence.Duration:F2}s)");
                            Console.WriteLine($"   内容: \"{completeSentence.Text}\"");
                        }
                        else
                        {
                            // 太短的句子与前一个合并
                            if (optimized.Count > 0)
                            {
                                var lastSegment = optimized[^1];
                                var mergedSegment = MergeSegments(lastSegment, completeSentence, config);
                                optimized[^1] = mergedSegment;
                                Console.WriteLine($"📎 合并短句子到段 {optimized.Count}: \"{mergedSegment.Text}\"");
                            }
                            else
                            {
                                optimized.Add(completeSentence);
                                Console.WriteLine($"✅ 创建短句子段 {optimized.Count}: \"{completeSentence.Text}\"");
                            }
                        }
                        
                        // 重置当前句子部分
                        currentSentenceParts.Clear();
                    }
                }
            }
        }

        // 处理最后剩余的部分
        if (currentSentenceParts.Count > 0)
        {
            var finalSegment = CombineSegments(currentSentenceParts);
            
            if (finalSegment.Duration >= config.MinSegmentDuration)
            {
                optimized.Add(finalSegment);
                Console.WriteLine($"✅ 创建最终段 {optimized.Count}: [{finalSegment.StartTime:F2}s-{finalSegment.EndTime:F2}s]");
                Console.WriteLine($"   内容: \"{finalSegment.Text}\"");
            }
            else if (optimized.Count > 0)
            {
                // 与前一个段合并
                var lastSegment = optimized[^1];
                var mergedSegment = MergeSegments(lastSegment, finalSegment, config);
                optimized[^1] = mergedSegment;
                Console.WriteLine($"📎 合并最终段: \"{mergedSegment.Text}\"");
            }
            else
            {
                optimized.Add(finalSegment);
                Console.WriteLine($"✅ 创建唯一最终段: \"{finalSegment.Text}\"");
            }
        }

        Console.WriteLine($"\n🎯 优化完成！共创建 {optimized.Count} 个句子音频段:");
        for (int i = 0; i < optimized.Count; i++)
        {
            var seg = optimized[i];
            Console.WriteLine($"   {i + 1}. [{seg.StartTime:F2}s-{seg.EndTime:F2}s] ({seg.Duration:F2}s) \"{seg.Text}\"");
        }
        
        return optimized;
    }

    private List<AudioSegment> SplitTextBySentenceEnding(string text, AudioSegment originalSegment, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        var sentences = ExtractSentencesWithPositions(text, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"🔍 [DEBUG] 分析文本: \"{text}\"");
            Console.WriteLine($"🔍 [DEBUG] 检测到 {sentences.Count} 个句子片段");
        }
        
        // 如果只有一个句子，直接返回原segment
        if (sentences.Count <= 1)
        {
            if (config.DebugMode)
                Console.WriteLine($"🔍 [DEBUG] 单句子，直接返回原始段落");
            return new List<AudioSegment> { originalSegment };
        }
        
        // 智能时间分配
        var timeAllocatedSegments = AllocateTimeToSentences(sentences, originalSegment, config);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"🔍 [DEBUG] 时间分配结果:");
            for (int i = 0; i < timeAllocatedSegments.Count; i++)
            {
                var seg = timeAllocatedSegments[i];
                Console.WriteLine($"   {i+1}. [{seg.StartTime:F3}s-{seg.EndTime:F3}s] ({seg.Duration:F3}s) \"{seg.Text}\"");
            }
        }
        
        return timeAllocatedSegments;
    }

    private List<SentenceInfo> ExtractSentencesWithPositions(string text, SplitterConfig config)
    {
        var sentences = new List<SentenceInfo>();
        var currentSentence = new StringBuilder();
        var startPos = 0;
        
        for (int i = 0; i < text.Length; i++)
        {
            currentSentence.Append(text[i]);
            
            // 检查是否是句子结束符
            if (IsSentenceEndingChar(text[i]))
            {
                var sentenceText = currentSentence.ToString().Trim();
                
                // 应用最小字符数过滤
                if (sentenceText.Length >= config.MinSentenceCharacters)
                {
                    sentences.Add(new SentenceInfo
                    {
                        Text = sentenceText,
                        StartPosition = startPos,
                        EndPosition = i,
                        CharacterLength = sentenceText.Length
                    });
                    
                    startPos = i + 1;
                    currentSentence.Clear();
                }
                // 太短的句子继续累积
            }
        }
        
        // 处理剩余部分
        if (currentSentence.Length > 0)
        {
            var remainingText = currentSentence.ToString().Trim();
            if (remainingText.Length >= config.MinSentenceCharacters)
            {
                sentences.Add(new SentenceInfo
                {
                    Text = remainingText,
                    StartPosition = startPos,
                    EndPosition = text.Length - 1,
                    CharacterLength = remainingText.Length
                });
            }
            else if (sentences.Count > 0)
            {
                // 太短的尾部文本合并到最后一个句子
                var lastSentence = sentences[^1];
                lastSentence.Text += " " + remainingText;
                lastSentence.EndPosition = text.Length - 1;
                lastSentence.CharacterLength = lastSentence.Text.Length;
            }
        }
        
        return sentences;
    }

    private List<AudioSegment> AllocateTimeToSentences(List<SentenceInfo> sentences, AudioSegment original, SplitterConfig config)
    {
        var result = new List<AudioSegment>();
        
        if (sentences.Count == 0) return result;
        if (sentences.Count == 1)
        {
            return new List<AudioSegment> { original };
        }
        
        double totalDuration = original.Duration;
        double currentTime = original.StartTime;
        
        // 预留边界调整时间
        double reservedPadding = config.SentenceBoundaryPadding * sentences.Count;
        double availableDuration = Math.Max(totalDuration - reservedPadding, totalDuration * 0.8);
        
        if (config.DebugMode)
        {
            Console.WriteLine($"🔍 [DEBUG] 时间分配策略: {config.TimeAllocationMode}");
            Console.WriteLine($"🔍 [DEBUG] 总时长: {totalDuration:F3}s, 可用时长: {availableDuration:F3}s, 预留填充: {reservedPadding:F3}s");
        }
        
        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            double duration;
            
            // 根据配置选择时间分配方式
            if (config.TimeAllocationMode == "equal")
            {
                duration = availableDuration / sentences.Count;
            }
            else // proportional
            {
                int totalChars = sentences.Sum(s => s.CharacterLength);
                double proportion = (double)sentence.CharacterLength / totalChars;
                duration = availableDuration * proportion;
            }
            
            // 应用边界填充
            if (config.EnableSmartBoundaryAdjustment)
            {
                // 句子开始前的填充
                if (i > 0)
                {
                    currentTime += config.SentenceBoundaryPadding / 2;
                }
                
                // 句子结束后的填充（如果有标点符号）
                if (IsNaturalBreakPoint(sentence.Text))
                {
                    duration += config.SilencePaddingAfterPunctuation;
                }
            }
            
            // 确保最后一个句子的结束时间正确
            double endTime = currentTime + duration;
            if (i == sentences.Count - 1)
            {
                endTime = original.EndTime;
                duration = endTime - currentTime;
            }
            
            var segment = new AudioSegment
            {
                StartTime = currentTime,
                EndTime = endTime,
                Duration = duration,
                Text = sentence.Text
            };
            
            result.Add(segment);
            
            if (config.DebugMode)
            {
                Console.WriteLine($"🔍 [DEBUG] 句子 {i+1}: \"{sentence.Text}\" -> [{currentTime:F3}s-{endTime:F3}s] ({duration:F3}s)");
            }
            
            currentTime = endTime;
        }
        
        return result;
    }

    // 辅助数据结构
    private class SentenceInfo
    {
        public string Text { get; set; } = "";
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public int CharacterLength { get; set; }
    }

    private bool IsSentenceEndingChar(char c)
    {
        return c == '.' || c == '!' || c == '?' || c == ';' || 
               c == '。' || c == '！' || c == '？' || c == '；';
    }

    private bool IsNaturalBreakPoint(string text)
    {
        // 更精确的句子结束符号检测
        var breakPoints = new[] { ".", "!", "?", ";", "。", "！", "？", "；" };
        var trimmedText = text.Trim();
        
        // 检查文本是否以句子结束符号结尾
        return breakPoints.Any(bp => trimmedText.EndsWith(bp));
    }

    private AudioSegment CombineSegments(List<AudioSegment> segments)
    {
        if (segments.Count == 0)
            throw new ArgumentException("segments cannot be empty");

        if (segments.Count == 1)
            return segments[0];

        return new AudioSegment
        {
            StartTime = segments[0].StartTime,
            EndTime = segments[^1].EndTime,
            Duration = segments[^1].EndTime - segments[0].StartTime,
            Text = string.Join(" ", segments.Select(s => s.Text.Trim())).Trim()
        };
    }

    private AudioSegment MergeSegments(AudioSegment segment1, AudioSegment segment2, SplitterConfig config)
    {
        var merged = new AudioSegment
        {
            StartTime = segment1.StartTime,
            EndTime = segment2.EndTime,
            Duration = segment2.EndTime - segment1.StartTime,
            Text = (segment1.Text + " " + segment2.Text).Trim()
        };
        
        return merged;
    }

    private async Task SplitAudioFiles(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("🔪 开始切割音频文件...");

        using var reader = new AudioFileReader(sourceAudio);
        var format = reader.WaveFormat;

        Console.WriteLine($"源音频格式: {format.SampleRate}Hz, {format.Channels}通道, {format.BitsPerSample}位");
        Console.WriteLine($"源音频时长: {reader.TotalTime.TotalSeconds:F2}秒");

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            
            // 创建更清晰的文件名
            var cleanText = CleanTextForFilename(segment.Text);
            var outputFileName = $"sentence_{i + 1:D2}_{segment.StartTime:F1}s-{segment.EndTime:F1}s_{cleanText}.wav";
            var outputPath = Path.Combine(config.OutputDirectory, outputFileName);

            try
            {
                Console.WriteLine($"\n🎵 切割句子 {i + 1}/{segments.Count}:");
                Console.WriteLine($"   时间: {segment.StartTime:F2}s - {segment.EndTime:F2}s ({segment.Duration:F2}s)");
                Console.WriteLine($"   内容: \"{segment.Text}\"");
                Console.WriteLine($"   文件: {outputFileName}");

                // 计算精确的采样位置
                var startSample = (long)(segment.StartTime * format.SampleRate);
                var endSample = (long)(segment.EndTime * format.SampleRate);
                var sampleCount = endSample - startSample;

                // 计算字节位置
                var bytesPerSample = format.BitsPerSample / 8 * format.Channels;
                var startByte = startSample * bytesPerSample;
                var totalBytes = sampleCount * bytesPerSample;

                Console.WriteLine($"   采样范围: {startSample} - {endSample} ({sampleCount} samples)");
                Console.WriteLine($"   字节范围: {startByte} - {startByte + totalBytes} ({totalBytes} bytes)");

                // 设置读取位置
                reader.Position = startByte;

                // 创建输出文件
                using var writer = new WaveFileWriter(outputPath, format);
                
                // 使用较小的缓冲区以获得更好的精度
                var bufferSize = Math.Min(format.AverageBytesPerSecond / 4, (int)totalBytes); // 0.25秒或实际需要的大小
                var buffer = new byte[bufferSize];
                var bytesRead = 0L;

                while (bytesRead < totalBytes)
                {
                    var bytesToRead = (int)Math.Min(buffer.Length, totalBytes - bytesRead);
                    var actualBytesRead = reader.Read(buffer, 0, bytesToRead);

                    if (actualBytesRead == 0) 
                    {
                        Console.WriteLine($"   ⚠️ 提前结束读取，已读取 {bytesRead} / {totalBytes} 字节");
                        break;
                    }

                    writer.Write(buffer, 0, actualBytesRead);
                    bytesRead += actualBytesRead;
                }

                // 更新段信息
                segment.OutputFileName = outputFileName;
                segment.OutputPath = outputPath;

                // 验证生成的文件
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"   ✅ 已生成: {fileInfo.Length / 1024:F1} KB");

                // 验证音频文件时长
                using var verifyReader = new AudioFileReader(outputPath);
                var actualDuration = verifyReader.TotalTime.TotalSeconds;
                Console.WriteLine($"   📏 实际时长: {actualDuration:F2}s (期望: {segment.Duration:F2}s)");
                
                if (Math.Abs(actualDuration - segment.Duration) > 0.1)
                {
                    Console.WriteLine($"   ⚠️ 时长差异较大: {Math.Abs(actualDuration - segment.Duration):F2}s");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ 切割片段 {i + 1} 时出错: {ex.Message}");
            }
        }

        Console.WriteLine($"\n🎉 音频切割完成！共生成 {segments.Count} 个句子音频文件");
    }

    private void GenerateReport(List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("📊 生成结果报告...");

        var report = new
        {
            Config = config,
            TotalSegments = segments.Count,
            TotalDuration = Math.Round(segments.Sum(s => s.Duration), 2),
            ProcessedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Segments = segments.Select((s, i) => new
            {
                Index = i + 1,
                StartTime = Math.Round(s.StartTime, 2),
                EndTime = Math.Round(s.EndTime, 2),
                Duration = Math.Round(s.Duration, 2),
                Text = s.Text,
                OutputFileName = s.OutputFileName
            })
        };

        var jsonPath = Path.Combine(config.OutputDirectory, "sentence_split_report.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions));

        // 生成文本清单
        var textListPath = Path.Combine(config.OutputDirectory, "sentence_list.txt");
        var textLines = segments.Select((s, i) =>
            $"{i + 1:D2}. [{s.StartTime:F2}s-{s.EndTime:F2}s] ({s.Duration:F2}s) {s.OutputFileName}\n    📝 \"{s.Text}\"");
        File.WriteAllLines(textListPath, textLines);

        Console.WriteLine($"📋 报告已生成:");
        Console.WriteLine($"   📄 JSON报告: {jsonPath}");
        Console.WriteLine($"   📝 句子清单: {textListPath}");
        Console.WriteLine($"   🎵 句子音频: {segments.Count} 个文件");
        
        Console.WriteLine($"\n📈 处理统计:");
        Console.WriteLine($"   总句子数: {segments.Count}");
        Console.WriteLine($"   总时长: {segments.Sum(s => s.Duration):F2} 秒");
        Console.WriteLine($"   平均句长: {segments.Average(s => s.Duration):F2} 秒");
        Console.WriteLine($"   最短句子: {segments.Min(s => s.Duration):F2} 秒");
        Console.WriteLine($"   最长句子: {segments.Max(s => s.Duration):F2} 秒");
    }

    private string CleanTextForFilename(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";

        // 取前20个字符，移除不安全的文件名字符
        var cleaned = text.Length > 20 ? text.Substring(0, 20) : text;
        
        // 移除或替换不安全的字符
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            cleaned = cleaned.Replace(c, '_');
        }
        
        // 移除标点符号并替换空格
        cleaned = cleaned.Replace(" ", "_")
                        .Replace(".", "")
                        .Replace("!", "")
                        .Replace("?", "")
                        .Replace(";", "")
                        .Replace(",", "")
                        .Replace("'", "")
                        .Replace("\"", "");
        
        return string.IsNullOrWhiteSpace(cleaned) ? "segment" : cleaned;
    }
}

public class AudioSegment
{
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public double Duration { get; set; }
    public string Text { get; set; } = "";
    public string OutputFileName { get; set; } = "";
    public string OutputPath { get; set; } = "";
}

public class SplitterConfig
{
    // 基本配置
    public string InputAudioPath { get; set; } = "";
    public string OutputDirectory { get; set; } = "output_segments";
    public string Language { get; set; } = "zh";
    public string ModelSize { get; set; } = "tiny"; // tiny, base, small, medium, large

    // 时长控制参数
    public double MaxSegmentDuration { get; set; } = 30.0;
    public double MinSegmentDuration { get; set; } = 1.0;

    // 🎯 切割精度调整参数 (新增)
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
}