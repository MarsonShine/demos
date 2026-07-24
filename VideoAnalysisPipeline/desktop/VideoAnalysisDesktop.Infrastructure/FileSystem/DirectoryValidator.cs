namespace VideoAnalysisDesktop.Infrastructure.FileSystem;

/// <summary>
/// Validates the operator-selected source and destination folders before the
/// engine can modify data. A resume is allowed to reuse its existing output;
/// a brand-new job is not.
/// </summary>
public static class DirectoryValidator
{
    public static ValidationResult Validate(
        string inputRoot,
        string outputRoot,
        long totalInputSizeBytes,
        bool allowExistingOutput = false)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(inputRoot))
            errors.Add("Input root is required.");
        if (string.IsNullOrWhiteSpace(outputRoot))
            errors.Add("Output root is required.");
        if (errors.Count > 0)
            return new ValidationResult(false, errors);

        string inputFull;
        string outputFull;
        try
        {
            inputFull = NormalizePath(inputRoot);
            outputFull = NormalizePath(outputRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors.Add($"Invalid input or output path: {exception.Message}");
            return new ValidationResult(false, errors);
        }

        if (!Directory.Exists(inputFull))
        {
            errors.Add($"Input root does not exist: {inputFull}");
            return new ValidationResult(false, errors);
        }

        if (PathsEqual(inputFull, outputFull))
            errors.Add("Input root and output root cannot be the same directory.");
        else if (IsDescendantOf(outputFull, inputFull))
            errors.Add("Output root cannot be inside the input root directory tree.");
        else if (IsDescendantOf(inputFull, outputFull))
            errors.Add("Input root cannot be inside the output root directory tree.");

        // Do not create an output directory or write a probe file until the
        // two roots are proven disjoint. In particular, selecting
        // <input>\output must leave the source tree untouched when rejected.
        if (errors.Count > 0)
            return new ValidationResult(false, errors);

        try
        {
            // Force an enumeration to catch unreadable input folders before
            // spawning a long-running engine process.
            _ = Directory.EnumerateFileSystemEntries(inputFull).Take(1).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot read input directory: {exception.Message}");
        }

        try
        {
            if (Directory.Exists(outputFull))
            {
                if (!allowExistingOutput && Directory.EnumerateFileSystemEntries(outputFull).Any())
                {
                    errors.Add("Output directory must be empty for a new job. Choose a different or empty directory.");
                }
            }
            else
            {
                Directory.CreateDirectory(outputFull);
            }

            if (Directory.Exists(outputFull))
                VerifyWritable(outputFull);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot write to output directory: {exception.Message}");
        }

        try
        {
            var root = Path.GetPathRoot(outputFull);
            if (string.IsNullOrWhiteSpace(root))
                throw new IOException($"Could not determine drive root for {outputFull}.");

            var drive = new DriveInfo(root);
            var requiredSpace = Math.Max(10L * 1024 * 1024 * 1024, checked(Math.Max(0, totalInputSizeBytes) * 3));
            if (drive.AvailableFreeSpace < requiredSpace)
            {
                errors.Add(
                    $"Insufficient free space. Required: {FormatBytes(requiredSpace)}, " +
                    $"Available: {FormatBytes(drive.AvailableFreeSpace)}");
            }
        }
        catch (OverflowException)
        {
            errors.Add("Input size is too large to calculate required free space safely.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errors.Add($"Cannot verify free disk space: {exception.Message}");
        }

        return new ValidationResult(errors.Count == 0, errors);
    }

    private static void VerifyWritable(string directory)
    {
        var probe = Path.Combine(directory, $".video-analysis-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
        }
        finally
        {
            if (File.Exists(probe))
                File.Delete(probe);
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? fullPath;
        return fullPath.Length <= root.Length
            ? root
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    internal static bool IsDescendantOf(string candidate, string parent)
    {
        // Path.GetRelativePath returns an absolute path when the two inputs
        // belong to different volumes (for example F:\\tmp relative to E:\\in).
        // An absolute result is not a child path, so roots must agree first.
        var candidateRoot = Path.GetPathRoot(candidate);
        var parentRoot = Path.GetPathRoot(parent);
        if (string.IsNullOrWhiteSpace(candidateRoot) ||
            string.IsNullOrWhiteSpace(parentRoot) ||
            !string.Equals(candidateRoot, parentRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(parent, candidate);
        return !Path.IsPathRooted(relative) &&
            relative is not "." and not ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);
