namespace VideoAnalysisDesktop.Application.Services;

/// <summary>
/// Builds the command-line argument list for the Python engine process.
/// Uses ProcessStartInfo.ArgumentList (no shell string concatenation).
/// 
/// Fixed command format:
/// <python_exe> -m video_analysis_pipeline.desktop_entry batch
///   --config <attempt_dir>\pipeline_config.json
///   --event-file <attempt_dir>\events.jsonl
///   --run-id <attempt_guid>
///   --input-root <user_selected_dir>
///   --output-root <user_selected_dir>
///   [fixed preset args]
/// </summary>
public sealed class CommandBuilder
{
    private readonly string _pythonExePath;

    public CommandBuilder(string pythonExePath, string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pythonExePath);
        _pythonExePath = pythonExePath;
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(Path.GetFullPath(pythonExePath)) ?? Environment.CurrentDirectory
            : workingDirectory;
    }

    /// <summary>
    /// Builds the full argument list for a batch command.
    /// </summary>
    public IReadOnlyList<string> BuildBatchArgs(
        string configFilePath,
        string eventFilePath,
        string runId,
        string inputRoot,
        string outputRoot,
        bool resume = true)
    {
        var args = new List<string>
        {
            "-m",
            "video_analysis_pipeline.desktop_entry",
            "batch",
            "--config", configFilePath,
            "--event-file", eventFilePath,
            "--run-id", runId,
            "--input-root", inputRoot,
            "--output-root", outputRoot,
        };

        // Append fixed preset args
        foreach (var kvp in PresetProvider.FixedArgs)
        {
            args.Add(kvp.Key);
            if (!string.IsNullOrEmpty(kvp.Value))
            {
                args.Add(kvp.Value);
            }
        }

        if (resume)
            args.Add("--resume");

        return args;
    }

    /// <summary>
    /// Builds the argument list for a batch-preflight command.
    /// </summary>
    public IReadOnlyList<string> BuildPreflightArgs(string inputRoot, string resultFilePath)
    {
        return new List<string>
        {
            "-m",
            "video_analysis_pipeline.desktop_entry",
            "batch-preflight",
            "--input-root", inputRoot,
            "--result-file", resultFilePath,
        };
    }

    public string PythonExecutablePath => _pythonExePath;

    public string WorkingDirectory { get; }
}
