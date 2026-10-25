using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SonaBar.Storage;

namespace SonaBar.App;
public sealed partial class MainWindow : ThemedWindow
{
    private readonly AppController c;
    private readonly ContentControl page = new();
    private readonly TextBlock heading = Ui.Text("Overview",28);
    private readonly TextBlock subtitle = Ui.Text("",13,true);
    private readonly ScrollViewer pageScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel subNavigation = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,8) };
    private Grid? pageHeader;
    private readonly Dictionary<string,ToggleButton> tabButtons=[];
    private readonly Dictionary<string,ToggleButton> subTabButtons=[];
    private Button? footerSave;
    private Button? footerAction;
    private Button? recordButton;
    private Button? maximizeButton;
    private TextBlock? footerStatus;
    private TextBlock? footerDetail;
    private System.Windows.Shapes.Ellipse? footerDot;
    private Waveform? footerWave;
    private DispatcherTimer? footerTimer;
    private readonly Stopwatch footerClock = new();
    private string currentPage = "Overview";
    private CancellationTokenSource? download;
    // The six top-level destinations mirror the segmented Preferences toolbar of WhisperBar.
    // Secondary pages stay reachable through the quiet sub-navigation row below it.
    private static readonly (string Key,string Label,string[] Pages)[] Tabs =
    [
        ("General","General",["Overview","General","Files","Meetings"]),
        ("Transcription","Transcription",["Dictation","Models","Languages","Writing","Custom Words","Snippets","Applications"]),
        ("Shortcuts","Shortcuts",["Shortcuts"]),
        ("Recording","Recording",["Audio","Privacy","Advanced","Debug"]),
        ("History","History",["History","Statistics"]),
        ("About","About",["About","Help"])
    ];
    public MainWindow(AppController controller)
    {
        c = controller; Title = "SonaBar";
        // The window never opens larger than the screen, so the title bar and the footer stay reachable on small laptops.
        var workArea = SystemParameters.WorkArea; Width = Math.Min(1080, workArea.Width - 32); Height = Math.Min(780, workArea.Height - 48); MinWidth = 900; MinHeight = 620; WindowStartupLocation = WindowStartupLocation.CenterScreen; DataContext = c;
        // A borderless utility frame lets us draw the Windows caption controls and the
        // segmented toolbar while keeping Windows resize and snap behaviour.
        UseUtilityChrome();
        var layout = new Grid();
        layout.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"Surface");
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(BuildTitleBar());
        var tabBar = BuildTabBar(); Grid.SetRow(tabBar,1); layout.Children.Add(tabBar);
        var content = new Grid { Margin = new Thickness(32,8,32,0) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        pageHeader = new Grid { MaxWidth=920,HorizontalAlignment=HorizontalAlignment.Stretch };
        pageHeader.ColumnDefinitions.Add(new ColumnDefinition()); pageHeader.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        heading.Margin=new Thickness(0,0,0,2); heading.FontWeight=FontWeights.SemiBold;
        subtitle.Margin=new Thickness(0,0,0,12);
        var titles=Ui.Stack(heading,subtitle); pageHeader.Children.Add(titles);
        var privacy=Ui.StatusPill("●  Local & private");privacy.VerticalAlignment=VerticalAlignment.Top;privacy.Margin=new Thickness(10,3,0,0);Grid.SetColumn(privacy,1); pageHeader.Children.Add(privacy);
        content.Children.Add(pageHeader);
        Grid.SetRow(subNavigation,1); content.Children.Add(subNavigation);
        page.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        pageScroll.Content=page; pageScroll.Margin=new Thickness(0,4,0,14); pageScroll.MaxWidth=920; pageScroll.HorizontalAlignment=HorizontalAlignment.Stretch; pageScroll.HorizontalContentAlignment=HorizontalAlignment.Stretch; Grid.SetRow(pageScroll,2); content.Children.Add(pageScroll);
        Grid.SetRow(content,2); layout.Children.Add(content);
        var footer = BuildFooter(); Grid.SetRow(footer,3); layout.Children.Add(footer);
        Content=layout;
        StateChanged += (_,_) => UpdateCaptionButtons();
        UpdateCaptionButtons();
        Closing += (_,e) => { e.Cancel=true; Hide(); };
        AllowDrop = true; Drop += async (_,e) => { if(e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files) { Navigate("Files"); await ProcessFilesAsync(files); } };
        c.Machine.Changed += MachineChanged;
        c.PropertyChanged += ControllerChanged;
        c.Audio.Level += AudioLevel;
        SourceInitialized += (_,_) => Theme.Apply(c.Settings.Theme);
        Closed += (_,_) => { c.Machine.Changed -= MachineChanged; c.PropertyChanged -= ControllerChanged; c.Audio.Level -= AudioLevel; footerTimer?.Stop(); };
        footerTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100),DispatcherPriority.Render,(_,_)=> { if(c.Machine.State==OperationState.Recording&&footerDetail is not null)footerDetail.Text="Listening   "+footerClock.Elapsed.ToString(@"mm\:ss"); },Dispatcher);
        footerTimer.Stop();
        Navigate("Overview");
    }
    private Grid BuildTitleBar()
    {
        var bar = new Grid { Height=44 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });

        var brand = new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(18,0,0,0) };
        var logo = new Border { Width=24,Height=24,CornerRadius=new CornerRadius(6),ClipToBounds=true,Margin=new Thickness(0,0,9,0),VerticalAlignment=VerticalAlignment.Center };
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri("pack://application:,,,/SonaBar.App;component/Assets/SonaBar-Source.png", UriKind.Absolute);
        source.EndInit();
        source.Freeze();
        var mark = new System.Windows.Controls.Image { Source=source,Stretch=Stretch.UniformToFill };
        mark.Clip = new RectangleGeometry(new Rect(0,0,24,24),6,6);
        RenderOptions.SetBitmapScalingMode(mark,BitmapScalingMode.HighQuality);
        logo.Child=mark;
        brand.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment=VerticalAlignment.Center };
        var wordmark = Ui.Text("SonaBar",13); wordmark.FontWeight=FontWeights.SemiBold; wordmark.Margin=new Thickness(0); wordmark.LineHeight=16;
        var byline = Ui.Text("by SilasNet",9,true); byline.Margin=new Thickness(0); byline.LineHeight=12;
        names.Children.Add(wordmark); names.Children.Add(byline);
        brand.Children.Add(names);
        bar.Children.Add(brand);

        var actions = new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top };
        var gear = new Button { Content="\uE713",Style=(Style)FindResource("IconButton"),ToolTip="Settings",HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0),FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=14 };
        gear.Click += (_,_)=>Navigate("General");
        System.Windows.Automation.AutomationProperties.SetName(gear,"Settings");
        MakeChromeControl(gear);
        actions.Children.Add(gear);
        actions.Children.Add(CaptionButton("\uE921","Minimise",()=>WindowState=WindowState.Minimized));
        maximizeButton = CaptionButton("\uE922","Maximise",ToggleMaximize);
        actions.Children.Add(maximizeButton);
        actions.Children.Add(CaptionButton("\uE8BB","Close",Hide,true));
        Grid.SetColumn(actions,2); bar.Children.Add(actions);
        return bar;
    }
    private Button CaptionButton(string glyph,string tooltip,Action action,bool close=false)
    {
        var button = new Button { Content=glyph,Style=(Style)FindResource(close?"CloseCaptionButton":"CaptionButton"),ToolTip=tooltip,Focusable=false };
        System.Windows.Automation.AutomationProperties.SetName(button,tooltip);
        button.Click += (_,_)=>action();
        MakeChromeControl(button);
        return button;
    }
    private void UpdateCaptionButtons()
    {
        if(maximizeButton is null)return;
        var maximized=WindowState==WindowState.Maximized;
        var label=maximized?"Restore":"Maximise";
        maximizeButton.Content=maximized?"\uE923":"\uE922";
        maximizeButton.ToolTip=label;
        System.Windows.Automation.AutomationProperties.SetName(maximizeButton,label);
    }
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private Border BuildTabBar()
    {
        var stack = new StackPanel { Orientation=Orientation.Horizontal };
        foreach(var tab in Tabs)
        {
            var button = new ToggleButton { Content=tab.Label,Style=(Style)FindResource("TabButton") };
            button.Click += (_,_)=>Navigate(tab.Pages[0]);
            stack.Children.Add(button);
            tabButtons[tab.Key]=button;
        }
        var border = new Border { Child=stack,CornerRadius=new CornerRadius(9),Padding=new Thickness(2),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,6,0,2) };
        border.SetResourceReference(Border.BackgroundProperty,"Sidebar");
        return border;
    }
    private Border BuildFooter()
    {
        var grid = new Grid { MaxWidth=1080,HorizontalAlignment=HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        recordButton = new Button { Style=(Style)FindResource("RecordButton"),Content=Ui.EarIcon(23),ToolTip="Start or stop dictation" };
        recordButton.Click += (_,_)=>DictationAction();
        grid.Children.Add(recordButton);
        var statusPanel = new StackPanel { Margin=new Thickness(14,0,18,0),VerticalAlignment=VerticalAlignment.Center };
        var statusLine = new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        footerDot = new System.Windows.Shapes.Ellipse { Width=8,Height=8,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center };
        footerDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Success");
        footerStatus = Ui.Text("Ready to listen",14); footerStatus.FontWeight=FontWeights.SemiBold; footerStatus.Margin=new Thickness(0,0,0,1);
        footerDetail = Ui.Text("Ctrl + Shift + Space",11,true); footerDetail.Margin=new Thickness(0);
        statusLine.Children.Add(footerDot); statusLine.Children.Add(footerStatus);
        statusPanel.Children.Add(statusLine); statusPanel.Children.Add(footerDetail);
        Grid.SetColumn(statusPanel,1); grid.Children.Add(statusPanel);
        footerWave = new Waveform { Width=110,Height=26,Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed };
        Grid.SetColumn(footerWave,2); grid.Children.Add(footerWave);
        footerSave = Ui.Button("Save settings",()=>{c.SaveSettings();c.Inform("Settings saved");},true);
        footerSave.Style=(Style)FindResource("PositiveButton"); footerSave.MinWidth=112; footerSave.Height=36; footerSave.Margin=new Thickness(0,0,10,0); footerSave.HorizontalAlignment=HorizontalAlignment.Right; footerSave.Visibility=Visibility.Collapsed;
        Grid.SetColumn(footerSave,3); grid.Children.Add(footerSave);
        footerAction = Ui.Button("Start dictation",DictationAction,true);
        footerAction.Style=(Style)FindResource("BlueButton"); footerAction.MinWidth=154; footerAction.Height=36; footerAction.Margin=new Thickness(0); footerAction.HorizontalAlignment=HorizontalAlignment.Right;
        Grid.SetColumn(footerAction,4); grid.Children.Add(footerAction);
        var border = new Border { Child=grid,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(24,10,24,10) };
        border.SetResourceReference(Border.BackgroundProperty,"Surface");
        border.SetResourceReference(Border.BorderBrushProperty,"Line");
        return border;
    }
    private static string TabFor(string page)
    {
        foreach(var tab in Tabs)if(tab.Pages.Contains(page))return tab.Key;
        return "General";
    }
    private static string SubLabel(string page) => page switch { "General"=>"Settings","Custom Words"=>"Dictionary","Applications"=>"App profiles","Writing"=>"Writing tools",_=>page };
    private static bool IsSettingsPage(string name) => name is "General" or "Dictation" or "Writing" or "Audio" or "Shortcuts" or "Privacy" or "Advanced" or "Debug" or "Languages";
    private void RebuildSubTabs(string tabKey)
    {
        subNavigation.Children.Clear(); subTabButtons.Clear();
        var pages = Tabs.First(tab=>tab.Key==tabKey).Pages;
        if(pages.Length<=1){subNavigation.Visibility=Visibility.Collapsed;return;}
        subNavigation.Visibility=Visibility.Visible;
        foreach(var pageName in pages)
        {
            var button = new ToggleButton { Content=SubLabel(pageName),Style=(Style)FindResource("SubTabButton") };
            button.Click += (_,_)=>Navigate(pageName);
            subNavigation.Children.Add(button);
            subTabButtons[pageName]=button;
        }
    }
    private void DictationAction()
    {
        if(c.Machine.State==OperationState.Recording)c.ToggleDictation();
        else if(c.Busy)c.Cancel();
        else c.ToggleDictation();
    }
    private void ControllerChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(AppController.PresenceLabel) or nameof(AppController.StageLabel) or nameof(AppController.Busy) or nameof(AppController.LastDictationLatency))
            Dispatcher.BeginInvoke(UpdateFooter);
    }
    private void MachineChanged(OperationState state)
    {
        Dispatcher.BeginInvoke(()=>{UpdateFooter();UpdateWaveformState();});
    }
    private void AudioLevel(float level) => Dispatcher.BeginInvoke(()=>footerWave?.Add(level));
    private void UpdateWaveformState()
    {
        if(footerWave is null)return;
        switch(c.Machine.State)
        {
            case OperationState.Recording: footerWave.BeginListening(); footerClock.Restart(); footerTimer?.Start(); break;
            case OperationState.Transcribing:
            case OperationState.Enhancing:
            case OperationState.Inserting: footerWave.BeginProcessing(); footerTimer?.Stop(); break;
            default: footerWave.Stop(); footerTimer?.Stop(); break;
        }
    }
    private void UpdateFooter()
    {
        if(footerStatus is null||footerAction is null||footerDetail is null||footerSave is null||footerWave is null)return;
        var state=c.Machine.State; var recording=state==OperationState.Recording; var busy=c.Busy;
        footerStatus.Text=c.PresenceLabel;
        footerStatus.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimary");
        var dotColour=c.Readiness.Stage switch { ReadinessStage.Recording or ReadinessStage.Transcribing or ReadinessStage.Inserting or ReadinessStage.ApplyingCommand=>"Accent",ReadinessStage.Error=>"Danger",ReadinessStage.NeedsModel=>"Warning",_=>"Success" };
        footerDot?.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,dotColour);
        footerAction.Content=recording?"Stop & insert":busy?"Cancel":"Start dictation";
        footerAction.Style=(Style)FindResource("BlueButton");
        footerAction.IsEnabled=true;
        footerSave.Visibility=IsSettingsPage(currentPage)?Visibility.Visible:Visibility.Collapsed;
        footerDetail.Text=busy?c.StageLabel:c.Settings.PlainHotkey+(string.IsNullOrEmpty(c.LastDictationLatency)?"":"   ·   "+c.LastDictationLatency);
        footerWave.Visibility=state is OperationState.Recording or OperationState.Transcribing or OperationState.Enhancing or OperationState.Inserting?Visibility.Visible:Visibility.Collapsed;
    }
    public void Navigate(string name)
    {
        currentPage=name;
        var tabKey=TabFor(name);
        foreach(var (key,button) in tabButtons)button.IsChecked=key==tabKey;
        RebuildSubTabs(tabKey);
        foreach(var (key,button) in subTabButtons)button.IsChecked=key==name;
        heading.Text=name switch { "Overview"=>"SonaBar","Custom Words"=>"Dictionary","Applications"=>"App profiles","Audio"=>"Microphone","General"=>"Settings","Writing"=>"Writing tools",_=>name };
        // Overview and onboarding carry their own headline; a second title above them only
        // repeats the message and pushes the actual content down.
        var showHeader = name is not ("Welcome" or "Overview");
        if(pageHeader is not null)pageHeader.Visibility=showHeader?Visibility.Visible:Visibility.Collapsed;
        subtitle.Text=name switch { "Overview"=>"Private, offline speech to text. Your words stay on this PC.","Files"=>"Turn recordings into searchable text.","Meetings"=>"Capture conversations and keep the important parts.","History"=>"Find and reuse previous dictations.","Models"=>"Manage the local engines behind your workflow.","Dictation"=>"Make speech recognition work your way.","General"=>"Make SonaBar yours, without losing focus.","Privacy"=>"Your data stays under your control.","Help"=>"Quick start, delivery help and privacy in one place.",_=>"Set up your private writing workspace." };
        if(footerSave is not null)footerSave.Visibility=IsSettingsPage(name)?Visibility.Visible:Visibility.Collapsed;
        page.Content = name switch
        {
            "Welcome" => Welcome(), "Overview" => Overview(), "General" => General(), "Dictation" => Dictation(), "Writing" => WritingTools(), "Models" => Models(), "Languages" => LanguagePage(), "Audio" => AudioPage(), "Shortcuts" => Shortcuts(), "Custom Words" => CustomWords(), "Snippets" => Snippets(), "Applications" => Applications(), "History" => History(), "Statistics" => Statistics(), "Privacy" => Privacy(), "Advanced" => Advanced(), "Debug" => Debug(), "About" => About(), "Help" => Help(), "Files" => Files(), "Meetings" => Meetings(), _ => Overview()
        };
        pageScroll.ScrollToTop();
        UpdateFooter();
    }
    private UIElement Overview()
    {
        var last = Ui.Input(c.LastText,true); last.IsReadOnly = true; last.SetBinding(TextBox.TextProperty,new Binding(nameof(AppController.LastText)) { Mode = BindingMode.OneWay });
        last.MinHeight=125;last.MaxHeight=260;
        var actions=Ui.Button("Refine text  ···",()=>{});actions.Click+=(_,_)=>{actions.ContextMenu=QuickActions.Create(c);actions.ContextMenu.PlacementTarget=actions;actions.ContextMenu.IsOpen=true;};
        var readiness=Ui.Text(c.ModelPreparation,12,true);readiness.SetBinding(TextBlock.TextProperty,new Binding(nameof(AppController.ModelPreparation)));
        var eyebrow=Ui.Text("SONABAR  /  VOICE WORKSPACE",11);eyebrow.FontWeight=FontWeights.Bold;eyebrow.SetResourceReference(TextBlock.ForegroundProperty,"Accent");
        var heroTitle=Ui.Text("Speak naturally.\nText follows.",30);heroTitle.LineHeight=36;heroTitle.Margin=new Thickness(0,7,0,10);
        var shortcut=Ui.Text(c.Settings.PlainHotkey.ToString(),14);shortcut.FontWeight=FontWeights.Bold;shortcut.Margin=new Thickness(0);
        var shortcutBadge=new Border{Child=shortcut,Padding=new Thickness(12,8,12,8),CornerRadius=new CornerRadius(10),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,5,0,0)};
        shortcutBadge.SetResourceReference(Border.BackgroundProperty,"Card");
        var begin=Ui.Button("Start a dictation",c.ToggleDictation,true);begin.Margin=new Thickness(12,16,8,0);
        var configure=Ui.Button("Set up input",()=>Navigate("Dictation"));configure.Margin=new Thickness(0,16,8,0);
        shortcutBadge.VerticalAlignment=VerticalAlignment.Center;shortcutBadge.Margin=new Thickness(0,16,0,0);
        var heroCopy=Ui.Stack(eyebrow,heroTitle,Ui.Text("Put your cursor where you want the words. SonaBar listens, understands and places the result there — privately on this PC.",14,true),Ui.Row(shortcutBadge,begin,configure));
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
        return Ui.Stack(Ui.Card(Ui.Text("Appearance & startup",18),Ui.SettingRow("Appearance","Match Windows or choose a fixed theme.",appearance),Ui.SettingRow("Start with Windows","Keep SonaBar available from the system tray.",startup),Ui.SettingRow("Automatic updates","Download verified releases quietly and install them when SonaBar closes.",updates)),Ui.Text("WORKSPACE SETTINGS",11,true),shortcuts,Ui.Row(Ui.Button("About SonaBar",()=>Navigate("About"))));
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
        return Ui.Stack(Ui.Card(Ui.Text("Transcription",16),Ui.SettingRow("Quality","Balanced is recommended for everyday speech.",quality),Ui.SettingRow("Automatic profile","Let SonaBar pick the profile that fits this computer.",automatic),recommendation,Ui.Row(Ui.AsyncButton("Prepare selected quality",async()=>{c.SaveSettings();await c.PrepareQualityAsync();},true),status),Ui.SettingRow("Language","Automatic keeps mixed German and English natural.",language)),Ui.Card(Ui.Text("Smart formatting",16),Ui.SettingRow("Writing cleanup","Corrects punctuation, obvious speech errors and self-corrections without rewriting your message.",smartFormatting),Ui.SettingRow("Remove hesitation fillers","Remove only unambiguous filler words.",fillers),Ui.SettingRow("App-aware formatting","Use a restrained writing hint for the active app.",appContext),Ui.SettingRow("Spoken actions","Recognize final translation, formatting and prompt commands.",spoken),Ui.SettingRow("Output",null,output),Ui.Row(Ui.Button("Prepare writing assistant",()=>Navigate("Models")))),Ui.Card(Ui.Text("Spoken commands",16),Ui.Text("Say the command after your dictation. SonaBar separates it from your words and keeps the rest of your text unchanged.",13,true),commands,Ui.Text("Casing commands run instantly without the writing model. Translation, summaries and rewrites need the local writing model in Models.",13,true)),Ui.Card(Ui.Text("Recording & insertion",16),Ui.SettingRow("Push to talk","Hold to record and release to finish.",push),Ui.SettingRow("Quick actions","Show concise text actions after delivery.",quick),Ui.SettingRow("Edit selected text","Open actions for a safely readable selection.",selection),Ui.SettingRow("Clipboard",null,clipboard)));
    }
    private UIElement WritingTools() => Ui.Stack(Ui.Card(Ui.Text("Writing tools, when you ask for them.",22),Ui.Text("SonaBar uses its local writing model only for translation, rewrites, prompt creation and selected-text actions. Everyday dictation stays fast and direct.",14,true),Ui.Check("Keep writing model loaded between uses",c.Settings.KeepEnhancementLoaded,x=>c.Settings.KeepEnhancementLoaded=x),Ui.Row(Ui.Button("Download writing model",()=>Navigate("Models")),Ui.Button("Try the writing assistant",()=>new RewriteWindow(c, "").ShowDialog()))));
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
        var content=Ui.Stack(Ui.Card(Ui.Text("Input device",20),Ui.Choice(devices,devices.FirstOrDefault(x=>x.Id==c.Settings.MicrophoneId)??devices[0],x=>c.Settings.MicrophoneId=x.Id),meter,message,test,Ui.Button("Refresh devices",()=>Navigate("Audio"))),Ui.Card(Ui.Text("Feedback sounds",18),Ui.Text("Optional built-in tones confirm recording start, finish and errors. They play through the default output and never change Windows sound settings.",13,true),Ui.Choice(new[]{"Off","Minimal","Standard"},c.Settings.Sounds,x=>c.Settings.Sounds=x),Ui.Text("Minimal plays the finished tone only. Standard adds the start and error tones.",13,true)),Ui.Card(Ui.Check("Lower system audio while dictating",c.Settings.DuckAudio,x=>c.Settings.DuckAudio=x),Ui.Text("Recording shortcuts stay silent unless feedback sounds are enabled. SonaBar never changes Windows sound settings.",13,true)));
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
        return Ui.Stack(Ui.Card(Ui.Text("Names worth getting right",22),Ui.Text("One name, brand, gaming term or abbreviation per line. These guide speech recognition and local writing.",14,true),words,Ui.Row(Ui.Button("Import…",()=> { var d=new Microsoft.Win32.OpenFileDialog { Filter="Text files|*.txt" }; if(d.ShowDialog()==true) words.Text=File.ReadAllText(d.FileName); }),Ui.Button("Export…",()=> { var d=new Microsoft.Win32.SaveFileDialog { Filter="Text files|*.txt",FileName="SonaBar-custom-words.txt" }; if(d.ShowDialog()==true)File.WriteAllText(d.FileName,words.Text); }),Ui.Button("Save words",()=> { c.Settings.CustomWords=words.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToList(); c.SaveSettings(); c.Inform("Custom words saved"); },true))),Ui.Card(Ui.Text("Teach a recurring correction",20),Ui.Text("Only corrections you explicitly add are saved. Complete words and phrases are matched; other words stay unchanged.",13,true),mappings,Ui.Text("Often recognized as",12,true),heard,Ui.Text("What I actually mean",12,true),intended,Ui.Row(Ui.Button("Add correction",()=>{if(string.IsNullOrWhiteSpace(heard.Text)||string.IsNullOrWhiteSpace(intended.Text))throw new InvalidOperationException("Enter both the heard and intended word.");c.Settings.VocabularyCorrections.Add(new(heard.Text.Trim(),intended.Text.Trim()));if(!c.Settings.CustomWords.Contains(intended.Text.Trim()))c.Settings.CustomWords.Add(intended.Text.Trim());c.SaveSettings();Navigate("Custom Words");},true),Ui.Button("Remove selected",()=>{if(mappings.SelectedIndex>=0){c.Settings.VocabularyCorrections.RemoveAt(mappings.SelectedIndex);c.SaveSettings();Navigate("Custom Words");}}))));
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
    private UIElement Privacy()=>Ui.Stack(Ui.Card(Ui.Text("Private by design",22),Ui.Text("No telemetry. No accounts. No uploaded recordings. Speech and writing models run locally. Internet is used only when you download models.",14,true),Ui.Check("Store transcription history on this computer",c.Settings.StoreHistory,x=>c.Settings.StoreHistory=x),Ui.Text("Keep transcript history for",13,true),Ui.Choice(new[]{0,7,30,90,365},c.Settings.HistoryRetentionDays,x=>c.Settings.HistoryRetentionDays=x),Ui.Text("0 means keep it until you clear it. New SonaBar installations keep history off by default. Turning history off prevents new transcript storage. Existing history remains until you clear it. Lifetime totals contain counts, languages, model names and application names; reset them in Statistics.",13,true),Ui.Button("Clear all history",()=> {if(Confirm("Permanently delete all saved history?"))c.History.ClearHistory();}),Ui.Button("Open local data folder",()=>Process.Start(new ProcessStartInfo(Brand.DataRoot){UseShellExecute=true}))));
    private UIElement Advanced()=>Ui.Stack(Ui.Card(Ui.Text("This device",22),Ui.Text(c.DescribeHardware(),13,true),Ui.Text(HardwareRecommendations.AccelerationText(c.Hardware),13,true),Ui.Text(HardwareRecommendations.Explain(c.Hardware),13,true),Ui.Row(Ui.Button("Detect again",()=>{c.RefreshHardware();c.Inform("Hardware details refreshed");Navigate("Advanced");}))),Ui.Card(Ui.Text("Performance",22),Ui.Text("Processing backend · restart SonaBar after changing",13,true),Ui.Choice(new[]{"Automatic","CPU","GPU / accelerated"},c.Settings.Backend,x=>c.Settings.Backend=x),Ui.Check("Keep speech model loaded between uses",c.Settings.KeepSpeechLoaded,x=>c.Settings.KeepSpeechLoaded=x),Ui.Text("Automatic uses CPU for speech, which is faster for short dictations on many Windows devices. GPU acceleration is optional; measure it on your computer before choosing it. Keeping a model loaded trades memory for faster repeat use.",13,true),Ui.Button("Diagnostics",()=>Navigate("Debug"))));
    private UIElement Debug()
    {
        var timings=Ui.Check("",c.Settings.DebugLogging,x=>c.Settings.DebugLogging=x);
        return Ui.Stack(Ui.Card(Ui.Text("Diagnostics without your words",18),Ui.Text($"SonaBar {Brand.Version}\n{Environment.OSVersion}\n{Environment.ProcessorCount} logical processors\nSpeech: {c.Settings.SpeechModel}\nBackend: {c.Settings.Backend}\nMicrophone: {c.Settings.MicrophoneId??"Windows default"}",13,true),Ui.SettingRow("Record dictation timings","Write T0–T11 durations locally for this device.",timings),Ui.Row(Ui.Button("Copy diagnostics",()=>c.Clipboard.SetText($"SonaBar {Brand.Version}\n{Environment.OSVersion}\nCPU threads: {Environment.ProcessorCount}\nModel: {c.Settings.SpeechModel}\nBackend: {c.Settings.Backend}")),Ui.Button("Open log folder",()=> {var path=Path.Combine(Brand.DataRoot,"Logs");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}))),Ui.Text("Timing logs contain stage durations and delivery state, never transcripts, audio, selected text, or native exception messages.",13,true));
    }
    private UIElement Help()=>Ui.Stack(Ui.Hero(Ui.Text("Start here",28),Ui.Text("Start dictating, solve delivery problems and keep your words private.",16,true)),Ui.Card(Ui.Text("Quick start",20),Ui.Text("Choose a speech model in Models. Put the cursor where the text should appear, then press your dictation shortcut and speak. Press the shortcut again to stop; SonaBar transcribes locally and inserts the result into the window that had focus when you started.",14,true),Ui.Row(Ui.Button("Choose a model",()=>Navigate("Models")),Ui.Button("Set the shortcut",()=>Navigate("Shortcuts")),Ui.Button("Test the microphone",()=>Navigate("Audio")))),Ui.Card(Ui.Text("If text does not arrive",20),Ui.Text("Keep the target window focused while SonaBar works. Some elevated applications reject input from a non-elevated app; run both at the same privilege level. For games or opaque windows, select the Compatibility insertion profile for that app. The delivery state and timing are recorded without transcript content.",14,true),Ui.Row(Ui.Button("Open app profiles",()=>Navigate("Applications")),Ui.Button("Open diagnostics",()=>Navigate("Debug")))),Ui.Card(Ui.Text("Privacy and data",20),Ui.Text("Speech and writing run on this PC. SonaBar has no account, telemetry or cloud transcription. History is optional, and model downloads only start when you ask for them. The local data folder contains settings, history and logs; audio is removed after processing.",14,true),Ui.Row(Ui.Button("Privacy settings",()=>Navigate("Privacy")),Ui.Button("Open local data folder",()=>Process.Start(new ProcessStartInfo(Brand.DataRoot){UseShellExecute=true})))),Ui.Card(Ui.Text("Free, without a licence server",20),Ui.Text("SonaBar is free to use. There is no checkout, subscription, trial timer or licence activation. Optional model downloads remain subject to their upstream licences.",14,true)),Ui.Card(Ui.Text("Shortcuts and support",20),Ui.Text("The active shortcut is shown in the footer and can be changed in Shortcuts. Copy diagnostics includes the version, model, backend and device details, never transcripts or audio.",14,true),Ui.Row(Ui.Button("Keyboard shortcuts",()=>Navigate("Shortcuts")),Ui.Button("About SonaBar",()=>Navigate("About")))));
    private UIElement About()=>Ui.Stack(Ui.Hero(Ui.Text("SonaBar",36),Ui.Text("Speech to Text. Private. Offline.",20),Ui.Text("Created by SilasNet",14,true)),Ui.Card(Ui.Text("About this build",20),Ui.Text("Version "+Brand.Version+" · unsigned development build",13,true),Ui.Text("Built independently for Windows. Speech: whisper.cpp. Local writing: llama.cpp. Models are downloaded separately under their respective licenses.",14,true),Ui.Button("Third-party notices",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"THIRD_PARTY_NOTICES.md")){UseShellExecute=true}))),Ui.Card(Ui.Text("Updates",20),Ui.Text("When a release feed is configured, SonaBar checks in the background, verifies the downloaded installer with SHA-256 and installs it automatically when you quit. You never need to download an update by hand.",13,true),Ui.Button("Open update settings file",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"sonabar-update.json")){UseShellExecute=true}))));
    private static bool Confirm(string message)=>MessageBox.Show(message,"SonaBar",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
}
