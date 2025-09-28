using NAudio.Wave;
using System.Text.RegularExpressions;
using AudioAlignmentDemo.Models;

namespace AudioAlignmentDemo.Services;

/// <summary>
/// ��Ƶ�и����
/// ������Ƶ�ļ����շָ���и�ɶ����������Ƶ�ļ�
/// </summary>
public class AudioSplittingService
{
    /// <summary>
    /// �и���Ƶ�ļ�
    /// </summary>
    public async Task SplitAudioFilesAsync(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("?? ��ʼ�и���Ƶ�ļ�...");
        
        // ?? ��ȡԴ�ļ���Ϣ
        var sourceExtension = Path.GetExtension(sourceAudio).ToLowerInvariant();
        var isOriginalFormat = !sourceAudio.Contains("processed.wav");
        
        Console.WriteLine($"?? Դ�ļ���ʽ: {sourceExtension.ToUpper().TrimStart('.')}");
        Console.WriteLine($"?? �����ʽ: {(isOriginalFormat ? sourceExtension.ToUpper().TrimStart('.') : "WAV")} (����ԭʼ����)");

        // ����Դ�ļ���ʽѡ���и��
        if (isOriginalFormat && sourceExtension != ".wav")
        {
            // ?? ʹ��FFmpegֱ���и�ԭʼ��ʽ�ļ� (����ԭʼ����)
            await SplitAudioWithFFmpegAsync(sourceAudio, segments, config);
        }
        else
        {
            // ?? ʹ��NAudio�и�WAV�ļ�
            await SplitWavAudioWithNAudioAsync(sourceAudio, segments, config);
        }

        Console.WriteLine($"\n?? ��Ƶ�и���ɣ������� {segments.Count} ��������Ƶ�ļ�");
    }

