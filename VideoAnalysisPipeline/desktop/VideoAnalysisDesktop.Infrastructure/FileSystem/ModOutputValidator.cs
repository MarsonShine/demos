namespace VideoAnalysisDesktop.Infrastructure.FileSystem;

/// <summary>
/// Validates MOD output after a successful batch run:
/// - movie_dubbing.xlsx exists and is non-empty
/// - dubbing\1..N directories exist, matching preflight item count
/// - Each item directory has non-empty 01.jpg, 01.mp4, 02.mp4, 03.mp3
/// </summary>
public static class ModOutputValidator
{
    public sealed record ModValidationResult(
        bool IsValid,
        int ExpectedItemCount,
        int ActualItemCount,
        List<string> Errors);

    public static ModValidationResult Validate(string outputRoot, int expectedItemCount)
    {
        var errors = new List<string>();

        // Check movie_dubbing.xlsx
        var xlsxPath = Path.Combine(outputRoot, "movie_dubbing.xlsx");
        if (!File.Exists(xlsxPath))
        {
            errors.Add($"movie_dubbing.xlsx not found in {outputRoot}");
        }
        else if (new FileInfo(xlsxPath).Length == 0)
        {
            errors.Add("movie_dubbing.xlsx is empty");
        }

        // Check dubbing directories
        var dubbingRoot = Path.Combine(outputRoot, "dubbing");
        if (!Directory.Exists(dubbingRoot))
        {
            errors.Add($"dubbing directory not found in {outputRoot}");
            return new ModValidationResult(false, expectedItemCount, 0, errors);
        }

        var itemDirs = Directory.GetDirectories(dubbingRoot)
            .Select(d => new { Name = Path.GetFileName(d), Path = d })
            .Where(item => int.TryParse(item.Name, out _))
            .OrderBy(item => int.Parse(item.Name))
            .ToList();

        if (itemDirs.Count != expectedItemCount)
        {
            errors.Add(
                $"Expected {expectedItemCount} dubbing item directories, " +
                $"found {itemDirs.Count}");
        }

        var expectedNames = Enumerable.Range(1, expectedItemCount)
            .Select(index => index.ToString())
            .ToHashSet(StringComparer.Ordinal);
        foreach (var expectedName in expectedNames)
        {
            if (!itemDirs.Any(item => string.Equals(item.Name, expectedName, StringComparison.Ordinal)))
                errors.Add($"Missing dubbing item directory: {expectedName}");
        }

        foreach (var unexpected in itemDirs.Where(item => !expectedNames.Contains(item.Name)))
            errors.Add($"Unexpected dubbing item directory: {unexpected.Name}");

        var requiredFiles = new[] { "01.jpg", "01.mp4", "02.mp4", "03.mp3" };
        foreach (var itemDir in itemDirs)
        {
            var dirPath = itemDir.Path;
            foreach (var file in requiredFiles)
            {
                var filePath = Path.Combine(dirPath, file);
                if (!File.Exists(filePath))
                {
                    errors.Add($"Missing {file} in {dirPath}");
                }
                else if (new FileInfo(filePath).Length == 0)
                {
                    errors.Add($"Empty file: {filePath}");
                }
            }
        }

        return new ModValidationResult(
            errors.Count == 0,
            expectedItemCount,
            itemDirs.Count,
            errors);
    }
}
