using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VideoAnalysisDesktop.Application.Services;

namespace VideoAnalysisDesktop.Tests;

public sealed class PreflightServiceTests
{
    [Fact]
    public void ParseResult_MapsProducerContractAndUsesItsSnapshotHash()
    {
        const string json = """
            {
              "passed": true,
              "total_items": 1,
              "total_input_size_bytes": 42,
              "snapshot_sha256": "producer-hash",
              "items": [{
                "index": 1,
                "relative_dir": "lesson-1",
                "source_mp4": "C:\\input\\lesson-1\\video.mp4",
                "source_srt": "C:\\input\\lesson-1\\video.srt",
                "source_mp3": null,
                "mp4_size_bytes": 30,
                "srt_size_bytes": 12,
                "mp3_size_bytes": null
              }],
              "errors": []
            }
            """;
        using var document = JsonDocument.Parse(json);

        var result = PreflightService.ParseResult(document.RootElement);

        Assert.True(result.Passed);
        Assert.Equal(1, result.TotalItems);
        Assert.Equal(42, result.TotalInputSizeBytes);
        Assert.Equal("producer-hash", result.SnapshotHash);
        var item = Assert.Single(result.Items);
        Assert.Equal("lesson-1", item.RelativeDir);
        Assert.Equal(30, item.Mp4SizeBytes);
        Assert.Null(item.SourceMp3);
    }

    [Fact]
    public void ParseResult_ComputesCompatibleHashForOlderProducerPayload()
    {
        const string items = "[{\"index\":1,\"relative_dir\":\"lesson-1\"}]";
        var json = $$"""
            {
              "passed": false,
              "total_items": 1,
              "total_input_size_bytes": 0,
              "items": {{items}},
              "errors": [{"type":"invalid_directory_structure","message":"missing SRT"}]
            }
            """;
        using var document = JsonDocument.Parse(json);

        var result = PreflightService.ParseResult(document.RootElement);

        Assert.False(result.Passed);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(items))),
            result.SnapshotHash);
        var error = Assert.Single(result.Errors);
        Assert.Equal("invalid_directory_structure", error.Type);
        Assert.Equal("missing SRT", error.Message);
    }
}
