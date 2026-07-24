using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoAnalysisDesktop.Infrastructure.Python;

/// <summary>
/// The stable JSONL contract emitted by video_analysis_pipeline.desktop_events.
/// The Python producer deliberately uses snake_case, therefore every persisted
/// member is explicitly named instead of relying on serializer naming defaults.
/// </summary>
public sealed class JsonlEvent
{
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("run_id")]
    public string? RunId { get; set; }

    [JsonPropertyName("timestamp_utc")]
    public string? TimestampUtc { get; set; }

    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("total_items")]
    public int TotalItems { get; set; }

    [JsonPropertyName("completed_items")]
    public int CompletedItems { get; set; }

    [JsonPropertyName("item_index")]
    public int? ItemIndex { get; set; }

    [JsonPropertyName("relative_dir")]
    public string? RelativeDir { get; set; }

    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("error_summary")]
    public string? ErrorSummary { get; set; }
}

/// <summary>
/// Incrementally reads an append-only UTF-8 JSONL event file.  It advances the
/// file offset only after retaining the bytes after the last newline, so a
/// producer that is interrupted in the middle of an event cannot make the host
/// lose the event on the next poll.
/// </summary>
public sealed class JsonlTailReader : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private long _nextFileOffset;
    private byte[] _partialLineBytes = [];
    private bool _disposed;

    public JsonlTailReader(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <summary>
    /// Reads every complete JSON line appended since the preceding call.
    /// A malformed complete line is ignored rather than being treated as a
    /// partial write; a subsequent well-formed event remains readable.
    /// </summary>
    public IReadOnlyList<JsonlEvent> ReadNewLines()
    {
        ThrowIfDisposed();
        var events = new List<JsonlEvent>();

        if (!File.Exists(_filePath))
            return events;

        byte[] appended;
        try
        {
            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // A replacement/truncation means that the saved suffix belongs to
            // the old file and must not be prepended to the new content.
            if (stream.Length < _nextFileOffset)
            {
                _nextFileOffset = 0;
                _partialLineBytes = [];
            }

            stream.Seek(_nextFileOffset, SeekOrigin.Begin);
            appended = ReadToEnd(stream);
            _nextFileOffset = stream.Position;
        }
        catch (IOException)
        {
            // The writer may be atomically replacing the file. A later poll is
            // safer than producing a false progress update.
            return events;
        }
        catch (UnauthorizedAccessException)
        {
            return events;
        }

        if (appended.Length == 0)
            return events;

        var bytes = Combine(_partialLineBytes, appended);
        var lineStart = 0;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] != (byte)'\n')
                continue;

            var lineLength = index - lineStart;
            if (lineLength > 0 && bytes[index - 1] == (byte)'\r')
                lineLength--;

            if (lineLength > 0)
            {
                var line = Encoding.UTF8.GetString(bytes, lineStart, lineLength);
                TryAddEvent(line, events);
            }

            lineStart = index + 1;
        }

        _partialLineBytes = lineStart == bytes.Length
            ? []
            : bytes[lineStart..];

        return events;
    }

    private static void TryAddEvent(string line, List<JsonlEvent> events)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<JsonlEvent>(line, SerializerOptions);
            if (evt is not null && !string.IsNullOrWhiteSpace(evt.Event))
                events.Add(evt);
        }
        catch (JsonException)
        {
            // The line is newline-terminated, therefore it is malformed data
            // rather than an in-flight partial write. Keep tailing subsequent
            // events instead of permanently blocking the progress display.
        }
    }

    private static byte[] ReadToEnd(Stream stream)
    {
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Combine(byte[] prefix, byte[] suffix)
    {
        if (prefix.Length == 0)
            return suffix;

        var combined = new byte[prefix.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, combined, 0, prefix.Length);
        Buffer.BlockCopy(suffix, 0, combined, prefix.Length, suffix.Length);
        return combined;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        _disposed = true;
        _partialLineBytes = [];
    }
}
