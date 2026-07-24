using System.ComponentModel;
using System.Diagnostics;

namespace VideoAnalysisDesktop.Infrastructure.Python;

/// <summary>
/// Verifies that the binaries copied into the desktop engine are executable.
/// File existence alone is insufficient on Windows because package-manager
/// shim executables can be copied successfully while referring to an absent
/// package-manager installation directory.
/// </summary>
public static class FfmpegRuntimeValidator
{
    private static readonly string[] RequiredBinaries = ["ffmpeg.exe", "ffprobe.exe"];

    public static FfmpegValidationResult Validate(string directory, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return FfmpegValidationResult.Failed("The FFmpeg directory is empty.");

        var errors = new List<string>();
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        foreach (var binary in RequiredBinaries)
        {
            var executable = Path.Combine(directory, binary);
            if (!File.Exists(executable))
            {
                errors.Add($"Missing {binary}.");
                continue;
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executable,
                        Arguments = "-version",
                        WorkingDirectory = directory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                };
                if (!process.Start())
                {
                    errors.Add($"Could not start {binary}.");
                    continue;
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    errors.Add($"{binary} did not respond to -version within {effectiveTimeout.TotalSeconds:0} seconds.");
                    continue;
                }

                Task.WaitAll([stdoutTask, stderrTask]);
                var output = (stdoutTask.Result + Environment.NewLine + stderrTask.Result).Trim();
                if (process.ExitCode != 0)
                {
                    errors.Add($"{binary} -version failed ({process.ExitCode}): {Summarize(output)}");
                }
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
            {
                errors.Add($"{binary} could not be executed: {exception.Message}");
            }
        }

        return errors.Count == 0
            ? FfmpegValidationResult.Passed()
            : new FfmpegValidationResult(false, errors);
    }

    private static string Summarize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "no diagnostic output";

        var normalized = value.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
        return normalized[..Math.Min(500, normalized.Length)];
    }
}

public sealed record FfmpegValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static FfmpegValidationResult Passed() => new(true, []);
    public static FfmpegValidationResult Failed(string error) => new(false, [error]);
}
