using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VideoAnalysisDesktop.Infrastructure.Python;

namespace VideoAnalysisDesktop.Application.Services;

/// <summary>
/// Runs the Python batch-preflight contract and exposes both its human-readable
/// validation result and the stable input snapshot used to guard resume.
/// </summary>
public sealed class PreflightService
{
    private readonly CommandBuilder _commandBuilder;

    public PreflightService(CommandBuilder commandBuilder)
    {
        _commandBuilder = commandBuilder;
    }

    public async Task<PreflightResult> RunPreflightAsync(
        string inputRoot,
        string resultFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultFilePath);

        var args = _commandBuilder.BuildPreflightArgs(inputRoot, resultFilePath);
        var stderrLines = new List<string>();

        using var host = new PythonProcessHost();
        host.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stderrLines.Add(e.Data);
        };

        host.Start(
            _commandBuilder.PythonExecutablePath,
            args,
            _commandBuilder.WorkingDirectory,
            new Dictionary<string, string> { ["PYTHONUTF8"] = "1" });

        using var registration = cancellationToken.Register(host.Cancel);
        var exitCode = await host.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (!File.Exists(resultFilePath))
        {
            return PreflightResult.EngineFailure(
                $"Preflight failed with exit code {exitCode}. {string.Join(Environment.NewLine, stderrLines)}");
        }

        try
        {
            var json = await File.ReadAllTextAsync(resultFilePath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var result = ParseResult(document.RootElement);

            if (exitCode == 0)
                return result;

            var errors = result.Errors.ToList();
            errors.Add(new PreflightError(
                "engine_error",
                $"Preflight process exited with code {exitCode}. {string.Join(Environment.NewLine, stderrLines)}"));
            return result with { Passed = false, Errors = errors };
        }
        catch (JsonException exception)
        {
            return PreflightResult.EngineFailure($"Preflight wrote invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            return PreflightResult.EngineFailure($"Could not read preflight result: {exception.Message}");
        }
    }

    internal static PreflightResult ParseResult(JsonElement root)
    {
        var passed = root.TryGetProperty("passed", out var passedElement) && passedElement.GetBoolean();
        var totalItems = root.TryGetProperty("total_items", out var totalItemsElement)
            ? totalItemsElement.GetInt32()
            : 0;
        var totalSize = root.TryGetProperty("total_input_size_bytes", out var totalSizeElement)
            ? totalSizeElement.GetInt64()
            : 0;

        var items = new List<PreflightItem>();
        var snapshotJson = "[]";
        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
        {
            snapshotJson = itemsElement.GetRawText();
            foreach (var item in itemsElement.EnumerateArray())
            {
                items.Add(new PreflightItem(
                    GetInt(item, "index"),
                    GetString(item, "relative_dir"),
                    GetString(item, "source_mp4"),
                    GetOptionalString(item, "source_srt"),
                    GetOptionalString(item, "source_mp3"),
                    GetLong(item, "mp4_size_bytes"),
                    GetLong(item, "srt_size_bytes"),
                    GetOptionalLong(item, "mp3_size_bytes")));
            }
        }

        var errors = new List<PreflightError>();
        if (root.TryGetProperty("errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errorsElement.EnumerateArray())
            {
                errors.Add(new PreflightError(
                    GetString(error, "type", "unknown"),
                    GetString(error, "message", "Preflight reported an unspecified error.")));
            }
        }

        var snapshotHash = GetOptionalString(root, "snapshot_sha256")
            ?? ComputeSha256(snapshotJson);

        return new PreflightResult(
            passed,
            totalItems,
            totalSize,
            items,
            errors,
            snapshotJson,
            snapshotHash);
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static string? GetOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static long GetLong(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var result) ? result : 0;

    private static long? GetOptionalLong(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null && value.TryGetInt64(out var result)
            ? result
            : null;

    private static string ComputeSha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record PreflightResult(
    bool Passed,
    int TotalItems,
    long TotalInputSizeBytes,
    IReadOnlyList<PreflightItem> Items,
    IReadOnlyList<PreflightError> Errors,
    string SnapshotJson,
    string SnapshotHash)
{
    public static PreflightResult EngineFailure(string message)
        => new(false, 0, 0, [], [new PreflightError("engine_error", message)], "[]", "");
}

public sealed record PreflightItem(
    int Index,
    string RelativeDir,
    string SourceMp4,
    string? SourceSrt,
    string? SourceMp3,
    long Mp4SizeBytes,
    long SrtSizeBytes,
    long? Mp3SizeBytes);

public sealed record PreflightError(string Type, string Message);
