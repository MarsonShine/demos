using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VideoAnalysisDesktop.Application.Services;

/// <summary>
/// Provides the fixed MOD/720p/Android-compatible preset.
/// All encoding parameters are hard-coded; operators never see them.
/// The preset hash is used to detect configuration changes that require a new job.
/// </summary>
public sealed class PresetProvider
{
    public const string PresetVersion = "1.1.0";

    public static readonly IReadOnlyDictionary<string, string> FixedArgs = new Dictionary<string, string>
    {
        ["--video-target-size-ratio"] = "1000",
        ["--video-audio-bitrate-kbps"] = "128",
        ["--video-x264-preset"] = "veryfast",
        ["--video-mp4-muxer"] = "psp",
        ["--video-h264-profile"] = "main",
        ["--video-h264-level"] = "3.1",
        ["--video-keyframe-interval-seconds"] = "0.33",
        ["--video-reference-frames"] = "3",
        ["--video-frame-size"] = "1280x720",
        ["--video-fps"] = "25",
        ["--video-audio-sample-rate-hz"] = "44100",
        ["--video-audio-channels"] = "2",
        ["--video-audio-bit-depth"] = "32",
        ["--audio-target-size-ratio"] = "128",
        ["--final-output"] = "mod",
    };

    /// <summary>
    /// Human-readable label for the UI: "MOD 720p Android Compatible"
    /// </summary>
    public const string DisplayName = "MOD 720p Android";

    /// <summary>
    /// Description shown in the UI explaining the preset.
    /// </summary>
    public const string Description =
        "Video: 1280×720, H.264 Main@3.1, 1000 kbps, 25 fps, PSP muxer, veryfast preset, 0.33s keyframe interval.\n" +
        "Audio: AAC 128 kbps, 44100 Hz, stereo, 32-bit.\n" +
        "Background: MP3 128 kbps.\n" +
        "Output: MOD layout (dubbing/<seq>/ folders + movie_dubbing.xlsx).\n" +
        "ASR: faster-whisper base.en, CPU, int8. BGM: demucs htdemucs, CPU.";

    /// <summary>
    /// Computes a stable hash of the preset for change detection.
    /// </summary>
    public static string ComputePresetHash()
    {
        var canonicalArgs = FixedArgs
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(canonicalArgs);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Computes a stable semantic hash of a JSON config template for change
    /// detection. Whitespace and Windows/Linux line endings do not invalidate
    /// a resume contract.
    /// </summary>
    public static string ComputeConfigTemplateHash(string configJson)
    {
        ArgumentNullException.ThrowIfNull(configJson);
        using var document = JsonDocument.Parse(configJson);
        var canonicalJson = JsonSerializer.Serialize(document.RootElement);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Hashes every immutable input that affects a desktop run. A retry must
    /// use the same fixed encoding preset and bundled config template as the
    /// attempt that originally produced its partial output.
    /// </summary>
    public static string ComputeDesktopContractHash(string configJson)
    {
        ArgumentNullException.ThrowIfNull(configJson);
        var contractText = string.Join(
            "\n",
            PresetVersion,
            ComputePresetHash(),
            ComputeConfigTemplateHash(configJson));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(contractText)));
    }
}
