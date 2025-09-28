using Whisper.net;
using NAudio.Wave;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// ����ʶ��Ͷ������
/// ʹ��Whisperģ�ͽ�������ʶ��У��ʱ�����
/// </summary>
public class SpeechRecognitionService
{
    /// <summary>
    /// ִ������ʶ���ʱ�����
    /// </summary>
    public async Task<List<AudioSegment>> PerformAlignmentAsync(string audioPath, SplitterConfig config)
    {
        Console.WriteLine("ִ������ʶ��Ͷ���...");

        var segments = new List<AudioSegment>();

        try
        {
            // ��֤��Ƶ�ļ���ʽ
            ValidateAudioFile(audioPath);

            // ��ȡ������ģ��
            var modelPath = await GetOrDownloadModelAsync(config.ModelSize);

            using var whisperFactory = WhisperFactory.FromPath(modelPath);
            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage(config.Language)
                .WithThreads(Environment.ProcessorCount)
                .Build();

            await using var fileStream = File.OpenRead(audioPath);

            Console.WriteLine("��ʼ����ʶ��...");
            
            // ?? ��ȡ��Ƶ�ļ�ʵ��ʱ������У��
            double actualAudioDuration = GetActualAudioDuration(audioPath);

            await foreach (var result in processor.ProcessAsync(fileStream))
            {
                // result is SegmentData, process it directly
                if (!string.IsNullOrWhiteSpace(result.Text))
                {
                    var audioSegment = new AudioSegment
                    {
                        StartTime = result.Start.TotalSeconds,  // ? ʹ�� TotalSeconds ������ TotalMilliseconds
                        EndTime = result.End.TotalSeconds,     // ? ʹ�� TotalSeconds ������ TotalMilliseconds
                        Text = result.Text.Trim(),
                        Duration = (result.End - result.Start).TotalSeconds // ? ʹ�� TotalSeconds
                    };

                    segments.Add(audioSegment);
                    Console.WriteLine($"ʶ��: [{audioSegment.StartTime:F2}s-{audioSegment.EndTime:F2}s] {audioSegment.Text}");
                }
            }

            // ?? ����ʱ��У�������� Whisper ʶ��ʱ����ʵ����Ƶʱ����ƥ�������
            if (segments.Count > 0 && actualAudioDuration > 0 && config.EnableTimeCorrection)
            {
                PerformTimeCorrection(segments, actualAudioDuration, config);
            }
        }
        catch (Whisper.net.Wave.CorruptedWaveException ex)
        {
            return await HandleCorruptedWaveFile(audioPath, config, ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"����ʶ��ʧ��: {ex.Message}", ex);
        }

        Console.WriteLine($"ʶ����ɣ��� {segments.Count} ��Ƭ��");
        return segments;
    }

    private double GetActualAudioDuration(string audioPath)
    {
        try
        {
            using var audioReader = new AudioFileReader(audioPath);
            var duration = audioReader.TotalTime.TotalSeconds;
            Console.WriteLine($"?? ��Ƶ�ļ�ʵ��ʱ��: {duration:F3}��");
            return duration;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? �޷���ȡ��Ƶʵ��ʱ��: {ex.Message}");
            return 0;
        }
    }

    private void PerformTimeCorrection(List<AudioSegment> segments, double actualAudioDuration, SplitterConfig config)
    {
        var whisperTotalDuration = segments.Max(s => s.EndTime);
        var timeDifference = actualAudioDuration - whisperTotalDuration;
        
        Console.WriteLine($"?? ʱ���Աȷ���:");
        Console.WriteLine($"   Whisperʶ��ʱ��: {whisperTotalDuration:F3}��");
        Console.WriteLine($"   ��Ƶʵ��ʱ��: {actualAudioDuration:F3}��");
        Console.WriteLine($"   ʱ������: {timeDifference:F3}��");

        // ������쳬�����õ���ֵ����������У��
        if (Math.Abs(timeDifference) > config.TimeCorrectionThreshold)
        {
            Console.WriteLine($"?? ��⵽����ʱ������ (>{config.TimeCorrectionThreshold:F3}s)����ʼ����У��...");
            
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
            Console.WriteLine($"   ? ʱ��ƥ�����ã�����У�� (���� �� {config.TimeCorrectionThreshold:F3}s)");
        }
    }

    private void ApplyExtensionCorrection(List<AudioSegment> segments, double actualAudioDuration, double timeDifference, SplitterConfig config)
    {
        // ʵ����Ƶ��Whisperʶ��ĳ�����չ���һ������
        var lastSegment = segments[^1];
        var originalEnd = lastSegment.EndTime;
        
        // ?? ����1: ��������չ���һ������
        var extensionTime = Math.Min(timeDifference, config.MaxExtensionTime);
        lastSegment.EndTime = Math.Min(actualAudioDuration, originalEnd + extensionTime);
        lastSegment.Duration = lastSegment.EndTime - lastSegment.StartTime;
        
        Console.WriteLine($"   ? ��չ������: {originalEnd:F3}s �� {lastSegment.EndTime:F3}s (+{extensionTime:F3}s)");
        
        // ?? ����2: �������ʣ����죬�������������ж���
        var remainingDifference = actualAudioDuration - lastSegment.EndTime;
        if (remainingDifference > config.TimeCorrectionThreshold)
        {
            ApplyScaleCorrection(segments, actualAudioDuration, segments.Max(s => s.EndTime), config);
        }
    }

