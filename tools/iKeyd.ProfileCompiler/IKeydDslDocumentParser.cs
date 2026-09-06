using iKeyd.Core.Automation;
using iKeyd.Core.Behaviors;
using iKeyd.Core.Configuration;
using iKeyd.Core.State;

internal static class IKeydDslDocumentParser
{
    public static IKeydDslDocument Parse(string text, string sourcePath = "<memory>")
    {
        var state = IKeydStateDslParser.Extract(text, sourcePath);
        var stateBehaviorSyntax = IKeydSharedStateBehaviorSyntax.Rewrite(
            state.SourceWithoutState,
            state.Profile,
            sourcePath);
        var mouse = IKeydMouseDslParser.Extract(stateBehaviorSyntax.Source, sourcePath);
        var targets = IKeydTargetExtensionParser.Extract(mouse.SourceWithoutMouse, sourcePath);
        var parsed = IKeydDslParser.Parse(targets.SourceWithoutTargetBlocks, sourcePath);
        var behaviorDefinitions = stateBehaviorSyntax.Apply(parsed.BehaviorDefinitions.Values);
        var profile = new AutomationProfile(
            parsed.ChordWindowMs,
            parsed.Keymaps.Values,
            parsed.StartupMode,
            parsed.Hotkeys,
            behaviorDefinitions,
            parsed.Clipboard,
            state.Profile);
        ValidateCompileTimeBehaviorInvocations(profile, sourcePath);
        return new IKeydDslDocument(profile, mouse.Profile, targets.Extensions);
    }

    private static void ValidateCompileTimeBehaviorInvocations(AutomationProfile profile, string sourcePath)
    {
        IRuntimeStateStore runtimeState = profile.State.Count == 0
            ? EmptyRuntimeStateStore.Instance
            : new RuntimeStateStore(profile.State);

        foreach (var keymap in profile.Keymaps.Values)
        {
            foreach (var mapping in keymap.BehaviorMappings)
            {
                // User-defined behavior invocation options have an older compatibility
                // surface and are not globally revalidated here. Shared-state syntax
                // inside their definitions is already typed/validated by the state
                // lowering pass. Keep this pass scoped to first-class helpers.
                if (!RequiresCompileTimeValidation(mapping.Invocation.Name))
                    continue;

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
