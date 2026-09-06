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
}
