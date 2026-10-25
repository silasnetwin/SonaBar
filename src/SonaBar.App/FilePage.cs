using System.Collections.ObjectModel;
using SonaBar.Audio;
namespace SonaBar.App;
public sealed partial class MainWindow
{
    private readonly ObservableCollection<string> fileRows=[];
    private bool queuePaused;
    private TaskCompletionSource? queueResume;
    private CancellationTokenSource? fileQueue;
    private string exportFormat="TXT";
    private Button? pauseQueueButton, resumeQueueButton, cancelQueueButton;
    private UIElement Files()
    {
        pauseQueueButton=Ui.Button("Pause queue",()=>{if(fileQueue is not null&&!queuePaused){queuePaused=true;queueResume=new(TaskCreationOptions.RunContinuationsAsynchronously);RefreshQueueControls();}});
        resumeQueueButton=Ui.Button("Resume",()=>{queuePaused=false;queueResume?.TrySetResult();RefreshQueueControls();});
        cancelQueueButton=Ui.Button("Cancel",()=>{queuePaused=false;queueResume?.TrySetResult();fileQueue?.Cancel();RefreshQueueControls();});
        var content=Ui.Stack(
            Ui.Card(Ui.Text("Turn recordings into words",24),Ui.Text("Drop audio, video, or a folder here. Files are processed locally, one at a time. Exports are saved in a SonaBar Transcripts subfolder beside each source.",14,true),Ui.Row(Ui.AsyncButton("Choose files…",async()=>{var dialog=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Filter="Audio and video|*.wav;*.mp3;*.m4a;*.aac;*.flac;*.ogg;*.mp4;*.mov;*.mkv;*.webm"};if(dialog.ShowDialog()==true)await ProcessFilesAsync(dialog.FileNames);},true),Ui.AsyncButton("Choose folder…",async()=>{var dialog=new Microsoft.Win32.OpenFolderDialog();if(dialog.ShowDialog()==true)await ProcessFilesAsync([dialog.FolderName]);})),Ui.Text("Export format",12,true),Ui.Choice(new[]{"TXT","SRT","VTT"},exportFormat,x=>exportFormat=x)),
            Ui.Card(Ui.Text("Queue",17),new ListBox{ItemsSource=fileRows,MinHeight=170,MaxHeight=350},Ui.Row(pauseQueueButton,resumeQueueButton,cancelQueueButton)));
        RefreshQueueControls();return content;
    }
    private void RefreshQueueControls()
    {
        if(pauseQueueButton is not null)pauseQueueButton.IsEnabled=fileQueue is not null&&!queuePaused;
        if(resumeQueueButton is not null)resumeQueueButton.IsEnabled=fileQueue is not null&&queuePaused;
        if(cancelQueueButton is not null)cancelQueueButton.IsEnabled=fileQueue is not null;
    }
    private async Task ProcessFilesAsync(IEnumerable<string> paths)
    {
        if (fileQueue is not null) { c.Notify("A file queue is already running."); return; }
        var extensions=new HashSet<string>([".wav",".mp3",".m4a",".aac",".flac",".ogg",".mp4",".mov",".mkv",".webm"],StringComparer.OrdinalIgnoreCase);
        try
        {
            var files=paths.SelectMany(p=>Directory.Exists(p)?Directory.EnumerateFiles(p,"*",SearchOption.TopDirectoryOnly):[p]).Where(p=>extensions.Contains(Path.GetExtension(p))).Distinct().ToList();
            if(files.Count==0)throw new InvalidOperationException("No supported audio or video files found.");
            fileQueue = new CancellationTokenSource(); queuePaused=false; queueResume=null;RefreshQueueControls();
            var token = fileQueue.Token;
            var format=exportFormat;
            fileRows.Clear();foreach(var f in files)fileRows.Add("Pending · "+Path.GetFileName(f));
                for(var i=0;i<files.Count;i++)
                {
                    if(queuePaused&&queueResume is not null)await queueResume.Task.WaitAsync(token);
                    token.ThrowIfCancellationRequested();var file=files[i];var wave=c.TemporaryWave();
                    try
                    {
                        fileRows[i]="Decoding · "+Path.GetFileName(file);await AudioFiles.ConvertAsync(file,wave,token);
                        fileRows[i]="Transcribing · "+Path.GetFileName(file);var result=await c.TranscribeBackgroundAsync(wave,token);
                        result=result with {Text=OutputPipeline.Format(result.Text,c.Settings.OutputCasing),Segments=result.Segments.Select(s=>s with {Text=OutputPipeline.Format(s.Text,c.Settings.OutputCasing)}).ToList()};
                        var folder=Path.Combine(Path.GetDirectoryName(file)!,"SonaBar Transcripts");Directory.CreateDirectory(folder);
                        var content=format=="TXT"?result.Text:TextProcessing.Subtitle(result.Segments,format=="VTT");
                        var output=await WriteExportAsync(folder,Path.GetFileName(file),format,content,token);
                        c.History.Add(new(0,DateTimeOffset.UtcNow,result.Text,"",AudioFiles.Duration(wave).TotalSeconds,result.Language,"File transcription",c.LastSpeechModel),c.Settings.StoreHistory,c.Settings.HistoryRetentionDays);
                        c.SetResult(result.Text);fileRows[i]="Completed · "+output;
                    }
                    catch(OperationCanceledException){fileRows[i]="Cancelled · "+Path.GetFileName(file);throw;}
                    catch(Exception error){fileRows[i]="Failed · "+Path.GetFileName(file)+" · "+error.Message;}
                    finally{File.Delete(wave);}
                }
                c.Notify("File queue finished");
        }
        catch(OperationCanceledException){c.Notify("File queue cancelled");}
        catch(Exception e){c.Notify(e.Message);}
        finally { fileQueue?.Dispose(); fileQueue=null; queuePaused=false; queueResume=null;RefreshQueueControls(); }
    }
    internal static async Task<string> WriteExportAsync(string folder,string sourceName,string format,string content,CancellationToken token)
    {
        var draft=Path.Combine(folder,"."+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            await File.WriteAllTextAsync(draft,content,token);
            var extension="."+format.ToLowerInvariant();
            for(var suffix=0;;suffix++)
            {
                var name=suffix==0?sourceName+extension:$"{sourceName} ({suffix}){extension}";
                var output=Path.Combine(folder,name);
                try { File.Move(draft,output); return output; }
                catch(IOException) when(File.Exists(output)) { }
            }
        }
        finally { if(File.Exists(draft))File.Delete(draft); }
    }
}
