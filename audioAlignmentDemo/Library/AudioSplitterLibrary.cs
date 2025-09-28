using AudioAlignmentDemo.Core;
using AudioAlignmentDemo.Models;
using AudioAlignmentDemo.Configuration;

namespace AudioAlignmentDemo.Library;

/// <summary>
/// ��Ƶ�ָ�����ӿ�
/// �ṩ�����õ�API��������Ŀ����
/// </summary>
public class AudioSplitterLibrary
{
    private readonly AudioSplitter _audioSplitter;

    public AudioSplitterLibrary()
    {
        _audioSplitter = new AudioSplitter();
    }

    /// <summary>
    /// ��������Ƶ�ļ� (ʹ��Ĭ������)
    /// </summary>
    /// <param name="inputPath">������Ƶ�ļ�·��</param>
    /// <param name="outputDirectory">���Ŀ¼·��</param>
    /// <returns>������</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(string inputPath, string outputDirectory = "output_segments")
    {
        var config = ConfigurationManager.Presets.Balanced;
        config.InputAudioPath = inputPath;
        config.OutputDirectory = outputDirectory;

        return await ProcessAudioFileAsync(config);
    }

    /// <summary>
    /// ��������Ƶ�ļ� (ʹ���Զ�������)
    /// </summary>
    /// <param name="config">�ָ�����</param>
    /// <returns>������</returns>
    public async Task<AudioSplitResult> ProcessAudioFileAsync(SplitterConfig config)
    {
        var startTime = DateTime.Now;
        var result = new AudioSplitResult
        {
            InputFile = config.InputAudioPath,
            OutputDirectory = config.OutputDirectory,
            StartTime = startTime
        };

        try
        {
            await _audioSplitter.ProcessAsync(config);

            result.Success = true;
            result.ProcessingTime = DateTime.Now - startTime;
            
            // ͳ�����ɵ��ļ�
            if (Directory.Exists(config.OutputDirectory))
            {
                var outputFiles = Directory.GetFiles(config.OutputDirectory, "sentence_*.*")
                    .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                    .ToArray();
                
                result.GeneratedFiles = outputFiles.ToList();
                result.SegmentCount = outputFiles.Length;

                // ��ȡ�����ļ���ȡ������Ϣ
                var reportPath = Path.Combine(config.OutputDirectory, "sentence_split_report.json");
                if (File.Exists(reportPath))
                {
                    result.ReportPath = reportPath;
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Exception = ex;
            result.ProcessingTime = DateTime.Now - startTime;
        }

        return result;
    }

    /// <summary>
    /// ����������Ƶ�ļ�
    /// </summary>
    /// <param name="inputFiles">�����ļ��б�</param>
    /// <param name="baseOutputDirectory">�������Ŀ¼</param>
    /// <param name="preset">ʹ�õ�Ԥ������</param>
    /// <returns>����������</returns>
    public async Task<BatchSplitResult> ProcessAudioFilesAsync(
        IEnumerable<string> inputFiles, 
        string baseOutputDirectory = "output_batch",
        string preset = "balanced")
    {
        var config = GetPresetConfig(preset);
        config.OutputDirectory = baseOutputDirectory;

        var batchProcessor = new Services.BatchProcessingService();
        var inputFilesList = inputFiles.ToList();
        var startTime = DateTime.Now;

        var result = new BatchSplitResult
        {
            InputFiles = inputFilesList,
            BaseOutputDirectory = baseOutputDirectory,
            StartTime = startTime
        };

        try
        {
            // ����������Ҫ�޸�BatchProcessingService�����ؽ��
            await batchProcessor.ProcessBatchAsync(inputFilesList, config);

            result.Success = true;
            result.ProcessingTime = DateTime.Now - startTime;

            // �ռ��������ɵ��ļ�
            result.Results = new List<AudioSplitResult>();
            foreach (var inputFile in inputFilesList)
            {
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);
                var fileOutputDir = Path.Combine(baseOutputDirectory, SanitizeDirectoryName(fileNameWithoutExt));
                
                if (Directory.Exists(fileOutputDir))
                {
                    var outputFiles = Directory.GetFiles(fileOutputDir, "sentence_*.*")
                        .Where(f => !f.EndsWith(".json") && !f.EndsWith(".txt") && !f.EndsWith(".csv"))
                        .ToList();
                    
                    result.Results.Add(new AudioSplitResult
                    {
                        InputFile = inputFile,
                        OutputDirectory = fileOutputDir,
                        Success = true,
                        GeneratedFiles = outputFiles,
                        SegmentCount = outputFiles.Count
                    });
                }
            }

            result.TotalSegments = result.Results.Sum(r => r.SegmentCount);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.Exception = ex;
            result.ProcessingTime = DateTime.Now - startTime;
        }

        return result;
    }

    /// <summary>
    /// ��ȡԤ������
    /// </summary>
    /// <param name="presetName">Ԥ������</param>
    /// <returns>���ö���</returns>
    public static SplitterConfig GetPresetConfig(string presetName)
    {
        var presets = ConfigurationManager.Presets.GetAllPresets();
        return presets.TryGetValue(presetName.ToLower(), out var preset) 
            ? preset 
            : ConfigurationManager.Presets.Balanced;
    }

    /// <summary>
    /// ��ȡ���п��õ�Ԥ������
    /// </summary>
    /// <returns>Ԥ�������б�</returns>
    public static List<string> GetAvailablePresets()
    {
        return ConfigurationManager.Presets.GetAllPresets().Keys.ToList();
    }

    /// <summary>
    /// ��֤��Ƶ�ļ��Ƿ�֧��
    /// </summary>
    /// <param name="filePath">�ļ�·��</param>
    /// <returns>�Ƿ�֧��</returns>
    public static bool IsAudioFileSupported(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var supportedExtensions = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
        
        return supportedExtensions.Contains(extension);
    }

    /// <summary>
    /// ��Ŀ¼�в�������֧�ֵ���Ƶ�ļ�
    /// </summary>
    /// <param name="directoryPath">Ŀ¼·��</param>
    /// <param name="includeSubdirectories">�Ƿ������Ŀ¼</param>
    /// <returns>��Ƶ�ļ��б�</returns>
    public static List<string> FindAudioFiles(string directoryPath, bool includeSubdirectories = false)
    {
        if (!Directory.Exists(directoryPath))
            return new List<string>();

        var supportedExtensions = new[] { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac", ".ogg", ".mp4" };
        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return Directory.GetFiles(directoryPath, "*.*", searchOption)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
    }

    private string SanitizeDirectoryName(string name)
    {
        var invalidChars = Path.GetInvalidPathChars().Concat(Path.GetInvalidFileNameChars()).ToArray();
        var sanitized = name;
        
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized.Trim('_', ' ', '.');
    }
}

/// <summary>
/// ��Ƶ�ָ���
/// </summary>
public class AudioSplitResult
{
    public string InputFile { get; set; } = "";
    public string OutputDirectory { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Exception? Exception { get; set; }
    public List<string> GeneratedFiles { get; set; } = new();
    public int SegmentCount { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime StartTime { get; set; }
    public string? ReportPath { get; set; }
}

/// <summary>
/// �����ָ���
/// </summary>
public class BatchSplitResult
{
    public List<string> InputFiles { get; set; } = new();
    public string BaseOutputDirectory { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public Exception? Exception { get; set; }
    public List<AudioSplitResult> Results { get; set; } = new();
    public int TotalSegments { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public DateTime StartTime { get; set; }

    public int SuccessfulFiles => Results.Count(r => r.Success);
    public int FailedFiles => Results.Count(r => !r.Success);
}