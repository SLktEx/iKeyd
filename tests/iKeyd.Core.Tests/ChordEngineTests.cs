using System.Text.Json;
using iKeyd.Core.Chords;
using iKeyd.Core.Keymaps;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ChordEngineTests
{
    [Fact]
    public void First_key_remains_pending_through_the_inclusive_40ms_boundary()
    {
        var engine = CreateSimpleEngine();

        Assert.Empty(engine.OnKeyDown("Q", 0));
        Assert.Equal(ChordEngineState.PendingSingle, engine.State);
        Assert.Empty(engine.AdvanceTo(40));
        Assert.Equal(new KeyId("Q"), engine.PendingKeyId);

        Assert.Equal(["-"], engine.AdvanceTo(41));
        Assert.Equal(ChordEngineState.Idle, engine.State);
    }

    [Theory]
    [InlineData(39)]
    [InlineData(40)]
    public void Defined_chord_resolves_inside_or_at_the_boundary(int deltaMs)
    {
        var engine = CreateSimpleEngine();

        Assert.Empty(engine.OnKeyDown("K", 0));
        Assert.Equal(["fa"], engine.OnKeyDown("Q", deltaMs));
        Assert.Equal(ChordEngineState.Idle, engine.State);
    }

    [Fact]
    public void At_41ms_the_first_key_becomes_single_and_second_key_becomes_pending()
    {
        var engine = CreateSimpleEngine();

        Assert.Empty(engine.OnKeyDown("Q", 0));
        Assert.Equal(["-"], engine.OnKeyDown("K", 41));
        Assert.Equal(new KeyId("K"), engine.PendingKeyId);
        Assert.Equal(["i"], engine.AdvanceTo(82));
        Assert.Equal(ChordEngineState.Idle, engine.State);
    }

    [Fact]
    public void Undefined_chord_falls_back_to_first_single_and_keeps_second_pending()
    {
        var engine = CreateSimpleEngine();

        Assert.Empty(engine.OnKeyDown("Q", 0));
        Assert.Equal(["-"], engine.OnKeyDown("W", 10));
        Assert.Equal(new KeyId("W"), engine.PendingKeyId);
        Assert.Equal(["ni"], engine.Flush());
    }

    [Fact]
    public void Chord_lookup_is_order_independent()
    {
        var forward = CreateSimpleEngine();
        var reverse = CreateSimpleEngine();

        forward.OnKeyDown("K", 0);
        reverse.OnKeyDown("Q", 0);

        Assert.Equal(["fa"], forward.OnKeyDown("Q", 20));
        Assert.Equal(["fa"], reverse.OnKeyDown("K", 20));
    }

    [Fact]
    public void Duplicate_chord_declarations_preserve_legacy_first_match_behavior()
    {
        var keymap = new Keymap<string>(
            [new("SColon", "na"), new("V", "ru")],
            [new("SColon", "V", "nya"), new("V", "SColon", "pya")]);
        var engine = new ChordEngine<string>(keymap);

        engine.OnKeyDown("SColon", 0);
        Assert.Equal(["nya"], engine.OnKeyDown("V", 10));
    }

    [Fact]
    public void Duplicate_single_declarations_preserve_AHK_last_assignment_behavior()
    {
        var keymap = new Keymap<string>(
            [new("Q", "old"), new("Q", "new")],
            []);
        var engine = new ChordEngine<string>(keymap);

        engine.OnKeyDown("Q", 0);
        Assert.Equal(["new"], engine.Flush());
    }

    [Fact]
    public void Missing_single_mapping_emits_nothing()
    {
        var keymap = new Keymap<string>([], []);
        var engine = new ChordEngine<string>(keymap);

        engine.OnKeyDown("Q", 0);
        Assert.Empty(engine.Flush());
        Assert.Equal(ChordEngineState.Idle, engine.State);
    }

    [Fact]
    public void Cancel_discards_a_pending_key_without_output()
    {
        var engine = CreateSimpleEngine();

        engine.OnKeyDown("Q", 0);
        engine.Cancel();

        Assert.Equal(ChordEngineState.Idle, engine.State);
        Assert.Empty(engine.Flush());
    }

    [Fact]
    public void Non_monotonic_timestamps_are_rejected()
    {
        var engine = CreateSimpleEngine();
        engine.OnKeyDown("Q", 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => engine.OnKeyDown("K", 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.AdvanceTo(9));
    }

    [Theory]
    [InlineData("S", "K", "Q", "fa")]
    [InlineData("K", "K", "Q", "ti")]
    public void Legacy_fixture_can_drive_the_real_core(string mode, string first, string second, string expected)
    {
        var keymap = LoadLegacyKeymap(mode);
        var engine = new ChordEngine<string>(keymap);

        Assert.Empty(engine.OnKeyDown(first, 0));
        Assert.Equal([expected], engine.OnKeyDown(second, 40));
    }

    private static ChordEngine<string> CreateSimpleEngine()
    {
        var keymap = new Keymap<string>(
            [
                new("Q", "-"),
                new("W", "ni"),
                new("K", "i")
            ],
            [new("K", "Q", "fa")]);

        return new ChordEngine<string>(keymap);
    }

    private static Keymap<string> LoadLegacyKeymap(string mode)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hotkeySKG.behavior.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        var singles = root.GetProperty("singleStroke")
            .GetProperty(mode)
            .EnumerateObject()
            .Select(x => new SingleMapping<string>(x.Name, x.Value.GetString() ?? string.Empty))
            .ToArray();

        var chords = root.GetProperty("chords")
            .GetProperty(mode)
            .EnumerateArray()
            .Select(x => new ChordMapping<string>(
                x[0].GetString() ?? throw new InvalidDataException(),
                x[1].GetString() ?? throw new InvalidDataException(),
                x[2].GetString() ?? string.Empty))
            .ToArray();

        return new Keymap<string>(singles, chords);
    }
}
