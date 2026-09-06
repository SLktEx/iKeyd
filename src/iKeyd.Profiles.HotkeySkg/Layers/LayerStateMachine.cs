namespace iKeyd.Profiles.HotkeySkg.Layers;

public enum LayerEvent
{
    MDown,
    MUp,
    HDown,
    HUp,
    AltHDown,
    AltHUp,
    SpaceDown,
    SpaceUp,
    AltSpaceDown,
    AltSpaceUp,
    KanaDown,
    AltKanaDown
}

public enum LayerAction
{
    Tab,
    ShiftTab,
    ShiftEnter,
    ShiftSpace,
    Ctrl,
    Space,
    Enter,
    CtrlSpace,
    CtrlEnter,
    AltEnter,
    AltSpace,
    CtrlEsc,
    Muhenkan,
    Henkan,
    EndEnter,
    UpEndEnter
}

public sealed record LayerRuntimeState(LayerState Layers, bool Consumed)
{
    public static LayerRuntimeState Empty { get; } = new(LayerState.Empty, false);
    public LayerRuntimeState MarkConsumed() => this with { Consumed = true };
}

public sealed record LayerTransition(LayerRuntimeState State, IReadOnlyList<LayerAction> Actions);

public static class LayerStateMachine
{
    public static LayerTransition Apply(LayerRuntimeState state, LayerEvent @event)
        => @event switch
        {
            LayerEvent.MDown => PressM(state),
            LayerEvent.MUp => ReleaseM(state),
            LayerEvent.HDown => PressH(state),
            LayerEvent.HUp => ReleaseH(state),
            LayerEvent.AltHDown => PressAltH(state),
            LayerEvent.AltHUp => ReleaseAltH(state),
            LayerEvent.SpaceDown => PressSpace(state),
            LayerEvent.SpaceUp => ReleaseSpace(state),
            LayerEvent.AltSpaceDown => PressAltSpace(state),
            LayerEvent.AltSpaceUp => ReleaseAltSpace(state),
            LayerEvent.KanaDown => PressKana(state),
            LayerEvent.AltKanaDown => PressAltKana(state),
            _ => throw new ArgumentOutOfRangeException(nameof(@event))
        };

    private static LayerTransition PressM(LayerRuntimeState state)
        => Result(state with { Layers = state.Layers.Press(LayerKey.M), Consumed = false });

    private static LayerTransition ReleaseM(LayerRuntimeState state)
    {
        var actions = new List<LayerAction>();
        var consumed = state.Consumed;

        if (!consumed && state.Layers.IsExact(LayerKey.H, LayerKey.M))
        {
            actions.Add(LayerAction.ShiftTab);
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.S, LayerKey.M))
        {
            actions.Add(LayerAction.ShiftEnter);
            consumed = true;
        }

