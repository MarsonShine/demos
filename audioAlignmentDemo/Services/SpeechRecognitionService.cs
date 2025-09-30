using Whisper.net;
using NAudio.Wave;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// 语音识别和对齐服务
/// 使用Whisper模型进行语音识别并校正时间对齐
/// </summary>
public class SpeechRecognitionService
{
    /// <summary>
    /// 执行语音识别和时间对齐
    /// </summary>
    public async Task<List<AudioSegment>> PerformAlignmentAsync(string audioPath, SplitterConfig config)
    {
        Console.WriteLine("🎯 执行高精度语音识别和对齐...");

        var segments = new List<AudioSegment>();

        try
        {
            // 验证音频文件格式
            ValidateAudioFile(audioPath);

            // 获取或下载模型
            var modelPath = await GetOrDownloadModelAsync(config.ModelSize);

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            
            // 🔧 优化Whisper配置以获得更好的分割效果
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage(config.Language)
                .WithThreads(Environment.ProcessorCount)
                //.WithSegmentEventHandler()
                .WithTokenTimestamps()
                .WithProbabilities()
                //.SplitOnWord()                    // 降低随机性，提高一致性
                .Build();

            await using var fileStream = File.OpenRead(audioPath);

            Console.WriteLine("🎤 开始语音识别...");
            
            // 获取音频文件实际时长用于校正
            double actualAudioDuration = GetActualAudioDuration(audioPath);

            // 处理识别结果
            await foreach (var result in processor.ProcessAsync(fileStream))
            {
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
                    Console.WriteLine($"🎤 识别段落: [{audioSegment.StartTime:F3}s-{audioSegment.EndTime:F3}s] ({audioSegment.Duration:F3}s) \"{audioSegment.Text}\"");
                }
            }

            Console.WriteLine($"📊 Whisper初始识别结果: {segments.Count} 个段落");

            // 🆕 多级分割策略：如果识别结果不理想，尝试不同的分割方法
            if (ShouldTryAdvancedSplitting(segments, config))
            {
                Console.WriteLine("🔍 应用多级分割策略以获得更好的分割效果...");
                segments = await ApplyAdvancedSplittingStrategies(audioPath, segments, config);
            }

            // 智能时间校正：处理 Whisper 识别时长与实际音频时长不匹配的问题
            if (segments.Count > 0 && actualAudioDuration > 0 && config.EnableTimeCorrection)
            {
                PerformTimeCorrection(segments, actualAudioDuration, config);
            }

            // 最终优化段落边界
            segments = OptimizeSegmentBoundaries(segments, config);
        }
        catch (Whisper.net.Wave.CorruptedWaveException ex)
        {
            return await HandleCorruptedWaveFile(audioPath, config, ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"语音识别失败: {ex.Message}", ex);
        }

        Console.WriteLine($"✅ 识别完成，共 {segments.Count} 个片段");
        DisplaySegmentSummary(segments);
        
