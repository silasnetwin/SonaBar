using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SonaBar.App;

/// <summary>
/// The compact, everyday SonaBar window. It keeps the one action people need close at hand
/// while the full workspace remains available for models, snippets, history and diagnostics.
/// </summary>
public sealed class CompactWindow : ThemedWindow
{
    private readonly AppController c;
    private readonly Action<string> openWorkspace;
    private readonly TextBlock statusTitle = Ui.Text("Ready", 36);
    private readonly TextBlock statusDetail = Ui.Text("", 13, true);
    private readonly TextBlock modeValue = Ui.Text("", 13);
    private readonly TextBlock hotkeyValue = Ui.Text("", 13);
    private readonly TextBlock modelValue = Ui.Text("", 13);
    private readonly TextBlock processingValue = Ui.Text("", 13);
    private readonly TextBox lastBox;
    private readonly TextBlock lastPlaceholder = Ui.Text("Your last transcription will appear here.", 13, true);
    private readonly Button primary;
    private Button copy = null!;
    private SettingsDialog? settingsDialog;

    public CompactWindow(AppController controller, Action<string> openWorkspace)
    {
        c = controller;
        this.openWorkspace = openWorkspace;
        Title = "SonaBar";
        var area = SystemParameters.WorkArea;
        Width = Math.Min(430, area.Width - 24);
        MinWidth = Math.Min(380, Width);
        Height = Math.Max(320, Math.Min(660, area.Height - 32));
        MinHeight = Math.Min(520, Height);
        UseNativeChrome();
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        statusTitle.Margin = new Thickness(0, 0, 0, 4);
        statusTitle.LineHeight = 42;
        statusDetail.Margin = new Thickness(0);
        statusDetail.LineHeight = 19;

        var content = new StackPanel { Margin = new Thickness(22, 18, 22, 12) };
        content.Children.Add(BuildHeader());
        content.Children.Add(BuildStatusCard());
        content.Children.Add(BuildDetailsCard());

        lastBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            FontSize = 14,
            MinHeight = 82,
            MaxHeight = 112,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        AutomationProperties.SetName(lastBox, "Last transcription");
        lastPlaceholder.Margin = new Thickness(0, 3, 0, 0);
        lastPlaceholder.IsHitTestVisible = false;
        var lastArea = new Grid { MinHeight = 82, MaxHeight = 112 };
        lastArea.Children.Add(lastBox);
        lastArea.Children.Add(lastPlaceholder);
        content.Children.Add(BuildLastCard(lastArea));

        primary = new Button
        {
            Height = 46,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        primary.SetResourceReference(Control.StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(primary, "Start dictation");
        primary.Click += (_, _) =>
        {
            if (c.Readiness.Stage == ReadinessStage.NeedsModel) { openWorkspace("Models"); return; }
            if (c.Machine.State == OperationState.Recording) c.ToggleDictation();
            else if (c.Busy) c.Cancel();
            else c.ToggleDictation();
        };
        content.Children.Add(primary);

        var secondary = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        secondary.ColumnDefinitions.Add(new ColumnDefinition());
        secondary.ColumnDefinitions.Add(new ColumnDefinition());
        var settings = new Button { Content = "Settings", Height = 36, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var workspace = new Button { Content = "Open workspace", Height = 36, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(settings, "Settings");
        AutomationProperties.SetName(workspace, "Open workspace");
        settings.Click += (_, _) => OpenSettings();
        workspace.Click += (_, _) => openWorkspace("Overview");
        Grid.SetColumn(settings, 0); Grid.SetColumn(workspace, 1);
        secondary.Children.Add(settings); secondary.Children.Add(workspace);
        content.Children.Add(secondary);

        var footer = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var version = Ui.Text("SonaBar " + Brand.Version + " · SilasNet", 11, true);
        version.Margin = new Thickness(0);
        var privacy = Ui.Text("Free · local · no account", 11, true);
        privacy.Margin = new Thickness(0);
        privacy.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(version);
        Grid.SetColumn(privacy, 1); footer.Children.Add(privacy);
        content.Children.Add(footer);

        Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        Closing += (_, e) => { e.Cancel = true; Hide(); };
        c.Machine.Changed += MachineChanged;
        c.PropertyChanged += ControllerChanged;
        SourceInitialized += (_, _) => Theme.Apply(c.Settings.Theme);
        Closed += (_, _) =>
        {
            c.Machine.Changed -= MachineChanged;
            c.PropertyChanged -= ControllerChanged;
        };
        Activated += (_, _) => Refresh();
        Refresh();
    }

    public void OpenSettings()
    {
        if (settingsDialog is { IsVisible: true }) { settingsDialog.Activate(); return; }
        var dialog = new SettingsDialog(c, openWorkspace) { Owner = this };
        settingsDialog = dialog;
        try { dialog.ShowDialog(); }
        finally { settingsDialog = null; Refresh(); }
    }

    private UIElement BuildHeader()
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0) };
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri("pack://application:,,,/SonaBar.App;component/Assets/SonaBar-Source.png", UriKind.Absolute);
        source.EndInit();
        source.Freeze();
        logo.Child = new System.Windows.Controls.Image { Source = source, Stretch = Stretch.UniformToFill };
        brand.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var wordmark = Ui.Text("SonaBar", 14);
        wordmark.FontWeight = FontWeights.SemiBold;
        wordmark.Margin = new Thickness(0);
        wordmark.LineHeight = 17;
        var byline = Ui.Text("by SilasNet", 10, true);
        byline.Margin = new Thickness(0);
        byline.LineHeight = 13;
        names.Children.Add(wordmark); names.Children.Add(byline);
        brand.Children.Add(names);
        header.Children.Add(brand);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var privacy = Ui.StatusPill("●  Local & private");
        privacy.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(privacy);
        var gear = new Button
        {
            Content = "\uE713",
            Style = (Style)FindResource("IconButton"),
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            ToolTip = "Settings"
        };
        AutomationProperties.SetName(gear, "Settings");
        gear.Click += (_, _) => OpenSettings();
        actions.Children.Add(gear);
        Grid.SetColumn(actions, 2); header.Children.Add(actions);
        return header;
    }

    private UIElement BuildStatusCard()
    {
        var card = new Border
        {
            Padding = new Thickness(20, 18, 20, 16),
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(0),
            Child = Ui.Stack(statusTitle, statusDetail)
        };
        card.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        return card;
    }

    private UIElement BuildDetailsCard()
    {
        var grid = new Grid();
        for (var row = 0; row < 4; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        void Add(int row, string label, TextBlock value)
        {
            var name = Ui.Text(label, 12, true);
            name.Margin = new Thickness(0, 0, 10, 8);
            value.Margin = new Thickness(0, 0, 0, 8);
            value.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(name, row); Grid.SetColumn(name, 0); grid.Children.Add(name);
            Grid.SetRow(value, row); Grid.SetColumn(value, 1); grid.Children.Add(value);
        }
        Add(0, "Mode", modeValue);
        Add(1, "Hotkey", hotkeyValue);
        Add(2, "Model", modelValue);
        Add(3, "Processing", processingValue);

        var card = new Border
        {
            Padding = new Thickness(16, 14, 16, 6),
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = grid
        };
        card.SetResourceReference(Border.BackgroundProperty, "BackgroundElevated");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        return card;
    }

    private UIElement BuildLastCard(Grid lastArea)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text("Last transcription", 13);
        title.Margin = new Thickness(0);
        header.Children.Add(title);

        copy = new Button
        {
            Content = "Copy",
            FontSize = 11,
            Height = 27,
            Padding = new Thickness(10, 3, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(copy, "Copy last transcription");
        copy.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(c.LastText)) return;
            try { c.Clipboard.SetText(c.LastText); c.Inform("Copied the last transcription"); }
            catch (Exception error) { c.Notify("Could not copy the text: " + error.Message); }
        };
        Grid.SetColumn(copy, 1); header.Children.Add(copy);

        var card = new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Child = Ui.Stack(header, lastArea)
        };
        card.SetResourceReference(Border.BackgroundProperty, "BackgroundElevated");
        card.SetResourceReference(Border.BorderBrushProperty, "Border");
        return card;
    }

    private void MachineChanged(OperationState _) => Refresh();

    private void ControllerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
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

        statusTitle.Text = title;
        statusDetail.Text = detail;
        modeValue.Text = c.Settings.PushToTalk ? "Push to talk" : "Toggle";
        hotkeyValue.Text = c.Settings.PlainHotkey.ToString();
        modelValue.Text = c.SpeechModelName;
        processingValue.Text = c.ProcessingSummary;
        lastBox.Text = c.LastText;
        lastPlaceholder.Visibility = string.IsNullOrWhiteSpace(c.LastText) ? Visibility.Visible : Visibility.Collapsed;
        copy.IsEnabled = !string.IsNullOrWhiteSpace(c.LastText);
        var preparing = !c.Busy && c.Readiness.Stage is ReadinessStage.CheckingModels or ReadinessStage.PreparingModel;
        primary.IsEnabled = !c.Paused && !preparing;
        primary.Content = c.Readiness.Stage == ReadinessStage.NeedsModel ? "Choose a model"
            : preparing ? "Preparing..."
            : state == OperationState.Recording ? "Stop & insert"
            : c.Busy ? "Cancel"
            : "Start dictation";
        AutomationProperties.SetName(primary, primary.Content?.ToString() ?? "Start dictation");
    }
}