    private void ApplyCompressionCorrection(List<AudioSegment> segments, double actualAudioDuration, double whisperTotalDuration)
    {
        // Whisperʶ��ı�ʵ����Ƶ���������������ܷ�����
        var compressionFactor = actualAudioDuration / whisperTotalDuration;
        Console.WriteLine($"   ?? Ӧ��ʱ��ѹ������: {compressionFactor:F4}");
        
        foreach (var segment in segments)
        {
            segment.StartTime *= compressionFactor;
            segment.EndTime *= compressionFactor;
            segment.Duration = segment.EndTime - segment.StartTime;
        }
        
        Console.WriteLine($"   ? ʱ��ѹ��У�����");
    }

    private void ApplyScaleCorrection(List<AudioSegment> segments, double actualAudioDuration, double currentTotalDuration, SplitterConfig config)
    {
        var scaleFactor = actualAudioDuration / currentTotalDuration;
        Console.WriteLine($"   ?? Ӧ��ʱ����������: {scaleFactor:F4}");
        
        foreach (var segment in segments)
        {
            var segmentOriginalStart = segment.StartTime;
            var segmentOriginalEnd = segment.EndTime;
            
            segment.StartTime *= scaleFactor;
            segment.EndTime *= scaleFactor;
            segment.Duration = segment.EndTime - segment.StartTime;
            
            if (config.DebugMode)
            {
                Console.WriteLine($"     ����У��: [{segmentOriginalStart:F3}-{segmentOriginalEnd:F3}] �� [{segment.StartTime:F3}-{segment.EndTime:F3}]");
            }
        }
        
        Console.WriteLine($"   ? ʱ������У�����");
    }

    private void ValidateCorrectionResults(List<AudioSegment> segments, double actualAudioDuration, SplitterConfig config)
    {
        var correctedTotalDuration = segments.Max(s => s.EndTime);
        var finalDifference = Math.Abs(actualAudioDuration - correctedTotalDuration);
        
        Console.WriteLine($"?? У�����:");
        Console.WriteLine($"   У����ʱ��: {correctedTotalDuration:F3}��");
        Console.WriteLine($"   ʣ�����: {finalDifference:F3}��");
        
        if (finalDifference <= config.TimeCorrectionThreshold)
        {
            Console.WriteLine($"   ? У���ɹ���ʱ��ƥ������");
        }
        else
        {
            Console.WriteLine($"   ?? ���в��죬�������Ը���");
        }
    }

    private void ValidateAudioFile(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);
        Console.WriteLine($"������Ƶ�ļ�: {audioPath}");
        Console.WriteLine($"��ʽ: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ͨ��, {reader.WaveFormat.BitsPerSample}λ");
        Console.WriteLine($"ʱ��: {reader.TotalTime.TotalSeconds:F2}��");
        Console.WriteLine($"����: {reader.WaveFormat.Encoding}");
        
        // ����ļ��Ƿ�Ϊ��
        if (reader.TotalTime.TotalSeconds < 0.1)
        {
            throw new InvalidOperationException("��Ƶ�ļ�ʱ�����̻�Ϊ��");
        }
        
        // ����Ƿ�Ϊ֧�ֵĸ�ʽ
        if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm && 
            reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            Console.WriteLine($"����: ��Ƶ�����ʽ {reader.WaveFormat.Encoding} ������Ҫת��");
        }
    }

    private async Task<string> GetOrDownloadModelAsync(string modelSize)
    {
        var modelFileName = $"ggml-{modelSize}.bin";
        var modelPath = Path.Combine("models", modelFileName);

        if (File.Exists(modelPath))
        {
            Console.WriteLine($"ʹ������ģ��: {modelPath}");
            return modelPath;
        }

        // ����ģ��Ŀ¼
        Directory.CreateDirectory("models");

        Console.WriteLine($"�״����У���������ģ�� {modelSize}...");
        Console.WriteLine("���Եȣ�ģ�ͽϴ������Ҫ������ʱ��...");

        // ʹ��Whisper.net���������ع���
        using var httpClient = new HttpClient();
        var modelUrl = GetModelDownloadUrl(modelSize);

        var response = await httpClient.GetAsync(modelUrl);
        response.EnsureSuccessStatusCode();

        await using var fileStream = File.Create(modelPath);
        await response.Content.CopyToAsync(fileStream);

        Console.WriteLine($"ģ���������: {modelPath}");
        return modelPath;
    }

    private string GetModelDownloadUrl(string modelSize)
    {
        var baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";
        return $"{baseUrl}/ggml-{modelSize}.bin";
    }

    private async Task<List<AudioSegment>> HandleCorruptedWaveFile(string audioPath, SplitterConfig config, Exception originalException)
    {
        Console.WriteLine($"WAV�ļ���ʽ����: {originalException.Message}");
        Console.WriteLine("�������´�����Ƶ�ļ�...");
        
        // ��������ת����Ƶ�ļ�
        var backupPath = audioPath + ".fixed.wav";
        
        // ������Ҫ������Ƶת�����񣬵�Ϊ�˱���ѭ��������������ʱ�򻯴���
        // ��ʵ��Ӧ���У�Ӧ��ͨ������ע��������
        
        // �ݹ���ã���Ҫ��ֹ���޵ݹ�
        if (!audioPath.Contains(".fixed.wav"))
        {
            // ����Ӧ�õ���AudioConversionService���޸��ļ�
            // var conversionService = new AudioConversionService();
            // conversionService.ConvertWavToOptimalFormat(audioPath, backupPath);
            
            var backupSegments = await PerformAlignmentAsync(backupPath, config);
            
            // �������ļ�
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            
            return backupSegments;
        }
        else
        {
            throw new InvalidOperationException($"�޷�������Ƶ�ļ���ʽ����ʹ������ת����: {originalException.Message}", originalException);
        }
    }
}