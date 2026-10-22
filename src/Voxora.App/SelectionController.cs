using Voxora.Audio;
using Voxora.Storage;
using Voxora.WindowsIntegration;

namespace Voxora.App;

public sealed partial class AppController
{
    private bool starting;
    private bool finishAfterStart;
    private SelectionActionsWindow? selectionWindow;
    private TaskCompletionSource? instructionStop;
    private CancellationTokenSource? backgroundSpeech;

    public async void EditSelection()
    {
        if (selectionWindow?.IsVisible == true) { selectionWindow.ToggleVoice(); return; }
        if (Busy || Paused) return;
        try
        {
            var destination = await Task.Run(insertion.CaptureTarget).WaitAsync(TimeSpan.FromSeconds(1));
            var selection = insertion.SelectionFor(destination) ?? throw new InvalidOperationException("Select editable text first, then press the shortcut.");
            ShowSelection(selection);
        }
        catch (Exception error) { Notify(error.Message); }
    }

    private void ShowSelection(SelectedPassage selection)
    {
        selectionWindow = new SelectionActionsWindow(this, selection);
        selectionWindow.Closed += (_, _) => selectionWindow = null;
        selectionWindow.Show();
    }

    private string ContextInstruction(string executable, string title)
    {
        var explicitProfile = Settings.Profiles.FirstOrDefault(x => x.Executable.Equals(executable, StringComparison.OrdinalIgnoreCase));
        return explicitProfile?.Instruction ?? WritingContext.Instruction(WritingContext.Detect(executable, title));
    }

    public async Task<string> TransformSelectionAsync(SelectedPassage selected, RewriteAction? action, string instruction, string language, CancellationToken cancellation)
    {
        TextInsertionService.ValidateSelection(selected);
        var result = "";
        await RunExclusiveAsync(async token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellation);
            if (action?.Id is "lowercase" or "uppercase")
                result = OutputPipeline.Format(selected.Text, action.Id == "lowercase" ? OutputCasing.Lowercase : OutputCasing.Uppercase);
            else if (action?.Id == "translate")
                result = await TranslateAsync(selected.Text, language, linked.Token);
            else
            {
                var request = SpokenInstructions.Parse("Selection. " + instruction);
                if (action is null && request.Action == SpokenAction.Translate)
                    result = await TranslateAsync(selected.Text, request.Language!, linked.Token);
                else
                {
                    var command = action?.Id == "prompt" || request.Action == SpokenAction.ChatGptPrompt
                        ? OutputPipeline.EditingInstruction(Settings, "", new(selected.Text, SpokenAction.ChatGptPrompt))
                        : action?.Instruction ?? instruction;
                    result = await EnhanceAsync(selected.Text, command + " Preserve facts, names, numbers, negations and scope. Return only revised text.", linked.Token);
                }
            }
            linked.Token.ThrowIfCancellationRequested();
            TextInsertionService.ValidateSelection(selected);
            LastOriginal = selected.Text;
            LastText = result;
            target = selected.Target;
            Machine.Move(OperationState.Inserting);
            await insertion.ReplaceSelectionAsync(selected, result, Settings.Clipboard == ClipboardMode.Replace ? ClipboardMode.Replace : ClipboardMode.Preserve, linked.Token);
        });
        Changed(nameof(CanUndo));
        overlay?.ShowNotice("Updated", false);
        return result;
    }

    public async Task<string> RecordInstructionAsync(CancellationToken cancellation)
    {
        if (Busy || Audio.IsCapturing) throw new InvalidOperationException("Finish the current recording first.");
        var path = TemporaryWave();
        operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var token = operation.Token;
        instructionStop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Machine.Move(OperationState.Recording);
        backgroundSpeech?.Cancel();
        var start = Task.Run(() => Audio.Start(path, CaptureMode.Microphone, Settings.MicrophoneId));
        try
        {
            await start;
            await instructionStop.Task.WaitAsync(token);
            await Audio.StopAsync();
            token.ThrowIfCancellationRequested();
            Machine.Move(OperationState.Transcribing);
            return (await TranscribeAsync(path, token)).Text;
        }
        finally
        {
            try { await start; await Audio.StopAsync(); }
            catch (Exception error) { SafeLog.Write("InstructionCapture", error); }
            try { File.Delete(path); } catch (IOException error) { SafeLog.Write("InstructionCleanup", error); }
            instructionStop = null;
            Reset();
        }
    }

    public void StopInstruction() => instructionStop?.TrySetResult();

    public async Task<TranscriptionResult> TranscribeBackgroundAsync(string wave, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            await idleSignal.Task.WaitAsync(token);
            using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
            backgroundSpeech = source;
            try { return await TranscribeAsync(wave, source.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            finally { if (ReferenceEquals(backgroundSpeech, source)) backgroundSpeech = null; }
        }
    }

    public async Task RestoreOriginalAsync()
    {
        if (LastOriginal.Length == 0) return;
        var receipt = insertion.LastReceipt;
        await RunExclusiveAsync(async token =>
        {
            var text = OutputPipeline.Format(LastOriginal, Settings.OutputCasing);
            if (receipt is not null) await insertion.ReplaceReceiptAsync(receipt, text, ClipboardMode.Preserve, token);
            LastText = text;
        });
        Changed(nameof(CanUndo));
    }
}
