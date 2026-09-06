using iKeyd.Core.Chords;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class KeyIdTests
{
    [Theory]
    [InlineData("a", KeyCode.A, "A")]
    [InlineData("0", KeyCode.Digit0, "0")]
    [InlineData("f12", KeyCode.F12, "F12")]
    [InlineData("scolon", KeyCode.SColon, "SCOLON")]
    [InlineData("Comma", KeyCode.Comma, "COMMA")]
    [InlineData("AT", KeyCode.At, "AT")]
    [InlineData("ro", KeyCode.Ro, "RO")]
    [InlineData("int1", KeyCode.Ro, "RO")]
    [InlineData("yen", KeyCode.Yen, "YEN")]
    [InlineData("int3", KeyCode.Yen, "YEN")]
    [InlineData("henkan", KeyCode.Henkan, "HENKAN")]
    [InlineData("int4", KeyCode.Henkan, "HENKAN")]
    [InlineData("muhenkan", KeyCode.Muhenkan, "MUHENKAN")]
    [InlineData("int5", KeyCode.Muhenkan, "MUHENKAN")]
    [InlineData("kana", KeyCode.KatakanaHiragana, "KATAKANAHIRAGANA")]
    [InlineData("lang5", KeyCode.ZenkakuHankaku, "ZENKAKUHANKAKU")]
    [InlineData("rctrl", KeyCode.RightControl, "RCONTROL")]
    [InlineData("numpad7", KeyCode.Numpad7, "NUMPAD7")]
    [InlineData("kp_comma", KeyCode.NumpadComma, "NUMPADCOMMA")]
    public void Known_names_are_compact_numeric_ids(string input, KeyCode expectedCode, string expectedName)
    {
        var key = new KeyId(input);

        Assert.True(key.IsCompact);
        Assert.Equal(expectedCode, key.Code);
        Assert.Equal(expectedName, key.Value);
        Assert.Equal(new KeyId(expectedCode), key);
    }

    [Fact]
    public void Migration_only_unknown_names_remain_supported()
    {
        var key = new KeyId(" custom_key ");

        Assert.False(key.IsCompact);
        Assert.Equal(KeyCode.Custom, key.Code);
        Assert.Equal("CUSTOM_KEY", key.Value);
    }

    [Theory]
    [InlineData('q', KeyCode.Q)]
    [InlineData('Z', KeyCode.Z)]
    [InlineData('7', KeyCode.Digit7)]
    public void Character_conversion_uses_compact_ids(char input, KeyCode expected)
    {
        Assert.True(KeyId.TryFromCharacter(input, out var key));
        Assert.Equal(expected, key.Code);
    }

    [Fact]
    public void Chord_key_is_order_independent_for_compact_ids()
    {
        Assert.Equal(
            new ChordKey(new KeyId(KeyCode.Q), new KeyId(KeyCode.K)),
            new ChordKey(new KeyId(KeyCode.K), new KeyId(KeyCode.Q)));
    }

    [Fact]
    public void Jis_specific_chords_stay_on_compact_path()
    {
        var chord = new ChordKey(new KeyId("Muhenkan"), new KeyId("Ro"));

        Assert.True(chord.First.IsCompact);
        Assert.True(chord.Second.IsCompact);
    }
}
