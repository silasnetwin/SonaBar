using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using SonaBar.Audio;
using SonaBar.Enhancement;
using SonaBar.Models;
using SonaBar.Storage;
using SonaBar.Transcription;
using SonaBar.WindowsIntegration;

namespace SonaBar.App;

/// <summary>A language code paired with the name shown in the speech picker.</summary>
public sealed record SpeechLanguage(string Code, string Name);

public sealed partial class AppController : INotifyPropertyChanged, IDisposable
{
    public AppSettings Settings { get; }
    public SettingsStore SettingsStore { get; }
    public HistoryRepository History { get; }
    public ModelManager Models { get; }
    public AudioCaptureService Audio { get; }
    public ITranscriptionEngine Speech { get; }
    public IEnhancementEngine Enhancement { get; }
    public ClipboardService Clipboard { get; }
    private readonly TextInsertionService insertion;
    private readonly GlobalHotkeyService hotkeys;
    private readonly AudioDucker ducker;
    private readonly FeedbackSounds feedback = new();
    public OperationMachine Machine { get; } = new();
    private HardwareProfile? hardware;
    /// <summary>Capabilities of the computer SonaBar runs on. Detected once, then reused.</summary>
    public HardwareProfile Hardware => hardware ??= HardwareDetector.Detect();
    /// <summary>The speech profile that is actually used, honouring the automatic choice.</summary>
    public string EffectiveSpeechModel => Settings.AutoQuality ? HardwareRecommendations.RecommendModel(Hardware) : Settings.EffectiveSpeechModel;
    public ModelDefinition? EffectiveSpeechModelDefinition => Models.Catalog.FirstOrDefault(model => model.Id == EffectiveSpeechModel);
    public string SpeechModelName => EffectiveSpeechModelDefinition?.Name ?? EffectiveSpeechModel;
    public string SpeechEngineName => EffectiveSpeechModelDefinition?.Engine ?? "Unknown";
    public bool SpeechSupportsCpuThreads => SpeechEngineName == "Whisper";
    public bool SpeechSupportsGpu => SpeechEngineName == "Whisper";
    /// <summary>An honest summary of what the selected engine can do with this PC's CPU and GPU.</summary>
    public string ProcessingSummary => SpeechEngineName switch
    {
        "Whisper" => WhisperProcessingSummary() + " · " + (Settings.CpuThreads > 0 ? $"{Settings.CpuThreads} threads" : "auto threads"),
        "NeMo-Speech.cpp" => "CPU only · engine-managed threads",
        "Qwen3-ASR" => "CPU sidecar · engine-managed threads",
        _ => ProcessingDeviceInfo.Label(Settings.ProcessingDevice)
    };
    private string WhisperProcessingSummary()
    {
        var usingGpu = Speech switch
        {
            TranscriptionEngineRouter router => router.WhisperUsingGpu,
            WhisperEngine => WhisperEngine.UsingGpu,
            _ => null
        };
        return Settings.ProcessingDevice switch
        {
            ProcessingDevice.Cpu => "CPU",
            ProcessingDevice.Gpu when usingGpu == false => "GPU unavailable (CPU fallback)",
            ProcessingDevice.Gpu => "GPU / accelerated",
            _ => "Automatic (CPU)"
        };
    }
    public void RefreshHardware() => hardware = HardwareDetector.Detect();
    /// <summary>The language codes the selected speech model can actually transcribe.</summary>
    public IReadOnlyList<SpeechLanguage> SupportedLanguages(ModelDefinition? selectedModel = null)
    {
        var model = selectedModel ?? ResolveSpeechModel() ?? Models.Catalog.FirstOrDefault(candidate => candidate.Id == EffectiveSpeechModel);
        IReadOnlyCollection<string> codes = Speech is TranscriptionEngineRouter router && model is not null
            ? router.CapabilitiesFor(Models.PathFor(model)).Languages
            : Speech.Capabilities.Languages;
        if (!codes.Contains("auto", StringComparer.OrdinalIgnoreCase)) codes = ["auto", .. codes];
        return codes.Select(code => new SpeechLanguage(code, LanguageName(model?.Engine, code))).ToArray();
    }
    private static string LanguageName(string? engine, string code)
    {
        if (engine == "Qwen3-ASR") return QwenLanguages.NameFor(code) ?? Languages.All.GetValueOrDefault(code) ?? code;
        return Languages.All.GetValueOrDefault(code) ?? code;
    }
    /// <summary>A readable summary of this computer for the settings pages.</summary>
    public string DescribeHardware()
    {
        var machine = Hardware;
        var acceleration = machine.CudaRuntime ? "NVIDIA CUDA runtime installed"
            : machine.VulkanRuntime ? "Vulkan runtime installed"
            : "no Vulkan or CUDA runtime found";
        return string.Join("\n", new[]
        {
            $"System       {machine.OperatingSystem}",
            $"Processor    {machine.LogicalProcessors} logical processors",
            $"Memory       {machine.MemoryText}",
            $"Graphics     {machine.GraphicsText}",
            $"Acceleration {acceleration}",
            $"Free space   {HardwareProfile.FormatBytes(machine.FreeDiskBytes)}",
            $"Speech model {EffectiveSpeechModel} ({SpeechProfiles.Description(Settings.AutoQuality ? HardwareRecommendations.RecommendQuality(machine) : Settings.Quality)})"
        });
    }
    /// <summary>Applies the profile the detected hardware suggests and reports it.</summary>
    public string ApplyRecommendedQuality()
    {
        var quality = HardwareRecommendations.RecommendQuality(Hardware);
        Settings.Quality = quality;
        Settings.UseCustomSpeechModel = false;
        Settings.AutoQuality = true;
        ModelPreparation = quality + " is ready offline";
        Changed(nameof(ModelPreparation));
        Changed(nameof(EffectiveSpeechModel));
        Changed(nameof(SpeechModelName));
        Changed(nameof(SpeechEngineName));
        Changed(nameof(ProcessingSummary));
        return quality.ToString();
    }
    /// <summary>Pushes the current device, thread and hotword choices into the engines.</summary>
    public void ApplyEngineSettings()
    {
        WhisperEngine.ConfigureBackend(Settings.ProcessingDevice, Settings.CpuThreads);
        LocalEnhancementEngine.ConfigureBackend(Settings.ProcessingDevice);
        if (Speech is TranscriptionEngineRouter router) router.ConfigureHotwords(Settings.CustomWords);
        Changed(nameof(EffectiveSpeechModelDefinition));
        Changed(nameof(SpeechModelName));
        Changed(nameof(SpeechEngineName));
        Changed(nameof(SpeechSupportsCpuThreads));
        Changed(nameof(SpeechSupportsGpu));
        Changed(nameof(ProcessingSummary));
    }
    private CancellationTokenSource? operation;
    private InsertionTarget? target;
    private string? recording;
    private Task recordingStart=Task.CompletedTask;
    private TaskCompletionSource startupSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool shuttingDown;
    private bool pushToTalkRelease;
    private long pushToTalkHeldMs;
    private Task speechWarmup=Task.CompletedTask;
    private Task preparationTask=Task.CompletedTask;
    private RecordingOverlay? overlay;
    private string status = "Ready when you are", lastText = "";
    private Readiness readiness = new(ReadinessStage.Ready);
    /// <summary>The explicit state of the application, shown in the status bar.</summary>
    public Readiness Readiness { get => readiness; private set { readiness = value; Changed(); Changed(nameof(StageLabel)); Changed(nameof(PresenceLabel)); } }
    /// <summary>The current state without decoration; the footer draws the coloured status dot.</summary>
    public string PresenceLabel => readiness.Label;
    /// <summary>A short readiness label for the status bar.</summary>
    public string StageLabel => readiness.Label + (string.IsNullOrEmpty(readiness.Detail) ? "" : " · " + readiness.Detail);
    private TaskCompletionSource idleSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Status { get => status; private set { status = value; Changed(); } }
    public string LastText { get => lastText; private set { lastText = value; Changed(); } }
    public bool Busy => starting || Machine.State is not OperationState.Idle;
    public bool Paused { get; set; }
    public string LastSpeechModel {get;private set;}="";
    public string LastOriginal {get;private set;}="";
    public bool CanUndo=>insertion.LastReceipt is not null;
    public string ModelPreparation {get;private set;}="";
    private CancellationTokenSource? preparation;
    private string? processingNotice;
    private readonly Dictionary<string,TimeSpan> dictationTimings=[];
    private readonly Stopwatch dictationClock=new();
    private readonly object timingGate=new();
    private float pendingLevel;
    private int levelDispatchQueued;
    private bool audioReceived;
    public IReadOnlyDictionary<string,TimeSpan> LastDictationTimings=>dictationTimings;
    /// <summary>The measured time from the end of speech to confirmed insertion. Empty until a dictation succeeds.</summary>
    public string LastDictationLatency {get;private set;}="";
    public event PropertyChangedEventHandler? PropertyChanged;
    public AppController(AppSettings settings, SettingsStore settingsStore, HistoryRepository history, ModelManager models, AudioCaptureService audio, ITranscriptionEngine speech, IEnhancementEngine enhancement, ClipboardService clipboard, TextInsertionService insertion, GlobalHotkeyService hotkeys, AudioDucker ducker)
    {
        Settings = settings; SettingsStore = settingsStore; History = history; Models = models; Audio = audio; Speech = speech; Enhancement = enhancement; Clipboard = clipboard; this.insertion = insertion; this.hotkeys = hotkeys; this.ducker = ducker;
        hotkeys.ExpandTypedSnippet = snippet =>
        {
            try { return TextProcessing.ExpandSnippet(snippet, DateTime.Now, Clipboard.Text); }
            catch (Exception error) { SafeLog.Write("TypedSnippetClipboard", error); return TextProcessing.ExpandSnippet(snippet, DateTime.Now, () => ""); }
        };
        hotkeys.TypedExpansionBlocked = () => Paused || shuttingDown;
        idleSignal.TrySetResult();
        startupSignal.TrySetResult();
        Machine.Changed += state => { if(state==OperationState.Idle)idleSignal.TrySetResult();else if(idleSignal.Task.IsCompleted)idleSignal=new(TaskCreationOptions.RunContinuationsAsynchronously); Changed(nameof(Busy)); overlay?.SetState(state); };
        Audio.Level += QueueLevel;
        Audio.Faulted += message => Application.Current.Dispatcher.BeginInvoke(() => { Notify(message); if (Machine.State == OperationState.Recording) ToggleDictation(); });
        if (speech is WhisperEngine whisper)
            whisper.BackendNotice += BackendFallback;
        else if (speech is TranscriptionEngineRouter router)
            router.BackendNotice += BackendFallback;
        ApplyEngineSettings();
    }
    private void BackendFallback(string message)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Notify(message);
            Changed(nameof(ProcessingSummary));
        });
    }
    public void AttachOverlay(RecordingOverlay value) => overlay = value;
    private void QueueLevel(float level)
    {
        Volatile.Write(ref pendingLevel,level);
        if(level >= .012f && Machine.State == OperationState.Recording && !audioReceived)
        {
            lock(timingGate)
            {
                if(!audioReceived) { audioReceived=true; MarkTiming("Audio"); }
            }
        }
        if(Interlocked.Exchange(ref levelDispatchQueued,1)!=0)return;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Render,() =>
        {
            overlay?.AddLevel(Volatile.Read(ref pendingLevel));
            Interlocked.Exchange(ref levelDispatchQueued,0);
        });
    }
    private void MarkTiming(string point)
    {
        if(!dictationClock.IsRunning)return;
        var elapsed=dictationClock.Elapsed;
        lock(timingGate)dictationTimings[point]=elapsed;
        if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Timing",("point",point),("elapsed_ms",$"{elapsed.TotalMilliseconds:F0}"));
    }
    private void CompleteTimings(bool delivered)
    {
        if(!dictationClock.IsRunning)return;
        dictationClock.Stop();
        string values;
        lock(timingGate)values=string.Join(" ",dictationTimings.OrderBy(x=>x.Key).Select(x=>$"{x.Key}={x.Value.TotalMilliseconds:F0}ms"));
        LastDictationLatency = delivered && dictationTimings.TryGetValue("T11", out var deliveredAt) ? $"{deliveredAt.TotalSeconds:F1} s" : "";
        Changed(nameof(LastDictationLatency));
        Debug.WriteLine($"SonaBar dictation delivered={delivered} {values}");
        if(Settings.DebugLogging)SafeLog.WriteTiming(values+$" audio_received={audioReceived}",delivered);
    }
    public void Initialize()
    {
        hotkeys.Triggered += (id, down) =>
        {
            if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Hotkey",("id",id),("down",down),("state",Machine.State.ToString()),("paused",Paused));
            if (Paused && down) return;
            if (id == 3) { if (down) EditSelection(); return; }
            if (Settings.PushToTalk)
            {
                if (down && !Busy) { pushToTalkRelease = false; pushToTalkHeldMs = 0; ToggleDictation(); }
                else if (!down && (Machine.State == OperationState.Recording || starting))
                {
                    // Capture the key-up time now. Waiting for a slow microphone or model
                    // start must not turn a short accidental press into a valid hold.
                    pushToTalkHeldMs = dictationClock.ElapsedMilliseconds;
                    pushToTalkRelease = true;
                    ToggleDictation();
                }
            }
            else if (down) ToggleDictation();
        };
        hotkeys.Error += Notify;
        try { hotkeys.Register(Settings); } catch (Exception e) { Notify(e.Message); }
        if (SettingsStore.RecoveryMessage is string message) Notify(message);
        try { WindowIntegration.SetStartup(Settings.LaunchAtLogin); }
        catch (Exception error) { SafeLog.Write("StartupRegistration", error); }
    }

    /// <summary>
    /// Brings the application into its ready state without ever blocking the user interface.
    /// Nothing is downloaded here: a missing model is reported as a state the user can act
    /// on instead of a silent transfer that looks like a hang.
    /// </summary>
    public async Task InitializeAsync()
    {
        Readiness = new Readiness(ReadinessStage.CheckingModels, "");
        ModelDefinition? model = null;
        try { model = await Task.Run(ResolveSpeechModel); }
        catch (Exception error) { SafeLog.Write("ModelScan", error); }
        if (model is null)
        {
            Readiness = new Readiness(ReadinessStage.NeedsModel, "Download a speech model in Models to start dictating.");
            return;
        }
        Readiness = new Readiness(ReadinessStage.PreparingModel, model.Name);
        try
        {
            await Models.EnsureVerifiedAsync(model, CancellationToken.None);
            await Speech.WarmAsync(Models.PathFor(model), CancellationToken.None);
            Readiness = new Readiness(ReadinessStage.Ready, "");
        }
        catch (Exception error)
        {
            SafeLog.Write("SpeechWarmupStartup", error);
            Readiness = new Readiness(ReadinessStage.NeedsModel, "The speech model could not be loaded. Open Models to install it again.");
        }
    }

    /// <summary>The speech model that should be used, or null when none is installed.</summary>
    public ModelDefinition? ResolveSpeechModel()
    {
        var preferred = AvailableSpeechModels();
        return preferred.Count == 0 ? null : preferred[0];
    }

    /// <summary>Reloads the model after the user changed the profile or installed a model.</summary>
    public async Task ReinitializeAsync()
    {
        Readiness = new Readiness(ReadinessStage.CheckingModels, "");
        var model = await Task.Run(ResolveSpeechModel);
        if (model is null)
        {
            Readiness = new Readiness(ReadinessStage.NeedsModel, "Download a speech model in Models to start dictating.");
            return;
        }
        Readiness = new Readiness(ReadinessStage.PreparingModel, model.Name);
        try
        {
            await Models.EnsureVerifiedAsync(model, CancellationToken.None);
            await Speech.WarmAsync(Models.PathFor(model), CancellationToken.None);
            Readiness = new Readiness(ReadinessStage.Ready, "");
        }
        catch (Exception error)
        {
            SafeLog.Write("SpeechWarmup", error);
            Readiness = new Readiness(ReadinessStage.Error, "The speech model could not be loaded.");
        }
    }
    public void SaveSettings()
    {
        Settings.Validate();
        // Register first: a shortcut conflict restores the previous bindings and must not
        // leave the in-memory engine settings changed when the save fails.
        hotkeys.Register(Settings);
        ApplyEngineSettings();
        SettingsStore.Save(Settings);
        History.Prune(Settings.HistoryRetentionDays);
        WindowIntegration.SetStartup(Settings.LaunchAtLogin);
        Theme.Apply(Settings.Theme);
    }
    public string TemporaryWave() => Path.Combine(Brand.DataRoot, "Temp", Guid.NewGuid() + ".wav");
    public void Notify(string text) { Status = text; overlay?.ShowNotice(text,true); }
    public void Inform(string text) { Status=text; }
    /// <summary>Plays the selected feedback mode so the choice can be heard before saving.</summary>
    public void PreviewFeedbackSound()
    {
        if (Settings.Sounds == "Off") { Inform("Choose Minimal or Standard to hear a preview."); return; }
        feedback.Play(Settings.Sounds, FeedbackSound.Finished);
    }
    public void RememberForegroundWindow() => insertion.RememberForegroundWindow();
    public async void ToggleDictation()
    {
        if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Dictation",("stage","toggle_requested"),("state",Machine.State.ToString()),("starting",starting),("busy",Busy),("paused",Paused));
        if(shuttingDown)return;
        if(selectionWindow?.IsVisible==true){selectionWindow.ToggleVoice();return;}
        if(starting){finishAfterStart=true;return;}
        if (Machine.State == OperationState.Recording) { await FinishDictationAsync(); return; }
        if (Busy || Paused) return;
        if(Audio.IsCapturing){Notify("The microphone is in use by another SonaBar operation.");return;}
        try
        {
            // Do not clear pushToTalkRelease here: a quick key-up can arrive while the
            // target/microphone is still starting and must remain visible to FinishDictationAsync.
            starting=true;startupSignal=new(TaskCreationOptions.RunContinuationsAsynchronously);finishAfterStart=false;lock(timingGate){dictationTimings.Clear();audioReceived=false;}dictationClock.Restart();MarkTiming("T0");Readiness=new(ReadinessStage.Starting);overlay?.ShowPreparing();MarkTiming("T1");
            // If this was started from SonaBar's own window, return to the last external
            // window first; otherwise the app's Start button would target its own button.
            insertion.PreferExternalTarget(restoreCaretOnInsert: true);
            // The inspection of the focused field runs on its own thread and has a hard
            // limit, so a busy target application can never freeze the program.
            target = await Task.Run(insertion.CaptureTarget).WaitAsync(TextInsertionService.CaptureTimeout + TimeSpan.FromMilliseconds(400));
            if(shuttingDown)throw new OperationCanceledException();
            // An opaque game window may expose no AutomationElement. Only the explicit
            // Compatibility profile may proceed without one; every normal field still
            // fails before recording so the user does not speak into a dead end.
            var destinationProfile=Settings.ProfileFor(target.Executable);
            if(target.Element is null&&destinationProfile.Strategy!=InsertionStrategy.Compatibility)
                throw new InvalidOperationException("Windows could not inspect this field safely. Focus an editable field and try again.");
            if(destinationProfile.Strategy!=InsertionStrategy.Compatibility&&!TextInsertionService.IsEditable(target))
                throw new InvalidOperationException("Windows could not inspect this field safely. Focus an editable field and try again.");
            if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Delivery",("stage","capture_target"),("target",TextInsertionService.DescribeTarget(target)));
            if(Settings.SelectionActions&&destinationProfile.Strategy!=InsertionStrategy.Compatibility&&insertion.SelectionFor(target) is {} selected){overlay?.Dismiss();Readiness=new(ReadinessStage.Ready);ShowSelection(selected);return;}
            if (AvailableSpeechModels().Count == 0) throw new InvalidOperationException("Prepare a speech model in Dictation settings before recording.");
            operation = new(); recording = TemporaryWave();
            var token=operation.Token;var path=recording;
            Machine.Move(OperationState.Recording); Status = "Listening";Readiness=new(ReadinessStage.Recording);
            backgroundSpeech?.Cancel();
            recordingStart=Task.Run(()=>Audio.Start(path,CaptureMode.Microphone,Settings.MicrophoneId));
            // A device that cannot be opened must not leave the program waiting forever.
            await recordingStart.WaitAsync(TimeSpan.FromSeconds(6));MarkTiming("T2");
            if(token.IsCancellationRequested||Machine.State!=OperationState.Recording)return;
            feedback.Play(Settings.Sounds, FeedbackSound.Started);
            var warmModel=AvailableSpeechModels()[0];
            speechWarmup=Task.Run(async()=>{await Models.EnsureVerifiedAsync(warmModel,token);await Speech.WarmAsync(Models.PathFor(warmModel),token);},token);
            if (Settings.DuckAudio) ducker.Begin();
            if(finishAfterStart)await FinishDictationAsync();
        }
        catch (OperationCanceledException) when (shuttingDown)
        {
            CompleteTimings(false);overlay?.Dismiss();Readiness=new(ReadinessStage.Ready);
        }
        catch (Exception error)
        {
            feedback.Play(Settings.Sounds, FeedbackSound.Error);
            if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Dictation",("stage","start_failed"),("exception",error.GetType().Name));
            insertion.CancelCaretRestore();
            CompleteTimings(false);
            if(Machine.State==OperationState.Recording)await FailAsync(error);else Notify(error is TimeoutException?"The focused app did not respond. Try the shortcut again in an editable field.":error.Message);
            Readiness=new(ReadinessStage.Ready);
        }
        finally{starting=false;startupSignal.TrySetResult();Changed(nameof(Busy));}
    }
    private async Task FinishDictationAsync()
    {
        if (operation is null || recording is null) return;
        var token = operation.Token;
        try
        {
            // A short accidental push-to-talk press is discarded. The recording starts
            // immediately, but only a deliberate hold is sent to the speech model.
            if (IsTooShortPushToTalk(Settings.PushToTalk, pushToTalkRelease, Settings.MinHoldMs, pushToTalkHeldMs))
            {
                pushToTalkRelease = false;
                pushToTalkHeldMs = 0;
                await recordingStart; await Audio.StopAsync(); ducker.Dispose();
                CompleteTimings(false);
                Status = $"Hold for at least {Settings.MinHoldMs / 1000d:0.0} s to dictate";
                Readiness = new(ReadinessStage.Ready);
                return;
            }
            pushToTalkRelease = false;
            pushToTalkHeldMs = 0;
            MarkTiming("T3");Machine.Move(OperationState.Transcribing); Status = "Transcribing on your computer";Readiness=new(ReadinessStage.Transcribing);
            await recordingStart;await Audio.StopAsync(); ducker.Dispose();
            feedback.Play(Settings.Sounds, FeedbackSound.Finished);
            try { await speechWarmup; } catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
            catch(Exception error) { SafeLog.Write("SpeechWarmup",error); }
            var result = await TranscribeAsync(recording, token,() => MarkTiming("T4"),() => MarkTiming("T5")); MarkTiming("T6"); token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(result.Text)) { if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Dictation",("stage","transcript_empty")); CompleteTimings(false); Notify("No speech was recognized. Check the microphone level and try again."); return; }
            LastOriginal=result.Text;
            insertion.ForgetReceipt();LastText=result.Text;
            if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Dictation",("stage","transcript_ready"),("speech_model",LastSpeechModel),("audio_received",audioReceived));
            var text=await ProcessOutputAsync(result.Text,target!.Executable,false,token,target.WindowTitle,() => MarkTiming("T7"),() => MarkTiming("T8"));
            string improved=text==result.Text?"":text;
            insertion.ForgetReceipt();LastText = text;
            Machine.Move(OperationState.Inserting); Status = "Inserting text";Readiness=new(ReadinessStage.Inserting);
            MarkTiming("T10");
            if(await InsertAsync(target, text, token,point => MarkTiming(point)))
            {
                MarkTiming("T11");CompleteTimings(true);
                var assumed=insertion.LastDeliveryAssumed;
                overlay?.ShowNotice(assumed?"Paste sent — check the app/target":(processingNotice??"Done"),processingNotice is not null);
                var historyEntry=new HistoryEntry(0, DateTimeOffset.UtcNow, result.Text, improved, AudioFiles.Duration(recording).TotalSeconds, result.Language, target!.Executable, LastSpeechModel);
                _=Task.Run(()=>{try{History.Add(historyEntry,Settings.StoreHistory,Settings.HistoryRetentionDays);}catch(Exception error){SafeLog.Write("History",error);}});
            }
            else CompleteTimings(false);
            Changed(nameof(CanUndo));
        }
        catch (OperationCanceledException) { CompleteTimings(false); Status = "Cancelled"; }
        catch (Exception e) { feedback.Play(Settings.Sounds, FeedbackSound.Error); CompleteTimings(false); SafeLog.Write("Dictation", e); Notify(e.Message); }
        finally { insertion.CancelCaretRestore(); speechWarmup=Task.CompletedTask;ducker.Dispose(); DeleteRecording(); Reset(); Readiness=new(ReadinessStage.Ready); }
    }
    public async Task<TranscriptionResult> TranscribeAsync(string wave, CancellationToken token,Action? preprocessed=null,Action? recognitionStarted=null)
    {
        var signal=await Task.Run(()=>AudioFiles.Analyze(wave,token),token);
        preprocessed?.Invoke();
        if(signal.Duration.TotalMilliseconds<250||signal.Rms<.0005)return new("",Settings.Language,signal.Duration,[]);
        var candidates = AvailableSpeechModels();
        if(candidates.Count==0)throw new InvalidOperationException("No speech model is installed. Open Dictation to prepare SonaBar.");
        var failures = new List<string>();
        // Leading and trailing silence carries no words but costs recognition time and makes
        // models guess. Only the outer edges are removed, so a pause inside a sentence stays.
        var prepared = wave;
        string? trimmed = null;
        try
        {
            trimmed = Path.Combine(Path.GetDirectoryName(wave)!,Path.GetFileNameWithoutExtension(wave)+".trim.wav");
            if(AudioFiles.TrimSilence(wave,trimmed,token))prepared=trimmed;
            else trimmed=null;
        }
        catch(OperationCanceledException){throw;}
        catch(Exception error){SafeLog.Write("SilenceTrim",error);trimmed=null;}
        try
        {
            foreach (var model in candidates)
            {
                try
                {
                    await Models.EnsureVerifiedAsync(model,token);
                    recognitionStarted?.Invoke();
                    var modelPath=Models.PathFor(model);
                    var language=Speech is TranscriptionEngineRouter router?router.LanguageFor(modelPath,Settings.Language):Settings.Language;
                    var result=await Speech.TranscribeAsync(prepared, modelPath, language, VocabularyPrompt.Build(Settings.CustomWords), Settings.KeepSpeechLoaded, token);
                    LastSpeechModel=model.Id;
                    if(model.Id!=EffectiveSpeechModel)Status="Using "+model.Name+" while the preferred model is unavailable";
                    return result;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    SafeLog.Write("SpeechModel", e);
                    failures.Add(model.Name + ": " + e.GetBaseException().Message);
                    if (failures.Count >= 2) break;
                }
            }
            throw new InvalidOperationException("Speech recognition failed. " + string.Join("; ", failures));
        }
        finally
        {
            if(trimmed is not null){try{File.Delete(trimmed);}catch(IOException error){SafeLog.Write("TempCleanup",error);}}
        }
    }
    private List<ModelDefinition> AvailableSpeechModels() =>
        Models.Catalog
            .Where(m => Models.IsInstalled(m) && m.Engine switch
            {
                "Whisper" => true,
                "NeMo-Speech.cpp" => File.Exists(Path.Combine(AppContext.BaseDirectory, "nemo_speech_asr_c.dll")),
                "Qwen3-ASR" => QwenEngine.RuntimeAvailable,
                _ => false
            })
            .OrderByDescending(m => m.Id == EffectiveSpeechModel)
            .ThenBy(m => m.Bytes)
            .ToList();
    public async Task<string> EnhanceAsync(string text, string instruction, CancellationToken token,bool preserveDetails=true,bool formatOutput=true)
    {
        var model = Models.Catalog.FirstOrDefault(m => m.Engine == "llama.cpp"&&Models.IsInstalled(m))??Models.Catalog.First(m=>m.Engine=="llama.cpp");
        if (!Models.IsInstalled(model)) throw new InvalidOperationException("Download the local writing model from Models to use enhancement.");
        await Models.EnsureVerifiedAsync(model,token);
        var result=await Enhancement.EnhanceAsync(text, instruction + " Preferred names and terms: " + string.Join(", ", Settings.CustomWords), Models.PathFor(model), Settings.KeepEnhancementLoaded, token);
        if(preserveDetails&&!OutputPipeline.PreservesProtectedDetails(text,result,Settings.CustomWords))throw new InvalidDataException("The writing model changed a protected name, number or time. Your original text was kept.");
        return formatOutput?OutputPipeline.Format(result,Settings.OutputCasing):result;
    }
    private async Task<bool> InsertAsync(InsertionTarget destination, string text, CancellationToken token,Action<string>? phase=null)
    {
        var mode = Settings.Clipboard;
        if (mode == ClipboardMode.Ask) mode = MessageBox.Show("Preserve your existing clipboard after inserting? Choose No to leave the new text on the clipboard.", "Clipboard", MessageBoxButton.YesNo) == MessageBoxResult.Yes ? ClipboardMode.Preserve : ClipboardMode.Replace;
        try
        {
            Status = await insertion.InsertAsync(destination, text, Settings.ProfileFor(destination.Executable), mode, token,phase,WriteDeliveryDiagnostic);
            if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Delivery",("stage","final"),("result",insertion.LastDeliveryAssumed?"assumed":"confirmed"));
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            feedback.Play(Settings.Sounds, FeedbackSound.Error);
            SafeLog.Write("Insertion", e);
            if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Delivery",("stage","final"),("result","failed"),("exception",e.GetType().Name));
            Notify("Could not insert text: " + e.Message + " Your text is saved in the SonaBar window; use Copy when ready.");
            return false;
        }
    }
    private void WriteDeliveryDiagnostic(string detail)
    {
        if(Settings.DebugLogging)SafeLog.WriteDiagnostic("Delivery",("detail",detail));
    }
    public async Task<string> TranslateAsync(string text,string language,CancellationToken token)
    {
        void Mark(string stage,string? reason=null)
        {
            if(!Settings.DebugLogging)return;
            if(reason is null)SafeLog.WriteDiagnostic("Translation",("stage",stage),("language",language),("chars",text.Length));
            else SafeLog.WriteDiagnostic("Translation",("stage",stage),("reason",reason));
        }
        // Markers protect names, numbers and clock times, but a local model may still split or
        // repeat one. Try the protected pass first, then fall back to a direct translation with
        // the same preservation contract instead of failing the whole dictation.
        Mark("start");
        var protectedText=ProtectedTranslation.Create(text,language,Settings.CustomWords);
        var instruction=OutputPipeline.EditingInstruction(Settings,"",new(text,SpokenAction.Translate,language))+" Copy every SONABARLITERAL token exactly once, unchanged. Treat these tokens as opaque text. Do not interpret, split, translate or expand them.";
        try
        {
            var result=protectedText.Restore(await EnhanceAsync(protectedText.Text,instruction,token,false,false));
            if(string.IsNullOrWhiteSpace(result))throw new InvalidDataException("Translation returned no text.");
            if(!TranslationUnchanged(text,result)&&OutputPipeline.PreservesProtectedDetails(text,result,Settings.CustomWords))
            {
                Mark("protected_ok");
                return OutputPipeline.Format(result,Settings.OutputCasing);
            }
            var reason=TranslationUnchanged(text,result)?"unchanged":"protected_detail";
            Mark("protected_rejected",reason);
            SafeLog.Write("Translation",new InvalidDataException(reason=="unchanged"
                ?"Protected translation returned the original text."
                :"Protected translation did not preserve every detail."));
        }
        catch(InvalidDataException error)
        {
            Mark("protected_error",error.GetType().Name);
            SafeLog.Write("Translation",error);
        }
        token.ThrowIfCancellationRequested();
        var fallbackInstruction=OutputPipeline.EditingInstruction(Settings,"",new(text,SpokenAction.Translate,language))
            +" Preserve every name in its ORIGINAL SPELLING, every number, time, currency amount, unit and negation exactly. Do not add time qualifiers. Return only the translation.";
        var fallback=await EnhanceAsync(text,fallbackInstruction,token,false,false);
        if(string.IsNullOrWhiteSpace(fallback))throw new InvalidDataException("Translation returned no text. Your original text was kept.");
        if(TranslationUnchanged(text,fallback))
        {
            Mark("fallback_rejected","unchanged");
            throw new InvalidDataException("Translation did not change the text. Your original text was kept.");
        }
        if(!OutputPipeline.PreservesProtectedDetails(text,fallback,Settings.CustomWords))
        {
            Mark("fallback_rejected","protected_detail");
            throw new InvalidDataException("Translation changed a protected detail. Your original text was kept.");
        }
        Mark("fallback_ok");
        return OutputPipeline.Format(fallback,Settings.OutputCasing);
    }
    /// <summary>
    /// Detects a model response that only repeats the source, possibly with punctuation, casing
    /// or spacing changes. Such a response is not a translation and must trigger the stronger
    /// fallback instead of being reported as success.
    /// </summary>
    internal static bool TranslationUnchanged(string source,string result)
    {
        static string Key(string value)
        {
            var normalized=value.Normalize();
            var builder=new System.Text.StringBuilder(normalized.Length);
            foreach(var c in normalized)
                if(!char.IsWhiteSpace(c)&&!char.IsPunctuation(c))
                    builder.Append(char.ToUpperInvariant(c));
            return builder.ToString();
        }
        static List<string> Words(string value)
        {
            var words=new List<string>();
            var word=new System.Text.StringBuilder();
            void Flush()
            {
                if(word.Length==0)return;
                var text=word.ToString();
                // Proper names are often identical across languages. Compare the remaining
                // lowercase words so a real translation with several names is not rejected.
                if(!char.IsUpper(text[0]))words.Add(text.ToLowerInvariant());
                word.Clear();
            }
            foreach(var c in value.Normalize())
            {
                if(char.IsLetter(c)){ word.Append(c); continue; }
                Flush();
            }
            Flush();
            return words;
        }
        var key=Key(source);
        if(key.Length==0)return false;
        if(key==Key(result))return true;
        // A model can return the source with a single name changed and still look different.
        // Compare the remaining words so a mostly unchanged sentence triggers the fallback.
        var sourceWords=Words(source);
        if(sourceWords.Count<2)return false;
        var resultWords=new HashSet<string>(Words(result),StringComparer.Ordinal);
        return sourceWords.Count(resultWords.Contains)/(double)sourceWords.Count>=0.8;
    }
    public async Task RunExclusiveAsync(Func<CancellationToken, Task> action)
    {
        if (Busy) throw new InvalidOperationException("Finish or cancel the current operation first.");
        operation = new(); Machine.Move(OperationState.Transcribing); backgroundSpeech?.Cancel();
        try { await action(operation.Token); }
        finally { Reset(); }
    }
    public void SetResult(string text) { insertion.ForgetReceipt();target=null;LastOriginal=text;LastText=text;Changed(nameof(CanUndo)); }
    public async void Cancel()
    {
        operation?.Cancel();
        if (instructionStop is not null) { instructionStop.TrySetResult(); return; }
        if (Machine.State == OperationState.Recording)
        {
            Machine.Move(OperationState.Cancelled);
            try { await recordingStart;await Audio.StopAsync();await speechWarmup; } catch (OperationCanceledException) { } catch (Exception e) { SafeLog.Write("Cancel", e); }
            finally { speechWarmup=Task.CompletedTask;ducker.Dispose(); DeleteRecording(); Reset(); Status = "Cancelled"; }
        }
    }
    private async Task FailAsync(Exception error)
    {
        try { await Audio.StopAsync(); } catch (Exception e) { SafeLog.Write("Stop", e); }
        ducker.Dispose(); DeleteRecording(); Reset(); SafeLog.Write("Operation", error); Notify(error.Message);
    }
    internal static bool IsTooShortPushToTalk(bool pushToTalk, bool released, int minHoldMs, long heldMs) =>
        pushToTalk && released && minHoldMs > 0 && heldMs < minHoldMs;
    private void Reset() { pushToTalkRelease=false; pushToTalkHeldMs=0; if (Machine.State != OperationState.Idle) { if(Machine.State==OperationState.Recording)Machine.Move(OperationState.Cancelled); Machine.Move(OperationState.Idle); } operation?.Dispose(); operation = null; }
    private void DeleteRecording() { if (recording is not null) { try { File.Delete(recording); } catch (IOException e) { SafeLog.Write("TempCleanup", e); } recording = null; } }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public Task SmokeTestAsync(bool trayCreated, bool settingsOpened, IReadOnlyList<string>? pagesRendered=null, IReadOnlyList<string>? pagesFailed=null)
    {
        var result = new { trayCreated, settingsOpened, modelCount = Models.Catalog.Count, microphoneCount = Audio.Microphones().Count, settingsValid = true, state = Machine.State.ToString(), pagesRendered = pagesRendered ?? [], pagesFailed = pagesFailed ?? [] };
        Settings.Validate(); return File.WriteAllTextAsync(Path.Combine(Brand.DataRoot, "smoke-test.json"), System.Text.Json.JsonSerializer.Serialize(result));
    }
    public void Dispose() { operation?.Cancel(); preparation?.Cancel(); ducker.Dispose(); feedback.Dispose(); overlay?.Close(); }
    public async Task StopSafelyAsync() { shuttingDown=true;preparation?.Cancel();operation?.Cancel();await startupSignal.Task;Cancel();await idleSignal.Task;await preparationTask;await Audio.StopAsync();ducker.Dispose();feedback.Dispose(); }
    public Task PrepareQualityAsync() => preparationTask=PrepareQualityCoreAsync();
    private async Task PrepareQualityCoreAsync()
    {
        preparation?.Cancel();var source=new CancellationTokenSource();preparation=source;
        var model=Models.Catalog.First(x=>x.Id==EffectiveSpeechModel);
        try
        {
            if(!Models.IsInstalled(model))await Models.DownloadAsync(model,new Progress<DownloadProgress>(p=>{ModelPreparation=$"Preparing {Settings.Quality} · {p.Stage} · {p.Percent:N0}%";Changed(nameof(ModelPreparation));}),source.Token);
            ModelPreparation=Settings.Quality+" is ready offline";Changed(nameof(ModelPreparation));
        }
        catch(OperationCanceledException){}
        catch(Exception error){ModelPreparation="Model preparation paused: "+error.Message;Changed(nameof(ModelPreparation));}
        finally {if(ReferenceEquals(preparation,source))preparation=null;source.Dispose();}
    }
    public async Task<string> ProcessOutputAsync(string original,string app,bool forceWritingCleanup,CancellationToken token,string windowTitle="",Action? enhancementStarted=null,Action? enhancementCompleted=null)
    {
        processingNotice=null;
        var text=TextProcessing.Expand(original,Settings.Snippets,DateTime.Now,Clipboard.Text);
        if(text!=original)return OutputPipeline.Format(text,Settings.OutputCasing);
        text=OutputPipeline.Prepare(text,Settings);
        var request=Settings.SpokenInstructions?SpokenInstructions.Parse(text):new SpokenRequest(text,SpokenAction.None);
        // The detected command is recorded without the words themselves, so a wrong match
        // can be traced without ever writing dictated content into a log.
        SafeLog.WriteDiagnostic("Dictation",("stage","spoken_command"),("action",request.Action.ToString()),("chars",request.Content.Length));
        // Casing commands are deterministic. They never start the writing model and never
        // change anything but the letter case.
        if(request.Action is SpokenAction.Lowercase or SpokenAction.Uppercase)
        {
            enhancementStarted?.Invoke();enhancementCompleted?.Invoke();
            return OutputPipeline.Format(request.Content,request.Action==SpokenAction.Lowercase?OutputCasing.Lowercase:OutputCasing.Uppercase);
        }
        // Ordinary dictation stays on the fast deterministic path. The writing model starts only
        // for an explicit transformation or a user-initiated cleanup of an existing result.
        var needsEditor=forceWritingCleanup||request.Action!=SpokenAction.None;
        if(needsEditor&&Models.Catalog.Any(x=>x.Engine=="llama.cpp"&&Models.IsInstalled(x)))
        {
            enhancementStarted?.Invoke();
            if(Machine.State==OperationState.Transcribing)Machine.Move(OperationState.Enhancing);
            Readiness=new(ReadinessStage.ApplyingCommand);
            Status=StatusFor(request.Action);
            try
            {
                var revised=request.Action==SpokenAction.Translate?await TranslateAsync(request.Content,request.Language!,token):await EnhanceAsync(request.Content,InstructionFor(request,app,windowTitle),token);
                if(request.Action==SpokenAction.None?OutputPipeline.IsConservativeEdit(request.Content,revised):OutputPipeline.AcceptsSpokenEdit(request.Action,request.Content,revised))text=revised;
                else if(request.Action!=SpokenAction.None)throw new InvalidDataException("The requested edit changed protected details. Nothing was inserted; your transcript is available to copy.");
                else processingNotice="Original text kept: the edit changed details.";
            }
            catch(Exception error)when(error is not OperationCanceledException){SafeLog.Write("TextProcessing",error);if(request.Action!=SpokenAction.None)throw new InvalidOperationException("The requested edit could not be completed. Nothing was inserted; your transcript is available to copy.",error);processingNotice="Local editing failed — original text kept";}
            finally { enhancementCompleted?.Invoke(); }
        }
        else
        {
            enhancementStarted?.Invoke(); enhancementCompleted?.Invoke();
            // Removing fillers has a deterministic fallback, so the command still works
            // without a writing model.
            if(request.Action==SpokenAction.RemoveFillers)return OutputPipeline.Format(SpeechCleanup.RemoveUnambiguousFillers(request.Content),Settings.OutputCasing);
            if(request.Action!=SpokenAction.None)throw new InvalidOperationException("Prepare the writing model in Models to use spoken instructions. Nothing was inserted; your transcript is available to copy.");
        }
        return OutputPipeline.Format(text,Settings.OutputCasing);
    }
    private static string StatusFor(SpokenAction action) => action switch
    {
        SpokenAction.Translate=>"Translating locally…",
        SpokenAction.ChatGptPrompt=>"Creating your prompt…",
        SpokenAction.Bullets=>"Making a bullet list…",
        SpokenAction.ProfessionalEmail=>"Writing your email…",
        SpokenAction.Friendly=>"Rewriting friendlier…",
        SpokenAction.SpellingOnly=>"Correcting spelling…",
        SpokenAction.Summarize=>"Summarizing…",
        SpokenAction.RemoveFillers=>"Removing fillers…",
        _=>"Polishing your words…"
    };
    private string InstructionFor(SpokenRequest request,string app,string windowTitle)
    {
        if(request.Action is SpokenAction.Translate or SpokenAction.ChatGptPrompt)
            return OutputPipeline.EditingInstruction(Settings,Settings.AppAwareFormatting?ContextInstruction(app,windowTitle):"",request);
        return SpokenActions.Instruction(request.Action);
    }
    public async Task RewriteLatestAsync(RewriteAction action,string? language=null)
    {
        if(string.IsNullOrWhiteSpace(LastText))return;
        var receipt=insertion.LastReceipt;var original=LastText;
        await RunExclusiveAsync(async token=>
        {
            var result=action.Id=="translate"?await TranslateAsync(original,language??"English",token):await EnhanceAsync(original,(action.Id=="prompt"?OutputPipeline.EditingInstruction(Settings,"",new(original,SpokenAction.ChatGptPrompt)):action.Instruction)+" Preserve all facts, numbers, names, negations and intent. Return only revised text.",token);
            LastText=OutputPipeline.Format(result,Settings.OutputCasing);
            if(receipt is not null)await insertion.ReplaceReceiptAsync(receipt,LastText,Settings.Clipboard==ClipboardMode.Replace?ClipboardMode.Replace:ClipboardMode.Preserve,token);
        });
        overlay?.ShowNotice("Updated",false);Changed(nameof(CanUndo));
    }
    public async Task SetLatestCasingAsync(OutputCasing casing)
    {
        if(Busy||LastText.Length==0)return;
        var receipt=insertion.LastReceipt;LastText=OutputPipeline.Format(LastText,casing);
        if(receipt is not null)await RunExclusiveAsync(token=>insertion.ReplaceReceiptAsync(receipt,LastText,Settings.Clipboard==ClipboardMode.Replace?ClipboardMode.Replace:ClipboardMode.Preserve,token));
    }
    public async Task UndoAsync()
    {
        var receipt=insertion.LastReceipt;if(receipt is null)return;
        await RunExclusiveAsync(token=>insertion.ReplaceReceiptAsync(receipt,receipt.Replaced,ClipboardMode.Preserve,token));
        LastText=receipt.Replaced;Changed(nameof(CanUndo));
    }
    public async Task RetryProcessingAsync()
    {
        if(LastOriginal.Length==0)return;var receipt=insertion.LastReceipt;
        await RunExclusiveAsync(async token=>{LastText=await ProcessOutputAsync(LastOriginal,target?.Executable??"",true,token);if(receipt is not null)await insertion.ReplaceReceiptAsync(receipt,LastText,ClipboardMode.Preserve,token);});
    }
}
