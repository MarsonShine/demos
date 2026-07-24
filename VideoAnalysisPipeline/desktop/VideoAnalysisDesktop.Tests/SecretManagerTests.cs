using VideoAnalysisDesktop.Infrastructure.Security;

namespace VideoAnalysisDesktop.Tests;

public sealed class SecretManagerTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsWithoutWritingPlainTextKey()
    {
        using var tmp = new TempDirectory();
        var manager = new SecretManager(tmp.Path);
        var secrets = new AzureOpenAiSecrets
        {
            Endpoint = "https://example.openai.azure.com/",
            ApiKey = "super-secret-key",
            Deployment = "gpt-deployment",
        };

        manager.Save(secrets);
        var loaded = Assert.IsType<AzureOpenAiSecrets>(manager.Load());

        Assert.Equal("https://example.openai.azure.com/", loaded.Endpoint);
        Assert.Equal("super-secret-key", loaded.ApiKey);
        Assert.Equal("gpt-deployment", loaded.Deployment);
        Assert.DoesNotContain("super-secret-key", File.ReadAllText(Path.Combine(tmp.Path, "secrets.dat")));
    }

    [Fact]
    public void Save_RejectsIncompleteOrNonHttpsConfiguration()
    {
        using var tmp = new TempDirectory();
        var manager = new SecretManager(tmp.Path);

        Assert.Throws<ArgumentException>(() => manager.Save(new AzureOpenAiSecrets
        {
            Endpoint = "http://example.test",
            ApiKey = "key",
            Deployment = "deployment",
        }));
        Assert.Throws<ArgumentException>(() => manager.Save(new AzureOpenAiSecrets
        {
            Endpoint = "https://example.test",
            ApiKey = "",
            Deployment = "deployment",
        }));
    }
}
