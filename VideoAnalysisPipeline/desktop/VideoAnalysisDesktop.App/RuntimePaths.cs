using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace VideoAnalysisDesktop.App;

internal static class RuntimePaths
{
    public static RuntimeEnvironment? TryResolveCurrent()
        => TryResolve(AppContext.BaseDirectory);

    public static string? TryFindPythonExecutable()
        => TryResolveCurrent()?.PythonExecutable;

    internal static RuntimeEnvironment? TryResolve(string appDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        var baseDirectory = Path.GetFullPath(appDirectory);
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "engine", "python", "python.exe"),
            Path.Combine(baseDirectory, "..", "engine", "python", "python.exe"),
        };

        var stagedPython = candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(IsStagedEnginePython);
        if (stagedPython is not null)
        {
            return new RuntimeEnvironment(
                stagedPython,
                Path.GetDirectoryName(stagedPython)!,
                IsStagedReleaseEngine: true);
        }

        // A source checkout created before the release staging tool used a
        // virtual environment at desktop/engine/python/Scripts/python.exe.
        // Support it only when the adjacent repository sources prove this is a
        // developer checkout. Installed applications never use this path and
        // still require the signed/staged engine manifest above.
        foreach (var ancestor in EnumerateAncestors(baseDirectory))
        {
            var legacyPython = Path.Combine(ancestor.FullName, "engine", "python", "Scripts", "python.exe");
            var repositoryRoot = ancestor.Parent;
            if (repositoryRoot is null ||
                !File.Exists(legacyPython) ||
                !File.Exists(Path.Combine(ancestor.FullName, "engine", "python", "pyvenv.cfg")) ||
                !Directory.Exists(Path.Combine(repositoryRoot.FullName, "video_analysis_pipeline")) ||
                !File.Exists(Path.Combine(repositoryRoot.FullName, "pipeline_config.json")))
            {
                continue;
            }

            return new RuntimeEnvironment(
                legacyPython,
                repositoryRoot.FullName,
                IsStagedReleaseEngine: false);
        }

        return null;
    }

    public static string? TryFindFfmpegDirectory(string pythonExecutablePath)
    {
        var engineDirectory = FindEngineDirectory(pythonExecutablePath);
        if (engineDirectory is null)
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(engineDirectory.FullName, "ffmpeg"),
            Path.Combine(engineDirectory.FullName, "ffmpeg", "bin"),
        };

        return candidates.FirstOrDefault(path =>
            File.Exists(Path.Combine(path, "ffmpeg.exe")) &&
            File.Exists(Path.Combine(path, "ffprobe.exe")));
    }

    public static string GetEngineVersion(string pythonExecutablePath)
    {
        var engineDirectory = FindEngineDirectory(pythonExecutablePath);
        var manifestPath = engineDirectory is null
            ? null
            : Path.Combine(engineDirectory.FullName, "engine.manifest.json");
        if (manifestPath is not null && File.Exists(manifestPath))
        {
            return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestPath)));
        }

        var version = FileVersionInfo.GetVersionInfo(pythonExecutablePath).FileVersion;
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version;
        }

        return File.GetLastWriteTimeUtc(pythonExecutablePath).ToString("O");
    }

    private static DirectoryInfo? FindEngineDirectory(string pythonExecutablePath)
    {
        DirectoryInfo? current = new(Path.GetDirectoryName(pythonExecutablePath) ?? string.Empty);
        while (current is not null)
        {
            if (string.Equals(current.Name, "engine", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsStagedEnginePython(string pythonExecutablePath)
    {
        if (!File.Exists(pythonExecutablePath))
        {
            return false;
        }

        var engineDirectory = FindEngineDirectory(pythonExecutablePath);
        return engineDirectory is not null &&
            File.Exists(Path.Combine(engineDirectory.FullName, "engine.manifest.json"));
    }

    private static IEnumerable<DirectoryInfo> EnumerateAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            yield return current;
        }
    }
}

internal sealed record RuntimeEnvironment(
    string PythonExecutable,
    string WorkingDirectory,
    bool IsStagedReleaseEngine);
