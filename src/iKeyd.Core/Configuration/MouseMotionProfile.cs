namespace iKeyd.Core.Configuration;

public sealed record MouseMotionProfile
{
    public static MouseMotionProfile Default { get; } = new(
        "virtual_stick",
        8,
        45,
        2,
        "smoothstep",
        2200.0,
        800.0,
        240.0,
        4400.0,
        "neutral",
        1,
        32);

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

    public string Engine { get; }
    public int UpdateIntervalMs { get; }
    public int PressMs { get; }
    public int ReleaseMs { get; }
    public string Curve { get; }
    public double NormalSpeed { get; }
    public double PrecisionSpeed { get; }
    public double FineSpeed { get; }
    public double FastSpeed { get; }
    public string Socd { get; }
    public int TapNudgePixels { get; }
    public int MaxCatchupMs { get; }
}
