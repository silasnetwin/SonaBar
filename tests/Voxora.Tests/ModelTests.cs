using System.Net;
using System.Security.Cryptography;
using Voxora.Core;
using Voxora.Models;
using Xunit;
namespace Voxora.Tests;
public sealed class ModelTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"Voxora-model-tests-"+Guid.NewGuid());
    private static readonly byte[] Data=Enumerable.Range(0,4096).Select(i=>(byte)i).ToArray();
    private static ModelDefinition Model=>new("unit","Unit","Whisper","https://models.example.test/model",Convert.ToHexString(SHA256.HashData(Data)),Data.Length,"test","MIT");
    [Fact] public void CatalogHasPinnedHashesAndExpectedModels(){using var manager=new ModelManager(directory);Assert.True(manager.Catalog.Count>=8);Assert.All(manager.Catalog,m=>{Assert.Equal(64,m.Sha256.Length);Assert.StartsWith("https://",m.Url);Assert.True(m.Bytes>0);});Assert.Contains(manager.Catalog,m=>m.Id=="large-v3-turbo");}
    [Fact] public void ParakeetCatalogEntryPinsTheOfficialRevision(){using var manager=new ModelManager(directory);var model=Assert.Single(manager.Catalog,m=>m.Id=="parakeet-tdt-0.6b-v3");Assert.Equal("NeMo-Speech.cpp",model.Engine);Assert.Equal("CC-BY-4.0",model.License);Assert.Contains("541d1f99c6b0c3cd0b11a95167540bb8edefd82b",model.Url);Assert.Equal("e3880d0aaaaf2c308ea2c35016b2b895c423eb3fda924c1b463d1c19b7f4d32e",model.Sha256);}
    [Fact] public async Task DownloadIsVerifiedBeforeInstallation(){using var manager=new ModelManager(directory,new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Data)}));await manager.DownloadAsync(Model,null,default);Assert.True(manager.IsInstalled(Model));Assert.True(await manager.VerifyAsync(Model,default));}
    [Fact] public async Task CorruptDownloadNeverBecomesInstalled(){using var manager=new ModelManager(directory,new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(new byte[Data.Length])}));await Assert.ThrowsAsync<IOException>(()=>manager.DownloadAsync(Model,null,default));Assert.False(manager.IsInstalled(Model));Assert.False(File.Exists(manager.PathFor(Model)+".partial"));}
    [Fact] public async Task PartialDownloadResumesWithRange()
    {
        using var manager=new ModelManager(directory,new Handler(request=>{Assert.Equal(1024,request.Headers.Range!.Ranges.Single().From);var response=new HttpResponseMessage(HttpStatusCode.PartialContent){Content=new ByteArrayContent(Data[1024..])};response.Content.Headers.ContentRange=new(1024,Data.Length-1,Data.Length);return response;}));
        File.WriteAllBytes(manager.PathFor(Model)+".partial",Data[..1024]);await manager.DownloadAsync(Model,null,default);Assert.True(manager.IsInstalled(Model));
    }
    [Fact] public async Task IgnoredRangeRestartsSafely(){using var manager=new ModelManager(directory,new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Data)}));File.WriteAllBytes(manager.PathFor(Model)+".partial",Data[..1024]);await manager.DownloadAsync(Model,null,default);Assert.Equal(Data,File.ReadAllBytes(manager.PathFor(Model)));}
    [Fact] public async Task CancellationKeepsPartialFile(){using var manager=new ModelManager(directory);File.WriteAllBytes(manager.PathFor(Model)+".partial",Data[..128]);using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>manager.DownloadAsync(Model,null,cancel.Token));Assert.Equal(128,new FileInfo(manager.PathFor(Model)+".partial").Length);}
    [Fact] public async Task ChangedInstalledModelIsVerifiedAgain()
    {
        using var manager=new ModelManager(directory,new Handler(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(Data)}));
        await manager.DownloadAsync(Model,null,default);
        File.WriteAllBytes(manager.PathFor(Model),new byte[Data.Length]);
        File.SetLastWriteTimeUtc(manager.PathFor(Model),DateTime.UtcNow.AddSeconds(2));
        await Assert.ThrowsAsync<InvalidDataException>(()=>manager.EnsureVerifiedAsync(Model,default));
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){var response=send(request);response.RequestMessage=request;return Task.FromResult(response);}
    }
    public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
