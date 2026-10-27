using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using SonaBar.Storage;

namespace SonaBar.App;
public sealed partial class MainWindow : ThemedWindow
{
    /// <summary>Raised after the Welcome flow has saved its final step.</summary>
    public event Action? OnboardingCompleted;
    private readonly AppController c;
    private readonly ContentControl page = new();
    private readonly TextBlock heading = Ui.Text("Overview",28);
    private readonly TextBlock subtitle = Ui.Text("",13,true);
    private readonly ScrollViewer pageScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, IsTabStop = false };
    private readonly ComboBox pageChoice = new();
    private Grid? navRow;
    private Grid? pageHeader;
    private Button? footerSave;
    private Button? footerAction;
    private Button? recordButton;
    private TextBlock? footerStatus;
    private TextBlock? footerDetail;
    private System.Windows.Shapes.Ellipse? footerDot;
    private Waveform? footerWave;
    private DispatcherTimer? footerTimer;
    private readonly Stopwatch footerClock = new();
    private string currentPage = "Overview";
    private CancellationTokenSource? download;
    private SettingsDialog? settingsDialog;
    private TextBlock? homeStatusTitle;
    private TextBlock? homeStatusDetail;
    private TextBlock? homeModeValue;
    private TextBlock? homeHotkeyValue;
    private TextBlock? homeModelValue;
    private TextBlock? homeProcessingValue;
    private TextBlock? homeLastText;
    private TextBlock? homeLastPlaceholder;
    private Button? homeCopy;
    private Button? homePrimary;
    private bool selectingPage;
    // One compact shell owns every destination. The selector is the only navigation
    // chrome, so the old 1080x780 workspace cannot reappear by accident.
    private static readonly (string Label,string Key)[] PageCatalog =
    [
        ("Home","Overview"),
        ("General","General"),
        ("Dictation","Dictation"),
        ("Writing","Writing"),
        ("Models","Models"),
        ("Languages","Languages"),
        ("Audio","Audio"),
        ("Shortcuts","Shortcuts"),
        ("Custom words","Custom Words"),
        ("Snippets","Snippets"),
        ("Applications","Applications"),
        ("History","History"),
        ("Statistics","Statistics"),
        ("Privacy","Privacy"),
        ("Advanced","Advanced"),
        ("Debug","Debug"),
        ("Files","Files"),
        ("Meetings","Meetings"),
        ("About","About"),
        ("Help","Help")
    ];
    public MainWindow(AppController controller)
    {
        c = controller; Title = "SonaBar";
        // The compact shell is the only product window. It never grows into the old
        // multi-page workspace, and all tools are reached through the page selector.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(430, workArea.Width - 24);
        Height = Math.Max(520, Math.Min(660, workArea.Height - 32));
        MinWidth = Math.Min(380, Width);
        MinHeight = Math.Min(520, Height);
        UseCompactChrome();
        DataContext = c;
        var layout = new Grid();
        layout.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty,"Surface");
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(BuildCompactHeader());
        var content = new Grid { Margin = new Thickness(20,0,20,0) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        pageHeader = new Grid { HorizontalAlignment=HorizontalAlignment.Stretch };
        heading.Margin=new Thickness(0,0,0,2); heading.FontWeight=FontWeights.SemiBold;
        subtitle.Margin=new Thickness(0,0,0,10);
        pageHeader.Children.Add(Ui.Stack(heading,subtitle));
        content.Children.Add(pageHeader);
        page.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        page.Focusable=false; page.IsTabStop=false;
        pageScroll.Content=page; pageScroll.Margin=new Thickness(0,2,0,10); pageScroll.HorizontalAlignment=HorizontalAlignment.Stretch; pageScroll.HorizontalContentAlignment=HorizontalAlignment.Stretch; Grid.SetRow(pageScroll,1); content.Children.Add(pageScroll);
        Grid.SetRow(content,1); layout.Children.Add(content);
        var footer = BuildFooter(); Grid.SetRow(footer,2); layout.Children.Add(footer);
        Content=layout;
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
    private UIElement BuildCompactHeader()
    {
        var root = new StackPanel { Margin = new Thickness(20, 10, 20, 8) };
        var top = new Grid { MinHeight = 32 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = Ui.Logo(32);
        logo.Margin = new Thickness(0, 0, 10, 0);
        brand.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var wordmark = Ui.Text("SonaBar", 14);
        wordmark.FontWeight = FontWeights.SemiBold;
        wordmark.Margin = new Thickness(0);
        wordmark.LineHeight = 17;
        var byline = Ui.Text("by SilasNet · " + Brand.Version, 10, true);
        byline.Margin = new Thickness(0);
        byline.LineHeight = 13;
        names.Children.Add(wordmark); names.Children.Add(byline);
        brand.Children.Add(names);
        brand.MouseLeftButtonDown += BeginWindowDrag;
        top.Children.Add(brand);

        var dragArea = new Border { Background = Brushes.Transparent, Margin = new Thickness(8, 0, 8, 0) };
        dragArea.MouseLeftButtonDown += BeginWindowDrag;
        Grid.SetColumn(dragArea, 1); top.Children.Add(dragArea);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var privacy = Ui.StatusPill("●  Local & private");
        privacy.Margin = new Thickness(0, 0, 6, 0);
        actions.Children.Add(privacy);
        var pin = new Button
        {
            Content = Ui.Icon(Ui.PinIcon, 14, filled: true),
            Style = (Style)FindResource("IconButton"),
            ToolTip = "Keep SonaBar on top",
            Margin = new Thickness(0, 0, 2, 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(pin, "Keep SonaBar on top");
        pin.Click += (_, _) => TogglePin(pin);
        actions.Children.Add(pin);
        var gear = new Button
        {
            Content = Ui.Icon(Ui.SettingsIcon, 15, filled: true),
            Style = (Style)FindResource("IconButton"),
            ToolTip = "Settings"
        };
        System.Windows.Automation.AutomationProperties.SetName(gear, "Settings");
        gear.Click += (_, _) => OpenSettings();
        actions.Children.Add(gear);
        actions.Children.Add(CaptionButton(Ui.MinimizeIcon, "Minimise", () => WindowState = WindowState.Minimized));
        actions.Children.Add(CaptionButton(Ui.CloseIcon, "Close", Hide, true));
        Grid.SetColumn(actions, 2); top.Children.Add(actions);
        root.Children.Add(top);

        navRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        navRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navRow.ColumnDefinitions.Add(new ColumnDefinition());
        var navLabel = Ui.Text("Go to", 11, true);
        navLabel.Margin = new Thickness(0, 0, 10, 0);
        navLabel.VerticalAlignment = VerticalAlignment.Center;
        navRow.Children.Add(navLabel);
        pageChoice.ItemsSource = PageCatalog.Select(item => item.Label).ToArray();
        pageChoice.SelectedItem = "Home";
        pageChoice.Height = 32;
        pageChoice.FontSize = 12;
        pageChoice.HorizontalAlignment = HorizontalAlignment.Stretch;
        pageChoice.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(pageChoice, "Go to");
        pageChoice.SelectionChanged += (_, _) =>
        {
            if (selectingPage) return;
            if (pageChoice.SelectedItem is string label) Navigate(PageKeyFor(label));
        };
        Grid.SetColumn(pageChoice, 1); navRow.Children.Add(pageChoice);
        root.Children.Add(navRow);
        return root;
    }
    private void BeginWindowDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
        // The compact shell is intentionally fixed. A double click on the title bar must not
        // expand it into the old workspace, so only a single press starts a drag.
        if (e.ClickCount != 1) { e.Handled = true; return; }
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }
    private Button CaptionButton(string icon, string tooltip, Action action, bool close = false)
    {
        var button = new Button
        {
            Content = Ui.Icon(icon, close ? 12 : 13, 1.6),
            Style = (Style)FindResource(close ? "CloseCaptionButton" : "CaptionButton"),
            ToolTip = tooltip,
            Margin = new Thickness(2, 0, 0, 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }
    private void TogglePin(Button pin)
    {
        var keepOnTop = !Topmost;
        Topmost = keepOnTop;
        pin.SetResourceReference(Control.ForegroundProperty, keepOnTop ? "Accent" : "TextSecondary");
        if (keepOnTop) pin.SetResourceReference(Control.BackgroundProperty, "AccentSoft");
        else pin.Background = Brushes.Transparent;
        var label = keepOnTop ? "Stop keeping SonaBar on top" : "Keep SonaBar on top";
        pin.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(pin, label);
    }
    private static string PageLabelFor(string key) => PageCatalog.FirstOrDefault(item => item.Key == key).Label ?? "Home";
    private static string PageKeyFor(string label) => PageCatalog.FirstOrDefault(item => item.Label == label).Key ?? "Overview";
    public void OpenSettings()
    {
        if (settingsDialog is { IsVisible: true }) { settingsDialog.Activate(); return; }
        var dialog = new SettingsDialog(c) { Owner = this };
        settingsDialog = dialog;
        try { dialog.ShowDialog(); }
        finally { settingsDialog = null; UpdateHome(); UpdateFooter(); }
    }
    private Border BuildFooter()
    {
        var grid = new Grid { HorizontalAlignment=HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        recordButton = new Button { Style=(Style)FindResource("RecordButton"),Content=Ui.EarIcon(21),ToolTip="Start or stop dictation",Width=40,Height=40 };
        recordButton.Click += (_,_)=>DictationAction();
        grid.Children.Add(recordButton);
        var statusPanel = new StackPanel { Margin=new Thickness(12,0,8,0),VerticalAlignment=VerticalAlignment.Center };
        var statusLine = new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
        footerDot = new System.Windows.Shapes.Ellipse { Width=7,Height=7,Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center };
        footerDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Success");
        footerStatus = Ui.Text("Ready",13); footerStatus.FontWeight=FontWeights.SemiBold; footerStatus.Margin=new Thickness(0,0,0,1); footerStatus.TextTrimming=TextTrimming.CharacterEllipsis;
        footerDetail = Ui.Text("Ctrl + Shift + Space",10,true); footerDetail.Margin=new Thickness(0); footerDetail.TextTrimming=TextTrimming.CharacterEllipsis;
        statusLine.Children.Add(footerDot); statusLine.Children.Add(footerStatus);
        statusPanel.Children.Add(statusLine); statusPanel.Children.Add(footerDetail);
        Grid.SetColumn(statusPanel,1); grid.Children.Add(statusPanel);
        footerWave = new Waveform { Width=58,Height=22,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed };
        Grid.SetColumn(footerWave,2); grid.Children.Add(footerWave);
        footerSave = Ui.Button("Save",()=>{c.SaveSettings();c.Inform("Settings saved");},true);
        footerSave.Style=(Style)FindResource("PositiveButton"); footerSave.MinWidth=56; footerSave.Height=32; footerSave.Padding=new Thickness(10,3,10,3); footerSave.Margin=new Thickness(0,0,8,0); footerSave.HorizontalAlignment=HorizontalAlignment.Right; footerSave.Visibility=Visibility.Collapsed;
        Grid.SetColumn(footerSave,3); grid.Children.Add(footerSave);
        footerAction = Ui.Button("Start",DictationAction,true);
        footerAction.Style=(Style)FindResource("BlueButton"); footerAction.MinWidth=116; footerAction.Height=34; footerAction.Padding=new Thickness(12,3,12,3); footerAction.Margin=new Thickness(0); footerAction.HorizontalAlignment=HorizontalAlignment.Right;
        Grid.SetColumn(footerAction,4); grid.Children.Add(footerAction);
        var border = new Border { Child=grid,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(18,8,18,8) };
        border.SetResourceReference(Border.BackgroundProperty,"Surface");
        border.SetResourceReference(Border.BorderBrushProperty,"Line");
        return border;
    }
    private static bool IsSettingsPage(string name) => name is "General" or "Dictation" or "Writing" or "Audio" or "Shortcuts" or "Privacy" or "Advanced" or "Debug" or "Languages";
    private void DictationAction()
    {
        if(c.Machine.State==OperationState.Recording)c.ToggleDictation();
        else if(c.Busy)c.Cancel();
        else c.ToggleDictation();
    }
    private void ControllerChanged(object? sender,PropertyChangedEventArgs e)
    {
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
        footerAction.Content=recording?"Stop":busy?"Cancel":"Start";
        footerAction.ToolTip=recording?"Stop and insert":busy?"Cancel the current operation":"Start dictation";
        footerAction.Style=(Style)FindResource("BlueButton");
        footerAction.IsEnabled=true;
        footerAction.Visibility=currentPage is "Overview" or "Welcome"?Visibility.Collapsed:Visibility.Visible;
        footerSave.Visibility=IsSettingsPage(currentPage)&&!busy?Visibility.Visible:Visibility.Collapsed;
        footerDetail.Text=busy?c.StageLabel:c.Settings.PlainHotkey+(string.IsNullOrEmpty(c.LastDictationLatency)?"":"   ·   "+c.LastDictationLatency);
        footerWave.Visibility=state is OperationState.Recording or OperationState.Transcribing or OperationState.Enhancing or OperationState.Inserting?Visibility.Visible:Visibility.Collapsed;
        UpdateHome();
    }
    public void Navigate(string name)
    {
        currentPage=name;
        var label=PageLabelFor(name);
        if(pageChoice.SelectedItem is not string selected||selected!=label)
        {
            selectingPage=true;
            pageChoice.SelectedItem=label;
            selectingPage=false;
        }
        if(navRow is not null)navRow.Visibility=name=="Welcome"?Visibility.Collapsed:Visibility.Visible;
        heading.Text=name switch { "Overview"=>"SonaBar","Custom Words"=>"Custom words","Applications"=>"Applications","Audio"=>"Audio & feedback","General"=>"General","Writing"=>"Writing tools",_=>name };
        // Home and onboarding carry their own headline; a second title above them only
        // repeats the message and pushes the actual content down.
        var showHeader = name is not ("Welcome" or "Overview");
        if(pageHeader is not null)pageHeader.Visibility=showHeader?Visibility.Visible:Visibility.Collapsed;
        subtitle.Text=name switch
        {
            "Welcome"=>"A private Windows dictation app.",
            "Overview"=>"Private, offline speech to text. Your words stay on this PC.",
            "General"=>"Appearance, startup and shortcuts.",
            "Dictation"=>"Make speech recognition work your way.",
            "Writing"=>"Local rewriting, translation and selected-text actions.",
            "Models"=>"Manage the local engines behind your workflow.",
            "Languages"=>"Choose the language SonaBar should expect.",
            "Audio"=>"Choose your microphone and optional feedback sounds.",
            "Shortcuts"=>"Set the keys for dictation and selected text.",
            "Custom Words"=>"Teach SonaBar names, brands and recurring corrections.",
            "Snippets"=>"Turn short phrases into reusable text.",
            "Applications"=>"Choose insertion behavior for each app.",
            "History"=>"Find and reuse previous dictations.",
            "Statistics"=>"A private summary of your dictation activity.",
            "Privacy"=>"Your data stays under your control.",
            "Advanced"=>"Hardware, performance and diagnostics.",
            "Debug"=>"Local diagnostics that never include your words.",
            "About"=>"Independent Windows dictation by SilasNet.",
            "Help"=>"Quick start, delivery help and privacy in one place.",
            "Files"=>"Turn recordings into searchable text.",
            "Meetings"=>"Capture conversations and keep the important parts.",
            _=>"Set up your private writing workspace."
        };
        page.Content = name switch
        {
            "Welcome" => Welcome(), "Overview" => Overview(), "General" => General(), "Dictation" => Dictation(), "Writing" => WritingTools(), "Models" => Models(), "Languages" => LanguagePage(), "Audio" => AudioPage(), "Shortcuts" => Shortcuts(), "Custom Words" => CustomWords(), "Snippets" => Snippets(), "Applications" => Applications(), "History" => History(), "Statistics" => Statistics(), "Privacy" => Privacy(), "Advanced" => Advanced(), "Debug" => Debug(), "About" => About(), "Help" => Help(), "Files" => Files(), "Meetings" => Meetings(), _ => Overview()
        };
        pageScroll.ScrollToTop();
        UpdateFooter();
    }
    private UIElement Overview()
    {
        homeStatusTitle = Ui.Text("Ready", 32);
        homeStatusTitle.Margin = new Thickness(0, 0, 0, 3);
        homeStatusTitle.LineHeight = 38;
        homeStatusDetail = Ui.Text("", 13, true);
        homeStatusDetail.Margin = new Thickness(0);
        homeStatusDetail.LineHeight = 19;
        homeStatusDetail.TextWrapping = TextWrapping.Wrap;
        var statusCard = new Border
        {
            Padding = new Thickness(20, 12, 20, 11),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(0),
            Child = Ui.Stack(homeStatusTitle, homeStatusDetail)
        };
        statusCard.SetResourceReference(Border.BackgroundProperty, "AccentSoft");

        var details = new Grid();
        for (var row = 0; row < 4; row++) details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        details.ColumnDefinitions.Add(new ColumnDefinition());
        void AddDetail(int row, string label, TextBlock value)
        {
            var name = Ui.Text(label, 12, true);
            name.Margin = new Thickness(0, 0, 10, 4);
            value.Margin = new Thickness(0, 0, 0, 4);
            value.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(name, row); Grid.SetColumn(name, 0); details.Children.Add(name);
            Grid.SetRow(value, row); Grid.SetColumn(value, 1); details.Children.Add(value);
        }
        homeModeValue = Ui.Text("", 13);
        homeHotkeyValue = Ui.Text("", 13);
        homeModelValue = Ui.Text("", 13);
        homeProcessingValue = Ui.Text("", 13);
        AddDetail(0, "Mode", homeModeValue);
        AddDetail(1, "Hotkey", homeHotkeyValue);
        AddDetail(2, "Model", homeModelValue);
        AddDetail(3, "Processing", homeProcessingValue);
        var detailsCard = new Border
        {
            Padding = new Thickness(16, 10, 16, 2),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = details
        };
        detailsCard.SetResourceReference(Border.BackgroundProperty, "BackgroundElevated");
        detailsCard.SetResourceReference(Border.BorderBrushProperty, "Border");

        homeLastText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 20,
            TextTrimming = TextTrimming.None
        };
        System.Windows.Automation.AutomationProperties.SetName(homeLastText, "Last transcription");
        homeLastText.SetBinding(TextBlock.TextProperty, new Binding(nameof(AppController.LastText)) { Mode = BindingMode.OneWay });
        var lastScroll = new ScrollViewer
        {
            Content = homeLastText,
            MinHeight = 54,
            MaxHeight = 84,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            Focusable = false,
            IsTabStop = false
        };
        homeLastPlaceholder = Ui.Text("Your last transcription will appear here.", 13, true);
        homeLastPlaceholder.Margin = new Thickness(0, 3, 0, 0);
        homeLastPlaceholder.IsHitTestVisible = false;
        var lastArea = new Grid { MinHeight = 54, MaxHeight = 84 };
        lastArea.Children.Add(lastScroll);
        lastArea.Children.Add(homeLastPlaceholder);

        var lastHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        lastHeader.ColumnDefinitions.Add(new ColumnDefinition());
        lastHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lastTitle = Ui.Text("Last transcription", 13);
        lastTitle.Margin = new Thickness(0);
        lastHeader.Children.Add(lastTitle);
        homeCopy = new Button
        {
            Content = "Copy",
            FontSize = 11,
            Height = 27,
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(homeCopy, "Copy last transcription");
        homeCopy.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(c.LastText)) return;
            try { c.Clipboard.SetText(c.LastText); c.Inform("Copied the last transcription"); }
            catch (Exception error) { c.Notify("Could not copy the text: " + error.Message); }
        };
        Grid.SetColumn(homeCopy, 1); lastHeader.Children.Add(homeCopy);
        var lastCard = new Border
        {
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = Ui.Stack(lastHeader, lastArea)
        };
        lastCard.SetResourceReference(Border.BackgroundProperty, "BackgroundElevated");
        lastCard.SetResourceReference(Border.BorderBrushProperty, "Border");

        homePrimary = new Button
        {
            Height = 46,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };
        homePrimary.SetResourceReference(Control.StyleProperty, "PrimaryButton");
        System.Windows.Automation.AutomationProperties.SetName(homePrimary, "Start dictation");
        homePrimary.Click += (_, _) =>
        {
            if (c.Readiness.Stage == ReadinessStage.NeedsModel) { Navigate("Models"); return; }
            if (c.Machine.State == OperationState.Recording) c.ToggleDictation();
            else if (c.Busy) c.Cancel();
            else c.ToggleDictation();
        };

        UpdateHome();
        return Ui.Stack(statusCard, detailsCard, lastCard, homePrimary);
    }
    private void UpdateHome()
    {
        if (homeStatusTitle is null || homeStatusDetail is null || homeModeValue is null || homeHotkeyValue is null ||
            homeModelValue is null || homeProcessingValue is null || homeLastText is null || homeLastPlaceholder is null || homeCopy is null || homePrimary is null)
            return;
        var state = c.Machine.State;
        var title = "Ready";
        var detail = c.Status == "Ready when you are"
            ? "Press " + c.Settings.PlainHotkey + " or start below."
            : c.Status;
        if (c.Paused) { title = "Paused"; detail = "Resume SonaBar from the tray menu."; }
        else if (state == OperationState.Recording) { title = "Listening"; detail = "Speak now. Release or press the shortcut again to insert."; }
        else if (state == OperationState.Transcribing) { title = "Transcribing"; detail = "Working locally on this PC."; }
        else if (state == OperationState.Enhancing) { title = "Writing"; detail = "Improving the text locally."; }
        else if (state == OperationState.Inserting) { title = "Inserting"; detail = "Sending text to the focused app."; }
        else if (c.Readiness.Stage == ReadinessStage.NeedsModel) { title = "No model yet"; detail = string.IsNullOrWhiteSpace(c.Readiness.Detail) ? "Open Models to download one." : c.Readiness.Detail; }
        else if (c.Readiness.Stage == ReadinessStage.Error) { title = "Needs attention"; detail = string.IsNullOrWhiteSpace(c.Readiness.Detail) ? c.Status : c.Readiness.Detail; }
        else if (c.Readiness.Stage is ReadinessStage.CheckingModels or ReadinessStage.PreparingModel) { title = "Preparing"; detail = "Checking the local model on this PC."; }
        else if (c.Readiness.Stage == ReadinessStage.Starting) { title = "Starting"; detail = "Getting the microphone ready."; }
        else if (c.Busy) { title = "Working"; detail = c.StageLabel; }
        homeStatusTitle.Text = title;
        homeStatusDetail.Text = detail;
        homeModeValue.Text = c.Settings.PushToTalk ? "Push to talk" : "Toggle";
        homeHotkeyValue.Text = c.Settings.PlainHotkey.ToString();
        homeModelValue.Text = c.SpeechModelName;
        homeProcessingValue.Text = c.ProcessingSummary;
        homeLastPlaceholder.Visibility = string.IsNullOrWhiteSpace(c.LastText) ? Visibility.Visible : Visibility.Collapsed;
        homeCopy.IsEnabled = !string.IsNullOrWhiteSpace(c.LastText);
        var preparing = !c.Busy && c.Readiness.Stage is ReadinessStage.CheckingModels or ReadinessStage.PreparingModel;
        homePrimary.IsEnabled = !c.Paused && !preparing;
        homePrimary.Content = c.Readiness.Stage == ReadinessStage.NeedsModel ? "Choose a model"
            : preparing ? "Preparing..."
            : state == OperationState.Recording ? "Stop & insert"
            : c.Busy ? "Cancel"
            : "Start dictation";
        System.Windows.Automation.AutomationProperties.SetName(homePrimary, homePrimary.Content?.ToString() ?? "Start dictation");
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
        var shortcuts=new System.Windows.Controls.Primitives.UniformGrid{Columns=1,Margin=new Thickness(0,2,0,4)};
        foreach(var item in new[]
        {
            ("Dictation","Language, formatting and voice controls.","Dictation"),
            ("Audio & feedback","Microphone, feedback sounds and ducking.","Audio"),
            ("Shortcuts","Global keys and push-to-talk.","Shortcuts"),
            ("Models","Speech and writing engines.","Models"),
            ("Writing","Local rewrite and translation.","Writing"),
            ("Custom words","Names and recurring corrections.","Custom Words"),
            ("Snippets","Reusable text for spoken or typed phrases.","Snippets"),
            ("Applications","Insertion behavior for each app.","Applications"),
            ("Privacy","History and local data controls.","Privacy"),
            ("Advanced","Processing, hardware and diagnostics.","Advanced")
        })shortcuts.Children.Add(Destination(item.Item1,item.Item2,item.Item3));
        return Ui.Stack(Ui.Card(Ui.Text("Appearance & startup",18),Ui.SettingRow("Appearance","Match Windows or choose a fixed theme.",appearance),Ui.SettingRow("Start with Windows","Keep SonaBar available from the system tray.",startup),Ui.SettingRow("Automatic updates","Download verified releases quietly and install them when SonaBar closes.",updates)),Ui.Text("TOOLS & SETTINGS",11,true),shortcuts,Ui.Row(Ui.Button("About SonaBar",()=>Navigate("About")),Ui.Button("Help",()=>Navigate("Help"))));
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
        var preview=Ui.Button("Preview",()=>c.PreviewFeedbackSound());preview.IsEnabled=c.Settings.Sounds!="Off";preview.Margin=new Thickness(8,0,0,0);
        var soundMode=Ui.Choice(new[]{"Off","Minimal","Standard"},c.Settings.Sounds,x=>{c.Settings.Sounds=x;preview.IsEnabled=x!="Off";});
        var content=Ui.Stack(Ui.Card(Ui.Text("Input device",20),Ui.Choice(devices,devices.FirstOrDefault(x=>x.Id==c.Settings.MicrophoneId)??devices[0],x=>c.Settings.MicrophoneId=x.Id),meter,message,test,Ui.Button("Refresh devices",()=>Navigate("Audio"))),Ui.Card(Ui.Text("Feedback sounds",18),Ui.Text("Optional built-in tones confirm recording start, finish and errors. They play through the default output and never change Windows sound settings.",13,true),Ui.Row(soundMode,preview),Ui.Text("Minimal plays the finished tone only. Standard adds the start and error tones. Preview plays the finished tone.",13,true)),Ui.Card(Ui.Check("Lower system audio while dictating",c.Settings.DuckAudio,x=>c.Settings.DuckAudio=x),Ui.Text("Recording stays silent unless feedback sounds are enabled. SonaBar never changes Windows sound settings.",13,true)));
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
        return Ui.Stack(Ui.Card(Ui.Text("Names worth getting right",22),Ui.Text("One name, brand, gaming term or abbreviation per line. Whisper and local writing use these. Parakeet TDT currently ignores word boosting; no accuracy gain is claimed.",14,true),words,Ui.Row(Ui.Button("Import…",()=> { var d=new Microsoft.Win32.OpenFileDialog { Filter="Text files|*.txt" }; if(d.ShowDialog()==true) words.Text=File.ReadAllText(d.FileName); }),Ui.Button("Export…",()=> { var d=new Microsoft.Win32.SaveFileDialog { Filter="Text files|*.txt",FileName="SonaBar-custom-words.txt" }; if(d.ShowDialog()==true)File.WriteAllText(d.FileName,words.Text); }),Ui.Button("Save words",()=> { c.Settings.CustomWords=words.Text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToList(); c.SaveSettings(); c.Inform("Custom words saved"); },true))),Ui.Card(Ui.Text("Teach a recurring correction",20),Ui.Text("Only corrections you explicitly add are saved. Complete words and phrases are matched; other words stay unchanged.",13,true),mappings,Ui.Text("Often recognized as",12,true),heard,Ui.Text("What I actually mean",12,true),intended,Ui.Row(Ui.Button("Add correction",()=>{if(string.IsNullOrWhiteSpace(heard.Text)||string.IsNullOrWhiteSpace(intended.Text))throw new InvalidOperationException("Enter both the heard and intended word.");c.Settings.VocabularyCorrections.Add(new(heard.Text.Trim(),intended.Text.Trim()));if(!c.Settings.CustomWords.Contains(intended.Text.Trim()))c.Settings.CustomWords.Add(intended.Text.Trim());c.SaveSettings();Navigate("Custom Words");},true),Ui.Button("Remove selected",()=>{if(mappings.SelectedIndex>=0){c.Settings.VocabularyCorrections.RemoveAt(mappings.SelectedIndex);c.SaveSettings();Navigate("Custom Words");}}))));
    }
    private UIElement Snippets()
    {
        var list=new ListBox { ItemsSource=c.Settings.Snippets.ToList(),DisplayMemberPath="Name",Height=130 }; var name=Ui.Input(); var trigger=Ui.Input(); var content=Ui.Input("",true);
        var empty=Ui.Text("No snippets yet. Add a name, trigger and content below.",13,true);empty.Visibility=c.Settings.Snippets.Count==0?Visibility.Visible:Visibility.Collapsed;list.Visibility=c.Settings.Snippets.Count==0?Visibility.Collapsed:Visibility.Visible;
        var delete=Ui.Button("Delete",()=> { if(list.SelectedItem is Snippet old && Confirm("Delete this snippet?")) { c.Settings.Snippets.Remove(old); c.SaveSettings(); Navigate("Snippets"); } });delete.IsEnabled=false;
        list.SelectionChanged+=(_,_)=> { delete.IsEnabled=list.SelectedItem is Snippet; if(list.SelectedItem is Snippet s) { name.Text=s.Name; trigger.Text=s.Trigger; content.Text=s.Content; } };
        var typed=Ui.Check("Expand snippets while typing",c.Settings.ExpandTypedSnippets,x=> { c.Settings.ExpandTypedSnippets=x; c.SaveSettings(); });
        return Ui.Stack(
            Ui.Card(Ui.Text("Say a little. Insert a lot.",22),Ui.Text("Exact phrases expand after dictation. Separate alternatives with |. Variables: {date}, {time}, {clipboard}.",13,true),list,empty,Ui.Text("Name"),name,Ui.Text("Trigger (spoken or typed)"),trigger,Ui.Text("Content"),content,Ui.Row(Ui.Button("New",()=> {list.SelectedItem=null; name.Clear(); trigger.Clear(); content.Clear();delete.IsEnabled=false;}),Ui.Button("Save snippet",()=> { if(string.IsNullOrWhiteSpace(trigger.Text)||string.IsNullOrWhiteSpace(name.Text))throw new InvalidOperationException("Enter a name and a trigger."); if(list.SelectedItem is Snippet old)c.Settings.Snippets.Remove(old); c.Settings.Snippets.Add(new(name.Text,trigger.Text,content.Text)); c.SaveSettings(); Navigate("Snippets"); },true),delete)),
            Ui.Card(Ui.Text("Typed expansion",18),typed,Ui.Text("Off by default. Type a trigger followed by a space, Enter, Tab or punctuation in another app. SonaBar skips its own window and password fields. Some games and elevated apps reject injected text.",13,true)));
    }
    private UIElement Applications()
    {
        var list=new ListBox { ItemsSource=c.Settings.Profiles.ToList(),DisplayMemberPath="Executable",Height=120 }; var exe=Ui.Input("Discord.exe"); var instruction=Ui.Input("Keep a natural conversational tone.",true); var strategy=Ui.Choice(Enum.GetValues<InsertionStrategy>(),InsertionStrategy.Automatic,_=>{}); var enter=Ui.Check("Press Enter after insertion (may send or execute text)",false,_=>{});
        var empty=Ui.Text("No app profiles yet. SonaBar uses Automatic insertion for every app until you add one.",13,true);empty.Visibility=c.Settings.Profiles.Count==0?Visibility.Visible:Visibility.Collapsed;list.Visibility=c.Settings.Profiles.Count==0?Visibility.Collapsed:Visibility.Visible;
        var delete=Ui.Button("Delete profile",()=> {if(list.SelectedItem is ApplicationProfile p&&Confirm("Delete this application profile?")){c.Settings.Profiles.Remove(p);c.SaveSettings();Navigate("Applications");}});delete.IsEnabled=false;
        list.SelectionChanged+=(_,_)=> {delete.IsEnabled=list.SelectedItem is ApplicationProfile;if(list.SelectedItem is ApplicationProfile p){exe.Text=p.Executable; instruction.Text=p.Instruction; strategy.SelectedItem=p.Strategy; enter.IsChecked=p.PressEnter;}};
        return Ui.Stack(Ui.Card(Ui.Text("A good fit for every app",22),Ui.Text("Profiles are matched by executable name. Use Compatibility for games and opaque windows that cannot be inspected.",13,true),list,empty,Ui.Text("Executable name"),exe,Ui.Text("Insertion method"),strategy,Ui.Text("Writing instruction"),instruction,enter,Ui.Row(Ui.Button("Save profile",()=> { var p=new ApplicationProfile(exe.Text.Trim(),(InsertionStrategy)strategy.SelectedItem,enter.IsChecked==true,instruction.Text); c.Settings.Profiles.RemoveAll(x=>x.Executable.Equals(p.Executable,StringComparison.OrdinalIgnoreCase)); c.Settings.Profiles.Add(p);c.SaveSettings();Navigate("Applications"); },true),delete)));
    }
    private UIElement History()
    {
        var search=Ui.Input(); var list=new ListBox { MinHeight=250,MaxHeight=410 }; var range=Ui.Choice(new[]{"All time","Today","Past week","Past month"},"All time",_=>{});
        var empty=Ui.Text("",13,true);empty.Margin=new Thickness(0,2,0,8);
        var emptyAction=Ui.Button("Open privacy settings",()=>Navigate("Privacy"));emptyAction.Visibility=Visibility.Collapsed;
        var copy=Ui.Button("Copy",()=> {if(list.SelectedItem is HistoryEntry h)c.Clipboard.SetText(h.Text);});
        var reuse=Ui.Button("Reuse",()=> {if(list.SelectedItem is HistoryEntry h){c.SetResult(h.Text);Navigate("Overview");}});
        var delete=Ui.Button("Delete",()=> {if(list.SelectedItem is HistoryEntry h&&Confirm("Delete this history entry?")){c.History.Delete(h.Id);Refresh();}});
        var clear=Ui.Button("Clear history",()=> {if(Confirm("Permanently delete all transcription history? Lifetime statistics will remain.")){c.History.ClearHistory();Refresh();}});
        void UpdateActions() { var has=list.SelectedItem is HistoryEntry; copy.IsEnabled=reuse.IsEnabled=delete.IsEnabled=has; clear.IsEnabled=list.Items.Count>0; }
        void Refresh()
        {
            var start=range.SelectedIndex switch {1=>(DateTimeOffset?)DateTimeOffset.Now.Date,2=>DateTimeOffset.Now.AddDays(-7),3=>DateTimeOffset.Now.AddMonths(-1),_=>null};
            var entries=c.History.Search(search.Text,start);
            list.ItemsSource=entries;
            empty.Text=entries.Count>0?"":!string.IsNullOrWhiteSpace(search.Text)?"No dictations match this search.":c.Settings.StoreHistory?"No dictations yet. Finish a dictation and it will appear here.":"History is off. Turn it on in Privacy to keep future dictations.";
            empty.Visibility=entries.Count==0?Visibility.Visible:Visibility.Collapsed;
            emptyAction.Visibility=entries.Count==0&&!c.Settings.StoreHistory&&string.IsNullOrWhiteSpace(search.Text)?Visibility.Visible:Visibility.Collapsed;
            list.Visibility=entries.Count==0?Visibility.Collapsed:Visibility.Visible;
        }
        list.SelectionChanged+=(_,_)=>UpdateActions(); search.TextChanged+=(_,_)=>{Refresh();UpdateActions();}; range.SelectionChanged+=(_,_)=>{Refresh();UpdateActions();};Refresh();UpdateActions();
        return Ui.Stack(Ui.Card(Ui.Text("Find your words again",22),Ui.Text("Search",12,true),search,range,list,empty,emptyAction,Ui.Row(copy,reuse,delete,clear)));
    }
    private UIElement Statistics()
    {
        var rows=c.History.Statistics(); var count=rows.Sum(x=>x.Dictations); var duration=rows.Sum(x=>x.Duration);
        var summary=Ui.Card(Ui.Text("Your voice, over time",22),Ui.Text($"{count:N0} dictations    ·    {rows.Sum(x=>x.Words):N0} words",25),Ui.Text($"{TimeSpan.FromSeconds(duration):g} recorded    ·    {(count>0?duration/count:0):N1}s average",14,true));
        if(count==0)
        {
            return Ui.Stack(summary,Ui.Card(Ui.Text("Nothing to measure yet",20),Ui.Text("Finish your first dictation and your private statistics will appear here. History does not need to be on for these local totals.",14,true),Ui.Row(Ui.Button("Start dictation",c.ToggleDictation,true),Ui.Button("Privacy settings",()=>Navigate("Privacy")))));
        }
        string Breakdown(IEnumerable<string> lines) => string.Join("\n",lines) is { Length:>0 } text ? text : "No data yet.";
        return Ui.Stack(summary,Ui.Card(Ui.Text("Languages",18),Ui.Text(Breakdown(rows.GroupBy(x=>x.Language).Select(g=>$"{g.Key}   ·   {g.Sum(x=>x.Words):N0} words"))),Ui.Text("Applications",18),Ui.Text(Breakdown(rows.GroupBy(x=>x.Application).Select(g=>$"{g.Key}   ·   {g.Sum(x=>x.Dictations):N0} dictations"))),Ui.Text("Models",18),Ui.Text(Breakdown(rows.GroupBy(x=>x.Model).Select(g=>$"{g.Key}   ·   {g.Sum(x=>x.Dictations):N0} dictations"))),Ui.Text("These totals stay on this PC and never include transcript text.",12,true)),Ui.Button("Reset lifetime statistics",()=> {if(Confirm("Permanently reset lifetime statistics?")){c.History.ResetStatistics();Navigate("Statistics");}}));
    }
    private UIElement Privacy()=>Ui.Stack(Ui.Card(Ui.Text("Private by design",22),Ui.Text("No telemetry. No accounts. No uploaded recordings. Speech and writing models run locally. Internet is used only when you download models.",14,true),Ui.Check("Store transcription history on this computer",c.Settings.StoreHistory,x=>c.Settings.StoreHistory=x),Ui.Text("Keep transcript history for",13,true),Ui.Choice(new[]{0,7,30,90,365},c.Settings.HistoryRetentionDays,x=>c.Settings.HistoryRetentionDays=x),Ui.Text("0 means keep it until you clear it. New SonaBar installations keep history off by default. Turning history off prevents new transcript storage. Existing history remains until you clear it. Lifetime totals contain counts, languages, model names and application names; reset them in Statistics.",13,true),Ui.Button("Clear all history",()=> {if(Confirm("Permanently delete all saved history?"))c.History.ClearHistory();}),Ui.Button("Open local data folder",()=>Process.Start(new ProcessStartInfo(Brand.DataRoot){UseShellExecute=true}))));
    private UIElement Advanced()
    {
        var threadLabels = new[] { "Auto" }.Concat(Enumerable.Range(1, Environment.ProcessorCount).Select(value => value == 1 ? "1 thread" : value + " threads")).ToArray();
        var selectedThreads = c.Settings.CpuThreads == 0 ? "Auto" : c.Settings.CpuThreads == 1 ? "1 thread" : c.Settings.CpuThreads + " threads";
        var threadChoice = Ui.Choice(threadLabels,selectedThreads,value=>c.Settings.CpuThreads=value=="Auto"?0:int.Parse(value.Split(' ')[0]));
        threadChoice.IsEnabled = c.SpeechSupportsCpuThreads;
        threadChoice.ToolTip = c.SpeechSupportsCpuThreads ? "Applied to Whisper recognition on this PC." : "Parakeet and Qwen manage their own CPU threads.";
        return Ui.Stack(
            Ui.Card(Ui.Text("This device",22),Ui.Text(c.DescribeHardware(),13,true),Ui.Text(HardwareRecommendations.AccelerationText(c.Hardware),13,true),Ui.Text(HardwareRecommendations.Explain(c.Hardware),13,true),Ui.Row(Ui.Button("Detect again",()=>{c.RefreshHardware();c.Inform("Hardware details refreshed");Navigate("Advanced");}))),
            Ui.Card(Ui.Text("Performance",22),
                Ui.Text("Processing device",13,true),
                Ui.Choice(new[]{"Automatic","CPU","GPU / accelerated"},ProcessingDeviceInfo.Label(c.Settings.ProcessingDevice),value=>c.Settings.ProcessingDevice=ProcessingDeviceInfo.Parse(value)),
                Ui.Text("Whisper CPU threads",13,true),
                threadChoice,
                Ui.Check("Keep speech model loaded between uses",c.Settings.KeepSpeechLoaded,x=>c.Settings.KeepSpeechLoaded=x),
                Ui.Text("Automatic uses CPU for speech, which is faster for short dictations on many Windows devices. GPU acceleration is optional and falls back to CPU if unavailable. Parakeet and Qwen always run on CPU; their engines manage their own threads. Keeping a model loaded trades memory for faster repeat use.",13,true),
                Ui.Button("Diagnostics",()=>Navigate("Debug"))));
    }
    private UIElement Debug()
    {
        var timings=Ui.Check("",c.Settings.DebugLogging,x=>c.Settings.DebugLogging=x);
        return Ui.Stack(Ui.Card(Ui.Text("Diagnostics without your words",18),Ui.Text($"SonaBar {Brand.Version}\n{Environment.OSVersion}\n{Environment.ProcessorCount} logical processors\nSpeech: {c.SpeechModelName}\nProcessing: {c.ProcessingSummary}\nMicrophone: {c.Settings.MicrophoneId??"Windows default"}",13,true),Ui.SettingRow("Record dictation timings","Write T0–T11 durations locally for this device.",timings),Ui.Row(Ui.Button("Copy diagnostics",()=>c.Clipboard.SetText($"SonaBar {Brand.Version}\n{Environment.OSVersion}\nCPU threads: {Environment.ProcessorCount}\nModel: {c.SpeechModelName}\nProcessing: {c.ProcessingSummary}")),Ui.Button("Open log folder",()=> {var path=Path.Combine(Brand.DataRoot,"Logs");Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}))),Ui.Text("Timing logs contain stage durations and delivery state, never transcripts, audio, selected text, or native exception messages.",13,true));
    }
    private UIElement Help()=>Ui.Stack(Ui.Hero(Ui.Text("Start here",28),Ui.Text("Start dictating, solve delivery problems and keep your words private.",16,true)),Ui.Card(Ui.Text("Quick start",20),Ui.Text("Choose a speech model in Models. Put the cursor where the text should appear, then press your dictation shortcut and speak. Press the shortcut again to stop; SonaBar transcribes locally and inserts the result into the window that had focus when you started. Optional feedback sounds can be previewed in Audio. Snippets turn recurring phrases into reusable text; typed expansion is opt-in on the Snippets page.",14,true),Ui.Row(Ui.Button("Choose a model",()=>Navigate("Models")),Ui.Button("Set the shortcut",()=>Navigate("Shortcuts")),Ui.Button("Test the microphone",()=>Navigate("Audio")),Ui.Button("Open snippets",()=>Navigate("Snippets")))),Ui.Card(Ui.Text("If text does not arrive",20),Ui.Text("Keep the target window focused while SonaBar works. Some elevated applications reject input from a non-elevated app; run both at the same privilege level. For games or opaque windows, select the Compatibility insertion profile for that app. The delivery state and timing are recorded without transcript content.",14,true),Ui.Row(Ui.Button("Open applications",()=>Navigate("Applications")),Ui.Button("Open diagnostics",()=>Navigate("Debug")))),Ui.Card(Ui.Text("Privacy and data",20),Ui.Text("Speech and writing run on this PC. SonaBar has no account, telemetry or cloud transcription. History is optional, and model downloads only start when you ask for them. The local data folder contains settings, history and logs; audio is removed after processing.",14,true),Ui.Row(Ui.Button("Privacy settings",()=>Navigate("Privacy")),Ui.Button("Open local data folder",()=>Process.Start(new ProcessStartInfo(Brand.DataRoot){UseShellExecute=true})))),Ui.Card(Ui.Text("Free, without a license server",20),Ui.Text("SonaBar is free to use. There is no checkout, subscription, trial timer or license activation. Optional model downloads remain subject to their upstream licenses.",14,true)),Ui.Card(Ui.Text("Shortcuts and diagnostics",20),Ui.Text("The active shortcut is shown in the footer and can be changed in Shortcuts. Diagnostics include the version, model, processing device, thread setting and device details, never transcripts or audio.",14,true),Ui.Row(Ui.Button("Keyboard shortcuts",()=>Navigate("Shortcuts")),Ui.Button("About SonaBar",()=>Navigate("About")))));
    private UIElement About()
    {
        var source = Ui.Button("Source code on GitHub", () => Process.Start(new ProcessStartInfo(Brand.SourceCode){UseShellExecute=true}));
        System.Windows.Automation.AutomationProperties.SetHelpText(source, Brand.SourceCode);
        return Ui.Stack(
            Ui.Hero(Ui.Text("SonaBar",36),Ui.Text("Speech to Text. Private. Offline.",20),Ui.Text("Created by SilasNet · sonabar.silasnet.win",14,true)),
            Ui.Card(
                Ui.Text("About this build",20),
                Ui.Text("Version "+Brand.Version+" · Windows x64 · unsigned",13,true),
                Ui.Text("Built independently for Windows. Speech: whisper.cpp. Local writing: llama.cpp. Models are downloaded separately under their respective licenses.",14,true),
                Ui.Row(
                    Ui.Button("Third-party notices",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"THIRD_PARTY_NOTICES.md")){UseShellExecute=true})),
                    source)),
            Ui.Card(
                Ui.Text("Updates",20),
                Ui.Text("When a release feed is configured, SonaBar checks in the background, verifies the downloaded installer with SHA-256 and installs it automatically when you quit. You never need to download an update by hand.",13,true),
                Ui.Button("Open update settings file",()=>Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"sonabar-update.json")){UseShellExecute=true}))));
    }
    private static bool Confirm(string message)=>MessageBox.Show(message,"SonaBar",MessageBoxButton.YesNo,MessageBoxImage.Warning)==MessageBoxResult.Yes;
}
