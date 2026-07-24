using System.Text;
using VideoAnalysisDesktop.Infrastructure.Python;

namespace VideoAnalysisDesktop.Tests;

public sealed class JsonlTailReaderTests
{
    [Fact]
    public void ReadNewLines_MapsPythonSnakeCaseAndRetainsPartialUtf8Line()
    {
        using var tmp = new TempDirectory();
        var eventFile = Path.Combine(tmp.Path, "events.jsonl");
        using var reader = new JsonlTailReader(eventFile);

        File.WriteAllText(
            eventFile,
            "{\"schema_version\":\"1.0\",\"run_id\":\"run-1\",\"event\":\"item_started\",\"total_items\":2",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        Assert.Empty(reader.ReadNewLines());

        File.AppendAllText(
            eventFile,
            ",\"completed_items\":1,\"item_index\":2,\"relative_dir\":\"深圳 lesson\",\"status\":\"running\"}\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var evt = Assert.Single(reader.ReadNewLines());
        Assert.Equal("1.0", evt.SchemaVersion);
        Assert.Equal("run-1", evt.RunId);
        Assert.Equal("item_started", evt.Event);
        Assert.Equal(2, evt.TotalItems);
        Assert.Equal(1, evt.CompletedItems);
        Assert.Equal(2, evt.ItemIndex);
        Assert.Equal("深圳 lesson", evt.RelativeDir);
    }

    [Fact]
    public void ReadNewLines_SkipsMalformedCompleteLineAndContinues()
    {
        using var tmp = new TempDirectory();
        var eventFile = Path.Combine(tmp.Path, "events.jsonl");
        File.WriteAllText(eventFile, "not json\n{\"event\":\"run_completed\",\"total_items\":1,\"completed_items\":1}\n");
        using var reader = new JsonlTailReader(eventFile);

        var evt = Assert.Single(reader.ReadNewLines());

        Assert.Equal("run_completed", evt.Event);
        Assert.Equal(1, evt.CompletedItems);
    }
}
