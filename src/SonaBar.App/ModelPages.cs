namespace SonaBar.App;
public sealed partial class MainWindow
{
    private UIElement Models()
    {
        var intro=Ui.Text("Start with Balanced speech. Add the writing model when you want local rewriting and translation. Downloads are verified before use.",13,true); intro.Margin=new Thickness(0,0,0,14);
        var recommended=Ui.Text("RECOMMENDED",11,true); recommended.FontWeight=FontWeights.Bold; recommended.Margin=new Thickness(0,0,0,10);
        var content=Ui.Stack(intro,recommended);
        var advanced=new StackPanel { Visibility=Visibility.Collapsed };
        foreach(var model in c.Models.Catalog)
        {
            var ready=c.Models.IsInstalled(model);
            var runtimeReady=model.Engine!="Qwen3-ASR"||SonaBar.Transcription.QwenEngine.RuntimeAvailable;
            var status=Ui.Text(!runtimeReady?"Runtime not packaged":ready?"Ready offline":"Not downloaded",13,true); var progress=new ProgressBar { Maximum=100,Visibility=Visibility.Collapsed };
            var pauseButton=Ui.Button("Pause",()=>download?.Cancel()); pauseButton.IsEnabled=false;
            var deleteButton=Ui.Button("Delete",()=> {if(c.Busy)throw new InvalidOperationException("Finish the current operation before deleting a model.");if(Confirm("Delete "+model.Name+" from this computer?")){c.Models.Delete(model);Navigate("Models");}}); deleteButton.IsEnabled=ready;
            Button? useButton=null;
            if(model.Engine is "Whisper" or "NeMo-Speech.cpp" or "Qwen3-ASR") { useButton=Ui.Button(c.Settings.EffectiveSpeechModel==model.Id?"Selected":"Use this model",()=> {if(!c.Models.IsInstalled(model))throw new InvalidOperationException("Download this model first.");if(!runtimeReady)throw new InvalidOperationException("The Qwen runtime is not packaged in this build. Reinstall SonaBar.");c.Settings.SpeechModel=model.Id;c.Settings.UseCustomSpeechModel=true;c.SaveSettings();Navigate("Models");});useButton.IsEnabled=ready&&runtimeReady&&c.Settings.EffectiveSpeechModel!=model.Id; }
            Button? downloadButton=null;
            downloadButton=Ui.AsyncButton(ready?"Verify integrity":"Download / resume",async()=>
            {
                if(ready) { status.Text=await c.Models.VerifyAsync(model,CancellationToken.None)?"SHA-256 verified":"Verification failed. Delete and download this model again."; return; }
                if(download is not null)throw new InvalidOperationException("Pause the current download first.");
                download=new();pauseButton.IsEnabled=true;progress.Visibility=Visibility.Visible;
                try { await c.Models.DownloadAsync(model,new Progress<DownloadProgress>(p=> {progress.Value=p.Percent;status.Text=$"{p.Stage} · {p.Received/1048576:N0} / {p.Total/1048576:N0} MB";}),download.Token); ready=true; status.Text="Ready offline";downloadButton!.Content="Verify integrity";deleteButton.IsEnabled=true;if(useButton is not null&&c.Settings.EffectiveSpeechModel!=model.Id)useButton.IsEnabled=true; }
                catch(OperationCanceledException){status.Text="Paused — choose Download / resume to continue";}
                finally{pauseButton.IsEnabled=false;progress.Visibility=Visibility.Collapsed;download.Dispose();download=null;}
            },!ready);
            // At compact width the model identity and its actions cannot share one row.
            // Stack them so long licence names and the action buttons remain readable.
            var actions=Ui.Row(); actions.VerticalAlignment=VerticalAlignment.Center; actions.HorizontalAlignment=HorizontalAlignment.Left; actions.Margin=new Thickness(0,8,0,0);
            if(useButton is not null)actions.Children.Add(useButton);
            actions.Children.Add(downloadButton); actions.Children.Add(pauseButton); actions.Children.Add(deleteButton);
            var info=Ui.Stack(Ui.Text(model.Name,17),Ui.Text($"{model.Engine}  ·  {model.Bytes/1048576:N0} MB  ·  {model.License}\n{model.Hardware}",12,true),status);
            info.VerticalAlignment=VerticalAlignment.Center;
            var row=new StackPanel();
            row.Children.Add(info); row.Children.Add(actions);
            var card=Ui.Card(row,progress); card.Padding=new Thickness(18,14,18,12);
            if(model.Id is "small" or "qwen3-4b" or "qwen3-asr-1.7b" || model.Id==c.Settings.EffectiveSpeechModel || ready)content.Children.Add(card);
            else advanced.Children.Add(card);
        }
        Button? advancedButton=null; advancedButton=Ui.Button("Show all models  +",()=>{advanced.Visibility=advanced.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;advancedButton!.Content=advanced.Visibility==Visibility.Visible?"Hide other models  −":"Show all models  +";});
        content.Children.Add(advancedButton);content.Children.Add(advanced);
        return content;
    }
    private int welcomeStep;
    private UIElement Welcome()
    {
        heading.Text="Welcome to SonaBar";
        var body=new ContentControl(); var step=Ui.Text($"STEP {welcomeStep+1} OF 6",11,true);
        body.Content=welcomeStep switch
        {
            0=>Ui.Card(Ui.Text("Your voice. Your words.\nYour computer.",34),Ui.Text("A quieter way to write. Speak naturally in your favorite Windows apps, with everything processed on your own computer.",16,true)),
            1=>AudioPage(),
            2=>LanguagePage(),
            3=>Ui.Card(Ui.Text("Choose your starting point",24),Ui.Text("Choose the balance that feels right. Balanced understands natural everyday speech. Your model downloads here once and then works offline.",14,true),Ui.Row(Ui.Button("Fast",()=>{c.Settings.Quality=SpeechQuality.Fast;Navigate("Welcome");}),Ui.Button("Balanced",()=>{c.Settings.Quality=SpeechQuality.Balanced;Navigate("Welcome");}),Ui.Button("Best quality",()=>{c.Settings.Quality=SpeechQuality.Maximum;Navigate("Welcome");})),Ui.Text("Selected: "+c.Settings.Quality),WelcomeDownload()),
            4=>Ui.Card(Ui.Text("Give your voice a try",24),Ui.Text("Click the field below, press "+c.Settings.PlainHotkey+", speak, then press it again. Your words should appear here.",14,true),Ui.Input("",true),Ui.Text("You can change all shortcuts later in Settings.",13,true)),
            _=>Ui.Card(Ui.Text("You're ready to speak.",30),Ui.Text("SonaBar stays in your system tray. Your shortcut is always one press away. Close this window whenever you're ready.",16,true))
        };
        var back=Ui.Button("Back",()=>{welcomeStep--;Navigate("Welcome");});back.IsEnabled=welcomeStep>0;
        return Ui.Stack(step,body,Ui.Row(back,Ui.Button(welcomeStep==5?"Finish":"Continue",()=> {if(welcomeStep==3&&c.Models.ResolveSpeech(c.Settings.EffectiveSpeechModel)is null)throw new InvalidOperationException("Download a speech model to continue."); if(welcomeStep==5){c.Settings.Onboarded=true;c.SaveSettings();Hide();OnboardingCompleted?.Invoke();}else{c.SaveSettings();welcomeStep++;Navigate("Welcome");}},true)));
    }
    private UIElement WelcomeDownload()
    {
        var status=Ui.Text("",13,true); var bar=new ProgressBar{Maximum=100};
        return Ui.Stack(status,bar,Ui.AsyncButton("Download selected speech model",async()=>
        {
            if(download is not null)throw new InvalidOperationException("A model download is already running.");
            var model=c.Models.Catalog.First(x=>x.Id==c.Settings.EffectiveSpeechModel);download=new();
            try{await c.Models.DownloadAsync(model,new Progress<DownloadProgress>(p=>{bar.Value=p.Percent;status.Text=$"{p.Stage} · {p.Percent:N0}%";}),download.Token);status.Text="Ready offline. Continue to test your shortcut.";}
            finally{download.Dispose();download=null;}
        },true),Ui.Button("Pause download",()=>download?.Cancel()));
    }
}
