using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SonaBar.Audio;
using SonaBar.Core;

namespace SonaBar.Meetings;

public sealed record AudioChunk(string Path, TimeSpan Offset, string Source);
public sealed class MeetingCapture : IDisposable
{
    private readonly List<Track> tracks=[];
    private readonly Channel<AudioChunk> chunks=Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions { SingleReader=true });
    private readonly string directory;
    public ChannelReader<AudioChunk> Chunks => chunks.Reader;
    public event Action<float>? Level;
    public event Action<string>? Error;
    public MeetingCapture(string directory) {this.directory=directory;}
    public void Start(CaptureMode mode,string? deviceId)
    {
        using var devices=new MMDeviceEnumerator();
        try
        {
            if(mode is CaptureMode.Microphone or CaptureMode.Both)
            {
                using var device=deviceId is null?devices.GetDefaultAudioEndpoint(DataFlow.Capture,Role.Communications):devices.GetDevice(deviceId);
                tracks.Add(new Track(new WasapiCapture(device),"Microphone",directory,chunks.Writer,x=>Level?.Invoke(x),x=>Error?.Invoke(x)));
            }
            if(mode is CaptureMode.System or CaptureMode.Both)tracks.Add(new Track(new WasapiLoopbackCapture(),"System audio",directory,chunks.Writer,x=>Level?.Invoke(x),x=>Error?.Invoke(x)));
            foreach(var track in tracks)track.Start();
        }
        catch {Dispose();throw;}
    }
    public async Task StopAsync()
    {
        foreach(var track in tracks)await track.StopAsync();
        chunks.Writer.TryComplete();
    }
    public void Dispose(){foreach(var track in tracks)track.Dispose();tracks.Clear();chunks.Writer.TryComplete();}
    private sealed class Track : IDisposable
    {
        private readonly WasapiCapture capture;
        private readonly string source, directory;
        private readonly ChannelWriter<AudioChunk> output;
        private readonly TaskCompletionSource stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MemoryStream buffer=new();
        private long written;
        private readonly int chunkSize;
        private bool stopping;
        public Track(WasapiCapture capture,string source,string directory,ChannelWriter<AudioChunk> output,Action<float> level,Action<string> error)
        {
            this.capture=capture;this.source=source;this.directory=directory;this.output=output;chunkSize=capture.WaveFormat.AverageBytesPerSecond*20;
            capture.DataAvailable+=(_,e)=>
            {
                lock(buffer)
                {
                    buffer.Write(e.Buffer,0,e.BytesRecorded);
                    if(capture.WaveFormat.BitsPerSample==32&&e.BytesRecorded>=4)level(Math.Clamp(Math.Abs(BitConverter.ToSingle(e.Buffer,0)),0,1));
                    if(buffer.Length>=chunkSize)Flush();
                }
            };
            capture.RecordingStopped+=(_,e)=>{lock(buffer){Flush();}if(e.Exception is not null&&!stopping)error("An audio device disconnected. Stop the meeting to finish processing the captured audio.");stopped.TrySetResult();};
        }
        private void Flush()
        {
            if(buffer.Length==0)return;
            var path=Path.Combine(directory,Guid.NewGuid()+".wav");
            using(var writer=new WaveFileWriter(path,capture.WaveFormat)){buffer.Position=0;buffer.CopyTo(writer);}
            output.TryWrite(new(path,TimeSpan.FromSeconds(written/(double)capture.WaveFormat.AverageBytesPerSecond),source));written+=buffer.Length;buffer.SetLength(0);
        }
        public void Start()=>capture.StartRecording();
        public async Task StopAsync(){stopping=true;capture.StopRecording();await stopped.Task.WaitAsync(TimeSpan.FromSeconds(8));}
        public void Dispose(){capture.Dispose();buffer.Dispose();}
    }
}
public interface ISpeakerDiarizationService
{
    Task<IReadOnlyList<TranscriptSegment>> LabelAsync(string wave,IReadOnlyList<TranscriptSegment> segments,string model,CancellationToken token);
}
public sealed class MeetingDocument
{
    public string Title {get;set;}="Meeting";
    public DateTimeOffset Date {get;set;}=DateTimeOffset.Now;
    public TimeSpan Duration {get;set;}
    public List<TranscriptSegment> Segments {get;}=[];
    public string Summary {get;set;}="";
    public void Rename(string oldName,string newName){for(int i=0;i<Segments.Count;i++)if(Segments[i].Speaker==oldName)Segments[i]=Segments[i] with {Speaker=newName};}
    public string Text(bool markdown=false)=>$"{(markdown?"# ":"")}{Title}\n{Date:g} · {Duration:g}\nParticipants: {string.Join(", ",Segments.Select(x=>x.Speaker).Where(x=>x.Length>0).Distinct())}\n\n"+string.Join("\n\n",Segments.OrderBy(x=>x.Start).Select(x=>$"[{x.Start:hh\\:mm\\:ss}] {x.Speaker}\n{x.Text}"))+(Summary.Length>0?"\n\nSummary\n"+Summary:"");
}
