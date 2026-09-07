namespace iKeyd.Windows.Tests;

internal static class LayerGestureTimingProfiles
{
    public static readonly (int PressGapMs, int OverlapMs, int ReleaseGapMs)[] All =
    [
        (1, 1, 1),
        (5, 5, 5),
        (10, 10, 10),
        (39, 1, 1),
        (40, 1, 1),
        (41, 1, 1),
        (1, 39, 1),
        (1, 40, 1),
        (1, 41, 1),
        (10, 100, 10),
        (100, 10, 100),
        (500, 10, 10),
        (10, 500, 10),
        (10, 10, 500),
    ];
}
