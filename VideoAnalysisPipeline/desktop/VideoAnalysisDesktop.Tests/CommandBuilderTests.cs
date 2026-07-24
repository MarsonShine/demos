using VideoAnalysisDesktop.Application.Services;

namespace VideoAnalysisDesktop.Tests;

public class CommandBuilderTests
{
    [Fact]
    public void BuildBatchArgs_IncludesAllFixedPresetArgs()
    {
        var builder = new CommandBuilder(@"C:\Program Files\Company\VideoAnalysisDesktop\engine\python\python.exe");

        var args = builder.BuildBatchArgs(
            configFilePath: @"C:\jobs\attempt\pipeline_config.json",
            eventFilePath: @"C:\jobs\attempt\events.jsonl",
            runId: "test-run-id",
            inputRoot: @"C:\input",
            outputRoot: @"C:\output");

        // Must start with -m video_analysis_pipeline.desktop_entry batch
        Assert.Equal("-m", args[0]);
        Assert.Equal("video_analysis_pipeline.desktop_entry", args[1]);
        Assert.Contains("batch", args);

        // Must contain required args
        Assert.Contains("--config", args);
        Assert.Contains("--event-file", args);
        Assert.Contains("--run-id", args);
        Assert.Contains("--input-root", args);
        Assert.Contains("--output-root", args);

        // Must contain fixed preset args
        Assert.Contains("--final-output", args);
        Assert.Contains("mod", args);
        Assert.Contains("--video-frame-size", args);
        Assert.Contains("1280x720", args);
        Assert.Contains("--video-fps", args);
        Assert.Contains("25", args);
        Assert.Contains("--video-h264-profile", args);
        Assert.Contains("main", args);
        Assert.Contains("--resume", args);

        // The caller supplies arguments individually to ProcessStartInfo.ArgumentList.
        // Legitimate Windows paths may contain shell metacharacters and must not
        // be rejected or concatenated into a shell command.
        Assert.Equal("--resume", args[^1]);
    }

    [Fact]
    public void BuildPreflightArgs_IsCorrect()
    {
        var builder = new CommandBuilder("python.exe");
        var args = builder.BuildPreflightArgs(@"C:\input", @"C:\result.json");

        Assert.Equal("-m", args[0]);
        Assert.Equal("video_analysis_pipeline.desktop_entry", args[1]);
        Assert.Equal("batch-preflight", args[2]);
        Assert.Equal("--input-root", args[3]);
        Assert.Equal(@"C:\input", args[4]);
        Assert.Equal("--result-file", args[5]);
        Assert.Equal(@"C:\result.json", args[6]);
    }

    [Fact]
    public void BuildBatchArgs_HandlesChineseAndSpacesInPaths()
    {
        var builder = new CommandBuilder("python.exe");
        var args = builder.BuildBatchArgs(
            @"C:\config.json",
            @"C:\events.jsonl",
            "run-1",
            @"C:\测试目录\input folder",
            @"C:\输出目录\output folder");

        Assert.Contains(@"C:\测试目录\input folder", args);
        Assert.Contains(@"C:\输出目录\output folder", args);
    }

    [Fact]
    public void BuildBatchArgs_PreservesSpecialCharactersAndCanDisableResume()
    {
        var builder = new CommandBuilder("python.exe");
        var input = @"C:\input & source\lesson; 1";

        var args = builder.BuildBatchArgs(
            @"C:\config.json",
            @"C:\events.jsonl",
            "run-1",
            input,
            @"C:\output | destination",
            resume: false);

        Assert.Contains(input, args);
        Assert.Contains(@"C:\output | destination", args);
        Assert.DoesNotContain("--resume", args);
    }
}
