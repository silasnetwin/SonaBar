using System.Net;
using System.Text;
using Voxora.Updater;
using Xunit;

namespace Voxora.Tests;

public sealed class UpdateTests
{
    [Fact]
    public async Task NewestValidStableReleaseIsSelected()
    {
        const string hash="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var body=$"[{{\"Version\":\"0.1.4\",\"Channel\":\"stable\",\"Url\":\"https://updates.example.test/old.exe\",\"Sha256\":\"{hash}\"}},{{\"Version\":\"0.1.5\",\"Channel\":\"stable\",\"Url\":\"https://updates.example.test/new.exe\",\"Sha256\":\"{hash}\"}},{{\"Version\":\"0.2.0\",\"Channel\":\"beta\",\"Url\":\"https://updates.example.test/beta.exe\",\"Sha256\":\"{hash}\"}}]";
        using var http=new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")}));
        var update=new UpdateService(http,new Uri("https://updates.example.test/releases.json"));
        var release=await update.CheckAsync(new Version(0,1,3),"stable",default);
        Assert.NotNull(release);Assert.Equal("0.1.5",release.Version);
    }

    [Fact]
    public void MissingFeedConfigurationStaysOffline()
    {
        var directory=Path.Combine(Path.GetTempPath(),"SineFlow-update-tests-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try { Assert.Equal("",UpdateFeedConfiguration.Load(directory).Feed); }
        finally { Directory.Delete(directory,true); }
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var response=send(request);response.RequestMessage=request;return Task.FromResult(response);
        }
    }
}
