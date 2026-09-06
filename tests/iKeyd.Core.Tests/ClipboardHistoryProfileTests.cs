using System.Text.Json;
using iKeyd.Core.Configuration;
using Xunit;

namespace iKeyd.Core.Tests;

public sealed class ClipboardHistoryProfileTests
{
    private const string BaseProfile = """
    {
      "source": { "chordWindowMs": 40 },
      "singleStroke": { "S": {}, "K": {} },
      "chords": { "S": [], "K": [] }
    }
    """;

    [Fact]
    public void Missing_clipboard_section_keeps_current_runtime_defaults()
    {
        var profile = AutomationProfileJson.Parse(BaseProfile);

        Assert.True(profile.Clipboard.History);
        Assert.Equal(20, profile.Clipboard.MaxItems);
        Assert.True(profile.Clipboard.Persist);
        Assert.True(profile.Clipboard.Images);
        Assert.Equal("user", profile.Clipboard.Encryption);
        Assert.Equal("auto", profile.Clipboard.Cipher);
        Assert.Null(profile.Clipboard.Directory);
    }

    [Fact]
    public void Clipboard_section_is_parsed_and_round_trips()
    {
        const string json = """
        {
          "source": { "chordWindowMs": 40 },
          "clipboard": {
            "history": true,
            "maxItems": 96,
            "persist": false,
            "images": false,
            "encryption": "user",
            "cipher": "chacha20-poly1305",
            "directory": "%LOCALAPPDATA%\\iKeyd-custom"
          },
          "singleStroke": { "S": {}, "K": {} },
          "chords": { "S": [], "K": [] }
        }
        """;

        var profile = AutomationProfileJson.Parse(json);

        Assert.True(profile.Clipboard.History);
        Assert.Equal(96, profile.Clipboard.MaxItems);
        Assert.False(profile.Clipboard.Persist);
        Assert.False(profile.Clipboard.Images);
        Assert.Equal("user", profile.Clipboard.Encryption);
        Assert.Equal("chacha20-poly1305", profile.Clipboard.Cipher);
        Assert.Equal("%LOCALAPPDATA%\\iKeyd-custom", profile.Clipboard.Directory);

        using var document = JsonDocument.Parse(AutomationProfileJson.Serialize(profile));
        var clipboard = document.RootElement.GetProperty("clipboard");
        Assert.Equal(96, clipboard.GetProperty("maxItems").GetInt32());
        Assert.False(clipboard.GetProperty("persist").GetBoolean());
        Assert.False(clipboard.GetProperty("images").GetBoolean());
    }

    [Fact]
    public void Invalid_clipboard_cipher_is_rejected()
    {
        var json = BaseProfile.Replace(
            "\"singleStroke\"",
            "\"clipboard\": { \"cipher\": \"magic\" }, \"singleStroke\"");

        Assert.Throws<InvalidDataException>(() => AutomationProfileJson.Parse(json));
    }
}