        return Result(
            state with
            {
                Layers = state.Layers.Release(LayerKey.K, LayerKey.A, LayerKey.M),
                Consumed = consumed
            },
            actions);
    }

    private static LayerTransition PressH(LayerRuntimeState state)
    {
        var actions = new List<LayerAction>();
        var consumed = false;

        if (state.Layers.IsExact(LayerKey.M, LayerKey.S))
        {
            actions.Add(LayerAction.ShiftSpace);
            consumed = true;
        }

        return Result(state with { Layers = state.Layers.Press(LayerKey.H), Consumed = consumed }, actions);
    }

    private static LayerTransition ReleaseH(LayerRuntimeState state)
    {
        var actions = new List<LayerAction>();
        var consumed = state.Consumed;

        if (!consumed && state.Layers.IsExact(LayerKey.M, LayerKey.H))
        {
            actions.Add(LayerAction.Tab);
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.H))
        {
            actions.Add(LayerAction.Ctrl);
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.K, LayerKey.H))
        {
            actions.Add(LayerAction.Henkan);
            consumed = true;
        }

        return Result(
            state with
            {
                Layers = state.Layers.Release(LayerKey.K, LayerKey.A, LayerKey.H),
                Consumed = consumed
            },
            actions);
    }

    private static LayerTransition PressAltH(LayerRuntimeState state)
        => Result(state with { Layers = state.Layers.Press(LayerKey.A).Press(LayerKey.H) });

    private static LayerTransition ReleaseAltH(LayerRuntimeState state)
    {
        var layers = state.Layers.IsExact(LayerKey.A, LayerKey.H)
            ? LayerState.Empty
            : state.Layers.Release(LayerKey.H);
        return Result(state with { Layers = layers });
    }

    private static LayerTransition PressSpace(LayerRuntimeState state)
    {
        var actions = new List<LayerAction>();
        var consumed = false;

        if (state.Layers.IsExact(LayerKey.M, LayerKey.H))
        {
            actions.Add(LayerAction.EndEnter);
            consumed = true;
        }
        else if (state.Layers.IsExact(LayerKey.H, LayerKey.M))
        {
            actions.Add(LayerAction.UpEndEnter);
            consumed = true;
        }

        return Result(state with { Layers = state.Layers.Press(LayerKey.S), Consumed = consumed }, actions);
    }

    private static LayerTransition ReleaseSpace(LayerRuntimeState state)
    {
        var actions = new List<LayerAction>();
        var consumed = state.Consumed;

        if (!consumed && state.Layers.IsExact(LayerKey.S))
            actions.Add(LayerAction.Space);
        else if (!consumed && state.Layers.IsExact(LayerKey.M, LayerKey.S))
        {
            actions.Add(LayerAction.Enter);
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.H, LayerKey.S))
        {
            actions.Add(LayerAction.CtrlSpace);
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.K, LayerKey.S))
        {
            actions.Add(LayerAction.ShiftSpace);
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.K, LayerKey.M, LayerKey.S))
        {
            actions.Add(LayerAction.CtrlEnter);
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.A, LayerKey.M, LayerKey.S))
        {
            actions.Add(LayerAction.AltEnter);
            consumed = true;
        }

        return Result(
            state with
            {
                Layers = state.Layers.Release(LayerKey.K, LayerKey.A, LayerKey.S),
                Consumed = consumed
            },
            actions);
    }

    private static LayerTransition PressAltSpace(LayerRuntimeState state)
        => Result(state with { Layers = state.Layers.Press(LayerKey.A).Press(LayerKey.S) });

    private static LayerTransition ReleaseAltSpace(LayerRuntimeState state)
    {
        var actions = new List<LayerAction>();
        var consumed = state.Consumed;

        if (!consumed && state.Layers.IsExact(LayerKey.A, LayerKey.M, LayerKey.S))
        {
            actions.Add(LayerAction.AltSpace);
            consumed = true;
        }

        var layers = state.Layers.IsExact(LayerKey.A, LayerKey.S)
            ? LayerState.Empty
            : state.Layers.Release(LayerKey.S);

        return Result(state with { Layers = layers, Consumed = consumed }, actions);
    }

    private static LayerTransition PressKana(LayerRuntimeState state)
    {
        if (state.Layers.IsExact(LayerKey.M))
            return Result(state.MarkConsumed(), [LayerAction.Muhenkan]);
        if (state.Layers.IsExact(LayerKey.H))
            return Result(state.MarkConsumed(), [LayerAction.Henkan]);
        if (state.Layers.IsExact(LayerKey.S))
            // The pinned compiled EXE leaves the Space layer unconsumed here, so
            // releasing Space emits a normal Space after Ctrl+Esc. The original
            // AHK source differs; that divergence is recorded in the scenario.
            return Result(state, [LayerAction.CtrlEsc]);
        if (!state.Layers.Contains(LayerKey.K))
            return Result(state with { Layers = state.Layers.Press(LayerKey.K) });
        if (state.Layers.IsExact(LayerKey.K))
            return Result(state with { Layers = LayerState.Empty });
        return Result(state);
    }

    private static LayerTransition PressAltKana(LayerRuntimeState state)
    {
        // AutoHotkey v1 emits its default Ctrl menu-mask tap for an Alt hotkey
        // before entering the !sc070 handler. Preserve that externally visible
        // behavior so releasing physical Alt cannot spuriously activate menus.
        return Result(
            state with { Layers = state.Layers.Press(LayerKey.A) },
            [LayerAction.Ctrl]);
    }

    private static LayerTransition Result(LayerRuntimeState state, IReadOnlyList<LayerAction>? actions = null)
        => new(state, actions ?? []);
}
