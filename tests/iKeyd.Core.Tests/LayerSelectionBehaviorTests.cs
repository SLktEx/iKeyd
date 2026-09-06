using iKeyd.Core.Behaviors;
using iKeyd.Core.Chords;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class LayerSelectionBehaviorTests
{
    [Fact]
    public void TG_emits_one_non_repeating_persistent_toggle()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.TG("NAV"));

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 5);
        var up = runtime.OnKeyUp(source, 10);

        Assert.True(down.Suppress);
        Assert.Equal([BehaviorAction.LayerToggle("NAV")], down.Actions);
        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
        Assert.True(up.Suppress);
        Assert.Empty(up.Actions);
    }

    [Fact]
    public void TO_emits_one_non_repeating_persistent_layer_set()
    {
        var source = new KeyId("A");
        var runtime = Runtime(source, StandardBehaviors.TO("NUM"));

        var down = runtime.OnKeyDown(source, 0);
        var repeat = runtime.OnKeyDown(source, 5);
        var up = runtime.OnKeyUp(source, 10);

        Assert.True(down.Suppress);
        Assert.Equal([BehaviorAction.LayerSet("NUM")], down.Actions);
        Assert.True(repeat.Suppress);
        Assert.Empty(repeat.Actions);
        Assert.True(up.Suppress);
        Assert.Empty(up.Actions);
    }

    [Theory]
    [InlineData("TG", "NAV")]
    [InlineData("TO", "NUM")]
    public void Factory_builds_persistent_layer_helpers(string name, string layer)
    {
        var definition = BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(name, [layer]));
        Assert.NotNull(definition);
    }

    [Theory]
    [InlineData("TG")]
    [InlineData("TO")]
    public void Factory_rejects_invalid_persistent_layer_helper_shapes(string name)
    {
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(name, [])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(name, ["NAV", "NUM"])));
        Assert.Throws<InvalidDataException>(() =>
            BehaviorDefinitionFactory.Create(new BehaviorInvocationProfile(
                name,
                ["NAV"],
                new Dictionary<string, string> { ["unexpected"] = "true" })));
    }

    private static BehaviorRuntime Runtime(KeyId source, BehaviorDefinition definition)
        => new(new Dictionary<KeyId, BehaviorDefinition> { [source] = definition });
}
