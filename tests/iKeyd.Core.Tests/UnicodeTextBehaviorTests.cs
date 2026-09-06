using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class UnicodeTextBehaviorTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("λ")]
    [InlineData("→")]
    [InlineData("✓")]
    [InlineData("あ")]
    [InlineData("🦀")]
    [InlineData("😀")]
    public void Unicode_action_accepts_exactly_one_scalar(string scalar)
    {
        var action = BehaviorAction.SendUnicode(scalar);

        Assert.Equal(BehaviorActionKind.SendUnicode, action.Kind);
        Assert.Equal(scalar, action.Text);
        Assert.Equal(BehaviorRepeatPolicy.PhysicalKeyDown, action.RepeatPolicy);
    }

    [Fact]
    public void Unicode_action_rejects_empty_multiple_or_invalid_scalars()
    {
        Assert.Throws<ArgumentException>(() => BehaviorAction.SendUnicode(string.Empty));
        Assert.Throws<ArgumentException>(() => BehaviorAction.SendUnicode("ab"));
        Assert.Throws<ArgumentException>(() => BehaviorAction.SendUnicode("🦀x"));
        Assert.Throws<ArgumentException>(() => BehaviorAction.SendUnicode("\uD800"));
    }

    [Fact]
    public void Text_action_preserves_mixed_unicode_but_is_not_implicitly_repeatable()
    {
        const string value = "hello 世界 🦀";

        var action = BehaviorAction.SendText(value);

        Assert.Equal(BehaviorActionKind.SendText, action.Kind);
        Assert.Equal(value, action.Text);
        Assert.Equal(BehaviorRepeatPolicy.Never, action.RepeatPolicy);
        Assert.Throws<ArgumentException>(() => BehaviorAction.SendText(string.Empty));
        Assert.Throws<ArgumentException>(() => BehaviorAction.SendText("x\uD800y"));
    }

    [Fact]
    public void Key_unicode_and_text_actions_remain_semantically_distinct()
    {
        var key = BehaviorAction.SendKey(new KeyId("A"));
        var unicode = BehaviorAction.SendUnicode("A");
        var text = BehaviorAction.SendText("A");

        Assert.Equal(BehaviorActionKind.SendKey, key.Kind);
        Assert.Equal(BehaviorActionKind.SendUnicode, unicode.Kind);
        Assert.Equal(BehaviorActionKind.SendText, text.Kind);
        Assert.Equal(BehaviorRepeatPolicy.PhysicalKeyDown, key.RepeatPolicy);
        Assert.Equal(BehaviorRepeatPolicy.PhysicalKeyDown, unicode.RepeatPolicy);
        Assert.Equal(BehaviorRepeatPolicy.Never, text.RepeatPolicy);
    }

    [Fact]
    public void Unicode_behavior_repeats_on_repeated_physical_down_and_stops_on_up()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.Unicode("🦀"));

        var first = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 20);
        var up = runtime.OnKeyUp(source, 30);

        Assert.Equal([BehaviorAction.SendUnicode("🦀")], first.Actions);
        Assert.Equal([BehaviorAction.SendUnicode("🦀")], repeat.Actions);
        Assert.True(up.Suppress);
        Assert.Empty(up.Actions);
        Assert.Equal(0, runtime.ActiveCount);
    }

    [Fact]
    public void Text_behavior_fires_once_even_when_physical_down_repeats()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.Text("hello 世界"));

        var first = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 20);
        var up = runtime.OnKeyUp(source, 30);

        Assert.Equal([BehaviorAction.SendText("hello 世界")], first.Actions);
        Assert.Empty(repeat.Actions);
        Assert.True(repeat.Suppress);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void Repeated_down_does_not_replay_stateful_layer_transition()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.MO("NAV"));

        var first = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 20);
        var up = runtime.OnKeyUp(source, 30);

        Assert.Equal([BehaviorAction.LayerOn("NAV")], first.Actions);
        Assert.Empty(repeat.Actions);
        Assert.Equal([BehaviorAction.LayerOff("NAV")], up.Actions);
    }

    [Fact]
    public void Factory_supports_option_backed_unicode_and_text_literals()
    {
        var unicode = BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(
            "UNICODE",
            [],
            new Dictionary<string, string> { ["value"] = "🦀" }));
        var text = BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(
            "TEXT",
            [],
            new Dictionary<string, string> { ["value"] = "hello 世界" }));

        Assert.Equal(
            [BehaviorAction.SendUnicode("🦀")],
            Runtime(new KeyId("A"), unicode).OnKeyDown(new KeyId("A"), 0).Actions);
        Assert.Equal(
            [BehaviorAction.SendText("hello 世界")],
            Runtime(new KeyId("B"), text).OnKeyDown(new KeyId("B"), 0).Actions);
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
