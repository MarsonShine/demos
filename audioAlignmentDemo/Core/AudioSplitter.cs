using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Services;

namespace AudioAlignmentDemo.Core;

/// <summary>
/// ��Ƶ�ָ�������
/// Э���������������Ƶ�ָ�����
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
    /// ִ����Ƶ�ָ��
    /// </summary>
    public async Task ProcessAsync(SplitterConfig config)
    {
        var startTime = DateTime.Now;
        
        try
        {
            Console.WriteLine("?? ��ʼ��Ƶ�ָ������...\n");
            
            // 1. ��֤�����ļ�
            ValidateInputFile(config);

            // 2. ׼�����Ŀ¼
            PrepareOutputDirectory(config);

            // 3. ��Ƶ��ʽת�� (������Whisperʶ��)
            Console.WriteLine("?? ���� 1/6: ��Ƶ��ʽת��");
            string processedAudio = await _conversionService.ConvertToWhisperFormatAsync(
                config.InputAudioPath, config.OutputDirectory);

            // 4. ����ʶ���ʱ�����
            Console.WriteLine("\n?? ���� 2/6: ����ʶ���ʱ�����");
            var segments = await _recognitionService.PerformAlignmentAsync(processedAudio, config);

            // 5. ���ӷ����ͷָ���Ż�
            Console.WriteLine("\n?? ���� 3/6: ���ӷ����ͷָ���Ż�");
            var optimizedSegments = _analysisService.OptimizeSegments(segments, config);

            // 6. ��Ƶ�ļ��и� (ʹ��ԭʼ��Ƶ�ļ���������)
            Console.WriteLine("\n?? ���� 4/6: ��Ƶ�ļ��и�");
            await _splittingService.SplitAudioFilesAsync(config.InputAudioPath, optimizedSegments, config);

            // 7. ���ɽ������
            Console.WriteLine("\n?? ���� 5/6: ���ɽ������");
            _reportService.GenerateReport(optimizedSegments, config);
            
            // 8. �������ܱ���
            Console.WriteLine("\n?? ���� 6/6: ���ܷ���");
            var processingTime = DateTime.Now - startTime;
            _reportService.GeneratePerformanceReport(optimizedSegments, processingTime, config.OutputDirectory);

            // 9. ������ʱ�ļ�
            CleanupTemporaryFiles(processedAudio, config);
            
            Console.WriteLine($"\n?? ������ɣ��ܺ�ʱ: {(DateTime.Now - startTime).TotalSeconds:F1} ��");
            Console.WriteLine($"?? ��鿴 '{config.OutputDirectory}' Ŀ¼�еĽ���ļ�");
        }
        catch (Exception ex)
        {
            var processingTime = DateTime.Now - startTime;
            Console.WriteLine($"\n? ����ʧ�� (��ʱ: {processingTime.TotalSeconds:F1} ��)");
            throw new AudioSplitterException($"��Ƶ�ָ��ʧ��: {ex.Message}", ex);
        }
    }

    private void ValidateInputFile(SplitterConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.InputAudioPath))
        {
            throw new ArgumentException("������Ƶ�ļ�·������Ϊ��", nameof(config.InputAudioPath));
        }

        if (!File.Exists(config.InputAudioPath))
        {
            throw new FileNotFoundException($"��Ƶ�ļ�������: {config.InputAudioPath}");
        }

        var fileInfo = new FileInfo(config.InputAudioPath);
        if (fileInfo.Length == 0)
        {
            throw new ArgumentException("��Ƶ�ļ�Ϊ��", nameof(config.InputAudioPath));
        }

        Console.WriteLine($"? �����ļ���֤ͨ��: {config.InputAudioPath} ({fileInfo.Length / 1024:F1} KB)");
    }

    private void PrepareOutputDirectory(SplitterConfig config)
    {
        try
        {
            if (Directory.Exists(config.OutputDirectory))
            {
                Console.WriteLine($"?? ���Ŀ¼�Ѵ���: {config.OutputDirectory}");
            }
            else
            {
                Directory.CreateDirectory(config.OutputDirectory);
                Console.WriteLine($"?? �������Ŀ¼: {config.OutputDirectory}");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"�޷��������Ŀ¼ '{config.OutputDirectory}': {ex.Message}", ex);
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
                    Console.WriteLine($"?? ������ʱת���ļ�: {processedAudio}");
                }
                else
                {
                    File.Delete(processedAudio);
                    Console.WriteLine($"??? ������ʱ�ļ�: {processedAudio}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"?? ������ʱ�ļ�ʱ��������: {ex.Message}");
            // ���׳��쳣����Ϊ�ⲻӰ����Ҫ����
        }
    }
}

/// <summary>
/// ��Ƶ�ָ���ר���쳣��
/// </summary>
public class AudioSplitterException : Exception
{
    public AudioSplitterException(string message) : base(message) { }
    public AudioSplitterException(string message, Exception innerException) : base(message, innerException) { }
}