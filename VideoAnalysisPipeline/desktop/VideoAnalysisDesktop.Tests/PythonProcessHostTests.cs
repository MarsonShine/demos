using System.Collections.Concurrent;
using VideoAnalysisDesktop.Infrastructure.Python;

namespace VideoAnalysisDesktop.Tests;

public sealed class PythonProcessHostTests
{
    [Fact]
    public async Task WaitForExitAsync_DrainsBothRedirectedStreams()
    {
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        var commandShell = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");

        using var host = new PythonProcessHost();
        host.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output.Enqueue(e.Data);
        };
        host.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                errors.Enqueue(e.Data);
        };

        host.Start(
            commandShell,
            ["/d", "/c", "echo stdout-line && echo stderr-line 1>&2"],
            Environment.CurrentDirectory);

        var exitCode = await host.WaitForExitAsync();

        Assert.Equal(0, exitCode);
        Assert.Contains(output, line => string.Equals(line.Trim(), "stdout-line", StringComparison.Ordinal));
        Assert.Contains(errors, line => string.Equals(line.Trim(), "stderr-line", StringComparison.Ordinal));
    }
}
