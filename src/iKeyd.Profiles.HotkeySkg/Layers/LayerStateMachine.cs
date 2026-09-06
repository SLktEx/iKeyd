using System.Collections;

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

public readonly record struct LayerRuntimeState(LayerState Layers, bool Consumed)
{
    public static LayerRuntimeState Empty => new(LayerState.Empty, false);
    public LayerRuntimeState MarkConsumed() => this with { Consumed = true };
}

/// <summary>
/// A zero-or-one action collection. Layer transitions in the hotkeySKG state
/// machine never emit more than one logical action per input event.
/// </summary>
public readonly struct LayerActionList : IReadOnlyList<LayerAction>
{
    private readonly LayerAction _action;

    public LayerActionList(LayerAction action)
    {
        _action = action;
        Count = 1;
    }

    public int Count { get; }

    public LayerAction this[int index]
        => Count == 1 && index == 0
            ? _action
            : throw new ArgumentOutOfRangeException(nameof(index));

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<LayerAction> IEnumerable<LayerAction>.GetEnumerator() => EnumerateBoxed();
    IEnumerator IEnumerable.GetEnumerator() => EnumerateBoxed();

    private IEnumerator<LayerAction> EnumerateBoxed()
    {
        if (Count == 1)
            yield return _action;
    }

    public struct Enumerator
    {
        private readonly LayerActionList _list;
        private bool _moved;

        internal Enumerator(LayerActionList list)
        {
            _list = list;
            _moved = false;
        }

        public LayerAction Current => _list._action;

        public bool MoveNext()
        {
            if (_moved || _list.Count == 0)
                return false;
            _moved = true;
            return true;
        }
    }
}

public readonly record struct LayerTransition(LayerRuntimeState State, LayerActionList Actions);

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
        var consumed = state.Consumed;
        LayerAction? action = null;

        if (!consumed && state.Layers.IsExact(LayerKey.H, LayerKey.M))
        {
            action = LayerAction.ShiftTab;
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.S, LayerKey.M))
        {
            action = LayerAction.ShiftEnter;
            consumed = true;
        }

        return Result(
            state with
            {
                Layers = state.Layers.Release(LayerKey.K, LayerKey.A, LayerKey.M),
                Consumed = consumed
            },
            action);
    }

    private static LayerTransition PressH(LayerRuntimeState state)
    {
        LayerAction? action = null;
        var consumed = false;

        if (state.Layers.IsExact(LayerKey.M, LayerKey.S))
        {
            action = LayerAction.ShiftSpace;
            consumed = true;
        }

        return Result(state with { Layers = state.Layers.Press(LayerKey.H), Consumed = consumed }, action);
    }

    private static LayerTransition ReleaseH(LayerRuntimeState state)
    {
        var consumed = state.Consumed;
        LayerAction? action = null;

        if (!consumed && state.Layers.IsExact(LayerKey.M, LayerKey.H))
        {
            action = LayerAction.Tab;
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.H))
        {
            action = LayerAction.Ctrl;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.K, LayerKey.H))
        {
            action = LayerAction.Henkan;
            consumed = true;
        }

        return Result(
            state with
            {
                Layers = state.Layers.Release(LayerKey.K, LayerKey.A, LayerKey.H),
                Consumed = consumed
            },
            action);
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
        LayerAction? action = null;
        var consumed = false;

        if (state.Layers.IsExact(LayerKey.M, LayerKey.H))
        {
            action = LayerAction.EndEnter;
            consumed = true;
        }
        else if (state.Layers.IsExact(LayerKey.H, LayerKey.M))
        {
            action = LayerAction.UpEndEnter;
            consumed = true;
        }

        return Result(state with { Layers = state.Layers.Press(LayerKey.S), Consumed = consumed }, action);
    }

    private static LayerTransition ReleaseSpace(LayerRuntimeState state)
    {
        var consumed = state.Consumed;
        LayerAction? action = null;

        if (!consumed && state.Layers.IsExact(LayerKey.S))
            action = LayerAction.Space;
        else if (!consumed && state.Layers.IsExact(LayerKey.M, LayerKey.S))
        {
            action = LayerAction.Enter;
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.H, LayerKey.S))
        {
            action = LayerAction.CtrlSpace;
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.K, LayerKey.S))
        {
            action = LayerAction.ShiftSpace;
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.K, LayerKey.M, LayerKey.S))
        {
            action = LayerAction.CtrlEnter;
            consumed = true;
        }
        else if (!consumed && state.Layers.IsExact(LayerKey.A, LayerKey.M, LayerKey.S))
        {
            action = LayerAction.AltEnter;
            consumed = true;
        }

        return Result(
            state with
            {
                Layers = state.Layers.Release(LayerKey.K, LayerKey.A, LayerKey.S),
                Consumed = consumed
            },
            action);
    }

    private static LayerTransition PressAltSpace(LayerRuntimeState state)
        => Result(state with { Layers = state.Layers.Press(LayerKey.A).Press(LayerKey.S) });

    private static LayerTransition ReleaseAltSpace(LayerRuntimeState state)
    {
        var consumed = state.Consumed;
        LayerAction? action = null;

        if (!consumed && state.Layers.IsExact(LayerKey.A, LayerKey.M, LayerKey.S))
        {
            action = LayerAction.AltSpace;
            consumed = true;
        }

        var layers = state.Layers.IsExact(LayerKey.A, LayerKey.S)
            ? LayerState.Empty
            : state.Layers.Release(LayerKey.S);

        return Result(state with { Layers = layers, Consumed = consumed }, action);
    }

    private static LayerTransition PressKana(LayerRuntimeState state)
    {
        if (state.Layers.IsExact(LayerKey.M))
            return Result(state.MarkConsumed(), LayerAction.Muhenkan);
        if (state.Layers.IsExact(LayerKey.H))
            return Result(state.MarkConsumed(), LayerAction.Henkan);
        if (state.Layers.IsExact(LayerKey.S))
        {
            // The pinned compiled hotkeySKG.exe emits Ctrl+Esc without consuming
            // the pending Space tap. The original AHK source sets the consumed
            // flag instead; the differential scenario records that divergence.
            return Result(state, LayerAction.CtrlEsc);
        }
        if (!state.Layers.Contains(LayerKey.K))
            return Result(state with { Layers = state.Layers.Press(LayerKey.K) });
        if (state.Layers.IsExact(LayerKey.K))
            return Result(state with { Layers = LayerState.Empty });
        return Result(state);
    }

    private static LayerTransition PressAltKana(LayerRuntimeState state)
        => Result(state with { Layers = state.Layers.Press(LayerKey.A) });

    private static LayerTransition Result(LayerRuntimeState state, LayerAction? action = null)
        => new(state, action is { } value ? new LayerActionList(value) : default);
}
