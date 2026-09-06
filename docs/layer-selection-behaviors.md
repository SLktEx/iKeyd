# Layer selection behaviors

This document defines the current `.ikeyd` layer-selection behavior contract shared by the generic Behavior runtime and the Windows reference backend.

## Helpers

### `MO(layer)`

`MO` owns a momentary layer for the lifetime of one physical key hold.

```ikeyd
Space = MO(NAV)
```

- the first physical key-down activates the layer;
- physical auto-repeat does not activate it again;
- key-up releases exactly the layer activation owned by that behavior instance;
- cancellation/reset also releases the owned activation.

### `TG(layer)`

`TG` toggles persistent membership of one layer.

```ikeyd
A = TG(NUM)
```

- first activation latches the layer on;
- activating `TG` for the same layer again removes that persistent selection;
- physical auto-repeat does not repeatedly toggle the layer;
- releasing the source key does not undo the persistent selection.

### `TO(layer)`

`TO` replaces the current persistent layer selection with one layer.

```ikeyd
B = TO(SYMBOL)
```

- existing persistent layer selections are cleared;
- the requested layer becomes the persistent layer;
- physical auto-repeat does not replay the transition;
- releasing the source key does not undo the selection.

## Momentary and persistent ownership are separate

Momentary ownership (`MO`, and the hold side of `LT`) is intentionally kept separate from persistent selection (`TG` / `TO`).

When both exist:

```text
momentary active layer
    > persistent selected layer
    > base keymap
```

A momentary layer therefore temporarily overrides the persistent selection. Releasing the momentary owner reveals the persistent layer again; it must not consume, clear, or otherwise mutate the persistent state.

This separation is required for deterministic cleanup. A `LayerOff` emitted by an owning behavior removes only that behavior-style momentary activation, never a latch established by `TG` or `TO`.

## Reset semantics

`Reset Input State`, lifecycle reset, and router disposal clear both transient momentary layer state and persistent `TG` / `TO` selection. The canonical profile remains unchanged; only process-local input/runtime state is reset.

## Validation

`LT`, `MO`, `TG`, and `TO` layer arguments must name a keymap present in the compiled profile. Canonical `.ikeyd` compilation rejects unknown layer targets rather than delaying the error until the first physical key press.

## Runtime architecture

`TG` and `TO` compile to ordinary Behavior actions with non-repeating semantics. The generic event runtime does not branch on helper names. Platform backends apply the resulting persistent layer actions while retaining ownership cleanup for momentary layer actions.

This keeps the authoring/runtime contract portable while allowing each target backend to map the same semantics to its native layer model where supported.
