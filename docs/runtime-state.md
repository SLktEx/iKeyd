# Shared runtime state

`.ikeyd` can declare a small amount of typed, process-local state that is shared by configured behaviors.

This is deliberately **not** a general-purpose scripting environment. The first state model contains only declared `bool` and `string` fields, bounded synchronous mutation, and equality/inequality conditions.

## Declaration

```ikeyd
state {
    mode: string = "normal"
    nav_locked: bool = false
}
```

Rules:

- there may be at most one top-level `state` block;
- every field has an explicit type and default value;
- the first implementation supports `bool` and `string` only;
- field names are case-insensitive at runtime and may be referred to as `mode` or `state.mode` by the Core state API;
- duplicate fields and invalid defaults are compile errors;
- no field is created implicitly by an assignment or condition.

## Lifetime and reset

Shared state lives for the lifetime of the active iKeyd runtime/profile.

- startup/profile construction initializes every field to its declared default;
- values survive ordinary key release and behavior cancellation;
- `Reset Input State` restores every shared field to its declared default in addition to clearing active behavior/layer/modifier state;
- restarting iKeyd constructs a fresh store from the profile defaults;
- persistence across restart is intentionally not part of this implementation.

This differs from `var` declarations inside a user-defined behavior: behavior locals belong to one active behavior instance, while `state.*` is shared between bindings and behavior instances.

## Standard state actions

### `SET`

```ikeyd
keymap S {
    Q = SET() {
        state = mode
        value = "coding"
    }
}
```

The compiler checks that the field exists and that the value matches the declared type.

For a boolean field:

```ikeyd
Q = SET() {
    state = nav_locked
    value = true
}
```

### `TOGGLE`

```ikeyd
W = TOGGLE() {
    state = nav_locked
}
```

`TOGGLE` accepts boolean fields only. Toggling a string is a compile error.

`SET` and `TOGGLE` use `BehaviorRepeatPolicy.Never`: Windows physical key auto-repeat cannot repeatedly set/toggle a field while the source key is held.

## State conditions

The existing bounded `WHEN` tree can read either the cached system-query snapshot from #116 or shared runtime state.

```ikeyd
E = WHEN() {
    state = mode
    operator = equals
    expected = "coding"
    then_kind = key
    then_value = Escape
    else_kind = key
    else_value = F1
}

R = WHEN() {
    state = nav_locked
    operator = not_equals
    expected = false
    then_kind = key
    then_value = Left
    else_kind = key
    else_value = Right
}
```

A `WHEN` condition node must name exactly one source:

- `query = foreground.process`, or
- `state = mode`.

Supported operators are `equals` / `not_equals` (semantic aliases `==` / `!=`). State comparison values are type-checked at compile time.

Nested `WHEN` nodes may freely combine cached host/system values and runtime-state values. They use the same bounded condition tree rather than separate expression engines.

State mutation is also available as a `WHEN` leaf through `set` and `toggle` branch kinds.

## User-defined behaviors

Custom behaviors can mutate and test the same shared store:

```ikeyd
behavior STATEFUL() {
    on_press {
        state.set(mode, "coding")
        state.toggle(nav_locked)

        if state.mode == "coding" {
            send Escape
        } else {
            send F1
        }

        if state.nav_locked != false {
            send Left
        }
    }
}
```

Supported custom-behavior shared-state statements in this slice are:

```text
state.set(field, value)
state.toggle(field)
if state.field == value { ... }
if state.field != value { ... }
```

The compiler lowers these to typed bounded statement IR (`state_set`, `state_toggle`, and state comparison statements). The runtime does not parse `.ikeyd` source text.

## Hot-path contract

Shared state is a Core capability and performs no host/platform I/O.

The current fixed-shape store allocates field slots when the profile is constructed. Ordinary state operations use those existing slots:

- boolean reads/writes use bounded volatile/atomic operations;
- string values use volatile reference publication;
- there is no filesystem, process, Win32, IME, clipboard, network, or shell access in state reads/writes;
- there is no unbounded lock wait on the keyboard path.

Adding state must not weaken the #116 system-condition rule. Host/system conditions continue to read an off-hook cached snapshot; they do not call the query provider for each key event.

## Static representation and portability

`.ikeyd` remains the source of truth:

```text
state declarations + behaviors
  -> typed RuntimeStateProfile / Behavior semantics
  -> generated static target representation
  -> runtime store
```

The generated Windows profile contains field names, types, and defaults. The resident app does not parse the `state` source block at runtime.

The semantic model is intentionally suitable for the planned Rust runtime: it relies on explicit descriptors and typed slots, not C# reflection, `dynamic`, or arbitrary object bags.

Other backends must either preserve the requested semantics, use an explicit target adaptation, or report an unsupported capability. State-dependent behavior must not be silently dropped.

## Non-goals

The first shared-state slice intentionally excludes:

- persistence across restart;
- arrays, objects, maps, or arbitrary user-defined types;
- arithmetic expression evaluation;
- loops or general user functions;
- command-result variables;
- implicit fields;
- platform/host I/O from a state action.
