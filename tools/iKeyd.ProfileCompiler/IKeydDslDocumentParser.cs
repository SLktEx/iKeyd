using iKeyd.Core.Configuration;

internal static class IKeydDslDocumentParser
{
    public static IKeydDslDocument Parse(string text, string sourcePath = "<memory>")
    {
        var mouse = IKeydMouseDslParser.Extract(text, sourcePath);
        var profile = IKeydDslParser.Parse(mouse.SourceWithoutMouse, sourcePath);
        return new IKeydDslDocument(profile, mouse.Profile);
    }
}

internal sealed record IKeydDslDocument(
    AutomationProfile Profile,
    MouseMotionProfile Mouse);
