using iKeyd.Core.Chords;
using iKeyd.Core.Keymaps;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class KeymapTableTests
{
    [Fact]
    public void Compiled_tables_resolve_single_and_unordered_chord()
    {
        var singles = new KeymapSlot<string>[Keymap<string>.CompactSingleSlotCount];
        singles[Keymap<string>.GetCompactSingleIndex(KeyCode.Q)] = new KeymapSlot<string>("single-q");

        var chords = new KeymapSlot<string>[Keymap<string>.CompactChordSlotCount];
        chords[Keymap<string>.GetCompactChordIndex(KeyCode.Q, KeyCode.K)] = new KeymapSlot<string>("qk");

        var keymap = Keymap<string>.FromCompiledTables(singles, chords);

        Assert.True(keymap.TryGetSingle(new KeyId(KeyCode.Q), out var single));
        Assert.Equal("single-q", single);
        Assert.True(keymap.TryGetChord(new KeyId(KeyCode.Q), new KeyId(KeyCode.K), out var forward));
        Assert.True(keymap.TryGetChord(new KeyId(KeyCode.K), new KeyId(KeyCode.Q), out var reverse));
        Assert.Equal("qk", forward);
        Assert.Equal("qk", reverse);
    }

    [Fact]
    public void Jis_keys_are_indexed_by_compact_generated_tables()
    {
        var singles = new KeymapSlot<string>[Keymap<string>.CompactSingleSlotCount];
        singles[Keymap<string>.GetCompactSingleIndex(KeyCode.Ro)] = new KeymapSlot<string>("ro");

        var chords = new KeymapSlot<string>[Keymap<string>.CompactChordSlotCount];
        chords[Keymap<string>.GetCompactChordIndex(KeyCode.Muhenkan, KeyCode.Ro)] = new KeymapSlot<string>("escape");

        var keymap = Keymap<string>.FromCompiledTables(singles, chords);

        Assert.True(keymap.TryGetSingle(new KeyId(KeyCode.Ro), out var single));
        Assert.Equal("ro", single);
        Assert.True(keymap.TryGetChord(new KeyId(KeyCode.Ro), new KeyId(KeyCode.Muhenkan), out var chord));
        Assert.Equal("escape", chord);
    }

    [Fact]
    public void Generic_constructor_preserves_legacy_duplicate_semantics()
    {
        var keymap = new Keymap<string>(
            [
                new SingleMapping<string>(new KeyId(KeyCode.Q), "first"),
                new SingleMapping<string>(new KeyId(KeyCode.Q), "last")
            ],
            [
                new ChordMapping<string>(new KeyId(KeyCode.Q), new KeyId(KeyCode.K), "first"),
                new ChordMapping<string>(new KeyId(KeyCode.K), new KeyId(KeyCode.Q), "last")
            ]);

        Assert.True(keymap.TryGetSingle(new KeyId(KeyCode.Q), out var single));
        Assert.Equal("last", single);
        Assert.True(keymap.TryGetChord(new KeyId(KeyCode.Q), new KeyId(KeyCode.K), out var chord));
        Assert.Equal("first", chord);
    }

    [Fact]
    public void Custom_import_keys_use_compatibility_fallback()
    {
        var custom = new KeyId("AHK_CUSTOM_KEY");
        var keymap = new Keymap<string>(
            [new SingleMapping<string>(custom, "custom")],
            []);

        Assert.True(keymap.TryGetSingle(custom, out var output));
        Assert.Equal("custom", output);
    }
}
