using NAudio.Wave;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// ��Ƶ��ʽת������
/// ���𽫸��ָ�ʽ����Ƶ�ļ�ת��ΪWhisper���ݵĸ�ʽ
/// </summary>
public class AudioConversionService
{
    /// <summary>
    /// ����Ƶ�ļ�ת��ΪWhisper���ݵ�WAV��ʽ
    /// </summary>
    public async Task<string> ConvertToWhisperFormatAsync(string inputPath, string outputDirectory)
    {
        var inputExtension = Path.GetExtension(inputPath).ToLowerInvariant();
        var outputPath = Path.Combine(outputDirectory, "processed.wav");
        
        Console.WriteLine($"?? �����Ƶ��ʽ: {inputExtension.ToUpper().TrimStart('.')}");
        
        // ֧�ֵ���Ƶ��ʽ���
        var supportedFormats = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
        
        if (!supportedFormats.Contains(inputExtension))
        {
            var supportedList = string.Join(", ", supportedFormats.Select(f => f.ToUpper().TrimStart('.')));
            throw new NotSupportedException($"? ��֧�ֵ���Ƶ��ʽ: {inputExtension.ToUpper().TrimStart('.')}\n? ֧�ֵĸ�ʽ: {supportedList}");
        }

        try
        {
            if (inputExtension == ".wav")
            {
                Console.WriteLine("?? ��⵽WAV��ʽ�����������Ż�����...");
                ConvertWavToOptimalFormat(inputPath, outputPath);
            }
            else
            {
                Console.WriteLine($"?? ת�� {inputExtension.ToUpper().TrimStart('.')} ��ʽ����Ʒ��WAV...");
                await ConvertToWavAsync(inputPath, outputPath);
            }
            
            // ��֤ת�����
            ValidateConversionResult(outputPath);
            
            return outputPath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"��Ƶ��ʽת��ʧ��: {ex.Message}", ex);
        }
    }

    private async Task ConvertToWavAsync(string inputPath, string outputPath)
    {
        try
        {
            using var reader = new AudioFileReader(inputPath);
            
            DisplayOriginalFormatInfo(reader);
            
            var targetFormat = DetermineTargetFormat(reader.WaveFormat, out string conversionStrategy);
            
            Console.WriteLine($"?? ת������: {conversionStrategy}");
            Console.WriteLine($"?? Ŀ���ʽ: {targetFormat.SampleRate}Hz, {targetFormat.BitsPerSample}λ, {targetFormat.Channels}���� PCM");
            
            // ?? ʹ�ø�Ʒ���ز�������
            using var resampler = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 100 // ?? ��������ز��� (0-100)
            };
            
            // ?? ���ɸ�Ʒ��WAV�ļ�
            WaveFileWriter.CreateWaveFile(outputPath, resampler);
            
            Console.WriteLine($"? ��Ʒ��ת�����: {outputPath}");
            
            // ?? ��ϸ��֤ת�����
            ValidateConvertedFile(outputPath, reader.WaveFormat, conversionStrategy);
        }
        catch (Exception ex)
        {
            // ���˲���
            Console.WriteLine($"?? ��Ʒ��ת��ʧ��: {ex.Message}");
            await TryFallbackConversion(inputPath, outputPath);
        }
    }

    private void ConvertWavToOptimalFormat(string inputPath, string outputPath)
    {
        Console.WriteLine("?? WAV��ʽ�����Ż�...");

        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            using var reader = new AudioFileReader(inputPath);
            
            Console.WriteLine($"?? ԭʼWAV��ʽ: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ͨ��, {reader.WaveFormat.BitsPerSample}λ");
            Console.WriteLine($"?? ԭʼ����: {reader.WaveFormat.Encoding}");

            var targetFormat = DetermineWavOptimizationFormat(reader.WaveFormat, out string optimizationStrategy);

            Console.WriteLine($"?? �Ż�����: {optimizationStrategy}");
            Console.WriteLine($"?? Ŀ���ʽ: {targetFormat.SampleRate}Hz, {targetFormat.Channels}ͨ��, {targetFormat.BitsPerSample}λ");

            // ����Ƿ���Ҫʵ��ת��
            if (reader.WaveFormat.Equals(targetFormat))
            {
                Console.WriteLine("?? ��ʽ�Ѿ������ţ�ֱ�Ӹ����ļ�...");
                File.Copy(inputPath, outputPath, true);
            }
            else
            {
                // ?? ʹ�����Ʒ���ز�������ת��
                using var resampler = new MediaFoundationResampler(reader, targetFormat)
                {
                    ResamplerQuality = 100 // ?? ��������ز���
                };

                WaveFileWriter.CreateWaveFile(outputPath, resampler);
            }

            Console.WriteLine($"? WAV��ʽ�Ż����: {outputPath}");
            
            ValidateConvertedFile(outputPath, reader.WaveFormat, optimizationStrategy);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"WAV��ʽ�Ż�ʧ��: {ex.Message}", ex);
        }
    }

    private WaveFormat DetermineTargetFormat(WaveFormat originalFormat, out string strategy)
    {
        // ����ԭʼ�����ʣ�������16kHz����Whisper������
        int targetSampleRate = Math.Max(originalFormat.SampleRate, 16000);
        
        // ���ԭʼ��Ƶ�Ǹ߲����ʣ����ֽϸߵ�Ʒ��
        if (originalFormat.SampleRate >= 44100)
        {
            strategy = "��Ʒ�ʱ���";
            return new WaveFormat(
                originalFormat.SampleRate, // ����ԭʼ������
                24, // ������24λ��ø��õĶ�̬��Χ
                Math.Min(originalFormat.Channels, 2) // ��ౣ��������
            );
        }
        else if (originalFormat.SampleRate >= 22050)
        {
            strategy = "Ʒ������";
            return new WaveFormat(
                Math.Max(originalFormat.SampleRate, 44100), // ������CDƷ��
                24, 
                Math.Min(originalFormat.Channels, 2)
            );
        }
        else
        {
            strategy = "��׼Ʒ��";
            return new WaveFormat(
                Math.Max(originalFormat.SampleRate, 22050), // ����22kHz
                24, // 24λ������16λ
                1 // ����������Whisper
            );
        }
    }

    private WaveFormat DetermineWavOptimizationFormat(WaveFormat originalFormat, out string strategy)
    {
        if (originalFormat.SampleRate >= 44100 && originalFormat.BitsPerSample >= 16)
        {
            // �Ѿ��Ǹ�Ʒ��WAV��ֻ��Ҫȷ��PCM��ʽ
            if (originalFormat.Encoding == WaveFormatEncoding.Pcm)
            {
                strategy = "��ʽ�����Ż�";
                return new WaveFormat(
                    originalFormat.SampleRate,
                    Math.Max(originalFormat.BitsPerSample, 24), // ����24λ
                    originalFormat.Channels
                );
            }
            else
            {
                strategy = "��Ʒ��PCMת��";
                return new WaveFormat(
                    originalFormat.SampleRate,
                    24, // 24λPCM
                    originalFormat.Channels
                );
            }
        }
        else
        {
            strategy = "Ʒ�������Ż�";
            return new WaveFormat(
                Math.Max(originalFormat.SampleRate, 44100), // ����44.1kHz
                24, // 24λ
                Math.Max(originalFormat.Channels, 1) // ���ٵ�����
            );
        }
    }

    private void DisplayOriginalFormatInfo(AudioFileReader reader)
    {
        Console.WriteLine($"?? ԭʼ��ʽ��Ϣ:");
        Console.WriteLine($"   ������: {reader.WaveFormat.SampleRate}Hz");
        Console.WriteLine($"   ������: {reader.WaveFormat.Channels}");
        Console.WriteLine($"   λ���: {reader.WaveFormat.BitsPerSample}λ");
        Console.WriteLine($"   ����: {reader.WaveFormat.Encoding}");
        Console.WriteLine($"   ʱ��: {reader.TotalTime.TotalSeconds:F2}��");
    }

    private void ValidateConversionResult(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("ת�������Ƶ�ļ�������");
        }

        var fileSize = new FileInfo(outputPath).Length;
        if (fileSize < 1024) // С��1KB����������
        {
            throw new InvalidOperationException($"ת�������Ƶ�ļ���С ({fileSize} bytes)������ת��ʧ��");
        }

        Console.WriteLine($"? ��Ƶת����ɣ��ļ���С: {fileSize / 1024:F1} KB");
    }

    private void ValidateConvertedFile(string filePath, WaveFormat originalFormat, string conversionStrategy)
    {
        try
        {
            using var reader = new WaveFileReader(filePath);
            
            Console.WriteLine($"?? ת�������֤ ({conversionStrategy}):");
            Console.WriteLine($"   ת�����ʽ: {reader.WaveFormat.SampleRate}Hz, {reader.WaveFormat.Channels}ͨ��, {reader.WaveFormat.BitsPerSample}λ");
            Console.WriteLine($"   ת�������: {reader.WaveFormat.Encoding}");
            Console.WriteLine($"   ʱ��: {reader.TotalTime.TotalSeconds:F2}��");
            Console.WriteLine($"   �ļ���С: {new FileInfo(filePath).Length / 1024:F2} KB");
            
            // ?? ���ʷ���
            var qualityScore = CalculateQualityScore(originalFormat, reader.WaveFormat);
            Console.WriteLine($"   ?? Ʒ������: {qualityScore}/100 ({GetQualityDescription(qualityScore)})");
            
            if (qualityScore < 70)
            {
                Console.WriteLine($"   ?? ����: ���ʿ����������½���������ת������");
            }
            else if (qualityScore >= 90)
            {
                Console.WriteLine($"   ? ����: ���ʱ������û�������");
            }
            
            // ? Whisper�����Լ��
            bool whisperCompatible = reader.WaveFormat.SampleRate >= 16000 && 
                                   reader.WaveFormat.Channels <= 2 && 
                                   reader.WaveFormat.BitsPerSample >= 16;
            
            Console.WriteLine($"   ?? Whisper������: {(whisperCompatible ? "? ����" : "? ��Ҫ��һ��ת��")}");
            Console.WriteLine("   ? ��Ƶ�ļ���֤���");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ת�����ļ���֤ʧ��: {ex.Message}", ex);
        }
    }

    private async Task TryFallbackConversion(string inputPath, string outputPath)
    {
        Console.WriteLine($"?? ���˵���׼Whisper��ʽת��...");
        
        try
        {
            await ConvertToStandardWhisperFormat(inputPath, outputPath);
        }
        catch
        {
            Console.WriteLine($"?? ����FFmpeg����ת������...");
            await TryFFmpegConversion(inputPath, outputPath);
        }
    }

    private async Task ConvertToStandardWhisperFormat(string inputPath, string outputPath)
    {
        using var reader = new AudioFileReader(inputPath);
        
        // ��׼Whisper��ʽ��16kHz, 16λ, ������
        var targetFormat = new WaveFormat(16000, 16, 1);
        
        Console.WriteLine($"?? ��׼Whisper��ʽת��: {targetFormat.SampleRate}Hz, {targetFormat.BitsPerSample}λ, {targetFormat.Channels}����");
        
        using var resampler = new MediaFoundationResampler(reader, targetFormat)
        {
            ResamplerQuality = 60 // ��׼�����ز���
        };
        
        WaveFileWriter.CreateWaveFile(outputPath, resampler);
        
        Console.WriteLine($"? ��׼��ʽת�����: {outputPath}");
        ValidateConvertedFile(outputPath, reader.WaveFormat, "��׼Whisper��ʽ");
    }

    private async Task TryFFmpegConversion(string inputPath, string outputPath)
    {
        try
        {
            Console.WriteLine("??? ����ʹ��FFmpeg���и�Ʒ��ת��...");
            
            var ffmpegArgs = $"-i \"{inputPath}\" " +
                           $"-acodec pcm_s24le " +     // 24λPCM����
                           $"-ar 44100 " +             // CDƷ�ʲ�����
                           $"-ac 2 " +                 // ������
                           $"-af \"aformat=sample_fmts=s24:sample_rates=44100\" " + // ��Ƶ������
                           $"-y \"{outputPath}\"";
            
            var success = await RunFFmpegCommand(ffmpegArgs);
            
            if (success)
            {
                Console.WriteLine("? FFmpeg��Ʒ��ת���ɹ�");
                ValidateConvertedFile(outputPath, null, "FFmpeg��Ʒ��");
            }
            else
            {
                await TryStandardFFmpegConversion(inputPath, outputPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? FFmpegת��ʧ��: {ex.Message}");
            await TryStandardFFmpegConversion(inputPath, outputPath);
        }
    }

    private async Task TryStandardFFmpegConversion(string inputPath, string outputPath)
    {
        try
        {
            Console.WriteLine("??? ʹ��FFmpeg��׼����ת��...");
            
            var ffmpegArgs = $"-i \"{inputPath}\" -ar 22050 -ac 2 -sample_fmt s16 -y \"{outputPath}\"";
            var success = await RunFFmpegCommand(ffmpegArgs);

            if (success)
            {
                Console.WriteLine("? FFmpeg��׼ת���ɹ�");
                ValidateConvertedFile(outputPath, null, "FFmpeg��׼");
            }
            else
            {
                throw new InvalidOperationException("����ת��������ʧ����");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? ����FFmpegת��������ʧ����: {ex.Message}");
            Console.WriteLine("?? ����:");
            Console.WriteLine("   1. ȷ��FFmpeg����ȷ��װ����ӵ�PATH��������");
            Console.WriteLine("   2. �����Ƶ�ļ��Ƿ���");
            Console.WriteLine("   3. ����ʹ��������Ƶת������Ԥ�����ļ�");
            Console.WriteLine("   4. ȷ�����㹻�Ĵ��̿ռ�");
            
            throw new InvalidOperationException($"�޷�ת����Ƶ��ʽ������ת��������ʧ�ܡ�������: {ex.Message}");
        }
    }

    private async Task<bool> RunFFmpegCommand(string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        
        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        
        await process.WaitForExitAsync();

        return process.ExitCode == 0;
    }

    private int CalculateQualityScore(WaveFormat original, WaveFormat converted)
    {
        int score = 100;
        
        // ���������� (40��)
        var sampleRateRatio = (double)converted.SampleRate / original.SampleRate;
        if (sampleRateRatio >= 1.0)
        {
            score += 0; // ���ֻ����������۷�
        }
        else if (sampleRateRatio >= 0.75)
        {
            score -= 10; // ��΢�½�
        }
        else if (sampleRateRatio >= 0.5)
        {
            score -= 25; // �����½�
        }
        else
        {
            score -= 40; // �����½�
        }
        
        // λ������� (30��)
        var bitDepthRatio = (double)converted.BitsPerSample / original.BitsPerSample;
        if (bitDepthRatio >= 1.0)
        {
            score += 0; // ���ֻ�����
        }
        else if (bitDepthRatio >= 0.75)
        {
            score -= 10; // ��΢�½�
        }
        else
        {
            score -= 30; // �����½�
        }
        
        // �������� (20��)
        if (converted.Channels >= original.Channels)
        {
            score += 0; // ���ֻ�����
        }
        else if (original.Channels == 2 && converted.Channels == 1)
        {
            score -= 15; // �������䵥����
        }
        else
        {
            score -= 20; // ������������
        }
        
        // �����ʽ���� (10��)
        if (converted.Encoding == WaveFormatEncoding.Pcm || converted.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            score += 0; // �����ʽ
        }
        else
        {
            score -= 10; // ���������ʽ
        }
        
        return Math.Max(0, Math.Min(100, score));
    }

    private string GetQualityDescription(int score)
    {
        return score switch
        {
            >= 95 => "׿Խ",
            >= 90 => "����", 
            >= 80 => "����",
            >= 70 => "�ɽ���",
            >= 60 => "һ��",
            >= 50 => "�ϲ�",
            _ => "�ܲ�"
        };
    }
}