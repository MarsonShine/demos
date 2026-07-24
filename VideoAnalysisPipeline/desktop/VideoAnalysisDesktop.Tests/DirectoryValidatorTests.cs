using VideoAnalysisDesktop.Infrastructure.FileSystem;

namespace VideoAnalysisDesktop.Tests;

public class DirectoryValidatorTests
{
    [Fact]
    public void SameInputAndOutput_Fails()
    {
        using var tmp = new TempDirectory();
        var result = DirectoryValidator.Validate(tmp.Path, tmp.Path, 0);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("same directory"));
    }

    [Fact]
    public void OutputInsideInput_Fails()
    {
        using var tmp = new TempDirectory();
        var outputPath = System.IO.Path.Combine(tmp.Path, "child");
        var result = DirectoryValidator.Validate(tmp.Path, outputPath, 0);
        Assert.False(result.IsValid);
        Assert.False(Directory.Exists(outputPath));
    }

    [Fact]
    public void InputInsideOutput_Fails()
    {
        using var tmp = new TempDirectory();
        var childPath = System.IO.Path.Combine(tmp.Path, "child");
        Directory.CreateDirectory(childPath);
        var result = DirectoryValidator.Validate(childPath, tmp.Path, 0);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void NonExistentInput_Fails()
    {
        var result = DirectoryValidator.Validate(@"Z:\nonexistent\path\that\does\not\exist", @"C:\output", 0);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void PathsOnDifferentDriveRoots_AreNotDescendants()
    {
        Assert.False(DirectoryValidator.IsDescendantOf(
            @"F:\tmp",
            @"E:\repositories\demos\VideoAnalysisPipeline\files\6B 绘本动画新增"));
        Assert.False(DirectoryValidator.IsDescendantOf(
            @"E:\repositories\demos\VideoAnalysisPipeline\files\6B 绘本动画新增",
            @"F:\tmp"));
    }

    [Fact]
    public void ExistingOutput_IsAllowedForResumeOnly()
    {
        using var tmp = new TempDirectory();
        var input = System.IO.Path.Combine(tmp.Path, "input");
        var output = System.IO.Path.Combine(tmp.Path, "output");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        File.WriteAllText(System.IO.Path.Combine(output, "partial-result.txt"), "partial");

        var fresh = DirectoryValidator.Validate(input, output, 0);
        var resume = DirectoryValidator.Validate(input, output, 0, allowExistingOutput: true);

        Assert.False(fresh.IsValid);
        Assert.Contains(fresh.Errors, error => error.Contains("must be empty"));
        // Disk-capacity validation is intentionally part of the production
        // preflight and depends on the volume hosting the test temp folder.
        // This test verifies only the resume-specific rule: an existing,
        // non-empty output directory must not itself block a retry.
        Assert.DoesNotContain(resume.Errors, error => error.Contains("must be empty"));
    }
}

public class ModOutputValidatorTests
{
    [Fact]
    public void MissingDubbingDirectory_Fails()
    {
        using var tmp = new TempDirectory();
        var result = ModOutputValidator.Validate(tmp.Path, 5);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("dubbing directory not found"));
    }

    [Fact]
    public void ItemCountMismatch_Fails()
    {
        using var tmp = new TempDirectory();
        var dubbingDir = System.IO.Path.Combine(tmp.Path, "dubbing");
        Directory.CreateDirectory(System.IO.Path.Combine(dubbingDir, "1"));
        Directory.CreateDirectory(System.IO.Path.Combine(dubbingDir, "2"));

        var result = ModOutputValidator.Validate(tmp.Path, 5);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Expected 5"));
    }

    [Fact]
    public void NonContinuousNumericDirectories_Fail()
    {
        using var tmp = new TempDirectory();
        var dubbingDir = System.IO.Path.Combine(tmp.Path, "dubbing");
        CreateCompleteItem(System.IO.Path.Combine(dubbingDir, "2"));
        CreateCompleteItem(System.IO.Path.Combine(dubbingDir, "3"));
        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "movie_dubbing.xlsx"), "not an empty file");

        var result = ModOutputValidator.Validate(tmp.Path, 2);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Missing dubbing item directory: 1"));
        Assert.Contains(result.Errors, error => error.Contains("Unexpected dubbing item directory"));
    }

    [Fact]
    public void CompleteContinuousModOutput_Passes()
    {
        using var tmp = new TempDirectory();
        var dubbingDir = System.IO.Path.Combine(tmp.Path, "dubbing");
        CreateCompleteItem(System.IO.Path.Combine(dubbingDir, "1"));
        CreateCompleteItem(System.IO.Path.Combine(dubbingDir, "2"));
        File.WriteAllText(System.IO.Path.Combine(tmp.Path, "movie_dubbing.xlsx"), "not an empty file");

        var result = ModOutputValidator.Validate(tmp.Path, 2);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors));
    }

    private static void CreateCompleteItem(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var file in new[] { "01.jpg", "01.mp4", "02.mp4", "03.mp3" })
            File.WriteAllText(System.IO.Path.Combine(directory, file), "data");
    }
}

// Simple disposable temp directory helper
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vda_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
