# Momentary behavior helpers

`MO(layer)` and `MOD(modifier)` are standard-library helpers built on the generic iKeyd Behavior runtime.

They do not add helper-name branches to the event runtime. Each physical press creates an ordinary behavior instance which owns the temporary resource until key-up or cancellation.

## `MO(layer)`

`MO` activates a named layer immediately while the source key is held:

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

Semantics:

- first physical key-down emits `layer.on(layer)`
- Windows auto-repeat does not activate the layer again
- source key-up emits `layer.off(layer)`
- runtime cancellation/reset also releases the owned layer

## `MOD(modifier)`

`MOD` owns a modifier for the physical hold duration:

```ikeyd
keymap S {
    Muhenkan = MOD(Ctrl)
}
```

Accepted portable authoring aliases in this slice are:

- `Ctrl` / `Control`
- `Shift`
- `Alt`
- `Gui` / `Win` / `Super`

Semantics:

- first physical key-down emits `modifier.down(modifier)`
- repeated key-down from keyboard auto-repeat is suppressed without another modifier-down
- source key-up emits `modifier.up(modifier)`
- cancellation/reset emits the matching modifier-up

The Windows backend consumes the existing generic `ModifierDown` / `ModifierUp` primitives; it does not know about the `MOD` helper itself.

## Relationship to #99

This is an incremental standard-library slice for #99. It deliberately does not implement `TG`, `TO`, `OSL`, `OSM`, tap dance, generic `on_hold` / `on_tap`, or timer/composition semantics. Those remain separate Behavior DSL work so momentary resource ownership can land without reviving the stale all-in-one PC-action draft.
