namespace Voxora.Core;

/// <summary>
/// The explicit states the application can be in. The readiness state is separate from the
/// operation state, because a missing model or an unfinished download is a state the user
/// has to see, not an endless "preparing".
/// </summary>
public enum ReadinessStage
{
    /// <summary>Nothing is happening.</summary>
    Idle,
    /// <summary>The process is starting and the first window is being built.</summary>
    Starting,
    /// <summary>The settings are being read.</summary>
    LoadingSettings,
    /// <summary>The capabilities of this computer are being measured.</summary>
    DetectingHardware,
    /// <summary>The installed models are being checked.</summary>
    CheckingModels,
    /// <summary>A model is being loaded into memory.</summary>
    PreparingModel,
    /// <summary>No speech model is installed. The user has to download one.</summary>
    NeedsModel,
    /// <summary>Dictation can start.</summary>
    Ready,
    /// <summary>The microphone is recording.</summary>
    Recording,
    /// <summary>The recording is being recognised.</summary>
    Transcribing,
    /// <summary>A spoken text command is being carried out.</summary>
    ApplyingCommand,
    /// <summary>The result is being placed into the target window.</summary>
    Inserting,
    /// <summary>Something went wrong and the reason is shown.</summary>
    Error
}

/// <summary>A readiness state with a short explanation that can be shown to the user.</summary>
public sealed record Readiness(ReadinessStage Stage, string Detail = "")
{
    public static Readiness Idle { get; } = new(ReadinessStage.Idle, "");

    /// <summary>True while the application is doing work that the user is waiting for.</summary>
    public bool IsBusy => Stage is ReadinessStage.Starting or ReadinessStage.LoadingSettings
        or ReadinessStage.DetectingHardware or ReadinessStage.CheckingModels
        or ReadinessStage.PreparingModel or ReadinessStage.Transcribing
        or ReadinessStage.ApplyingCommand or ReadinessStage.Inserting;

    /// <summary>A short label for the status bar.</summary>
    public string Label => Stage switch
    {
        ReadinessStage.Starting => "Starting",
        ReadinessStage.LoadingSettings => "Loading settings",
        ReadinessStage.DetectingHardware => "Checking this computer",
        ReadinessStage.CheckingModels => "Checking models",
        ReadinessStage.PreparingModel => "Preparing the speech model",
        ReadinessStage.NeedsModel => "No speech model yet",
        ReadinessStage.Ready => "Ready to listen",
        ReadinessStage.Recording => "Listening",
        ReadinessStage.Transcribing => "Recognising speech",
        ReadinessStage.ApplyingCommand => "Applying your command",
        ReadinessStage.Inserting => "Inserting text",
        ReadinessStage.Error => "Something went wrong",
        _ => ""
    };
}
