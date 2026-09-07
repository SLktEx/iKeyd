using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class BehaviorRetentionTests
{
    [Fact]
    public void Runtime_rejects_post_release_retention_without_a_deadline()
    {
        var source = new KeyId("A");
        var runtime = new BehaviorRuntime(
            new Dictionary<KeyId, BehaviorDefinition>
            {
                [source] = new UnboundedRetentionDefinition()
            });

        Assert.True(runtime.OnKeyDown(source, 0).Suppress);

        var error = Assert.Throws<InvalidOperationException>(() =>
            runtime.OnKeyUp(source, 1));

        Assert.Contains("bounded deadline", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runtime.ActiveCount);
        Assert.Equal(0, runtime.PendingCount);
        Assert.Null(runtime.NextDeadlineMs);
    }

    private sealed class UnboundedRetentionDefinition : BehaviorDefinition
    {
        internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
            => new UnboundedRetentionInstance(sourceKey);
    }

    private sealed class UnboundedRetentionInstance(KeyId sourceKey) : BehaviorInstance(sourceKey)
    {
        internal override bool KeepAliveAfterRelease => true;

        internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
        {
        }
    }
}
