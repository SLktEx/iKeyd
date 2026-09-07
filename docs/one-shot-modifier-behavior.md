# One-shot modifier behavior

`OSM(modifier)` provides a bounded one-shot modifier semantic through the generic Behavior action/runtime path.

## Semantics

- While the OSM key is physically held, the modifier is held normally.
- Repeated physical key-down events for the OSM source do not duplicate modifier-down.
- If another key interrupts the hold, releasing the OSM source only releases the held modifier; it does not arm a one-shot modifier.
- A clean tap releases the momentary modifier first and then arms that modifier for the next supported physical key lifecycle.
- When armed, the backend emits modifier-down before the target key-down is processed.
- The modifier remains held across physical repeats of that target.
- The backend emits modifier-up only after the matching target key-up has been processed.
- Cancellation/reset never arms a new one-shot modifier and releases any modifier that is currently held or consumed.

The ordering contract for a consumed target is therefore:

```text
modifier down
    -> target key down
    -> target key repeats (if any)
    -> target key up
modifier up
```

This ordering is intentional. Releasing the modifier before the target key-up can produce incorrect platform behavior; failing to release it on reset can leave a modifier logically stuck.

## Generic action model

`OSM` is a standard-library behavior, not a Windows-specific runtime branch. Its state machine emits the existing `ModifierDown` / `ModifierUp` primitives while held and a target-neutral `ModifierOneShot` action after a clean tap. Each platform backend owns the physical consumption and ordering needed to realize that action.

This keeps the semantic contract reusable by the planned Rust, ZMK, and QMK backends under #144/#149.

## Initial scope

The initial implementation intentionally arms one modifier for one physical target lifecycle. More advanced modifier composition, locking, or multi-tap behavior belongs to the later bounded composition/tap-dance work in #99 rather than being hidden inside OSM.