        return segments;
    }

    /// <summary>
    /// 判断是否需要尝试高级分割策略
    /// </summary>
    private bool ShouldTryAdvancedSplitting(List<AudioSegment> segments, SplitterConfig config)
    {
        // 如果只有一个长段落，肯定需要进一步分割
        if (segments.Count == 1 && segments[0].Duration > config.WhisperMinSegmentLength * 2)
        {
            return true;
        }

        // 如果段落数量太少且平均时长过长，也需要分割
        if (segments.Count > 0 && segments.Count < 3 && segments.Average(s => s.Duration) > config.MaxSegmentDuration)
        {
            return true;
        }

        // 如果所有段落都很长，也需要分割
        if (segments.Count > 0 && segments.All(s => s.Duration > config.MaxSegmentDuration))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 应用多级分割策略
    /// </summary>
    private async Task<List<AudioSegment>> ApplyAdvancedSplittingStrategies(string audioPath, List<AudioSegment> initialSegments, SplitterConfig config)
    {
        var bestSegments = initialSegments;
        
        // 策略1: 尝试不同的Whisper配置
        Console.WriteLine("🔬 策略1: 尝试不同的Whisper配置...");
        var whisperVariants = await TryDifferentWhisperConfigs(audioPath, config);
        if (whisperVariants.Count > bestSegments.Count)
        {
            Console.WriteLine($"✅ Whisper配置优化成功: {bestSegments.Count} → {whisperVariants.Count} 个段落");
            bestSegments = whisperVariants;
        }

        // 策略2: 对长段落进行静音检测分割
        Console.WriteLine("🔍 策略2: 静音检测分割...");
        var silenceBasedSegments = new List<AudioSegment>();
        foreach (var segment in bestSegments)
        {
            if (segment.Duration > config.MaxSegmentDuration)
            {
                var splitSegments = DetectSilenceBasedSegments(audioPath, segment, config);
                silenceBasedSegments.AddRange(splitSegments);
            }
            else
            {
                silenceBasedSegments.Add(segment);
            }
        }
        
        if (silenceBasedSegments.Count > bestSegments.Count)
        {
            Console.WriteLine($"✅ 静音检测分割成功: {bestSegments.Count} → {silenceBasedSegments.Count} 个段落");
            bestSegments = silenceBasedSegments;
        }

        // 策略3: 智能固定时长分割（最后的备选方案）
        if (bestSegments.Any(s => s.Duration > config.MaxSegmentDuration))
        {
            Console.WriteLine("⏱ 策略3: 智能固定时长分割...");
            var finalSegments = new List<AudioSegment>();
            foreach (var segment in bestSegments)
            {
                if (segment.Duration > config.MaxSegmentDuration)
                {
                    var splitSegments = PerformIntelligentFixedSplit(segment, config);
                    finalSegments.AddRange(splitSegments);
                }
                else
                {
                    finalSegments.Add(segment);
                }
            }
            bestSegments = finalSegments;
        }

        return bestSegments;
    }

    /// <summary>
    /// 尝试不同的Whisper配置
    /// </summary>
    private async Task<List<AudioSegment>> TryDifferentWhisperConfigs(string audioPath, SplitterConfig config)
    {
        var bestSegments = new List<AudioSegment>();
        var temperatures = new float[] { 0.1f, 0.3f, 0.5f };

        foreach (var temp in temperatures)
        {
            try
            {
                Console.WriteLine($"🌡️ 尝试温度参数: {temp}");
                
                var modelPath = await GetOrDownloadModelAsync(config.ModelSize);
                using var whisperFactory = WhisperFactory.FromPath(modelPath);
                
                using var processor = whisperFactory.CreateBuilder()
                    .WithLanguage(config.Language)
                    .WithThreads(Environment.ProcessorCount)
                    .WithTemperature(temp)
                    .Build();

                await using var fileStream = File.OpenRead(audioPath);
                var segments = new List<AudioSegment>();
                
                await foreach (var result in processor.ProcessAsync(fileStream))
                {
                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        segments.Add(new AudioSegment
                        {
                            StartTime = result.Start.TotalSeconds,
                            EndTime = result.End.TotalSeconds,
                            Text = result.Text.Trim(),
                            Duration = (result.End - result.Start).TotalSeconds
                        });
                    }
                }
                
                if (segments.Count > bestSegments.Count)
                {
                    bestSegments = segments;
                    Console.WriteLine($"   ✅ 温度 {temp} 产生更好结果: {segments.Count} 个段落");
                }
                else
                {
                    Console.WriteLine($"   📊 温度 {temp} 结果: {segments.Count} 个段落");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠ 温度 {temp} 配置失败: {ex.Message}");
            }
        }

        return bestSegments;
    }

    // 保持原有的静音检测方法，但简化实现...
    private List<AudioSegment> DetectSilenceBasedSegments(string audioPath, AudioSegment originalSegment, SplitterConfig config)
    {
        try
        {
            Console.WriteLine($"🔊 对段落进行静音检测分割: [{originalSegment.StartTime:F2}s-{originalSegment.EndTime:F2}s]");
            
            using var reader = new AudioFileReader(audioPath);
            var startSample = (long)(originalSegment.StartTime * reader.WaveFormat.SampleRate);
            var endSample = (long)(originalSegment.EndTime * reader.WaveFormat.SampleRate);
            var sampleCount = endSample - startSample;
            
            if (sampleCount <= 0) return new List<AudioSegment> { originalSegment };
            
            // 读取指定范围的音频数据
            reader.Position = startSample * reader.WaveFormat.BlockAlign;
            var samples = new float[sampleCount];
            var actualRead = reader.Read(samples, 0, (int)Math.Min(sampleCount, samples.Length));
            
            if (actualRead < samples.Length) 
            {
                Array.Resize(ref samples, actualRead);
            }

            // 简化的静音检测
            var silenceThreshold = CalculateOptimalThreshold(samples);
            var minSilenceDuration = 0.3; // 300ms
            var sampleRate = reader.WaveFormat.SampleRate;
            var minSilenceSamples = (int)(minSilenceDuration * sampleRate);
            
            var silencePositions = FindSilencePositions(samples, silenceThreshold, minSilenceSamples, sampleRate);
            
            if (silencePositions.Count > 0)
            {
                return CreateSegmentsFromPositions(silencePositions, originalSegment, sampleRate);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 静音检测失败: {ex.Message}");
        }
        
        return new List<AudioSegment> { originalSegment };
    }

    private float CalculateOptimalThreshold(float[] samples)
    {
        if (samples.Length == 0) return 0.01f;
        
        var amplitudes = samples.Select(Math.Abs).OrderBy(x => x).ToArray();
        var median = amplitudes[amplitudes.Length / 2];
        var percentile20 = amplitudes[(int)(amplitudes.Length * 0.2)];
        
        return Math.Max(percentile20, median * 0.15f);
    }

    private List<int> FindSilencePositions(float[] samples, float threshold, int minSilenceSamples, int sampleRate)
    {
        var positions = new List<int>();
        var windowSize = sampleRate / 20; // 50ms窗口
        int silenceStart = -1;
        
        for (int i = 0; i < samples.Length - windowSize; i += windowSize / 4)
        {
            // 计算窗口内的平均音量
            var windowVolume = 0f;
            for (int j = 0; j < windowSize && i + j < samples.Length; j++)
            {
                windowVolume += Math.Abs(samples[i + j]);
            }
            windowVolume /= windowSize;
            
            if (windowVolume < threshold)
            {
                if (silenceStart == -1) silenceStart = i;
            }
            else
            {
                if (silenceStart != -1 && (i - silenceStart) > minSilenceSamples)
                {
                    positions.Add(silenceStart + (i - silenceStart) / 2); // 静音中点
                }
                silenceStart = -1;
            }
        }
        
        return positions;
    }

    private List<AudioSegment> CreateSegmentsFromPositions(List<int> positions, AudioSegment original, int sampleRate)
    {
        var segments = new List<AudioSegment>();
        var words = original.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        double currentStart = original.StartTime;
        int wordIndex = 0;
        
        foreach (var pos in positions)
        {
            double splitTime = original.StartTime + (double)pos / sampleRate;
            
            if (splitTime - currentStart >= 1.0) // 至少1秒
            {
                int wordsForSegment = Math.Max(1, (int)Math.Round((double)words.Length * (splitTime - currentStart) / original.Duration));
                wordsForSegment = Math.Min(wordsForSegment, words.Length - wordIndex);
                
                if (wordsForSegment > 0)
                {
                    var segmentWords = words.Skip(wordIndex).Take(wordsForSegment);
                    segments.Add(new AudioSegment
                    {
                        StartTime = currentStart,
                        EndTime = splitTime,
                        Duration = splitTime - currentStart,
                        Text = string.Join(" ", segmentWords)
                    });
                    
                    currentStart = splitTime;
                    wordIndex += wordsForSegment;
                }
            }
        }
        
        // 添加最后一段
        if (wordIndex < words.Length)
        {
            var remainingWords = words.Skip(wordIndex);
            segments.Add(new AudioSegment
            {
                StartTime = currentStart,
                EndTime = original.EndTime,
                Duration = original.EndTime - currentStart,
                Text = string.Join(" ", remainingWords)
            });
        }
        
        Console.WriteLine($"   ✂️ 静音检测分割: {segments.Count} 个新段落");
        return segments.Count > 1 ? segments : new List<AudioSegment> { original };
    }

    /// <summary>
    /// 智能固定时长分割
    /// </summary>
    private List<AudioSegment> PerformIntelligentFixedSplit(AudioSegment segment, SplitterConfig config)
    {
        var targetDuration = config.MaxSegmentDuration * 0.8; // 稍微小于最大时长
        var segmentCount = Math.Max(2, (int)Math.Ceiling(segment.Duration / targetDuration));
        
        var segments = new List<AudioSegment>();
        var words = segment.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var segmentDuration = segment.Duration / segmentCount;
        
        Console.WriteLine($"⏱ 智能固定分割: {segment.Duration:F1}s → {segmentCount}段 (每段约{segmentDuration:F1}s)");
        
        for (int i = 0; i < segmentCount; i++)
        {
            var startTime = segment.StartTime + i * segmentDuration;
            var endTime = (i == segmentCount - 1) ? segment.EndTime : startTime + segmentDuration;
            
            var wordStart = (int)Math.Round((double)words.Length * i / segmentCount);
            var wordEnd = (i == segmentCount - 1) ? words.Length : (int)Math.Round((double)words.Length * (i + 1) / segmentCount);
            var segmentWords = words.Skip(wordStart).Take(wordEnd - wordStart);
            
            if (segmentWords.Any())
            {
                segments.Add(new AudioSegment
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    Duration = endTime - startTime,
                    Text = string.Join(" ", segmentWords)
                });
            }
        }
        
        return segments;
    }

    /// <summary>
    /// 优化段落边界 - 修复时间间隙和重叠问题
    /// </summary>
    private List<AudioSegment> OptimizeSegmentBoundaries(List<AudioSegment> segments, SplitterConfig config)
    {
        if (segments.Count <= 1) return segments;
        
        Console.WriteLine("🔧 优化段落边界...");
        int optimizations = 0;
        
        for (int i = 0; i < segments.Count - 1; i++)
        {
            var current = segments[i];
            var next = segments[i + 1];
            
            // 修复重叠
            if (current.EndTime > next.StartTime)
            {
                double midPoint = (current.EndTime + next.StartTime) / 2;
                current.EndTime = midPoint;
                current.Duration = current.EndTime - current.StartTime;
                next.StartTime = midPoint;
                next.Duration = next.EndTime - next.StartTime;
                optimizations++;
                
                if (config.DebugMode)
                {
                    Console.WriteLine($"   🔧 修复重叠: 段落{i+1}-{i+2} 在{midPoint:F3}s处分割");
                }
            }
            // 🆕 修复间隙 - 降低阈值并确保无缝连接
            else if (current.EndTime < next.StartTime)
            {
                double gap = next.StartTime - current.EndTime;
                
                // 对于任何间隙都进行处理，确保完美对接
                if (gap > 0.001) // 只要大于1毫秒就处理
                {
                    // 🎯 关键修复：确保下一段的开始时间 = 当前段的结束时间
                    next.StartTime = current.EndTime;
                    next.Duration = next.EndTime - next.StartTime;
                    optimizations++;
                    
                    if (config.DebugMode)
                    {
                        Console.WriteLine($"   🔧 消除间隙: 段落{i+2} 开始时间调整 {gap:F3}s (从{next.StartTime + gap:F3}s调整到{next.StartTime:F3}s)");
                    }
                }
            }
        }
        
        // 🆕 最终验证：确保所有边界都完美对接
        if (config.DebugMode && optimizations > 0)
        {
            Console.WriteLine("\n🔍 边界优化后验证:");
            for (int i = 0; i < segments.Count - 1; i++)
            {
                var current = segments[i];
                var next = segments[i + 1];
                var gap = next.StartTime - current.EndTime;
                
                if (Math.Abs(gap) > 0.001)
                {
                    Console.WriteLine($"   ⚠️  段落{i+1}-{i+2} 仍有间隙: {gap:F3}s");
                }
                else
                {
                    Console.WriteLine($"   ✅ 段落{i+1}-{i+2} 完美对接: [{current.StartTime:F3}s-{current.EndTime:F3}s] → [{next.StartTime:F3}s-{next.EndTime:F3}s]");
                }
            }
        }
        
        if (optimizations > 0)
        {
            Console.WriteLine($"   ✅ 完成 {optimizations} 项边界优化");
        }
        else
        {
            Console.WriteLine($"   ✅ 段落边界已经完美对接，无需优化");
        }
        
        return segments;
    }

    /// <summary>
    /// 显示段落摘要信息
    /// </summary>
    private void DisplaySegmentSummary(List<AudioSegment> segments)
    {
        if (segments.Count == 0) return;
        
        Console.WriteLine("\n📊 识别结果摘要:");
        Console.WriteLine($"   段落数量: {segments.Count}");
        Console.WriteLine($"   总时长: {segments.Max(s => s.EndTime):F2}s");
        Console.WriteLine($"   平均段落时长: {segments.Average(s => s.Duration):F2}s");
        Console.WriteLine($"   最短段落: {segments.Min(s => s.Duration):F2}s");
        Console.WriteLine($"   最长段落: {segments.Max(s => s.Duration):F2}s");
        
        if (segments.Count <= 8 && segments.Count > 0)
        {
            Console.WriteLine("   段落详情:");
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var preview = seg.Text.Length > 50 ? seg.Text.Substring(0, 50) + "..." : seg.Text;
                Console.WriteLine($"     {i+1}. [{seg.StartTime:F2}s-{seg.EndTime:F2}s] \"{preview}\"");
            }
        }
        else if (segments.Count > 8)
        {
            Console.WriteLine($"   (显示前3个和后3个段落)");
            for (int i = 0; i < 3; i++)
            {
                var seg = segments[i];
                var preview = seg.Text.Length > 40 ? seg.Text.Substring(0, 40) + "..." : seg.Text;
                Console.WriteLine($"     {i+1}. [{seg.StartTime:F2}s-{seg.EndTime:F2}s] \"{preview}\"");
            }
            Console.WriteLine("     ...");
            for (int i = segments.Count - 3; i < segments.Count; i++)
            {
                var seg = segments[i];
                var preview = seg.Text.Length > 40 ? seg.Text.Substring(0, 40) + "..." : seg.Text;
                Console.WriteLine($"     {i+1}. [{seg.StartTime:F2}s-{seg.EndTime:F2}s] \"{preview}\"");
            }
        }
    }

    // 保持原有的辅助方法...
    private double GetActualAudioDuration(string audioPath)
    {
        try
        {
            using var audioReader = new AudioFileReader(audioPath);
            var duration = audioReader.TotalTime.TotalSeconds;
            Console.WriteLine($"🎵 音频文件实际时长: {duration:F3}秒");
            return duration;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ 无法获取音频实际时长: {ex.Message}");
            return 0;
        }
    }

    private void PerformTimeCorrection(List<AudioSegment> segments, double actualAudioDuration, SplitterConfig config)
    {
        var whisperTotalDuration = segments.Max(s => s.EndTime);
        var timeDifference = actualAudioDuration - whisperTotalDuration;
        
        Console.WriteLine($"⏰ 时长对比分析:");
        Console.WriteLine($"   Whisper识别时长: {whisperTotalDuration:F3}秒");
        Console.WriteLine($"   音频实际时长: {actualAudioDuration:F3}秒");
        Console.WriteLine($"   时长差异: {timeDifference:F3}秒");

        if (Math.Abs(timeDifference) > config.TimeCorrectionThreshold)
        {
            Console.WriteLine($"🔧 检测到明显时长差异 (>{config.TimeCorrectionThreshold:F3}s)，开始智能校正...");
            
            if (timeDifference > 0)
            {
                ApplyExtensionCorrection(segments, actualAudioDuration, timeDifference, config);
            }
            else
            {
                ApplyCompressionCorrection(segments, actualAudioDuration, whisperTotalDuration);
            }
            
            ValidateCorrectionResults(segments, actualAudioDuration, config);
        }
        else
        {
            Console.WriteLine($"   ✅ 时长匹配良好，无需校正 (差异 ≤ {config.TimeCorrectionThreshold:F3}s)");
        }
    }

    private void ApplyExtensionCorrection(List<AudioSegment> segments, double actualAudioDuration, double timeDifference, SplitterConfig config)
    {
        var lastSegment = segments[^1];
        var originalEnd = lastSegment.EndTime;
        
        var extensionTime = Math.Min(timeDifference, config.MaxExtensionTime);
        lastSegment.EndTime = Math.Min(actualAudioDuration, originalEnd + extensionTime);
        lastSegment.Duration = lastSegment.EndTime - lastSegment.StartTime;
        
        Console.WriteLine($"   ➡️ 扩展最后段落: {originalEnd:F3}s → {lastSegment.EndTime:F3}s (+{extensionTime:F3}s)");
        
        var remainingDifference = actualAudioDuration - lastSegment.EndTime;
        if (remainingDifference > config.TimeCorrectionThreshold)
        {
            ApplyScaleCorrection(segments, actualAudioDuration, segments.Max(s => s.EndTime), config);
        }
    }

    private void ApplyCompressionCorrection(List<AudioSegment> segments, double actualAudioDuration, double whisperTotalDuration)
    {
        var compressionFactor = actualAudioDuration / whisperTotalDuration;
        Console.WriteLine($"   ⬇️ 应用时间压缩因子: {compressionFactor:F4}");
        
        foreach (var segment in segments)
        {
            segment.StartTime *= compressionFactor;
            segment.EndTime *= compressionFactor;
            segment.Duration = segment.EndTime - segment.StartTime;
        }
        
        Console.WriteLine($"   ✅ 时间压缩校正完成");
    }

    private void ApplyScaleCorrection(List<AudioSegment> segments, double actualAudioDuration, double currentTotalDuration, SplitterConfig config)
    {
        var scaleFactor = actualAudioDuration / currentTotalDuration;
        Console.WriteLine($"   📏 应用时间缩放因子: {scaleFactor:F4}");
        
        foreach (var segment in segments)
        {
            var segmentOriginalStart = segment.StartTime;
            var segmentOriginalEnd = segment.EndTime;
            
            segment.StartTime *= scaleFactor;
            segment.EndTime *= scaleFactor;
            segment.Duration = segment.EndTime - segment.StartTime;
            
            if (config.DebugMode)
            {
                Console.WriteLine($"     段落校正: [{segmentOriginalStart:F3}-{segmentOriginalEnd:F3}] → [{segment.StartTime:F3}-{segment.EndTime:F3}]");
            }
        }
        
        Console.WriteLine($"   ✅ 时间缩放校正完成");
    }

    private void ValidateCorrectionResults(List<AudioSegment> segments, double actualAudioDuration, SplitterConfig config)
    {
        var correctedTotalDuration = segments.Max(s => s.EndTime);
        var finalDifference = Math.Abs(actualAudioDuration - correctedTotalDuration);
        
        Console.WriteLine($"🎯 校正结果:");
        Console.WriteLine($"   校正后时长: {correctedTotalDuration:F3}秒");
        Console.WriteLine($"   剩余差异: {finalDifference:F3}秒");
        
        if (finalDifference <= config.TimeCorrectionThreshold)
        {
            Console.WriteLine($"   ✅ 校正成功！时长匹配良好");
        }
        else
        {
            Console.WriteLine($"   ⚠️ 仍有差异，但已明显改善");
        }
    }

    private void ValidateAudioFile(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        Console.WriteLine($"🎵 处理音频文件: {audioPath}");
        Console.WriteLine($"   格式: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}通道, {reader.WaveFormat.BitsPerSample}位");
        Console.WriteLine($"   时长: {reader.TotalTime.TotalSeconds:F2}秒");
        Console.WriteLine($"   编码: {reader.WaveFormat.Encoding}");
        
        // 检查文件是否为空
        if (reader.TotalTime.TotalSeconds < 0.1)
        {
            throw new InvalidOperationException("音频文件时长过短或为空");
        }
        
        // 检查是否为支持的格式
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm && 
            reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            Console.WriteLine($"⚠️ 警告: 音频编码格式 {reader.WaveFormat.Encoding} 可能需要转换");
        }
    }

    private async Task<string> GetOrDownloadModelAsync(string modelSize)
    {
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = Path.Combine("models", modelFileName);

        if (File.Exists(modelPath))
        {
            Console.WriteLine($"📁 使用现有模型: {modelPath}");
            return modelPath;
        }

        // 创建模型目录
        Directory.CreateDirectory("models");

        Console.WriteLine($"🔽 首次运行，正在下载模型 {modelSize}...");
        Console.WriteLine("⏳ 请稍等，模型较大可能需要几分钟时间...");

        // 使用Whisper.net的内置下载功能
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10); // 设置超时
        var modelUrl = GetModelDownloadUrl(modelSize);

        var response = await httpClient.GetAsync(modelUrl);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(modelPath);
        await response.Content.CopyToAsync(fileStream);

        Console.WriteLine($"✅ 模型下载完成: {modelPath}");
        return modelPath;
    }

    private string GetModelDownloadUrl(string modelSize)
    {
        var baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
        return $"{baseUrl}/ggml-{modelSize}.bin";
    }

    private async Task<List<AudioSegment>> HandleCorruptedWaveFile(string audioPath, SplitterConfig config, Exception originalException)
    {
        Console.WriteLine($"❌ WAV文件格式错误: {originalException.Message}");
        Console.WriteLine("🔄 尝试重新处理音频文件...");
        
        // 尝试重新转换音频文件
        var backupPath = audioPath + ".fixed.wav";
        
        // 这里需要引用音频转换服务，但为了避免循环依赖，我们暂时简化处理
        // 在实际应用中，应该通过依赖注入来处理
        
        // 递归调用，但要防止无限递归
        if (!audioPath.Contains(".fixed.wav"))
        {
            // 这里应该调用AudioConversionService来修复文件
            // var conversionService = new AudioConversionService();
            // conversionService.ConvertWavToOptimalFormat(audioPath, backupPath);
            
            var backupSegments = await PerformAlignmentAsync(backupPath, config);
            
            // 清理备份文件
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            
            return backupSegments;
        }
        else
        {
            throw new InvalidOperationException($"无法处理音频文件格式，即使在重新转换后: {originalException.Message}", originalException);
        }
    }
}