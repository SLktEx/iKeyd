# One-shot layer behavior

`OSL(layer)` provides a bounded one-shot layer semantic through the generic Behavior action/runtime path.

```ikeyd
keymap BASE {
    A = OSL(NUM)
}
```

## Tap semantics

A clean tap of the OSL source key arms the target layer for exactly the next supported physical key lifecycle.

```text
OSL down
  -> target layer is momentarily active
OSL up without interruption
  -> momentary activation ends
  -> target layer is armed one-shot
next supported physical key down
  -> one-shot layer is consumed for that key
same key repeats
  -> continue using the consumed one-shot layer
that physical key up
  -> consumed one-shot layer expires
```

The one-shot is consumed by the next supported physical key even when the selected layer is transparent at that position. In that case routing falls through to lower layers/base/fallback, but the one-shot still expires on that key's release.

A second different physical key pressed while the consumed key remains held does not inherit that consumed one-shot layer.

## Hold semantics

While the OSL source key is held, the target layer behaves like a normal momentary layer.

If another key interrupts the held OSL:

- the target layer remains active while OSL is held;
- multiple keys may use that momentary layer;
- releasing OSL releases the momentary activation;
- the interrupted hold does **not** arm another one-shot afterward.

This avoids turning normal held-layer use into an unexpected extra one-shot after release.

## Layer priority

The Windows reference backend resolves transparent overlays in this order:

```text
newest momentary layer
  > one-shot layer consumed by the current physical key
  > persistent TG/TO layers
  > base Behavior binding
  > existing legacy/base fallback
```

Base ordinary single/chord handling remains in the existing fallback so OSL does not bypass the established simultaneous-key engine.

## Reset and cancellation

`Reset Input State`, lifecycle reset, and router disposal clear both armed and currently consumed one-shot layer state.

Cancelling an OSL while it is held releases its momentary layer but never arms a one-shot.

## Repeat policy

Physical auto-repeat of the OSL source key does not duplicate layer activation or arm multiple one-shots.

After a one-shot is consumed by a key, repeated physical key-down events for that same held key continue to see the same one-shot layer until key-up.

## Scope

This slice intentionally implements `OSL` only.

`OSM(modifier)` is separate because a one-shot modifier must preserve the modifier-down / target-key-down / target-key-up / modifier-up ordering and cleanup contract without creating stuck modifiers. It should not be implemented as a superficial copy of the layer state.

Locking, tap-toggle, multi-tap, and tap-dance semantics are also outside this helper and remain generic Behavior work rather than OSL-specific runtime branches.
