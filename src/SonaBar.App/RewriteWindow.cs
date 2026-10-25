using SonaBar.Storage;
namespace SonaBar.App;
public sealed class RewriteWindow : ThemedWindow
{
    public string? Result { get; private set; }
    private readonly CancellationTokenSource cancellation = new();
    public RewriteWindow(AppController controller,string selected)
    {
        Title="SonaBar · Writing assistant"; Width=720;Height=700;MinWidth=540;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var source=Ui.Input(selected,true);var instruction=Ui.Input();var result=Ui.Input("",true);var status=Ui.Text("",12,true);
        source.MinHeight=130;result.MinHeight=150;result.IsReadOnly=true;
        var copy=Ui.Button("Copy result",()=>{if(result.Text.Length>0)controller.Clipboard.SetText(result.Text);},true);copy.IsEnabled=false;
        result.TextChanged+=(_,_)=>copy.IsEnabled=!string.IsNullOrWhiteSpace(result.Text);
        var actions=Ui.Choice(RewriteActions.All,RewriteActions.All[0],_=>{});
        var language=Ui.Choice(Languages.All.Values.Where(x=>x!="Automatic").ToArray(),"English",_=>{});
        var run=Ui.AsyncButton("Rewrite locally",async()=>
        {
            if(string.IsNullOrWhiteSpace(source.Text))throw new InvalidOperationException("Enter some text first.");
            status.Text="Improving text on your computer…";
            var action=(RewriteAction)actions.SelectedItem;
            await controller.RunExclusiveAsync(async token=> {using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,cancellation.Token);result.Text=action.Id=="translate"?await controller.TranslateAsync(source.Text,(string)language.SelectedItem,linked.Token):await controller.EnhanceAsync(source.Text,(action.Id=="prompt"?OutputPipeline.EditingInstruction(controller.Settings,"",new(source.Text,SpokenAction.ChatGptPrompt)):action.Instruction)+". "+instruction.Text,linked.Token);});status.Text="Review your revised text before replacing the selection.";
        },true);
        string? recording=null;
        var record=Ui.AsyncButton("Speak instruction / stop",async()=>
        {
            if(recording is null){if(controller.Busy||controller.Audio.IsCapturing)throw new InvalidOperationException("Finish the current operation first.");recording=controller.TemporaryWave();try{controller.Audio.Start(recording,CaptureMode.Microphone,controller.Settings.MicrophoneId);}catch{recording=null;throw;}status.Text="Listening to your instruction…";}
            else {await controller.Audio.StopAsync();try{await controller.RunExclusiveAsync(async token=>instruction.Text=(await controller.TranscribeAsync(recording,token)).Text);}finally{File.Delete(recording);recording=null;}status.Text="Instruction ready.";}
        });
        var intro=Ui.Hero(Ui.Text("Make it sound like you.",28),Ui.Text("Rewrite and translate with your local writing model.",14,true));intro.Padding=new Thickness(22,19,22,14);
        Content=new ScrollViewer{Content=new StackPanel{Margin=new Thickness(26,24,26,16),Children={intro,Ui.Panel(Ui.Text("Your text",17),source),Ui.Panel(Ui.Text("How should it change?",17),actions,language,Ui.Text("Additional instruction",12,true),instruction,Ui.Row(run,record),status),Ui.Panel(Ui.Text("Revised text",17),result,Ui.Row(copy,Ui.Button("Close",()=>{cancellation.Cancel();Close();}))) }},VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Closed+=async(_,_)=>{cancellation.Cancel();try{if(recording is not null){await controller.Audio.StopAsync();File.Delete(recording);}}catch(Exception error){SafeLog.Write("RewriteRecording",error);}finally{cancellation.Dispose();}};
    }
}
