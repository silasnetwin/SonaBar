using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Voxora.Transcription;

/// <summary>Length-prefixed JSON messages used only on SineFlow's private named pipe.</summary>
public static class QwenProtocol
{
    public const int MaxFrameBytes = 8 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    public static byte[] Encode(object message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length <= 0 || payload.Length > MaxFrameBytes) throw new InvalidDataException("Invalid sidecar message size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException("Invalid sidecar message size.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
        return payload;
    }
    /// <summary>HMAC proof used in both directions. The secret never crosses the pipe.</summary>
    public static string CreateProof(string secretBase64, string role, string nonce)
    {
        var secret = Convert.FromBase64String(secretBase64);
        return Convert.ToBase64String(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"sineflow-qwen-{role}:{nonce}")));
    }
    public static bool VerifyProof(string secretBase64, string role, string nonce, string? proof)
    {
        if (string.IsNullOrWhiteSpace(proof)) return false;
        try
        {
            var expected = Convert.FromBase64String(CreateProof(secretBase64, role, nonce));
            var actual = Convert.FromBase64String(proof);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>The languages Qwen3-ASR can transcribe. "tl" is the app's code for Filipino.</summary>
public static class QwenLanguages
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ar"] = "Arabic", ["yue"] = "Cantonese", ["zh"] = "Chinese", ["cs"] = "Czech",
        ["da"] = "Danish", ["nl"] = "Dutch", ["en"] = "English", ["fil"] = "Filipino",
        ["fi"] = "Finnish", ["fr"] = "French", ["de"] = "German", ["el"] = "Greek",
        ["hi"] = "Hindi", ["hu"] = "Hungarian", ["id"] = "Indonesian", ["it"] = "Italian",
        ["ja"] = "Japanese", ["ko"] = "Korean", ["mk"] = "Macedonian", ["ms"] = "Malay",
        ["fa"] = "Persian", ["pl"] = "Polish", ["pt"] = "Portuguese", ["ro"] = "Romanian",
        ["ru"] = "Russian", ["es"] = "Spanish", ["sv"] = "Swedish", ["th"] = "Thai",
        ["tr"] = "Turkish", ["vi"] = "Vietnamese"
    };
    public static IReadOnlyCollection<string> Codes { get; } = ["auto", .. Names.Keys.Where(code => code != "fil"), "tl"];
    public static bool IsSupported(string? code) => !string.IsNullOrWhiteSpace(code) && (code.Equals("auto", StringComparison.OrdinalIgnoreCase) || Names.ContainsKey(Normalize(code)));
    public static string Normalize(string code) => code.Equals("tl", StringComparison.OrdinalIgnoreCase) ? "fil" : code.ToLowerInvariant();
    public static string? NameFor(string? code) => string.IsNullOrWhiteSpace(code) ? null : Names.GetValueOrDefault(Normalize(code));
    public static string? CodeForName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var pair in Names)
            if (pair.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                return pair.Key == "fil" ? "tl" : pair.Key;
        return null;
    }
}
