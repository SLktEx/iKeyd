using iKeyd.Core.Configuration;

internal static class IKeydDslDocumentParser
{
    public static IKeydDslDocument Parse(string text, string sourcePath = "<memory>")
    {
        var mouse = IKeydMouseDslParser.Extract(text, sourcePath);
        var targets = IKeydTargetExtensionParser.Extract(mouse.SourceWithoutMouse, sourcePath);
        var profile = IKeydDslParser.Parse(targets.SourceWithoutTargetBlocks, sourcePath);
        return new IKeydDslDocument(profile, mouse.Profile, targets.Extensions);
    }
}

internal sealed record IKeydDslDocument(
    AutomationProfile Profile,
    MouseMotionProfile Mouse,
    IReadOnlyList<TargetExtensionIr> TargetExtensions);
