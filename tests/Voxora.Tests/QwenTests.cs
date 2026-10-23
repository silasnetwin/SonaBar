using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Voxora.Core;
using Voxora.Models;
using Voxora.Transcription;
using Xunit;

namespace Voxora.Tests;

public sealed class QwenTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Voxora-qwen-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] First = Encoding.UTF8.GetBytes(new string('a', 4096));
    private static readonly byte[] Second = Encoding.UTF8.GetBytes(new string('b', 2048));

    [Fact]
    public void MutualProofsAreDirectional()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var proof = QwenProtocol.CreateProof(secret, "client", "nonce");
        Assert.True(QwenProtocol.VerifyProof(secret, "client", "nonce", proof));
        Assert.False(QwenProtocol.VerifyProof(secret, "server", "nonce", proof));
        Assert.False(QwenProtocol.VerifyProof(secret, "client", "other", proof));
        Assert.False(QwenProtocol.VerifyProof(secret, "client", "nonce", "not-base64"));
    }

    [Fact]
    public async Task LengthPrefixedFramesRoundTrip()
    {
        var payload = QwenProtocol.Encode(new { op = "hello", nonce = "abc" });
        using var stream = new MemoryStream();
        await QwenProtocol.WriteFrameAsync(stream, payload, default);
        stream.Position = 0;
        var received = await QwenProtocol.ReadFrameAsync(stream, default);
        Assert.Equal(payload, received);
        using var document = JsonDocument.Parse(received);
        Assert.Equal("hello", document.RootElement.GetProperty("op").GetString());
    }

    [Fact]
    public async Task InvalidFrameSizeIsRejected()
    {
        using var stream = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 0);
        await stream.WriteAsync(header);
        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => QwenProtocol.ReadFrameAsync(stream, default));
    }

    [Fact]
    public void EngineDisposalIsIdempotent()
    {
        var engine = new QwenEngine();
        engine.Dispose();
        engine.Dispose();
    }

    [Fact]
    public void QwenLanguagesUseTheAppTagalogCode()
    {
        Assert.True(QwenLanguages.IsSupported("auto"));
        Assert.True(QwenLanguages.IsSupported("tl"));
        Assert.Equal("fil", QwenLanguages.Normalize("tl"));
        Assert.Equal("tl", QwenLanguages.CodeForName("Filipino"));
        Assert.False(QwenLanguages.IsSupported("he"));
        Assert.Contains("auto", QwenLanguages.Codes);
        Assert.Contains("tl", QwenLanguages.Codes);
        Assert.DoesNotContain("fil", QwenLanguages.Codes);
    }

    [Fact]
    public void SpeechLanguageCapabilitiesFollowTheSelectedEngine()
    {
        using var router = new TranscriptionEngineRouter(new WhisperEngine(), new ParakeetEngine(), new QwenEngine());
        Assert.Contains("auto", router.CapabilitiesFor("parakeet-tdt-0.6b-v3").Languages);
        Assert.Contains("en", router.CapabilitiesFor("parakeet-tdt-0.6b-v3").Languages);
        Assert.DoesNotContain("tl", router.CapabilitiesFor("parakeet-tdt-0.6b-v3").Languages);
        Assert.Contains("tl", router.CapabilitiesFor("qwen3-asr-1.7b").Languages);
        Assert.Contains("yue", router.CapabilitiesFor("qwen3-asr-1.7b").Languages);
        Assert.DoesNotContain("he", router.CapabilitiesFor("qwen3-asr-1.7b").Languages);
        Assert.Equal("tl", router.LanguageFor("qwen3-asr-1.7b", "tl"));
        Assert.Equal("auto", router.LanguageFor("qwen3-asr-1.7b", "he"));
        Assert.Equal("auto", router.LanguageFor("parakeet-tdt-0.6b-v3", "tl"));
        Assert.Equal("en", router.LanguageFor("parakeet-tdt-0.6b-v3", "en"));
    }

    [Fact]
    public void QwenCatalogEntryIsAPinnedBundle()
    {
        using var manager = new ModelManager(directory);
        var model = Assert.Single(manager.Catalog, item => item.Id == "qwen3-asr-1.7b");
        Assert.Equal("Qwen3-ASR", model.Engine);
        Assert.Equal("Apache-2.0", model.License);
        Assert.True(model.IsBundle);
        Assert.Equal(7, model.Files.Count);
        Assert.Equal(model.Bytes, model.Files.Sum(file => file.Bytes));
        Assert.Contains(model.Files, file => file.Path == "model.safetensors" && file.Bytes == 4_076_193_080);
        Assert.Equal(Path.Combine(directory, "qwen3-asr-1.7b"), manager.PathFor(model));
        Assert.All(model.Files, file => Assert.Equal(64, file.Sha256.Length));
    }

    [Fact]
    public async Task BundleVerificationWritesAndRemovesTheFingerprint()
    {
        var model = Bundle();
        var path = Path.Combine(directory, model.Id);
        Directory.CreateDirectory(path);
        await File.WriteAllBytesAsync(Path.Combine(path, "first.bin"), First);
        await File.WriteAllBytesAsync(Path.Combine(path, "second.bin"), Second);
        using var manager = new ModelManager(directory);
        Assert.False(manager.IsInstalled(model));
        Assert.True(await manager.VerifyAsync(model, default));
        Assert.True(manager.IsInstalled(model));
        await File.WriteAllBytesAsync(Path.Combine(path, "second.bin"), new byte[Second.Length]);
        Assert.False(await manager.VerifyAsync(model, default));
        Assert.False(manager.IsInstalled(model));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.EnsureVerifiedAsync(model, default));
        manager.Delete(model);
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public async Task BundleDownloadWritesAllFilesAndReportsCompletion()
    {
        var model = Bundle();
        var handler = new BundleHandler();
        var progress = new List<DownloadProgress>();
        using var manager = new ModelManager(directory, handler);
        await manager.DownloadAsync(model, new InlineProgress<DownloadProgress>(progress.Add), default);
        Assert.True(manager.IsInstalled(model));
        Assert.Equal(First, await File.ReadAllBytesAsync(Path.Combine(manager.PathFor(model), "first.bin")));
        Assert.Equal(Second, await File.ReadAllBytesAsync(Path.Combine(manager.PathFor(model), "second.bin")));
        Assert.Contains(progress, item => item.Stage == "Ready offline" && item.Received == model.Bytes);
        Assert.Equal(2, handler.Requests);
    }

    private static ModelDefinition Bundle()
    {
        var files = new[]
        {
            new ModelFile("first.bin", "https://models.example.test/first", Convert.ToHexString(SHA256.HashData(First)), First.Length),
            new ModelFile("second.bin", "https://models.example.test/second", Convert.ToHexString(SHA256.HashData(Second)), Second.Length)
        };
        return new ModelDefinition("bundle-test", "Bundle", "Qwen3-ASR", "https://models.example.test/bundle", Convert.ToHexString(SHA256.HashData(First)), First.Length + Second.Length, "test", "Apache-2.0") { Files = files };
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class BundleHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var data = request.RequestUri!.AbsolutePath.EndsWith("first", StringComparison.Ordinal) ? First : Second;
            var offset = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(data[(int)offset..])
            };
            if (offset != 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, data.Length - 1, data.Length);
            return Task.FromResult(response);
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
    }
}
