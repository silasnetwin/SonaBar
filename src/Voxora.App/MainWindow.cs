using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Input;
using Voxora.Storage;

namespace Voxora.App;
public sealed partial class MainWindow : ThemedWindow
{
    private readonly AppController c;
    private readonly ContentControl page = new();
    private readonly TextBlock heading = Ui.Text("Overview",34);
    private readonly TextBlock subtitle = Ui.Text("",14,true);
    private readonly ScrollViewer pageScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private Button? footerSave;
    private readonly Dictionary<string,System.Windows.Controls.Primitives.ToggleButton> navigationButtons=[];
    private readonly Dictionary<string,StackPanel> navigationGroups=[];
    private Button? moreToggle;
    private CancellationTokenSource? download;
    public MainWindow(AppController controller)
    {
        c = controller; Title = "SineFlow";
        // The window never opens larger than the screen, so the title bar and the footer stay reachable on small laptops.
        var workArea = SystemParameters.WorkArea; Width = Math.Min(1240, workArea.Width - 32); Height = Math.Min(820, workArea.Height - 48); MinWidth = 940; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterScreen; DataContext = c;
        UseNativeChrome();
        var layout = new Grid(); layout.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"Surface"); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new Grid(); sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); sidebar.RowDefinitions.Add(new RowDefinition()); sidebar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var identity = new StackPanel { Margin = new Thickness(22,20,18,16) };
        var logo = new System.Windows.Controls.Image { Width=30,Height=30,HorizontalAlignment=HorizontalAlignment.Left,Stretch=Stretch.Uniform,Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/SineFlow.App;component/Assets/SineFlow-Mark.png",UriKind.Absolute)) };
        System.Windows.Automation.AutomationProperties.SetName(logo,"SineFlow logo");
        var brandRow=Ui.Row(logo,Ui.Stack(Ui.Text("SineFlow",17),Ui.Text("LOCAL & PRIVATE",9,true))); brandRow.Margin=new Thickness(0);identity.Children.Add(brandRow);
        var presence=Ui.StatusPill("●  Ready to listen");presence.Margin=new Thickness(0,14,0,0);
        if(presence.Child is TextBlock presenceText)presenceText.SetBinding(TextBlock.TextProperty,new Binding(nameof(AppController.PresenceLabel)));
        identity.Children.Add(presence); sidebar.Children.Add(identity);
        var navigation = new StackPanel { Margin = new Thickness(12,0,12,12) };
        AddNavigationGroup(navigation,"WORKSPACE",false,("Overview","Home"),("History","History"),("Files","Files"),("Meetings","Meetings"));
        AddNavigationGroup(navigation,"SETTINGS",false,("General","Settings"));
        AddNavigationGroup(navigation,"MORE",true,("Custom Words","Dictionary"),("Snippets","Snippets"),("Applications","App profiles"),("Privacy","Privacy"),("Advanced","Advanced"));
        var navScroll = new ScrollViewer { Content=navigation,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }; Grid.SetRow(navScroll,1); sidebar.Children.Add(navScroll);
        var start=Ui.Button("Start dictation",c.ToggleDictation,true);start.HorizontalAlignment=HorizontalAlignment.Stretch;start.Margin=new Thickness(22,0,22,9);
        var shortcutHint=Ui.Text("Ctrl + Shift + Space",11,true);shortcutHint.HorizontalAlignment=HorizontalAlignment.Center;shortcutHint.Margin=new Thickness(0,0,0,19);
        var sidebarFooter=new StackPanel();sidebarFooter.Children.Add(start);sidebarFooter.Children.Add(shortcutHint);Grid.SetRow(sidebarFooter,2); sidebar.Children.Add(sidebarFooter);
        var side = new Border { BorderThickness = new Thickness(0,0,1,0), Child = sidebar }; side.SetResourceReference(Border.BackgroundProperty,"Sidebar"); side.SetResourceReference(Border.BorderBrushProperty,"Line"); layout.Children.Add(side);
        var main = new Grid { Margin = new Thickness(40,28,40,20) }; main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header=new Grid { MaxWidth=920,HorizontalAlignment=HorizontalAlignment.Stretch }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        var titles=Ui.Stack(heading,subtitle); heading.Margin=new Thickness(0,0,0,1); header.Children.Add(titles);
        var privacy=Ui.StatusPill("●  Local & private");privacy.VerticalAlignment=VerticalAlignment.Top;privacy.Margin=new Thickness(10,3,0,0);Grid.SetColumn(privacy,1); header.Children.Add(privacy); main.Children.Add(header);
        pageScroll.Content=page; pageScroll.Margin=new Thickness(0,26,0,14); pageScroll.MaxWidth=920; pageScroll.HorizontalAlignment=HorizontalAlignment.Stretch; Grid.SetRow(pageScroll,1); main.Children.Add(pageScroll);
        var status=Ui.Text("",12,true); status.Margin=new Thickness(0); status.SetBinding(TextBlock.TextProperty,new Binding(nameof(AppController.StageLabel)));
        var footer=new Grid(); footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); status.VerticalAlignment=VerticalAlignment.Center; footer.Children.Add(status);
        footerSave=Ui.Button("Save settings",()=>{c.SaveSettings();c.Inform("Settings saved");},true); footerSave.Margin=new Thickness(10,0,0,0); Grid.SetColumn(footerSave,1); footer.Children.Add(footerSave);
        var statusBorder=new Border { BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,14,0,0),Child=footer,MaxWidth=920,HorizontalAlignment=HorizontalAlignment.Stretch }; statusBorder.SetResourceReference(Border.BorderBrushProperty,"Line"); Grid.SetRow(statusBorder,2); main.Children.Add(statusBorder); Grid.SetColumn(main,1); layout.Children.Add(main);
        Content=layout;
        Closing += (_,e) => { e.Cancel=true; Hide(); };
        AllowDrop = true; Drop += async (_,e) => { if(e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files) { Navigate("Files"); await ProcessFilesAsync(files); } };
        Navigate("Overview");
    }
    private void AddNavigationGroup(StackPanel parent,string title,bool collapsible,params (string Key,string Label)[] items)
    {
        var group=new StackPanel(); navigationGroups[title]=group;
        var label=Ui.Text(title+(collapsible?"  +":""),10,true); label.FontWeight=FontWeights.SemiBold; label.Margin=new Thickness(11,12,0,7);
        if(collapsible)
        {
            Button? toggle=null; toggle=Ui.Button(title+"  +",()=>{group.Visibility=group.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;toggle!.Content=title+(group.Visibility==Visibility.Visible?"  −":"  +");});
            if(title=="MORE")moreToggle=toggle;
            toggle.Style=(Style)FindResource("SectionButton"); toggle.Margin=new Thickness(0,10,0,5); parent.Children.Add(toggle); group.Visibility=Visibility.Collapsed;
        }
        else parent.Children.Add(label);
        foreach(var (key,caption) in items)
        {
            var button=new System.Windows.Controls.Primitives.ToggleButton { Content=caption,Style=(Style)FindResource("NavigationButton") };
            button.Click+=(_,_)=>Navigate(key); group.Children.Add(button); navigationButtons[key]=button;
        }
        parent.Children.Add(group);
    }
    public void Navigate(string name)
    {
        heading.Text=name switch { "Overview"=>"Your words, anywhere.","Custom Words"=>"Dictionary","Applications"=>"App profiles","Audio"=>"Microphone","General"=>"Settings",_=>name };
        // The overview carries its own headline, so a second one above it would only repeat
        // the same message and make the window look like a web dashboard.
        var showHeader = name is not ("Overview" or "Welcome");
        heading.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
        subtitle.Visibility = showHeader ? Visibility.Visible : Visibility.Collapsed;
        subtitle.Text=name switch { "Overview"=>"Speak naturally. SineFlow turns it into text on your computer.","Files"=>"Turn recordings into searchable text.","Meetings"=>"Capture conversations and keep the important parts.","History"=>"Find and reuse previous dictations.","Models"=>"Manage the local engines behind your workflow.","Dictation"=>"Make speech recognition work your way.","General"=>"Make SineFlow yours, without losing focus.","Privacy"=>"Your data stays under your control.",_=>"Set up your private writing workspace." };
        foreach(var (key,button) in navigationButtons)button.IsChecked=key==name;
        if(footerSave is not null)footerSave.Visibility=name is "General" or "Dictation" or "Writing" or "Audio" or "Shortcuts" or "Privacy" or "Advanced" or "Debug" or "Languages"?Visibility.Visible:Visibility.Collapsed;
        if(name is "Custom Words" or "Snippets" or "Applications" or "Privacy" or "Advanced") { navigationGroups["MORE"].Visibility=Visibility.Visible;if(moreToggle is not null)moreToggle.Content="MORE  −"; }
        page.Content = name switch
        {
            "Welcome" => Welcome(), "Overview" => Overview(), "General" => General(), "Dictation" => Dictation(), "Writing" => WritingTools(), "Models" => Models(), "Languages" => LanguagePage(), "Audio" => AudioPage(), "Shortcuts" => Shortcuts(), "Custom Words" => CustomWords(), "Snippets" => Snippets(), "Applications" => Applications(), "History" => History(), "Statistics" => Statistics(), "Privacy" => Privacy(), "Advanced" => Advanced(), "Debug" => Debug(), "About" => About(), "Files" => Files(), "Meetings" => Meetings(), _ => Overview()
        };
        pageScroll.ScrollToTop();
    }
    private UIElement Overview()
    {
        var last = Ui.Input(c.LastText,true); last.IsReadOnly = true; last.SetBinding(TextBox.TextProperty,new Binding(nameof(AppController.LastText)) { Mode = BindingMode.OneWay });
        last.MinHeight=125;last.MaxHeight=260;
        var actions=Ui.Button("Refine text  ···",()=>{});actions.Click+=(_,_)=>{actions.ContextMenu=QuickActions.Create(c);actions.ContextMenu.PlacementTarget=actions;actions.ContextMenu.IsOpen=true;};
        var readiness=Ui.Text(c.ModelPreparation,12,true);readiness.SetBinding(TextBlock.TextProperty,new Binding(nameof(AppController.ModelPreparation)));
        var eyebrow=Ui.Text("SINEFLOW  /  VOICE WORKSPACE",11);eyebrow.FontWeight=FontWeights.Bold;eyebrow.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        var heroTitle=Ui.Text("Speak naturally.\nText follows.",30);heroTitle.LineHeight=36;heroTitle.Margin=new Thickness(0,7,0,10);
        var shortcut=Ui.Text(c.Settings.PlainHotkey.ToString(),14);shortcut.FontWeight=FontWeights.Bold;shortcut.Margin=new Thickness(0);
        var shortcutBadge=new Border{Child=shortcut,Padding=new Thickness(12,8,12,8),CornerRadius=new CornerRadius(10),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,0)};
        shortcutBadge.SetResourceReference(Border.BackgroundProperty,"Card");
        var begin=Ui.Button("Start a dictation",c.ToggleDictation,true);begin.Margin=new Thickness(12,16,8,0);
        var configure=Ui.Button("Set up input",()=>Navigate("Dictation"));configure.Margin=new Thickness(0,16,8,0);
        shortcutBadge.VerticalAlignment=VerticalAlignment.Center;shortcutBadge.Margin=new Thickness(0,16,0,0);
        var heroCopy=Ui.Stack(eyebrow,heroTitle,Ui.Text("Put your cursor where you want the words. SineFlow listens, understands and places the result there — privately on this PC.",14,true),Ui.Row(shortcutBadge,begin,configure));
        var heroGrid=new Grid();heroGrid.Children.Add(heroCopy);
        var hero=Ui.Panel(heroGrid,readiness);hero.Padding=new Thickness(26,24,26,22);
        var flowLabel=Ui.Text("WHERE TO GO NEXT",11,true);flowLabel.FontWeight=FontWeights.Bold;flowLabel.Margin=new Thickness(0,4,0,7);
        Button FlowCard(string number,string title,string detail,string destination)
        {
            var index=Ui.Text(number,11);index.SetResourceReference(TextBlock.ForegroundProperty,"Accent");index.FontWeight=FontWeights.Bold;
            var name=Ui.Text(title,17);name.Margin=new Thickness(0,7,0,3);
            var description=Ui.Text(detail,12,true);description.Margin=new Thickness(0);
            var button=Ui.Button("",()=>Navigate(destination));button.Content=Ui.Stack(index,name,description);button.Height=116;button.Padding=new Thickness(16,14,16,12);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.HorizontalAlignment=HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(button,title+" — "+detail);
            return button;
        }
        // The three entry points share the full content width instead of huddling on the left.
        var flows=new Grid(); for(var i=0;i<3;i++)flows.ColumnDefinitions.Add(new ColumnDefinition());
        var flowCards=new[]{FlowCard("01","Dictation","Write directly into any app.","Dictation"),FlowCard("02","Files","Turn recordings into text.","Files"),FlowCard("03","Meetings","Keep a conversation's words.","Meetings")};
        for(var i=0;i<3;i++){flowCards[i].Margin=new Thickness(0,0,i<2?12:0,12);Grid.SetColumn(flowCards[i],i);flows.Children.Add(flowCards[i]);}
        var latestTitle=Ui.Text("Latest words",20);
        var latest=Ui.Panel(latestTitle,last,Ui.Row(Ui.Button("Copy text",()=> { if(c.LastText.Length>0) c.Clipboard.SetText(c.LastText); },true),actions,Ui.Button("View history",()=>Navigate("History"))));
        return Ui.Stack(hero,flowLabel,flows,latest);
    }
    private UIElement General()
    {
        var appearance=Ui.Choice(new[]{"System","Light","Dark"},c.Settings.Theme,x=> { c.Settings.Theme=x; Theme.Apply(x); });
        var startup=Ui.Check("",c.Settings.LaunchAtLogin,x=>c.Settings.LaunchAtLogin=x);
        var updates=Ui.Check("",c.Settings.AutomaticUpdates,x=>c.Settings.AutomaticUpdates=x);
        Button Destination(string title,string detail,string pageName)
        {
            var button=Ui.Button("",()=>Navigate(pageName));button.Content=Ui.Stack(Ui.Text(title+"  ›",15),Ui.Text(detail,12,true));button.HorizontalContentAlignment=HorizontalAlignment.Left;button.HorizontalAlignment=HorizontalAlignment.Stretch;button.MinWidth=220;button.MinHeight=78;button.Margin=new Thickness(0,0,10,10);return button;
        }
        var shortcuts=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,2,0,4)};
        foreach(var item in new[]{("Dictation","Language, formatting and voice controls.","Dictation"),("Microphone","Input device and audio behavior.","Audio"),("Shortcuts","Global keys and push-to-talk.","Shortcuts"),("Models","Speech and writing engines.","Models"),("Writing","Local rewrite and translation.","Writing"),("Dictionary","Names and recurring corrections.","Custom Words"),("Privacy","History and local data controls.","Privacy"),("More settings","Snippets, app profiles and advanced tools.","Advanced")})shortcuts.Children.Add(Destination(item.Item1,item.Item2,item.Item3));
        return Ui.Stack(Ui.Card(Ui.Text("Appearance & startup",18),Ui.SettingRow("Appearance","Match Windows or choose a fixed theme.",appearance),Ui.SettingRow("Start with Windows","Keep SineFlow available from the system tray.",startup),Ui.SettingRow("Automatic updates","Download verified releases quietly and install them when SineFlow closes.",updates)),Ui.Text("WORKSPACE SETTINGS",11,true),shortcuts,Ui.Row(Ui.Button("About SineFlow",()=>Navigate("About"))));
    }
    private List<LanguageChoice> LanguageChoices()
    {
        var languages=c.SupportedLanguages().Select(x=>new LanguageChoice(x.Code,x.Name)).ToList();
        if(languages.Count==0)languages.Add(new("auto","Automatic"));
        if(!languages.Any(x=>x.Code==c.Settings.Language))c.Settings.Language=languages[0].Code;
        return languages;
    }
    private UIElement Dictation()
    {
        var quality=Ui.Choice(Enum.GetValues<SpeechQuality>(),c.Settings.Quality,x=>{c.Settings.Quality=x;c.Settings.UseCustomSpeechModel=false;c.Settings.AutoQuality=false;});
        var languages=LanguageChoices();
        var status=Ui.Text(c.ModelPreparation,12,true);status.SetBinding(TextBlock.TextProperty,new Binding(nameof(AppController.ModelPreparation)));
        var language=Ui.Choice(languages,languages.FirstOrDefault(x=>x.Code==c.Settings.Language)??languages[0],x=>c.Settings.Language=x.Code);
        var smartFormatting=Ui.Check("",c.Settings.AutomaticPunctuation&&c.Settings.CorrectObviousMistakes,x=>{c.Settings.AutomaticPunctuation=x;c.Settings.CorrectObviousMistakes=x;});
        var fillers=Ui.Check("",c.Settings.RemoveFillers,x=>c.Settings.RemoveFillers=x);
        var appContext=Ui.Check("",c.Settings.AppAwareFormatting,x=>c.Settings.AppAwareFormatting=x);
        var spoken=Ui.Check("",c.Settings.SpokenInstructions,x=>c.Settings.SpokenInstructions=x);
        var output=Ui.Choice(Enum.GetValues<OutputCasing>(),c.Settings.OutputCasing,x=>c.Settings.OutputCasing=x);
        var push=Ui.Check("",c.Settings.PushToTalk,x=>c.Settings.PushToTalk=x);
        var quick=Ui.Check("",c.Settings.ShowQuickActions,x=>c.Settings.ShowQuickActions=x);
        var selection=Ui.Check("",c.Settings.SelectionActions,x=>c.Settings.SelectionActions=x);
        var clipboard=Ui.Choice(Enum.GetValues<ClipboardMode>(),c.Settings.Clipboard,x=>c.Settings.Clipboard=x);
        var automatic=Ui.Check("",c.Settings.AutoQuality,x=>{c.Settings.AutoQuality=x;if(x)c.ApplyRecommendedQuality();Navigate("Dictation");});
        var recommendation=Ui.Text(HardwareRecommendations.Explain(c.Hardware),13,true);
        var commands=Ui.Stack(SpokenActions.Examples.Select(example=>Ui.Text("„"+example.Spoken+"“  →  "+example.Effect,13,true)).ToArray());
        return Ui.Stack(Ui.Card(Ui.Text("Transcription",16),Ui.SettingRow("Quality","Balanced is recommended for everyday speech.",quality),Ui.SettingRow("Automatic profile","Let SineFlow pick the profile that fits this computer.",automatic),recommendation,Ui.Row(Ui.AsyncButton("Prepare selected quality",async()=>{c.SaveSettings();await c.PrepareQualityAsync();},true),status),Ui.SettingRow("Language","Automatic keeps mixed German and English natural.",language)),Ui.Card(Ui.Text("Smart formatting",16),Ui.SettingRow("Writing cleanup","Corrects punctuation, obvious speech errors and self-corrections without rewriting your message.",smartFormatting),Ui.SettingRow("Remove hesitation fillers","Remove only unambiguous filler words.",fillers),Ui.SettingRow("App-aware formatting","Use a restrained writing hint for the active app.",appContext),Ui.SettingRow("Spoken actions","Recognize final translation, formatting and prompt commands.",spoken),Ui.SettingRow("Output",null,output),Ui.Row(Ui.Button("Prepare writing assistant",()=>Navigate("Models")))),Ui.Card(Ui.Text("Spoken commands",16),Ui.Text("Say the command after your dictation. SineFlow separates it from your words and keeps the rest of your text unchanged.",13,true),commands,Ui.Text("Casing commands run instantly without the writing model. Translation, summaries and rewrites need the local writing model in Models.",13,true)),Ui.Card(Ui.Text("Recording & insertion",16),Ui.SettingRow("Push to talk","Hold to record and release to finish.",push),Ui.SettingRow("Quick actions","Show concise text actions after delivery.",quick),Ui.SettingRow("Edit selected text","Open actions for a safely readable selection.",selection),Ui.SettingRow("Clipboard",null,clipboard)));
    }
    private UIElement WritingTools() => Ui.Stack(Ui.Card(Ui.Text("Writing tools, when you ask for them.",22),Ui.Text("SineFlow uses its local writing model only for translation, rewrites, prompt creation and selected-text actions. Everyday dictation stays fast and direct.",14,true),Ui.Check("Keep writing model loaded between uses",c.Settings.KeepEnhancementLoaded,x=>c.Settings.KeepEnhancementLoaded=x),Ui.Row(Ui.Button("Download writing model",()=>Navigate("Models")),Ui.Button("Try the writing assistant",()=>new RewriteWindow(c, "").ShowDialog()))));
    private UIElement LanguagePage()
    {
        var languages = LanguageChoices();
        return Ui.Stack(Ui.Card(Ui.Text("Speak your language",22),Ui.Text("Automatic detects the language when the selected speech model supports it. The list follows the engine in Models.",14,true),Ui.Choice(languages,languages.FirstOrDefault(x=>x.Code==c.Settings.Language)??languages[0],x=>c.Settings.Language=x.Code)));
    }
    private sealed record LanguageChoice(string Code,string Name) { public override string ToString()=>Name; }
    private UIElement AudioPage()
    {
        var devices = new List<DeviceChoice> { new(null,"Windows default microphone") }; devices.AddRange(c.Audio.Microphones().Select(x=>new DeviceChoice(x.Id,x.Name)));
        var meter = new ProgressBar { Maximum=1,Height=10 }; var message = Ui.Text("Speak to test your microphone.",13,true); string? testPath=null;
        Action<float> level = x=>Dispatcher.BeginInvoke(()=>meter.Value=x);
        async Task StopTestAsync()
        {
            var path=testPath;
            if(path is null)return;
            testPath=null;
            try { await c.Audio.StopAsync();message.Text="Microphone test finished."; }
            finally { c.Audio.Level-=level;try { File.Delete(path); } catch(IOException error) { SafeLog.Write("MicrophoneTestCleanup",error); }meter.Value=0; }
        }
        var test = Ui.AsyncButton("Start / stop microphone test",async()=>
        {
            if(testPath is null) { if(c.Busy||c.Audio.IsCapturing) throw new InvalidOperationException("Finish the current operation first."); var path=c.TemporaryWave(); testPath=path; c.Audio.Level+=level; try{c.Audio.Start(path,CaptureMode.Microphone,c.Settings.MicrophoneId);}catch{c.Audio.Level-=level;testPath=null;try{File.Delete(path);}catch(IOException error){SafeLog.Write("MicrophoneTestCleanup",error);}throw;} message.Text="Listening — speak to see the actual microphone level."; }
            else await StopTestAsync();
        });
        var content=Ui.Stack(Ui.Card(Ui.Text("Input device",20),Ui.Choice(devices,devices.FirstOrDefault(x=>x.Id==c.Settings.MicrophoneId)??devices[0],x=>c.Settings.MicrophoneId=x.Id),meter,message,test,Ui.Button("Refresh devices",()=>Navigate("Audio"))),Ui.Card(Ui.Check("Lower system audio while dictating",c.Settings.DuckAudio,x=>c.Settings.DuckAudio=x),Ui.Text("Recording shortcuts are silent. SineFlow never changes Windows sound settings.",13,true)));
        content.Unloaded+=async(_,_)=> { try { await StopTestAsync(); } catch(Exception error) { SafeLog.Write("MicrophoneTest",error); } }; return content;
    }
    private sealed record DeviceChoice(string? Id,string Name) { public override string ToString()=>Name; }
    private UIElement Shortcuts()
    {
        TextBox Capture(string label,Hotkey value,Action<Hotkey> set)
        {
            var box=Ui.Input(value.ToString()); box.IsReadOnly=true; box.ToolTip="Focus this field and press your new shortcut"; System.Windows.Automation.AutomationProperties.SetName(box,label);
            box.PreviewKeyDown+=(_,e)=> { e.Handled=true; var key=e.Key==Key.System?e.SystemKey:e.Key; if(key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; uint mods=0; var m=Keyboard.Modifiers; if(m.HasFlag(ModifierKeys.Control))mods|=2; if(m.HasFlag(ModifierKeys.Shift))mods|=4; if(m.HasFlag(ModifierKeys.Alt))mods|=1; if(m.HasFlag(ModifierKeys.Windows))mods|=8; var h=new Hotkey(mods,(uint)KeyInterop.VirtualKeyFromKey(key)); set(h); box.Text=h.ToString(); }; return box;
        }
        return Ui.Stack(Ui.Card(Ui.Text("One shortcut. Your words, anywhere.",22),Ui.Text("Click a field and press a key combination. Saving checks for conflicts.",13,true),Ui.Text("Dictation shortcut"),Capture("Dictation shortcut",c.Settings.PlainHotkey,x=>c.Settings.PlainHotkey=x),Ui.Text("Edit selected text"),Capture("Edit selected text",c.Settings.EditHotkey,x=>c.Settings.EditHotkey=x)));
    }
    private UIElement CustomWords()
    {
        var words=Ui.Input(string.Join(Environment.NewLine,c.Settings.CustomWords),true); words.MinHeight=240;
        var mappings=new ListBox{ItemsSource=c.Settings.VocabularyCorrections.Select(x=>$"{x.Heard}  →  {x.Intended}").ToList(),Height=120};
        var heard=Ui.Input();var intended=Ui.Input();
        return Ui.Stack(Ui.Card(Ui.Text("Names worth getting right",22),Ui.Text("One name, brand, gaming term or abbreviation per line. These guide speech recognition and local writing.",14,true),words,Ui.Row(Ui.Button("Import…",()=> { var d=new Microsoft.Win32.OpenFileDialog { Filter="Text files|*.txt" }; if(d.ShowDialog()==true) words.Text=File.ReadAllText(d.FileName); }),Ui.Button("Export…",()=> { var d=new Microsoft.Win32.SaveFileDialog { Filter="Text files|*.txt",FileName="SineFlow-custom-words.txt" }; if(d.ShowDialog()==true)File.WriteAllText(d.FileName,words.Text); }),Ui.Button("Save words",()=> { c.Settings.CustomWords=words.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToList(); c.SaveSettings(); c.Inform("Custom words saved"); },true))),Ui.Card(Ui.Text("Teach a recurring correction",20),Ui.Text("Only corrections you explicitly add are saved. Complete words and phrases are matched; other words stay unchanged.",13,true),mappings,Ui.Text("Often recognized as",12,true),heard,Ui.Text("What I actually mean",12,true),intended,Ui.Row(Ui.Button("Add correction",()=>{if(string.IsNullOrWhiteSpace(heard.Text)||string.IsNullOrWhiteSpace(intended.Text))throw new InvalidOperationException("Enter both the heard and intended word.");c.Settings.VocabularyCorrections.Add(new(heard.Text.Trim(),intended.Text.Trim()));if(!c.Settings.CustomWords.Contains(intended.Text.Trim()))c.Settings.CustomWords.Add(intended.Text.Trim());c.SaveSettings();Navigate("Custom Words");},true),Ui.Button("Remove selected",()=>{if(mappings.SelectedIndex>=0){c.Settings.VocabularyCorrections.RemoveAt(mappings.SelectedIndex);c.SaveSettings();Navigate("Custom Words");}}))));
    }
    private UIElement Snippets()
    {
        var list=new ListBox { ItemsSource=c.Settings.Snippets.ToList(),DisplayMemberPath="Name",Height=130 }; var name=Ui.Input(); var trigger=Ui.Input(); var content=Ui.Input("",true);
        list.SelectionChanged+=(_,_)=> { if(list.SelectedItem is Snippet s) { name.Text=s.Name; trigger.Text=s.Trigger; content.Text=s.Content; } };
        return Ui.Stack(Ui.Card(Ui.Text("Say a little. Insert a lot.",22),Ui.Text("Exact spoken phrases expand into your text. Separate alternatives with |. Variables: {date}, {time}, {clipboard}.",13,true),list,Ui.Text("Name"),name,Ui.Text("Spoken trigger"),trigger,Ui.Text("Content"),content,Ui.Row(Ui.Button("New",()=> {list.SelectedItem=null; name.Clear(); trigger.Clear(); content.Clear();}),Ui.Button("Save snippet",()=> { if(string.IsNullOrWhiteSpace(trigger.Text)||string.IsNullOrWhiteSpace(name.Text))throw new InvalidOperationException("Enter a name and a trigger."); if(list.SelectedItem is Snippet old)c.Settings.Snippets.Remove(old); c.Settings.Snippets.Add(new(name.Text,trigger.Text,content.Text)); c.SaveSettings(); Navigate("Snippets"); },true),Ui.Button("Delete",()=> { if(list.SelectedItem is Snippet old && Confirm("Delete this snippet?")) { c.Settings.Snippets.Remove(old); c.SaveSettings(); Navigate("Snippets"); } }))));
    }
    private UIElement Applications()
    {
        var list=new ListBox { ItemsSource=c.Settings.Profiles.ToList(),DisplayMemberPath="Executable",Height=120 }; var exe=Ui.Input("Discord.exe"); var instruction=Ui.Input("Keep a natural conversational tone.",true); var strategy=Ui.Choice(Enum.GetValues<InsertionStrategy>(),InsertionStrategy.Automatic,_=>{}); var enter=Ui.Check("Press Enter after insertion (may send or execute text)",false,_=>{});
        list.SelectionChanged+=(_,_)=> {if(list.SelectedItem is ApplicationProfile p){exe.Text=p.Executable; instruction.Text=p.Instruction; strategy.SelectedItem=p.Strategy; enter.IsChecked=p.PressEnter;}};
        return Ui.Stack(Ui.Card(Ui.Text("A good fit for every app",22),list,Ui.Text("Executable name"),exe,Ui.Text("Insertion method"),strategy,Ui.Text("Writing instruction"),instruction,enter,Ui.Row(Ui.Button("Save profile",()=> { var p=new ApplicationProfile(exe.Text.Trim(),(InsertionStrategy)strategy.SelectedItem,enter.IsChecked==true,instruction.Text); c.Settings.Profiles.RemoveAll(x=>x.Executable.Equals(p.Executable,StringComparison.OrdinalIgnoreCase)); c.Settings.Profiles.Add(p);c.SaveSettings();Navigate("Applications"); },true),Ui.Button("Delete profile",()=> {if(list.SelectedItem is ApplicationProfile p&&Confirm("Delete this application profile?")){c.Settings.Profiles.Remove(p);c.SaveSettings();Navigate("Applications");}}))));
    }
    private UIElement History()
    {
        var search=Ui.Input(); var list=new ListBox { MinHeight=250,MaxHeight=410 }; var range=Ui.Choice(new[]{"All time","Today","Past week","Past month"},"All time",_=>{});
        void Refresh(){var start=range.SelectedIndex switch {1=>(DateTimeOffset?)DateTimeOffset.Now.Date,2=>DateTimeOffset.Now.AddDays(-7),3=>DateTimeOffset.Now.AddMonths(-1),_=>null};list.ItemsSource=c.History.Search(search.Text,start);}
        search.TextChanged+=(_,_)=>Refresh(); range.SelectionChanged+=(_,_)=>Refresh();Refresh();
        return Ui.Stack(Ui.Card(Ui.Text("Find your words again",22),Ui.Text("Search",12,true),search,range,list,Ui.Row(Ui.Button("Copy",()=> {if(list.SelectedItem is HistoryEntry h)c.Clipboard.SetText(h.Text);}),Ui.Button("Reuse",()=> {if(list.SelectedItem is HistoryEntry h){c.SetResult(h.Text);Navigate("Overview");}}),Ui.Button("Delete",()=> {if(list.SelectedItem is HistoryEntry h&&Confirm("Delete this history entry?")){c.History.Delete(h.Id);Refresh();}}),Ui.Button("Clear history",()=> {if(Confirm("Permanently delete all transcription history? Lifetime statistics will remain.")){c.History.ClearHistory();Refresh();}}))));
    }
    private UIElement Statistics()
    {
        var rows=c.History.Statistics(); var count=rows.Sum(x=>x.Dictations); var duration=rows.Sum(x=>x.Duration);
        return Ui.Stack(Ui.Card(Ui.Text("Your voice, over time",22),Ui.Text($"{count:N0} dictations    ·    {rows.Sum(x=>x.Words):N0} words",25),Ui.Text($"{TimeSpan.FromSeconds(duration):g} recorded    ·    {(count>0?duration/count:0):N1}s average",14,true)),Ui.Card(Ui.Text("Languages",18),Ui.Text(string.Join("\n",rows.GroupBy(x=>x.Language).Select(g=>$"{g.Key}   ·   {g.Sum(x=>x.Words):N0} words"))),Ui.Text("Applications",18),Ui.Text(string.Join("\n",rows.GroupBy(x=>x.Application).Select(g=>$"{g.Key}   ·   {g.Sum(x=>x.Dictations):N0} dictations"))),Ui.Text("Models",18),Ui.Text(string.Join("\n",rows.GroupBy(x=>x.Model).Select(g=>$"{g.Key}   ·   {g.Sum(x=>x.Dictations):N0} dictations")))),Ui.Button("Reset lifetime statistics",()=> {if(Confirm("Permanently reset lifetime statistics?")){c.History.ResetStatistics();Navigate("Statistics");}}));
    }
    private UIElement Privacy()=>Ui.Stack(Ui.Card(Ui.Text("Private by design",22),Ui.Text("No telemetry. No accounts. No uploaded recordings. Speech and writing models run locally. Internet is used only when you download models.",14,true),Ui.Check("Store transcription history on this computer",c.Settings.StoreHistory,x=>c.Settings.StoreHistory=x),Ui.Text("Keep transcript history for",13,true),Ui.Choice(new[]{0,7,30,90,365},c.Settings.HistoryRetentionDays,x=>c.Settings.HistoryRetentionDays=x),Ui.Text("0 means keep it until you clear it. New SineFlow installations keep history off by default. Turning history off prevents new transcript storage. Existing history remains until you clear it. Lifetime totals contain counts, languages, model names and application names; reset them in Statistics.",13,true),Ui.Button("Clear all history",()=> {if(Confirm("Permanently delete all saved history?"))c.History.ClearHistory();}),Ui.Button("Open local data folder",()=>Process.Start(new ProcessStartInfo(Brand.DataRoot){UseShellExecute=true}))));
    private UIElement Advanced()=>Ui.Stack(Ui.Card(Ui.Text("This device",22),Ui.Text(c.DescribeHardware(),13,true),Ui.Text(HardwareRecommendations.AccelerationText(c.Hardware),13,true),Ui.Text(HardwareRecommendations.Explain(c.Hardware),13,true),Ui.Row(Ui.Button("Detect again",()=>{c.RefreshHardware();c.Inform("Hardware details refreshed");Navigate("Advanced");}))),Ui.Card(Ui.Text("Performance",22),Ui.Text("Processing backend · restart SineFlow after changing",13,true),Ui.Choice(new[]{"Automatic","CPU","GPU / accelerated"},c.Settings.Backend,x=>c.Settings.Backend=x),Ui.Check("Keep speech model loaded between uses",c.Settings.KeepSpeechLoaded,x=>c.Settings.KeepSpeechLoaded=x),Ui.Text("Automatic uses CPU for speech, which is faster for short dictations on many Windows devices. GPU acceleration is optional; measure it on your computer before choosing it. Keeping a model loaded trades memory for faster repeat use.",13,true),Ui.Button("Diagnostics",()=>Navigate("Debug"))));
    private UIElement Debug()
    {
        var timings=Ui.Check("",c.Settings.DebugLogging,x=>c.Settings.DebugLogging=x);
        return Ui.Stack(Ui.Card(Ui.Text("Diagnostics without your words",18),Ui.Text($"SineFlow {Brand.Version}\n{Environment.OSVersion}\n{Environment.ProcessorCount} logical processors\nSpeech: {c.Settings.SpeechModel}\nBackend: {c.Settings.Backend}\nMicrophone: {c.Settings.MicrophoneId??"Windows default"}",13,true),Ui.SettingRow("Record dictation timings","Write T0–T11 durations locally for this device.",timings),Ui.Row(Ui.Button("Copy diagnostics",()=>c.Clipboard.SetText($"SineFlow {Brand.Version}\n{Environment.OSVersion}\nCPU threads: {Environment.ProcessorCount}\nModel: {c.Settings.SpeechModel}\nBackend: {c.Settings.Backend}")),Ui.Button("Open log folder",()=> {var path=Path.Combine(Brand.DataRoot,"Logs");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}))),Ui.Text("Timing logs contain stage durations and delivery state, never transcripts, audio, selected text, or native exception messages.",13,true));
    }
    private UIElement About()=>Ui.Stack(Ui.Hero(Ui.Text("SineFlow",36),Ui.Text("Speech to Text. Private. Offline.",20),Ui.Text("Created by SNine Studios",14,true)),Ui.Card(Ui.Text("About this build",20),Ui.Text("Version "+Brand.Version+" · unsigned development build",13,true),Ui.Text("Built independently for Windows. Speech: whisper.cpp. Local writing: llama.cpp. Models are downloaded separately under their respective licenses.",14,true),Ui.Button("Third-party notices",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"THIRD_PARTY_NOTICES.md")){UseShellExecute=true}))),Ui.Card(Ui.Text("Updates",20),Ui.Text("When a release feed is configured, SineFlow checks in the background, verifies the downloaded installer with SHA-256 and installs it automatically when you quit. You never need to download an update by hand.",13,true),Ui.Button("Open update settings file",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"sineflow-update.json")){UseShellExecute=true}))));
    private static bool Confirm(string message)=>MessageBox.Show(message,"SineFlow",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
}
