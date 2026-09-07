namespace iKeyd.Windows.Tests;

internal static class LayerGestureTimingProfiles
{
    public static readonly (int PressGapMs, int OverlapMs, int ReleaseGapMs)[] All =
    [
        // Fast/reference profiles.
        (1, 1, 1),
        (5, 5, 5),
        (10, 10, 10),

        // Cross the 40 ms boundary independently in every phase of the gesture.
        (39, 1, 1),
        (40, 1, 1),
        (41, 1, 1),
        (1, 39, 1),
        (1, 40, 1),
        (1, 41, 1),
        (1, 1, 39),
        (1, 1, 40),
        (1, 1, 41),

        // Long holds independently in every phase.  Keeping the other two gaps at
        // 10 ms makes a failure attributable to one phase instead of conflating
        // press and release timing in a single profile.
        (100, 10, 10),
        (10, 100, 10),
        (10, 10, 100),
        (500, 10, 10),
        (10, 500, 10),
        (10, 10, 500),
    ];
}
