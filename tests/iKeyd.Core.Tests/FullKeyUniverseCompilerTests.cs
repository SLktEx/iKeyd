using iKeyd.Core.Chords;
using iKeyd.Core.Input;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class FullKeyUniverseCompilerTests
{
    [Fact]
    public void Typed_compiler_preserves_full_physical_key_ids_in_static_profile()
    {
        const string source = """
            profile full_keys {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
                SPACE = " "
                NONCONVERT = "muhenkan"
                RCTRL = "right-control"
                RWIN = "right-win"
                NUMPADENTER = "numpad-enter"
            }

            keymap K {
                HANKAKUZENKAKU = "toggle-ime"
                RO = "ro"
            }
            """;

        var profile = IKeydDslParser.Parse(source, "full-keys.ikeyd");
        var generated = TypedProfileCompiler.Compile(profile);

        Assert.Contains("KeyCode.Space", generated, StringComparison.Ordinal);
        Assert.Contains("KeyCode.NonConvert", generated, StringComparison.Ordinal);
        Assert.Contains("KeyCode.RightCtrl", generated, StringComparison.Ordinal);
        Assert.Contains("KeyCode.RightWin", generated, StringComparison.Ordinal);
        Assert.Contains("KeyCode.NumpadEnter", generated, StringComparison.Ordinal);
        Assert.Contains("KeyCode.HankakuZenkaku", generated, StringComparison.Ordinal);
        Assert.Contains("KeyCode.Ro", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Jis109_physical_key_compiles_from_canonical_DSL_name()
    {
        var mappings = string.Join(
            Environment.NewLine,
            Jis109PhysicalKeyRegistry.Keys.Select(item => $"        {new KeyId(item.Code).Value} = \"mapped\""));
        var source = $$"""
            profile jis109 {
                chord_window = 40ms
                startup_mode = S
            }

            keymap S {
            {{mappings}}
            }

            keymap K {
                A = "a"
            }
            """;

        var profile = IKeydDslParser.Parse(source, "jis109.ikeyd");
        var generated = TypedProfileCompiler.Compile(profile);

        foreach (var physicalKey in Jis109PhysicalKeyRegistry.Keys)
            Assert.Contains($"KeyCode.{physicalKey.Code}", generated, StringComparison.Ordinal);
    }
}
