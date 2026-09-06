namespace iKeyd.Core.Configuration;

public sealed record MouseMotionProfile
{
    public static MouseMotionProfile Default { get; } = new();

    public string Engine { get; init; } = "virtual_stick";
    public int UpdateIntervalMs { get; init; } = 8;
    public int PressMs { get; init; } = 45;
    public int ReleaseMs { get; init; } = 2;
    public string Curve { get; init; } = "smoothstep";
    public double NormalSpeed { get; init; } = 2200.0;
    public double PrecisionSpeed { get; init; } = 800.0;
    public double FineSpeed { get; init; } = 240.0;
    public double FastSpeed { get; init; } = 4400.0;
    public string Socd { get; init; } = "neutral";
    public int TapNudgePixels { get; init; } = 1;
    public int MaxCatchupMs { get; init; } = 32;

    public MouseMotionProfile()
    {
    }

    public MouseMotionProfile(
        string engine,
        int updateIntervalMs,
        int pressMs,
        int releaseMs,
        string curve,
        double normalSpeed,
        double precisionSpeed,
        double fineSpeed,
        double fastSpeed,
        string socd,
        int tapNudgePixels,
        int maxCatchupMs)
    {
        if (!string.Equals(engine, "virtual_stick", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mouse engine currently supports only 'virtual_stick'.", nameof(engine));
        if (updateIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(updateIntervalMs), "Mouse update interval must be positive.");
        if (pressMs < 0)
            throw new ArgumentOutOfRangeException(nameof(pressMs), "Mouse press smoothing must be non-negative.");
        if (releaseMs < 0)
            throw new ArgumentOutOfRangeException(nameof(releaseMs), "Mouse release smoothing must be non-negative.");
        if (!string.Equals(curve, "linear", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(curve, "smoothstep", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mouse curve must be 'linear' or 'smoothstep'.", nameof(curve));
        }
        if (!string.Equals(socd, "neutral", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Mouse SOCD currently supports only 'neutral'.", nameof(socd));
        if (normalSpeed < 0 || precisionSpeed < 0 || fineSpeed < 0 || fastSpeed < 0)
            throw new ArgumentOutOfRangeException(nameof(normalSpeed), "Mouse speeds must be non-negative.");
        if (tapNudgePixels < 0)
            throw new ArgumentOutOfRangeException(nameof(tapNudgePixels), "Mouse tap nudge must be non-negative.");
        if (maxCatchupMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCatchupMs), "Mouse max catchup must be positive.");

        Engine = "virtual_stick";
        UpdateIntervalMs = updateIntervalMs;
        PressMs = pressMs;
        ReleaseMs = releaseMs;
        Curve = curve.ToLowerInvariant();
        NormalSpeed = normalSpeed;
        PrecisionSpeed = precisionSpeed;
        FineSpeed = fineSpeed;
        FastSpeed = fastSpeed;
        Socd = "neutral";
        TapNudgePixels = tapNudgePixels;
        MaxCatchupMs = maxCatchupMs;
    }
}
