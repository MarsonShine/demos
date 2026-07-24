using VideoAnalysisDesktop.Application.Services;

namespace VideoAnalysisDesktop.Tests;

public sealed class PresetProviderTests
{
    [Fact]
    public void DesktopContractHash_ChangesWhenBundledConfigChanges()
    {
        const string originalConfig = "{\"asr\":{\"provider\":\"faster-whisper\"}}";
        const string changedConfig = "{\"asr\":{\"provider\":\"azure-speech\"}}";

        var original = PresetProvider.ComputeDesktopContractHash(originalConfig);
        var changed = PresetProvider.ComputeDesktopContractHash(changedConfig);

        Assert.NotEqual(original, changed);
        Assert.Equal(original, PresetProvider.ComputeDesktopContractHash(originalConfig));
        Assert.Equal(
            PresetProvider.ComputeConfigTemplateHash(originalConfig),
            PresetProvider.ComputeConfigTemplateHash("{\n  \"asr\": { \"provider\": \"faster-whisper\" }\n}"));
    }
}
