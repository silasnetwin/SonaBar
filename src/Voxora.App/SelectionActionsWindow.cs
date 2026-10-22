using System.Windows.Input;
using System.Windows.Interop;
using Voxora.WindowsIntegration;

namespace Voxora.App;

public sealed class SelectionActionsWindow : ThemedWindow
{
    private readonly AppController controller;
    private readonly SelectedPassage selected;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TextBox describe = Ui.Input();
    private readonly TextBlock status = Ui.Text("Choose an action, describe a change, or press the dictation shortcut to say it.", 12, true);
    private readonly Button apply;
    private readonly Button voice;
    private readonly ComboBox language;
    private RewriteAction? selectedAction;
    private bool recording;

    public SelectionActionsWindow(AppController controller, SelectedPassage selected)
    {
        this.controller = controller; this.selected = selected;
        Title = "SineFlow · Edit selection"; Width = 540; Height = 530; MinWidth = 480; MinHeight = 420;
        WindowStyle = WindowStyle.ToolWindow; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual;
        var preview = Ui.Input(selected.Text, true); preview.IsReadOnly = true; preview.MinHeight = 66; preview.MaxHeight = 94;
        describe.ToolTip = "Describe a change…";
        describe.TextChanged += (_, _) => { if (!recording && !string.IsNullOrWhiteSpace(describe.Text)) selectedAction = null; };
        describe.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; _ = ApplyAsync(); } };
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var id in new[] { "improve", "grammar", "shorten", "expand", "professional", "casual", "friendly", "direct", "simple", "bullets", "tasks", "prompt" })
        {
            var action = RewriteActions.All.First(x => x.Id == id);
            var button = Ui.Button(action.Label, () => { selectedAction = action; describe.Clear(); status.Text = action.Label + " selected. Press Enter to apply."; });
            button.MinHeight = 30; button.Padding = new Thickness(10, 5, 10, 5); actions.Children.Add(button);
        }
        actions.Children.Add(Ui.Button("lowercase", () => { selectedAction = new("lowercase", "lowercase", ""); _ = ApplyAsync(); }));
        actions.Children.Add(Ui.Button("UPPERCASE", () => { selectedAction = new("uppercase", "UPPERCASE", ""); _ = ApplyAsync(); }));
        language = Ui.Choice(new[] { "English", "German", "French", "Spanish", "Italian", "Portuguese", "Chinese", "Japanese", "Korean", "Arabic", "Hindi", "Russian", "Turkish", "Polish", "Dutch" }, "English", _ => { });
        var translate = Ui.Button("Translate", () => { selectedAction = RewriteActions.All.First(x => x.Id == "translate"); _ = ApplyAsync(); });
        voice = Ui.Button("Speak change", ToggleVoice);
        apply = Ui.Button("Apply", () => _ = ApplyAsync(), true); apply.IsDefault = true;
        var cancel = Ui.Button("Cancel", Close); cancel.IsCancel = true;
        var intro=Ui.Hero(Ui.Text("Edit selection",24),Ui.Text("Make a precise local change, then review it where you wrote it.",12,true));intro.Padding=new Thickness(18,15,18,10);
        var body=Ui.Stack(intro,Ui.Text("Original text",12,true),preview,Ui.Text("Describe a change",12,true),describe,Ui.Text("Or choose a quick action",12,true),actions,Ui.Row(language,translate),Ui.Row(voice,apply,cancel),status);
        Content = new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=new Border { Padding = new Thickness(22,20,22,14), Child=body } };
        Loaded += (_, _) => { Position(); describe.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { cancellation.Cancel(); Close(); e.Handled = true; return; }
            if (e.Key is Key.Up or Key.Down)
            {
                var controls=actions.Children.OfType<Button>().Where(x=>x.IsEnabled).ToList();
                if(controls.Count==0)return;
                var current=controls.FindIndex(x=>x.IsKeyboardFocused);
                var next=(current+(e.Key==Key.Down?1:-1)+controls.Count)%controls.Count;
                controls[next].Focus(); e.Handled=true;
            }
        };
        Closed += (_, _) => { cancellation.Cancel(); if (recording) controller.StopInstruction(); cancellation.Dispose(); };
    }

    public void ToggleVoice() => _ = ToggleVoiceAsync();
    private async Task ToggleVoiceAsync()
    {
        if (recording) { controller.StopInstruction(); return; }
        recording = true; voice.Content = "Stop listening"; status.Text = "Listening for a change… Press the same shortcut again or choose Stop listening.";
        try
        {
            var spoken = await controller.RecordInstructionAsync(cancellation.Token);
            if (string.IsNullOrWhiteSpace(spoken)) { status.Text = "No instruction heard."; return; }
            selectedAction = null;
            describe.Text = spoken;
            recording = false;
            status.Text = "Instruction heard. Applying it…";
            await ApplyAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { status.Text = error.Message; }
        finally { recording = false; voice.Content = "Speak change"; }
    }
    private async Task ApplyAsync()
    {
        if (recording) return;
        if (selectedAction is null && string.IsNullOrWhiteSpace(describe.Text)) { status.Text = "Choose an action or describe a change."; describe.Focus(); return; }
        apply.IsEnabled = voice.IsEnabled = false;
        try { status.Text = "Updating selection locally…"; await controller.TransformSelectionAsync(selected, selectedAction, describe.Text.Trim(), (string)language.SelectedItem, cancellation.Token); Close(); }
        catch (OperationCanceledException) { status.Text = "Cancelled."; }
        catch (Exception error) { status.Text = error.Message; }
        finally { if (IsVisible) apply.IsEnabled = voice.IsEnabled = true; }
    }
    private void Position()
    {
        var point = System.Windows.Forms.Cursor.Position; var area = System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Left = Math.Clamp(point.X * transform.M11 - Width / 2, area.Left * transform.M11, area.Right * transform.M11 - Width);
        Top = Math.Clamp(point.Y * transform.M22 + 16, area.Top * transform.M22, area.Bottom * transform.M22 - Height);
    }
}
