namespace Voxora.Core;

/// <summary>
/// Voice activity detection for a finished recording.
/// <para>
/// Only the outer edges of a recording are removed. A pause inside a sentence is never
/// touched, so a dictated clause can not be cut away, and a short silence never ends a
/// recording on its own: the decision happens after the user stops talking.
/// </para>
/// </summary>
public static class SilenceTrim
{
    /// <summary>Length of one analysis frame.</summary>
    public const double FrameSeconds = 0.02;
    /// <summary>Silence kept before the first and after the last detected sound.</summary>
    public const double MarginSeconds = 0.20;
    /// <summary>Absolute level below which audio counts as silence regardless of the noise floor.</summary>
    public const double AbsoluteFloor = 0.006;
    /// <summary>How far above the measured noise floor speech has to be.</summary>
    public const double NoiseMultiplier = 3.0;
    /// <summary>A recording is never shortened below this share of its original length.</summary>
    public const double MinimumKeptFraction = 0.30;
    /// <summary>Trimming has to save at least this much time to be worth it.</summary>
    public const double MinimumSavedSeconds = 0.30;

    public readonly record struct TrimPlan(int StartSample, int LengthSamples, double Threshold, bool Trimmed, int OriginalSamples)
    {
        public int EndSample => StartSample + LengthSamples;
        /// <summary>How many samples the trim removes.</summary>
        public int SavedSamples => Trimmed ? Math.Max(0, OriginalSamples - LengthSamples) : 0;
    }

    public static int FrameSize(int sampleRate) => Math.Max(1, (int)Math.Round(sampleRate * FrameSeconds));

    /// <summary>Analyses a complete mono buffer. Used for tests and short clips.</summary>
    public static TrimPlan Plan(float[] samples, int sampleRate)
    {
        var frame = FrameSize(sampleRate);
        var levels = new List<double>((samples.Length / frame) + 1);
        for (var offset = 0; offset < samples.Length; offset += frame)
        {
            var end = Math.Min(samples.Length, offset + frame);
            double squareSum = 0;
            for (var index = offset; index < end; index++) squareSum += (double)samples[index] * samples[index];
            levels.Add(end > offset ? Math.Sqrt(squareSum / (end - offset)) : 0);
        }
        return PlanFromLevels(levels, sampleRate, samples.Length);
    }

    /// <summary>Analyses measured frame levels, so long recordings never need to be held in memory.</summary>
    public static TrimPlan PlanFromLevels(IReadOnlyList<double> levels, int sampleRate, int totalSamples)
    {
        if (levels.Count == 0 || totalSamples <= 0) return new(0, Math.Max(0, totalSamples), 0, false, Math.Max(0, totalSamples));
        var sorted = levels.ToArray();
        Array.Sort(sorted);
        var noiseFloor = sorted[Math.Clamp((int)(sorted.Length * 0.10), 0, sorted.Length - 1)];
        var threshold = Math.Max(AbsoluteFloor, noiseFloor * NoiseMultiplier);

        var first = -1;
        var last = -1;
        for (var index = 0; index < levels.Count; index++)
        {
            if (levels[index] < threshold) continue;
            if (first < 0) first = index;
            last = index;
        }
        if (first < 0) return new(0, totalSamples, threshold, false, totalSamples);

        var frame = FrameSize(sampleRate);
        var margin = (int)Math.Round(sampleRate * MarginSeconds);
        var start = Math.Max(0, (first * frame) - margin);
        var end = Math.Min(totalSamples, ((last + 1) * frame) + margin);
        var kept = Math.Max(0, end - start);
        // A trim that removes too much would cut a sentence, so it is refused.
        if (kept < totalSamples * MinimumKeptFraction) return new(0, totalSamples, threshold, false, totalSamples);
        if (totalSamples - kept < sampleRate * MinimumSavedSeconds) return new(0, totalSamples, threshold, false, totalSamples);
        return new(start, kept, threshold, true, totalSamples);
    }
}
