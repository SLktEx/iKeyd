using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;

internal static class IKeydDslDocumentParser
{
    public static IKeydDslDocument Parse(string text, string sourcePath = "<memory>")
    {
        var mouse = IKeydMouseDslParser.Extract(text, sourcePath);
        var targets = IKeydTargetExtensionParser.Extract(mouse.SourceWithoutMouse, sourcePath);
        var profile = IKeydDslParser.Parse(targets.SourceWithoutTargetBlocks, sourcePath);
        ValidateLiteralOutputInvocations(profile, sourcePath);
        return new IKeydDslDocument(profile, mouse.Profile, targets.Extensions);
    }

    private static void ValidateLiteralOutputInvocations(AutomationProfile profile, string sourcePath)
    {
        foreach (var keymap in profile.Keymaps.Values)
        {
            foreach (var mapping in keymap.BehaviorMappings)
            {
                if (!IsLiteralOutputHelper(mapping.Invocation.Name))
                    continue;

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

    private static bool IsLiteralOutputHelper(string name)
        => name.Equals("UNICODE", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TEXT", StringComparison.OrdinalIgnoreCase);
}

internal sealed record IKeydDslDocument(
    AutomationProfile Profile,
    MouseMotionProfile Mouse,
    IReadOnlyList<TargetExtensionIr> TargetExtensions);
