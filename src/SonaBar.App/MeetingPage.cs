using System.Diagnostics;
using System.Windows.Threading;
using SonaBar.Audio;
using SonaBar.Meetings;
namespace SonaBar.App;
public sealed partial class MainWindow
{
    private MeetingDocument meeting=new();
    private TaskCompletionSource? meetingStop;
    private CaptureMode meetingMode=CaptureMode.Both;
    private bool labelSpeakers;
    private TextBox meetingText=Ui.Input("",true);
    private TextBlock? meetingClockLabel;
    private Button? meetingStartButton,meetingStopButton,meetingCancelButton;
    private Stopwatch? meetingClock;
    private UIElement Meetings()
    {
        meetingText=Ui.Input(meeting.Segments.Count>0?meeting.Text():"",true);
        var title=Ui.Input(meeting.Title);title.TextChanged+=(_,_)=>meeting.Title=title.Text;
        meetingClockLabel=Ui.Text(meetingStop is null?"Ready to record":meetingClock is null?"Recording…":$"Recording · {meetingClock.Elapsed:hh\\:mm\\:ss}",13,true);meetingText.MinHeight=220;meetingText.IsReadOnly=true;
        var from=Ui.Input("Speaker 1");var to=Ui.Input();
        meetingStartButton=Ui.AsyncButton("Start meeting",()=>StartMeetingAsync(),true);
        meetingStopButton=Ui.Button("Stop and finish",()=>meetingStop?.TrySetResult());
        meetingCancelButton=Ui.Button("Cancel",()=>{meetingStop?.TrySetResult();c.Cancel();});
        var content=Ui.Stack(
            Ui.Card(Ui.Text("Be present. Keep the words.",24),Ui.Text("Live transcripts arrive in roughly 20-second sections. Make sure everyone knows the meeting is being recorded.",14,true),Ui.Text("Meeting title",12,true),title,Ui.Text("Capture source",12,true),Ui.Choice(Enum.GetValues<CaptureMode>(),meetingMode,x=>meetingMode=x),Ui.Check("Recognize speakers locally (download WeSpeaker in Models)",labelSpeakers,x=>labelSpeakers=x),meetingClockLabel,Ui.Row(meetingStartButton,meetingStopButton,meetingCancelButton)),
            Ui.Card(Ui.Text("Transcript",17),meetingText,Ui.Text("Rename a speaker throughout this transcript",13,true),from,to,Ui.Button("Rename",()=>{if(!string.IsNullOrWhiteSpace(to.Text)){meeting.Rename(from.Text,to.Text);meetingText.Text=meeting.Text();}}),Ui.Row(Ui.Button("Export TXT…",()=>ExportMeeting("txt")),Ui.Button("Export Markdown…",()=>ExportMeeting("md")),Ui.Button("Export PDF…",()=>ExportMeeting("pdf")),Ui.AsyncButton("Summarize locally",async()=>{await c.RunExclusiveAsync(async token=>meeting.Summary=await c.EnhanceAsync(meeting.Text(),"Summarize this meeting. Include separate sections: Summary, Decisions, Action items. Attribute tasks only when explicitly stated. Do not invent decisions.",token,false));meetingText.Text=meeting.Text();}))));
        RefreshMeetingControls();return content;
    }
    private void RefreshMeetingControls()
    {
        if(meetingStartButton is not null)meetingStartButton.IsEnabled=meetingStop is null&&!c.Busy;
        if(meetingStopButton is not null)meetingStopButton.IsEnabled=meetingStop is not null;
        if(meetingCancelButton is not null)meetingCancelButton.IsEnabled=meetingStop is not null;
    }
    private async Task StartMeetingAsync()
    {
        if(c.Busy)throw new InvalidOperationException("Finish the current operation first.");
        if(c.Models.ResolveSpeech(c.Settings.EffectiveSpeechModel)is null)throw new InvalidOperationException("Download a speech model first.");
        var speakerModel=c.Models.Catalog.First(x=>x.Id=="wespeaker");
        if(labelSpeakers&&!c.Models.IsInstalled(speakerModel))throw new InvalidOperationException("Download WeSpeaker from Models first, or turn off speaker recognition.");
        meeting=new(){Title=meeting.Title};meetingText.Clear();
        try { await c.RunExclusiveAsync(async token=>
        {
            using var capture=new MeetingCapture(Path.Combine(Brand.DataRoot,"Temp"));using var diarization=new SpeakerDiarization();meetingStop=new(TaskCreationOptions.RunContinuationsAsynchronously);
            capture.Error+=message=>Dispatcher.BeginInvoke(()=>c.Notify(message));
            var clock=Stopwatch.StartNew();meetingClock=clock;RefreshMeetingControls();
            var timer=new DispatcherTimer(TimeSpan.FromSeconds(1),DispatcherPriority.Background,(_,_)=>{if(meetingClockLabel is not null)meetingClockLabel.Text=$"Recording · {clock.Elapsed:hh\\:mm\\:ss}";},Dispatcher);
            async Task Consume()
            {
                await foreach(var chunk in capture.Chunks.ReadAllAsync())
                {
                    var wave=c.TemporaryWave();
                    try
                    {
                        token.ThrowIfCancellationRequested();await AudioFiles.ConvertAsync(chunk.Path,wave,token);var result=await c.TranscribeAsync(wave,token);
                        var segments=labelSpeakers?await diarization.LabelAsync(wave,result.Segments,c.Models.PathFor(speakerModel),token):result.Segments.Select(s=>s with {Speaker=chunk.Source}).ToList();
                        meeting.Segments.AddRange(segments.Select(s=>s with {Start=s.Start+chunk.Offset,End=s.End+chunk.Offset,Text=OutputPipeline.Format(s.Text,c.Settings.OutputCasing)}));meeting.Duration=clock.Elapsed;meetingText.Text=meeting.Text();meetingText.ScrollToEnd();
                    }
                    catch(OperationCanceledException){}
                    catch(Exception error){c.Notify("A meeting section could not be transcribed: "+error.Message);}
                    finally{File.Delete(wave);File.Delete(chunk.Path);}
                }
            }
            Task? consumer=null;
            try
            {
                capture.Start(meetingMode,c.Settings.MicrophoneId);timer.Start();consumer=Consume();
                await meetingStop.Task.WaitAsync(token);
            }
            finally
            {
                timer.Stop();meeting.Duration=clock.Elapsed;if(meetingClockLabel is not null)meetingClockLabel.Text="Finishing transcript…";
                try { await capture.StopAsync();if(consumer is not null)await consumer; }
                finally { meetingStop=null;meetingClock=null;if(meetingClockLabel is not null)meetingClockLabel.Text=$"Finished · {meeting.Duration:hh\\:mm\\:ss}";RefreshMeetingControls(); }
            }
            token.ThrowIfCancellationRequested();
            var text=meeting.Text();c.SetResult(text);c.History.Add(new(0,DateTimeOffset.UtcNow,text,"",meeting.Duration.TotalSeconds,c.Settings.Language,"Meeting",c.LastSpeechModel),c.Settings.StoreHistory,c.Settings.HistoryRetentionDays);
        }); }
        finally { RefreshMeetingControls(); }
    }
    private void ExportMeeting(string extension)
    {
        if(meeting.Segments.Count==0)throw new InvalidOperationException("Record a meeting before exporting.");
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter=$"{extension.ToUpperInvariant()} files|*.{extension}",FileName="SonaBar-meeting."+extension};if(dialog.ShowDialog()!=true)return;
        if(extension=="pdf")MeetingExport.Pdf(meeting,dialog.FileName);else File.WriteAllText(dialog.FileName,meeting.Text(extension=="md"));
        c.Inform("Meeting exported");
    }
}
