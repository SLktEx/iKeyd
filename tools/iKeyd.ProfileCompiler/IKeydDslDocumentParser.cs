using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;
using iKeyd.Core.State;

internal static class IKeydDslDocumentParser
{
    public static IKeydDslDocument Parse(string text, string sourcePath = "<memory>")
    {
        var state = IKeydStateDslParser.Extract(text, sourcePath);
        var mouse = IKeydMouseDslParser.Extract(state.SourceWithoutState, sourcePath);
        var targets = IKeydTargetExtensionParser.Extract(mouse.SourceWithoutMouse, sourcePath);
        var profile = IKeydDslParser.Parse(targets.SourceWithoutTargetBlocks, sourcePath, state.Profile);
        ValidateCompileTimeBehaviorInvocations(profile, sourcePath);
        return new IKeydDslDocument(profile, mouse.Profile, targets.Extensions);
    }

    private static void ValidateCompileTimeBehaviorInvocations(AutomationProfile profile, string sourcePath)
    {
        var runtimeState = profile.State.Count == 0
            ? EmptyRuntimeStateStore.Instance
            : new RuntimeStateStore(profile.State);

        foreach (var keymap in profile.Keymaps.Values)
        {
            foreach (var mapping in keymap.BehaviorMappings)
            {
                if (!RequiresCompileTimeValidation(mapping.Invocation.Name) &&
                    !profile.BehaviorDefinitions.ContainsKey(mapping.Invocation.Name))
                {
                    continue;
                }

                try
                {
                    _ = BehaviorDefinitionFactory.Create(
                        mapping.Invocation,
                        profile.BehaviorDefinitions,
                        EmptySystemQuerySnapshot.Instance,
                        profile.State,
                        runtimeState);
                    _ = BehaviorDefinitionFactory.GetRequiredSystemQueries(mapping.Invocation, profile.State);
                }
                catch (Exception error) when (error is ArgumentException or InvalidDataException or KeyNotFoundException or NotSupportedException)
                {
                    throw new InvalidDataException(
                        $"{sourcePath}: invalid behavior '{mapping.Invocation.Name}' on {keymap.Name}.{mapping.Key}: {error.Message}",
                        error);
                }
            }
        }
    }

    private static bool RequiresCompileTimeValidation(string name)
        => name.Equals("UNICODE", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TEXT", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("EXEC", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("SHELL", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("QUERY", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("WHEN", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("SET", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("TOGGLE", StringComparison.OrdinalIgnoreCase);
}

internal sealed record IKeydDslDocument(
    AutomationProfile Profile,
    MouseMotionProfile Mouse,
    IReadOnlyList<TargetExtensionIr> TargetExtensions);
