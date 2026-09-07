using Xunit;

namespace iKeyd.Windows.Tests;

public sealed class LayerGestureTimingSmoke
{
    [Fact]
    public void Baseline_timing_profile_documents_current_10ms_grid()
    {
        var baseline = LayerGestureTimingProfiles.All.Single(profile =>
            profile.PressGapMs == 10 && profile.OverlapMs == 10 && profile.ReleaseGapMs == 10);

        Assert.Equal(10, baseline.PressGapMs);
        Assert.Equal(10, baseline.OverlapMs);
        Assert.Equal(10, baseline.ReleaseGapMs);
    }
}
