namespace SonaBar.Core;

/// <summary>
/// A snapshot of the machine SonaBar is running on. Detection lives in WindowsIntegration;
/// this type and the rules that act on it stay free of platform calls so they can be tested.
/// </summary>
public sealed record HardwareProfile(
    string OperatingSystem,
    int LogicalProcessors,
    long TotalMemoryBytes,
    long AvailableMemoryBytes,
    string? GraphicsAdapter,
    long GraphicsMemoryBytes,
    bool DiscreteGraphics,
    bool SoftwareAdapter,
    bool VulkanRuntime,
    bool CudaRuntime,
    long FreeDiskBytes)
{
    public bool HasGraphicsAdapter => !string.IsNullOrWhiteSpace(GraphicsAdapter);
    /// <summary>True when an acceleration runtime that a speech engine could use is actually installed.</summary>
    public bool HasAccelerationRuntime => VulkanRuntime || CudaRuntime;
    public long RequiredDiskBytes(long modelBytes) => modelBytes + (modelBytes / 10) + 64L * 1024 * 1024;
    public bool HasRoomFor(long modelBytes) => FreeDiskBytes <= 0 || FreeDiskBytes >= RequiredDiskBytes(modelBytes);
    public string MemoryText => $"{FormatBytes(AvailableMemoryBytes)} free of {FormatBytes(TotalMemoryBytes)}";
    public string GraphicsText
    {
        get
        {
            if (!HasGraphicsAdapter) return "No graphics adapter reported";
            var memory = GraphicsMemoryBytes > 0 ? $" · {FormatBytes(GraphicsMemoryBytes)} dedicated memory" : "";
            return GraphicsAdapter + memory + (SoftwareAdapter ? " · software adapter" : "");
        }
    }
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        double value = bytes;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value.ToString(index == 0 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture)} {units[index]}";
    }
}

/// <summary>
/// Turns detected hardware into a concrete starting point. The rules are deliberately
/// conservative: a profile that is too large is slower than a slightly smaller one,
/// and a profile that does not fit is unusable.
/// </summary>
public static class HardwareRecommendations
{
    /// <summary>Total memory a computer needs before the balanced model is a sensible choice.</summary>
    public const long BalancedTotalMemoryBytes = 4L * 1024 * 1024 * 1024;
    /// <summary>Free memory below this means the machine is under real pressure right now.</summary>
    public const long CriticalFreeMemoryBytes = 1500L * 1024 * 1024;
    /// <summary>The strongest profile is never chosen automatically, because it measured far slower on the processor.</summary>
    public const string MaximumNote = "Maximum stays a manual choice: on the reference machine it needed several times longer than Balanced for the same dictation.";

    public static SpeechQuality RecommendQuality(HardwareProfile profile)
    {
        // The total memory of the machine is the stable signal. Free memory only downgrades
        // the profile when the computer is under real pressure, so the choice does not
        // change from one minute to the next.
        if (profile.TotalMemoryBytes > 0 && profile.TotalMemoryBytes < BalancedTotalMemoryBytes) return SpeechQuality.Fast;
        if (profile.LogicalProcessors < 3) return SpeechQuality.Fast;
        if (profile.AvailableMemoryBytes > 0 && profile.AvailableMemoryBytes < CriticalFreeMemoryBytes) return SpeechQuality.Fast;
        return SpeechQuality.Balanced;
    }

    public static string RecommendModel(HardwareProfile profile) => SpeechProfiles.ModelFor(RecommendQuality(profile));

    /// <summary>Explains the recommendation in plain words, without CUDA versions or model paths.</summary>
    public static string Explain(HardwareProfile profile)
    {
        var quality = RecommendQuality(profile);
        return quality switch
        {
            SpeechQuality.Fast => $"{HardwareProfile.FormatBytes(profile.TotalMemoryBytes)} of memory and {profile.LogicalProcessors} logical processors. SonaBar suggests Fast: it starts instantly and stays usable on modest hardware.",
            SpeechQuality.Balanced => $"{HardwareProfile.FormatBytes(profile.TotalMemoryBytes)} of memory and {profile.LogicalProcessors} logical processors. SonaBar suggests Balanced as the best balance of accuracy and speed. " + MaximumNote,
            _ => MaximumNote
        };
    }

    /// <summary>Explains why an acceleration runtime is or is not worth enabling.</summary>
    public static string AccelerationText(HardwareProfile profile)
    {
        if (!profile.HasGraphicsAdapter) return "No graphics adapter was reported, so speech recognition runs on the processor.";
        if (profile.CudaRuntime) return "An NVIDIA CUDA runtime is installed. SonaBar still uses the processor for short dictations, because measurements on this app were faster there.";
        if (profile.VulkanRuntime) return "A Vulkan runtime is installed. SonaBar still uses the processor for short dictations, because measurements on this app were faster there.";
        return "No Vulkan or CUDA runtime was found, so speech recognition runs on the processor.";
    }
}
