using System.Windows.Automation;
using System.Windows.Input;

namespace SonaBar.App;

/// <summary>Compact, everyday settings. Every page stays available through the compact Go to selector and the tray Tools menu.</summary>
public sealed class SettingsDialog : ThemedWindow
{
    private readonly AppController c;
    private string selectedLanguage;
    private bool pushToTalk;
    private Hotkey hotkey;
    private ProcessingDevice device;
    private int cpuThreads;
    private int minHold;
    private ModelOption selectedModel;

    public SettingsDialog(AppController controller)
    {
        c = controller;
        Title = "SonaBar settings";
        var area = SystemParameters.WorkArea;
        Width = Math.Min(480, area.Width - 24);
        MinWidth = Math.Min(420, Width);
        Height = Math.Max(320, Math.Min(680, area.Height - 48));
        MinHeight = Math.Min(520, Height);
        UseNativeChrome();
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        selectedLanguage = c.Settings.Language;
        pushToTalk = c.Settings.PushToTalk;
        hotkey = c.Settings.PlainHotkey;
        device = c.Settings.ProcessingDevice;
        cpuThreads = c.Settings.CpuThreads;
        minHold = c.Settings.MinHoldMs;
        var modelOptions = ModelOptions();
        selectedModel = c.Settings.AutoQuality
            ? modelOptions[0]
            : modelOptions.FirstOrDefault(option => option.Id == c.EffectiveSpeechModel) ?? modelOptions[0];

        var root = new Grid();
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Surface");
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new ScrollViewer
        {
            Content = BuildContent(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        var footer = BuildFooter();
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        Content = root;
        SourceInitialized += (_, _) => Theme.Apply(c.Settings.Theme);
    }

    private UIElement BuildContent()
    {
        var mode = Ui.Choice(new[] { "Toggle", "Push to talk" }, pushToTalk ? "Push to talk" : "Toggle", value => pushToTalk = value == "Push to talk");
        mode.Width = 200;

        var hotkeyBox = Ui.Input(hotkey.ToString());
        hotkeyBox.IsReadOnly = true;
        hotkeyBox.Width = 200;
        hotkeyBox.ToolTip = "Focus this field and press your new shortcut";
        AutomationProperties.SetName(hotkeyBox, "Dictation shortcut");
        hotkeyBox.PreviewKeyDown += (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
            uint modifiers = 0;
            var current = Keyboard.Modifiers;
            if (current.HasFlag(ModifierKeys.Control)) modifiers |= 2;
            if (current.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
            if (current.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
            if (current.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
            hotkey = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
            hotkeyBox.Text = hotkey.ToString();
        };

        var holdOptions = new List<HoldOption> { new(0, "Off") };
        for (var value = 500; value <= 2000; value += 500) holdOptions.Add(new(value, HoldLabel(value)));
        if (holdOptions.All(option => option.Value != minHold)) holdOptions.Add(new(minHold, HoldLabel(minHold)));
        var hold = Ui.Choice(holdOptions, holdOptions.First(option => option.Value == minHold), option => minHold = option.Value);
        hold.Width = 200;

        ComboBox? threadChoice = null;
        void UpdateSpeechControls()
        {
            if (threadChoice is null) return;
            var definition = selectedModel.Id is null
                ? c.EffectiveSpeechModelDefinition
                : c.Models.Catalog.FirstOrDefault(candidate => candidate.Id == selectedModel.Id);
            var whisper = definition?.Engine == "Whisper";
            threadChoice.IsEnabled = whisper;
            threadChoice.ToolTip = whisper
                ? "Applied to Whisper recognition on this PC."
                : "Parakeet and Qwen manage their own CPU threads.";
        }

        ComboBox? languageChoice = null;
        void FillLanguages(string? preferredCode)
        {
            var definition = selectedModel.Id is null
                ? null
                : c.Models.Catalog.FirstOrDefault(candidate => candidate.Id == selectedModel.Id);
            var languages = c.SupportedLanguages(definition).Select(option => new LanguageOption(option.Code, option.Name)).ToList();
            var languageOption = languages.FirstOrDefault(option => option.Code == preferredCode) ?? languages[0];
            selectedLanguage = languageOption.Code;
            if (languageChoice is null)
            {
                languageChoice = Ui.Choice(languages, languageOption, option => selectedLanguage = option.Code);
                languageChoice.Width = 200;
            }
            else
            {
                languageChoice.ItemsSource = languages;
                languageChoice.SelectedItem = languageOption;
            }
        }

        var model = Ui.Choice(ModelOptions(), selectedModel, option =>
        {
            selectedModel = option;
            FillLanguages(selectedLanguage);
            UpdateSpeechControls();
        });
        model.Width = 200;

        FillLanguages(selectedLanguage);
        var language = languageChoice!;

        var devices = new[]
        {
            new DeviceOption(ProcessingDevice.Automatic, "Automatic"),
            new DeviceOption(ProcessingDevice.Cpu, "CPU"),
            new DeviceOption(ProcessingDevice.Gpu, "GPU / accelerated")
        };
        var deviceChoice = Ui.Choice(devices, devices.First(option => option.Value == device), option => device = option.Value);
        deviceChoice.Width = 200;

        var threadOptions = new List<ThreadOption> { new(0, "Auto") };
        for (var value = 1; value <= Environment.ProcessorCount; value++) threadOptions.Add(new(value, value == 1 ? "1 thread" : value + " threads"));
        threadChoice = Ui.Choice(threadOptions, threadOptions.FirstOrDefault(option => option.Value == cpuThreads) ?? threadOptions[0], option => cpuThreads = option.Value);
        threadChoice.Width = 200;
        UpdateSpeechControls();

        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };
        var title = Ui.Text("Settings", 26);
        title.Margin = new Thickness(0, 0, 0, 2);
        content.Children.Add(title);
        content.Children.Add(Ui.Text("Changes apply to your next dictation. Everything stays on this PC.", 13, true));
        content.Children.Add(Ui.Card(Ui.Text("Dictation", 20),
            Ui.SettingRow("Mode", "Push to talk discards accidental short presses.", mode),
            Ui.SettingRow("Hotkey", "Click the field and press a new shortcut.", hotkeyBox),
            Ui.SettingRow("Minimum hold", "A shorter push-to-talk press is discarded.", hold)));
        content.Children.Add(Ui.Card(Ui.Text("Speech", 20),
            Ui.SettingRow("Model", "Installed models, plus the current choice.", model),
            Ui.SettingRow("Language", "Automatic lets the model detect the language.", language),
            Ui.SettingRow("Processing device", "Whisper can use a GPU. Parakeet and Qwen run on CPU.", deviceChoice),
            Ui.SettingRow("CPU threads", "Used by Whisper. Parakeet and Qwen manage their own CPU threads.", threadChoice)));
        return content;
    }

    private UIElement BuildFooter()
    {
        var footer = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 14, 24, 16)
        };
        footer.SetResourceReference(Border.BorderBrushProperty, "Border");
        footer.SetResourceReference(Border.BackgroundProperty, "Surface");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cancel = Ui.Button("Cancel", () => DialogResult = false);
        cancel.MinWidth = 90;
        var save = Ui.AsyncButton("Save", async () =>
        {
            var before = Snapshot();
            bool modelChanged, deviceChanged;
            try
            {
                (modelChanged, deviceChanged) = Apply();
                c.SaveSettings();
            }
            catch
            {
                // A hotkey conflict must not leave the engine configured with values
                // that were never persisted. Restore both settings and the engine.
                Restore(before);
                c.ApplyEngineSettings();
                throw;
            }
            if (modelChanged || deviceChanged) await c.ReinitializeAsync();
            c.Inform("Settings saved");
            DialogResult = true;
        }, true);
        save.MinWidth = 100;
        Grid.SetColumn(cancel, 1); Grid.SetColumn(save, 2);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        grid.Children.Add(cancel); grid.Children.Add(save);
        footer.Child = grid;
        return footer;
    }

    private List<ModelOption> ModelOptions()
    {
        var options = new List<ModelOption> { new(null, "Automatic (recommended)") };
        foreach (var definition in c.Models.Catalog.Where(definition => IsSpeechEngine(definition.Engine) && c.Models.IsInstalled(definition)))
            options.Add(new(definition.Id, definition.Name));
        var current = c.EffectiveSpeechModelDefinition;
        if (current is not null && options.All(option => option.Id != current.Id))
            options.Add(new(current.Id, current.Name + " (not installed)"));
        return options;
    }

    private static bool IsSpeechEngine(string engine) => engine is "Whisper" or "NeMo-Speech.cpp" or "Qwen3-ASR";
    private static string HoldLabel(int value) => value == 0 ? "Off" : (value / 1000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

    private SettingsSnapshot Snapshot() => new(
        c.Settings.Language,
        c.Settings.ProcessingDevice,
        c.Settings.CpuThreads,
        c.Settings.MinHoldMs,
        c.Settings.PushToTalk,
        c.Settings.PlainHotkey,
        c.Settings.AutoQuality,
        c.Settings.UseCustomSpeechModel,
        c.Settings.SpeechModel);

    private void Restore(SettingsSnapshot snapshot)
    {
        c.Settings.Language = snapshot.Language;
        c.Settings.ProcessingDevice = snapshot.Device;
        c.Settings.CpuThreads = snapshot.CpuThreads;
        c.Settings.MinHoldMs = snapshot.MinHoldMs;
        c.Settings.PushToTalk = snapshot.PushToTalk;
        c.Settings.PlainHotkey = snapshot.Hotkey;
        c.Settings.AutoQuality = snapshot.AutoQuality;
        c.Settings.UseCustomSpeechModel = snapshot.UseCustomSpeechModel;
        c.Settings.SpeechModel = snapshot.SpeechModel;
    }

    private (bool ModelChanged, bool DeviceChanged) Apply()
    {
        var previousModel = c.EffectiveSpeechModel;
        var previousDevice = c.Settings.ProcessingDevice;
        c.Settings.Language = selectedLanguage;
        c.Settings.ProcessingDevice = device;
        c.Settings.CpuThreads = cpuThreads;
        c.Settings.MinHoldMs = minHold;
        c.Settings.PushToTalk = pushToTalk;
        c.Settings.PlainHotkey = hotkey;
        if (selectedModel.Id is null)
        {
            c.Settings.AutoQuality = true;
            c.Settings.UseCustomSpeechModel = false;
        }
        else
        {
            c.Settings.AutoQuality = false;
            c.Settings.UseCustomSpeechModel = true;
            c.Settings.SpeechModel = selectedModel.Id;
        }
        return (!string.Equals(previousModel, c.EffectiveSpeechModel, StringComparison.OrdinalIgnoreCase), previousDevice != device);
    }

    private sealed record ModelOption(string? Id, string Name) { public override string ToString() => Name; }
    private sealed record LanguageOption(string Code, string Name) { public override string ToString() => Name; }
    private sealed record DeviceOption(ProcessingDevice Value, string Name) { public override string ToString() => Name; }
    private sealed record ThreadOption(int Value, string Name) { public override string ToString() => Name; }
    private sealed record HoldOption(int Value, string Name) { public override string ToString() => Name; }
    private sealed record SettingsSnapshot(
        string Language,
        ProcessingDevice Device,
        int CpuThreads,
        int MinHoldMs,
        bool PushToTalk,
        Hotkey Hotkey,
        bool AutoQuality,
        bool UseCustomSpeechModel,
        string SpeechModel);
}
