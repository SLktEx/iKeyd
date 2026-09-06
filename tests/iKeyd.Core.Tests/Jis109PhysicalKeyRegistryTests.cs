using iKeyd.Core.Chords;
using iKeyd.Core.Input;
using iKeyd.Core.Keymaps;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class Jis109PhysicalKeyRegistryTests
{
    [Fact]
    public void Registry_contains_exactly_109_unique_compact_physical_keys()
    {
        Assert.Equal(109, Jis109PhysicalKeyRegistry.Keys.Count);
        Assert.Equal(109, Jis109PhysicalKeyRegistry.Keys.Select(item => item.Code).Distinct().Count());

        foreach (var key in Jis109PhysicalKeyRegistry.Keys)
        {
            var id = new KeyId(key.Code);
            Assert.True(id.IsCompact);
            Assert.True(KeyId.TryParseCompact(id.Value, out var reparsed));
            Assert.Equal(key.Code, reparsed);
            Assert.InRange(Keymap<string>.GetCompactSingleIndex(key.Code), 0, Keymap<string>.CompactSingleSlotCount - 1);
        }
    }

    [Theory]
    [InlineData("SCOLON", KeyCode.SColon)]
    [InlineData("COLON", KeyCode.Colon)]
    [InlineData("YEN", KeyCode.Yen)]
    [InlineData("RO", KeyCode.Ro)]
    [InlineData("MUHENKAN", KeyCode.NonConvert)]
    [InlineData("HENKAN", KeyCode.Convert)]
    [InlineData("HIRAGANA", KeyCode.Kana)]
    [InlineData("HANKAKUZENKAKU", KeyCode.HankakuZenkaku)]
    [InlineData("RCTRL", KeyCode.RightCtrl)]
    [InlineData("RALT", KeyCode.RightAlt)]
    [InlineData("RWIN", KeyCode.RightWin)]
    [InlineData("NUMPADENTER", KeyCode.NumpadEnter)]
    public void Canonical_DSL_names_and_aliases_resolve_to_physical_ids(string name, KeyCode expected)
    {
        Assert.True(KeyId.TryParseCompact(name, out var actual));
        Assert.Equal(expected, actual);
    }
}
