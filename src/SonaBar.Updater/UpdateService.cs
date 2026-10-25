using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
namespace SonaBar.Updater;
public sealed record ReleaseManifest(string Version,string Channel,string Url,string Sha256);
public sealed record UpdateFeedConfiguration(string Feed,string Channel="stable")
{
    public static UpdateFeedConfiguration Load(string applicationDirectory)
    {
        var path=Path.Combine(applicationDirectory,"sonabar-update.json");
        if(!File.Exists(path))return new("", "stable");
        var config=System.Text.Json.JsonSerializer.Deserialize<UpdateFeedConfiguration>(File.ReadAllText(path));
        return config is null ? new("", "stable") : config with { Feed=config.Feed?.Trim() ?? "", Channel=string.IsNullOrWhiteSpace(config.Channel)?"stable":config.Channel.Trim() };
    }
}
public sealed class UpdateService(HttpClient http,Uri feed)
{
    public async Task<ReleaseManifest?> CheckAsync(Version installed,string channel,CancellationToken token)
    {
        if(feed.Scheme!="https")throw new InvalidDataException("Update feeds must use HTTPS.");
        var releases=await http.GetFromJsonAsync<List<ReleaseManifest>>(feed,token)??[];
        return releases.Where(r=>r.Channel==channel&&Version.TryParse(r.Version,out var version)&&version>installed&&Uri.TryCreate(r.Url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&r.Sha256.Length==64&&r.Sha256.All(Uri.IsHexDigit)).OrderByDescending(r=>Version.Parse(r.Version)).FirstOrDefault();
    }
    public async Task DownloadAsync(ReleaseManifest release,string destination,CancellationToken token)
    {
        if(new Uri(release.Url).Scheme!="https")throw new InvalidDataException("Updates must use HTTPS.");
        var partial=destination+".partial";
        try
        {
            await using(var input=await http.GetStreamAsync(release.Url,token))await using(var output=File.Create(partial))await input.CopyToAsync(output,token);
            await using(var file=File.OpenRead(partial))if(!Convert.ToHexString(await SHA256.HashDataAsync(file,token)).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum failed.");
            File.Move(partial,destination,true);
        }
        finally{File.Delete(partial);}
    }
}
