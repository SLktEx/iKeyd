# Custom tap/hold behavior events

User-defined `.ikeyd` behaviors may opt into the same bounded tap/hold timing model used by the standard `LT` / `MT` family by defining `on_tap` and/or `on_hold` handlers.

## Syntax

```ikeyd
behavior SMART_TH(tap_key, layer_name) {
    on_hold {
        layer.on(layer_name)
    }

    on_tap {
        send tap_key
    }
}

keymap BASE {
    A = SMART_TH(X, NUM) {
        tapping_term = 170ms
        hold_on_other_key_press = false
    }
}
```

The handlers take no parameters.

## Invocation options

Tap/hold-capable user behaviors accept the bounded options already used by the standard tap/hold model:

- `tapping_term = Nms`
  - default: `200ms`
  - must be a non-negative millisecond duration
- `hold_on_other_key_press = true|false`
  - default: `true`

User behaviors that do not define `on_tap` or `on_hold` continue to reject invocation options. This avoids silently accepting timing configuration that has no semantic effect.

## Resolution and event order

A new behavior instance begins in a pending state when it defines `on_tap` or `on_hold`.

### Quick release

If the source key is released before hold resolution:

```text
on_press
on_tap
on_release
owned-resource cleanup
```

`on_tap` runs at most once.

### Timeout hold

When time reaches the inclusive tapping boundary (`pressed_at + tapping_term`):

```text
on_press
on_hold
...
on_release
owned-resource cleanup
```

`on_hold` runs at most once. Further time advancement does not replay it.

### Interruption

With the default `hold_on_other_key_press = true`, another physical key resolves the pending behavior to hold before the custom interrupt handler runs:

```text
on_press
on_hold
on_interrupt(other)
```

This ordering is intentional. Layer or modifier ownership established by `on_hold` is therefore visible when the interrupting key itself is resolved.

With `hold_on_other_key_press = false`, interruption does not force hold resolution. `on_interrupt(other)` still runs, and an early source-key release can still resolve as tap.

## Cancellation and cleanup

Cancellation/reset is not a synthetic tap, hold, or normal release. It does not invoke `on_tap`, `on_hold`, or `on_release`; it only releases layers/modifiers currently owned by the behavior instance.

Normal release executes `on_release` after the selected tap/hold handler and then performs the same ownership cleanup. This keeps user code deterministic while preventing leaked modifiers or layers.

## Timer model

This feature does not introduce a second asynchronous scripting scheduler. It uses the existing bounded `BehaviorInstance.AdvanceTo(timestamp)` / `BehaviorRuntime.AdvanceTo(timestamp)` mechanism already used by standard tap/hold behavior.

The behavior language still forbids unbounded loops, recursion, blocking I/O, and arbitrary asynchronous work in the input path.

## Compilation

`on_tap` / `on_hold` handlers and their invocation options are parsed and preserved by the canonical `.ikeyd` compiler and static generated profile path. Invalid custom behavior arity or timing options are rejected during `.ikeyd` compilation with source context rather than waiting for first runtime use.

JSON remains optional compatibility/debug tooling; it preserves these handlers/options but is not required by the normal `.ikeyd` build path.
