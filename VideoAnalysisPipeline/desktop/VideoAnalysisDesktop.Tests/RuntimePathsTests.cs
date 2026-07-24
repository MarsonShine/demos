using System.Security.Cryptography;
using VideoAnalysisDesktop.App;
using VideoAnalysisDesktop.Infrastructure.Python;

namespace VideoAnalysisDesktop.Tests;

public sealed class RuntimePathsTests
{
    [Fact]
    public void BundledEngine_UsesManifestVersionAndAdjacentFfmpeg()
    {
        using var tmp = new TempDirectory();
        var engine = Path.Combine(tmp.Path, "engine");
        var python = Path.Combine(engine, "python", "python.exe");
        var manifest = Path.Combine(engine, "engine.manifest.json");
        var ffmpegDirectory = Path.Combine(engine, "ffmpeg");
        Directory.CreateDirectory(Path.GetDirectoryName(python)!);
        Directory.CreateDirectory(ffmpegDirectory);
        File.WriteAllText(python, "placeholder");
        File.WriteAllText(manifest, "{\"schema_version\":\"1.0\"}");
        File.WriteAllText(Path.Combine(ffmpegDirectory, "ffmpeg.exe"), "placeholder");
        File.WriteAllText(Path.Combine(ffmpegDirectory, "ffprobe.exe"), "placeholder");

        var version = RuntimePaths.GetEngineVersion(python);
        var expectedVersion = Convert.ToHexStringLower(
            SHA256.HashData(File.ReadAllBytes(manifest)));

        Assert.Equal(expectedVersion, version);
        Assert.Equal(ffmpegDirectory, RuntimePaths.TryFindFfmpegDirectory(python));
    }

    [Fact]
    public void Resolve_UsesStagedEngineForInstalledLayout()
    {
        using var tmp = new TempDirectory();
        var app = Path.Combine(tmp.Path, "app");
        var python = Path.Combine(tmp.Path, "engine", "python", "python.exe");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.GetDirectoryName(python)!);
        File.WriteAllText(python, "placeholder");
        File.WriteAllText(Path.Combine(tmp.Path, "engine", "engine.manifest.json"), "{}");

        var runtime = RuntimePaths.TryResolve(app);

        Assert.NotNull(runtime);
        Assert.True(runtime!.IsStagedReleaseEngine);
        Assert.Equal(python, runtime.PythonExecutable);
        Assert.Equal(Path.GetDirectoryName(python), runtime.WorkingDirectory);
    }

    [Fact]
    public void Resolve_UsesLegacyEnvironmentOnlyInsideSourceCheckout()
    {
        using var tmp = new TempDirectory();
        var repository = Path.Combine(tmp.Path, "repository");
        var desktop = Path.Combine(repository, "desktop");
        var app = Path.Combine(desktop, "VideoAnalysisDesktop.App", "bin", "Debug");
        var python = Path.Combine(desktop, "engine", "python", "Scripts", "python.exe");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.GetDirectoryName(python)!);
        Directory.CreateDirectory(Path.Combine(repository, "video_analysis_pipeline"));
        File.WriteAllText(python, "placeholder");
        File.WriteAllText(Path.Combine(desktop, "engine", "python", "pyvenv.cfg"), "home = C:\\Python");
        File.WriteAllText(Path.Combine(repository, "pipeline_config.json"), "{}");

        var runtime = RuntimePaths.TryResolve(app);

        Assert.NotNull(runtime);
        Assert.False(runtime!.IsStagedReleaseEngine);
        Assert.Equal(python, runtime.PythonExecutable);
        Assert.Equal(repository, runtime.WorkingDirectory);
    }

    [Fact]
    public void FfmpegValidator_RequiresBothBundledBinaries()
    {
        using var tmp = new TempDirectory();

        var result = FfmpegRuntimeValidator.Validate(tmp.Path, TimeSpan.FromSeconds(1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Missing ffmpeg.exe", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("Missing ffprobe.exe", StringComparison.Ordinal));
    }
}