    private async Task SplitAudioWithFFmpegAsync(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("??? ʹ��FFmpeg����ԭʼ��ʽ�и� (�����������)...");
        
        var sourceExtension = Path.GetExtension(sourceAudio);
        
        // ?? ��ȡԴ�ļ���Ƶ��Ϣ
        await DisplaySourceAudioInfoAsync(sourceAudio);

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            await ProcessSegmentWithFFmpeg(sourceAudio, segment, sourceExtension, config, i, segments.Count);
        }
    }

    private async Task ProcessSegmentWithFFmpeg(string sourceAudio, AudioSegment segment, string sourceExtension, 
        SplitterConfig config, int segmentIndex, int totalSegments)
    {
        // ��������ļ��� (����ԭʼ��ʽ)
        var cleanText = CleanTextForFilename(segment.Text);
        var outputFileName = $"sentence_{segmentIndex + 1:D2}_{segment.StartTime:F1}s-{segment.EndTime:F1}s_{cleanText}{sourceExtension}";
        var outputPath = Path.Combine(config.OutputDirectory, outputFileName);

        try
        {
            Console.WriteLine($"\n?? �и���� {segmentIndex + 1}/{totalSegments}:");
            Console.WriteLine($"   ʱ��: {segment.StartTime:F2}s - {segment.EndTime:F2}s ({segment.Duration:F2}s)");
            Console.WriteLine($"   ����: \"{segment.Text}\"");
            Console.WriteLine($"   �ļ�: {outputFileName}");

            // ?? ʹ��FFmpeg��Ʒ���и����
            var ffmpegArgs = BuildFFmpegCutCommand(sourceAudio, segment, outputPath);
            Console.WriteLine($"   ??? FFmpeg����: ffmpeg {ffmpegArgs}");

            var success = await ExecuteFFmpegCommand(ffmpegArgs);

            if (success)
            {
                // ���¶���Ϣ
                segment.OutputFileName = outputFileName;
                segment.OutputPath = outputPath;

                // ��֤���ɵ��ļ�
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"   ? ������: {fileInfo.Length / 1024:F1} KB");
                
                // ?? ��֤��Ƶʱ�� (ʹ��FFprobe)
                await ValidateFFmpegOutputAsync(outputPath, segment.Duration);
            }
            else
            {
                Console.WriteLine($"   ? FFmpeg�и�ʧ��");
                
                // ?? ���˵��ر���ģʽ
                Console.WriteLine($"   ?? �����ر���ģʽ...");
                await SplitWithReencodingAsync(sourceAudio, segment, outputPath, sourceExtension);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ? �и�Ƭ�� {segmentIndex + 1} ʱ����: {ex.Message}");
        }
    }

    private string BuildFFmpegCutCommand(string sourceAudio, AudioSegment segment, string outputPath)
    {
        return $"-i \"{sourceAudio}\" " +
               $"-ss {segment.StartTime:F3} " +                    // ��ʼʱ�� (��ȷ������)
               $"-t {segment.Duration:F3} " +                      // ����ʱ��
               $"-c copy " +                                       // ?? �����ƣ������±��� (����ԭʼ����)
               $"-avoid_negative_ts make_zero " +                  // ���⸺ʱ���
               $"-y \"{outputPath}\"";                             // ��������ļ�
    }

    private async Task SplitWithReencodingAsync(string sourceAudio, AudioSegment segment, string outputPath, string sourceExtension)
    {
        // ?? ���ݸ�ʽѡ������ر������
        string codecArgs = GetReencodingParameters(sourceExtension);
        
        var ffmpegArgs = $"-i \"{sourceAudio}\" " +
                       $"-ss {segment.StartTime:F3} " +
                       $"-t {segment.Duration:F3} " +
                       $"{codecArgs} " +
                       $"-y \"{outputPath}\"";

        Console.WriteLine($"   ??? �ر�������: ffmpeg {ffmpegArgs}");

        var success = await ExecuteFFmpegCommand(ffmpegArgs);

        if (success)
        {
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"   ? �ر���ɹ�: {fileInfo.Length / 1024:F1} KB");
        }
        else
        {
            Console.WriteLine($"   ? �ر���Ҳʧ��");
        }
    }

    private string GetReencodingParameters(string sourceExtension)
    {
        return sourceExtension.ToLowerInvariant() switch
        {
            ".mp3" => "-c:a libmp3lame -b:a 320k",          // 320kbps MP3
            ".aac" => "-c:a aac -b:a 256k",                 // 256kbps AAC
            ".flac" => "-c:a flac",                         // ����FLAC
            ".ogg" => "-c:a libvorbis -q:a 8",             // ������Ogg
            _ => "-c:a libmp3lame -b:a 320k"               // Ĭ�ϸ�����MP3
        };
    }

    private async Task<bool> ExecuteFFmpegCommand(string arguments)
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

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"   FFmpeg����: {error}");
        }

        return process.ExitCode == 0;
    }

    private async Task DisplaySourceAudioInfoAsync(string audioPath)
    {
        try
        {
            // ʹ��FFprobe��ȡ��Ƶ��Ϣ
            var ffprobeArgs = $"-v quiet -print_format json -show_format -show_streams \"{audioPath}\"";
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = ffprobeArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                ParseAndDisplayAudioInfo(output, audioPath);
            }
            else
            {
                Console.WriteLine($"?? Դ��Ƶ: {Path.GetFileName(audioPath)} (�޷���ȡ��ϸ��Ϣ)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? Դ��Ƶ: {Path.GetFileName(audioPath)} (��Ϣ��ȡʧ��: {ex.Message})");
        }
    }

    private void ParseAndDisplayAudioInfo(string jsonOutput, string audioPath)
    {
        // �򵥽���JSON��Ϣ
        if (jsonOutput.Contains("codec_name"))
        {
            Console.WriteLine($"?? Դ��Ƶ��Ϣ: {Path.GetFileName(audioPath)}");
            
            // ��ȡ������Ϣ
            ExtractAndDisplayAudioProperty(jsonOutput, "sample_rate", "������", "Hz");
            ExtractAndDisplayAudioProperty(jsonOutput, "channels", "������", "");
            ExtractAndDisplayBitrate(jsonOutput);
            ExtractAndDisplayDuration(jsonOutput);
        }
    }

    private void ExtractAndDisplayAudioProperty(string json, string propertyName, string displayName, string unit)
    {
        if (json.Contains($"\"{propertyName}\""))
        {
            var value = ExtractJsonValue(json, propertyName);
            if (!string.IsNullOrEmpty(value))
            {
                Console.WriteLine($"   {displayName}: {value}{unit}");
            }
        }
    }

    private void ExtractAndDisplayBitrate(string json)
    {
        if (json.Contains("\"bit_rate\""))
        {
            var bitrate = ExtractJsonValue(json, "bit_rate");
            if (!string.IsNullOrEmpty(bitrate) && int.TryParse(bitrate, out int bitrateValue))
            {
                Console.WriteLine($"   ������: {bitrateValue / 1000}kbps");
            }
        }
    }

    private void ExtractAndDisplayDuration(string json)
    {
        if (json.Contains("\"duration\""))
        {
            var duration = ExtractJsonValue(json, "duration");
            if (!string.IsNullOrEmpty(duration) && double.TryParse(duration, out double durationValue))
            {
                Console.WriteLine($"   ʱ��: {durationValue:F2}��");
            }
        }
    }

    private string ExtractJsonValue(string json, string key)
    {
        try
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"?([^,\"\\}}]+)\"?";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    private async Task ValidateFFmpegOutputAsync(string outputPath, double expectedDuration)
    {
        try
        {
            // ʹ��FFprobe��֤����ļ�ʱ��
            var ffprobeArgs = $"-v quiet -show_entries format=duration -of csv:p=0 \"{outputPath}\"";
            
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = ffprobeArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new System.Diagnostics.Process { StartInfo = startInfo };
            
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output.Trim()))
            {
                ValidateDuration(output.Trim(), expectedDuration);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ?? �޷���֤����ļ�ʱ��: {ex.Message}");
        }
    }

    private void ValidateDuration(string durationString, double expectedDuration)
    {
        if (double.TryParse(durationString, out double actualDuration))
        {
            Console.WriteLine($"   ?? ʵ��ʱ��: {actualDuration:F2}s (����: {expectedDuration:F2}s)");
            
            var difference = Math.Abs(actualDuration - expectedDuration);
            if (difference > 0.1)
            {
                Console.WriteLine($"   ?? ʱ������: {difference:F2}s");
            }
            else
            {
                Console.WriteLine($"   ? ʱ��ƥ��");
            }
        }
    }

    private async Task SplitWavAudioWithNAudioAsync(string sourceAudio, List<AudioSegment> segments, SplitterConfig config)
    {
        Console.WriteLine("?? ʹ��NAudio����WAV��ʽ�и�...");
        
        using var reader = new AudioFileReader(sourceAudio);
        var format = reader.WaveFormat;

        Console.WriteLine($"Դ��Ƶ��ʽ: {format.SampleRate}Hz, {format.Channels}ͨ��, {format.BitsPerSample}λ");
        Console.WriteLine($"Դ��Ƶʱ��: {reader.TotalTime.TotalSeconds:F2}��");

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            await ProcessSegmentWithNAudio(reader, segment, format, config, i, segments.Count);
        }
    }

    private async Task ProcessSegmentWithNAudio(AudioFileReader reader, AudioSegment segment, WaveFormat format, 
        SplitterConfig config, int segmentIndex, int totalSegments)
    {
        // �������������ļ���
        var cleanText = CleanTextForFilename(segment.Text);
        var outputFileName = $"sentence_{segmentIndex + 1:D2}_{segment.StartTime:F1}s-{segment.EndTime:F1}s_{cleanText}.wav";
        var outputPath = Path.Combine(config.OutputDirectory, outputFileName);

        try
        {
            Console.WriteLine($"\n?? �и���� {segmentIndex + 1}/{totalSegments}:");
            Console.WriteLine($"   ʱ��: {segment.StartTime:F2}s - {segment.EndTime:F2}s ({segment.Duration:F2}s)");
            Console.WriteLine($"   ����: \"{segment.Text}\"");
            Console.WriteLine($"   �ļ�: {outputFileName}");

            // ���㾫ȷ�Ĳ���λ��
            var (startByte, totalBytes) = CalculateSamplePositions(segment, format);
            
            // ���ö�ȡλ��
            reader.Position = startByte;

            // ��������ļ�
            using var writer = new WaveFileWriter(outputPath, format);
            
            // ������Ƶ����
            await CopyAudioData(reader, writer, totalBytes, format);

            // ���¶���Ϣ
            segment.OutputFileName = outputFileName;
            segment.OutputPath = outputPath;

            // ��֤���ɵ��ļ�
            ValidateGeneratedFile(outputPath, segment);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ? �и�Ƭ�� {segmentIndex + 1} ʱ����: {ex.Message}");
        }
    }

    private (long startByte, long totalBytes) CalculateSamplePositions(AudioSegment segment, WaveFormat format)
    {
        var startSample = (long)(segment.StartTime * format.SampleRate);
        var endSample = (long)(segment.EndTime * format.SampleRate);
        var sampleCount = endSample - startSample;

        // �����ֽ�λ��
        var bytesPerSample = format.BitsPerSample / 8 * format.Channels;
        var startByte = startSample * bytesPerSample;
        var totalBytes = sampleCount * bytesPerSample;

        Console.WriteLine($"   ������Χ: {startSample} - {endSample} ({sampleCount} samples)");
        Console.WriteLine($"   �ֽڷ�Χ: {startByte} - {startByte + totalBytes} ({totalBytes} bytes)");

        return (startByte, totalBytes);
    }

    private async Task CopyAudioData(AudioFileReader reader, WaveFileWriter writer, long totalBytes, WaveFormat format)
    {
        // ʹ�ý�С�Ļ������Ի�ø��õľ���
        var bufferSize = Math.Min(format.AverageBytesPerSecond / 4, (int)totalBytes);
        var buffer = new byte[bufferSize];
        var bytesRead = 0L;

        while (bytesRead < totalBytes)
        {
            var bytesToRead = (int)Math.Min(buffer.Length, totalBytes - bytesRead);
            var actualBytesRead = reader.Read(buffer, 0, bytesToRead);

            if (actualBytesRead == 0) 
            {
                Console.WriteLine($"   ?? ��ǰ������ȡ���Ѷ�ȡ {bytesRead} / {totalBytes} �ֽ�");
                break;
            }

            writer.Write(buffer, 0, actualBytesRead);
            bytesRead += actualBytesRead;
        }
    }

    private void ValidateGeneratedFile(string outputPath, AudioSegment segment)
    {
        var fileInfo = new FileInfo(outputPath);
        Console.WriteLine($"   ? ������: {fileInfo.Length / 1024:F1} KB");

        // ��֤��Ƶ�ļ�ʱ��
        using var verifyReader = new AudioFileReader(outputPath);
        var actualDuration = verifyReader.TotalTime.TotalSeconds;
        Console.WriteLine($"   ?? ʵ��ʱ��: {actualDuration:F2}s (����: {segment.Duration:F2}s)");
        
        if (Math.Abs(actualDuration - segment.Duration) > 0.1)
        {
            Console.WriteLine($"   ?? ʱ������ϴ�: {Math.Abs(actualDuration - segment.Duration):F2}s");
        }
    }

    private string CleanTextForFilename(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";

        // ȡǰ20���ַ����Ƴ�����ȫ���ļ����ַ�
        var cleaned = text.Length > 20 ? text.Substring(0, 20) : text;
        
        // �Ƴ����滻����ȫ���ַ�
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            cleaned = cleaned.Replace(c, '_');
        }
        
        // �Ƴ������Ų��滻�ո�
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