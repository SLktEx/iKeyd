using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;

internal static class IKeydDslDocumentParser
{
    public static IKeydDslDocument Parse(string text, string sourcePath = "<memory>")
    {
        var mouse = IKeydMouseDslParser.Extract(text, sourcePath);
        var targets = IKeydTargetExtensionParser.Extract(mouse.SourceWithoutMouse, sourcePath);
        var profile = IKeydDslParser.Parse(targets.SourceWithoutTargetBlocks, sourcePath);
        ValidateBehaviorInvocations(profile, sourcePath);
        return new IKeydDslDocument(profile, mouse.Profile, targets.Extensions);
    }

    private static void ValidateBehaviorInvocations(AutomationProfile profile, string sourcePath)
    {
        foreach (var keymap in profile.Keymaps.Values)
        {
            foreach (var mapping in keymap.BehaviorMappings)
            {
                try
                {
                    _ = BehaviorDefinitionFactory.Create(mapping.Invocation, profile.BehaviorDefinitions);
                }
                catch (Exception error) when (error is ArgumentException or InvalidDataException or NotSupportedException)
                {
                    throw new InvalidDataException(
                        $"{sourcePath}: invalid behavior '{mapping.Invocation.Name}' on {keymap.Name}.{mapping.Key}: {error.Message}",
                        error);
                }
            }
        }
    }
}

internal sealed record IKeydDslDocument(
    AutomationProfile Profile,
    MouseMotionProfile Mouse,
    IReadOnlyList<TargetExtensionIr> TargetExtensions);
