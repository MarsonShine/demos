namespace VideoAnalysisDesktop.Infrastructure.Data;

/// <summary>
/// Resolves mutable desktop state. Installed builds use ProgramData; a source
/// checkout can opt into a per-user root so debugging does not require an
/// administrator-created ProgramData ACL.
/// </summary>
public static class DesktopDataPaths
{
    public const string DataRootEnvironmentVariable = "VIDEO_ANALYSIS_DESKTOP_DATA_ROOT";

    public static string GetDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
            return Path.GetFullPath(overrideRoot);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Company",
            "VideoAnalysisDesktop");
    }

    public static string GetJobsRoot() => Path.Combine(GetDataRoot(), "jobs");
}
