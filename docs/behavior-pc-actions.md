# PC actions in the Behavior DSL

This document covers the PC-oriented standard behaviors layered on top of the generic iKeyd Behavior runtime.

`LT`, `MT`, per-instance tap/hold options, and user-defined `behavior` blocks are documented separately in `ikeyd-dsl.md` and `behavior-dsl.md`. PC actions use the same `BehaviorDefinition` / `BehaviorAction` runtime rather than a second execution engine.

## Momentary layer and modifier helpers

`MO(layer)` activates a named keymap layer immediately on key down and releases it on key up or cancellation:

```ikeyd
keymap S {
    Space = MO(NAV)
}

keymap NAV {
    H = "left"
    J = "down"
    K = "up"
    L = "right"
}
```

`MOD(modifier)` owns an OS modifier for the physical hold duration:

```ikeyd
keymap S {
    Muhenkan = MOD(Ctrl)
}
```

The first supported modifier names are `Ctrl` / `Control`, `Shift`, `Alt`, and `Gui` / `Win` / `Super`.

## One-shot PC actions

The following behavior helpers emit one primitive action on physical key down. Auto-repeat does not re-run the action until the key is released and pressed again.

```ikeyd
keymap S {
    J = MOUSE_CLICK(Left)
    K = SCROLL(Down)
    L = MEDIA(PlayPause)
    U = WINDOW(LeftHalf)
    O = CLIPBOARD(History)
}
```

Available values in this slice:

- `MOUSE_CLICK(Left|Right|Middle)`
- `SCROLL(Up|Down)`; the IR also accepts an explicit wheel delta
- `MEDIA(VolumeUp|VolumeMute|VolumeDown|NextTrack|PreviousTrack|PlayPause)`
- `WINDOW(Minimize|ToggleMaximize|LeftHalf|RightHalf|TopHalf|BottomHalf|ToggleTopMost|OpacityUp|OpacityDown|ToggleCaption|ActivateBottomSameClass)`
- `CLIPBOARD(History|Capture|Paste)`

## Payload actions

The current authoring parser intentionally keeps behavior invocation arguments identifier-like. Payloads that need signed numbers, whitespace, commas, or literal punctuation use the existing per-invocation option-block syntax.

Mouse movement:

```ikeyd
keymap S {
    H = MOUSE_MOVE() {
        x = -30
        y = 10
    }
}
```

Literal text:

```ikeyd
keymap S {
    I = TEXT() {
        value = "^+{}"
    }
}
```

`TEXT` is literal output. It does not interpret AutoHotkey `Send` prefixes or brace syntax.

Macro template:

```ikeyd
keymap S {
    P = MACRO() {
        template = "hello, world"
    }
}
```

The profile IR may also contain direct arguments for these helpers. Option blocks are the preferred human-authored form until richer inline argument syntax is added as syntax sugar.

## Runtime boundaries

Synchronous desktop primitives (`MOUSE_*`, `SCROLL`, `MEDIA`, `WINDOW`) execute through the existing platform desktop backend.

Clipboard UI and macro execution are different: they may show UI or perform asynchronous work, so the Windows keyboard hook only posts a `BehaviorAction` to a host-action capability boundary. The WinForms application context marshals that work to its UI thread. This keeps blocking UI and macro execution out of the keyboard-hook path.

Conceptually:

```text
physical key
  -> BehaviorRuntime
  -> primitive BehaviorAction
       -> keyboard/desktop backend   (synchronous primitives)
       -> host-action sink           (clipboard / macro)
            -> WinForms UI thread
```

## Compatibility with user-defined behaviors

These helpers are standard-library names lowered by `BehaviorDefinitionFactory`. The Windows router switches only on primitive `BehaviorActionKind`; it does not contain branches for `MO`, `MOD`, `MEDIA`, or the other helper names.

User-defined `behavior` definitions therefore continue to use the same generic Behavior runtime and coexist with these PC-oriented standard helpers. This preserves the #99 design goal that built-in conveniences are library behaviors, not separate runtime engines.
