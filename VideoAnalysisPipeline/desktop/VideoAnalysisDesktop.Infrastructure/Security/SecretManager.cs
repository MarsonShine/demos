using System.Text.Json;

namespace VideoAnalysisDesktop.Infrastructure.Security;

/// <summary>
/// Stores and retrieves encrypted Azure OpenAI secrets.
/// Secrets are stored in %ProgramData%\Company\VideoAnalysisDesktop\config\secrets.dat
/// using DPAPI LocalMachine encryption.
/// Never written to JSON config, database, logs, or command lines.
/// </summary>
public sealed class SecretManager
{
    private readonly string _secretsFilePath;

    public SecretManager(string? dataRoot = null)
    {
        var root = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Company", "VideoAnalysisDesktop", "config");
        Directory.CreateDirectory(root);
        _secretsFilePath = Path.Combine(root, "secrets.dat");
    }

    public AzureOpenAiSecrets? Load()
    {
        if (!File.Exists(_secretsFilePath))
            return null;

        var encrypted = File.ReadAllBytes(_secretsFilePath);
        var json = DpapiEncryption.UnprotectString(
            Convert.ToBase64String(encrypted));
        var secrets = JsonSerializer.Deserialize<AzureOpenAiSecrets>(json);
        if (secrets is not null && !secrets.IsComplete)
            throw new InvalidDataException("Saved Azure OpenAI configuration is incomplete.");
        return secrets;
    }

    public void Save(AzureOpenAiSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        secrets.NormalizeAndValidate();

        var json = JsonSerializer.Serialize(secrets);
        var encrypted = Convert.FromBase64String(
            DpapiEncryption.ProtectString(json));

        var dir = Path.GetDirectoryName(_secretsFilePath)!;
        Directory.CreateDirectory(dir);

        // Atomic write: temp file + replace
        var tempPath = _secretsFilePath + ".tmp";
        File.WriteAllBytes(tempPath, encrypted);
        File.Move(tempPath, _secretsFilePath, overwrite: true);
    }

    public bool Exists() => File.Exists(_secretsFilePath);
}

public sealed class AzureOpenAiSecrets
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Deployment { get; set; } = "";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Deployment);

    public void NormalizeAndValidate()
    {
        Endpoint = Endpoint.Trim();
        ApiKey = ApiKey.Trim();
        Deployment = Deployment.Trim();

        if (!IsComplete)
            throw new ArgumentException("Azure OpenAI endpoint, API key, and deployment are all required.");

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Azure OpenAI endpoint must be an HTTPS absolute URI.");
    }
}
