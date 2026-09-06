# Cached conditional actions

`WHEN` selects one bounded output branch from cached host/system state.

It is intentionally not a general expression language. The initial condition model is:

- one registered system-query key
- `equals` / `not_equals`
- one expected string/boolean scalar
- one then branch
- optional else branch
- bounded nested `WHEN`

## Current authoring form

The current parser uses the existing Behavior option-block syntax so the semantic/runtime work does not require a second inline-expression parser.

```ikeyd
keymap APP {
    Q = WHEN() {
        query = foreground.process
        operator = equals
        expected = "Code.exe"
        then_kind = key
        then_value = Escape
        else_kind = key
        else_value = F1
    }
}
```

The compact `when(condition, then, else)` spelling may be added later as authoring sugar under #64. It must lower to the same semantics described here.

## Supported branch kinds

A branch may use an existing output action:

```text
key
unicode
text
exec
shell
query
when
```

Examples:

```ikeyd
A = WHEN() {
    query = keyboard.capslock
    operator = equals
    expected = true
    then_kind = exec
    then_value = "tool.exe"
    then_arg0 = "--caps"
    else_kind = text
    else_value = "caps off"
}
```

For an `exec` branch, arguments are `<prefix>arg0`, `<prefix>arg1`, ... and must be contiguous.

## Nested conditions

Nested `WHEN` uses the branch prefix recursively.

```ikeyd
A = WHEN() {
    query = foreground.process
    operator = equals
    expected = "Code.exe"

    then_kind = when
    then_query = keyboard.capslock
    then_operator = equals
    then_expected = true
    then_then_kind = key
    then_then_value = Escape
    then_else_kind = key
    then_else_value = F1

    else_kind = text
    else_value = "not Code"
}
```

The compiler rejects unknown or unconsumed options instead of silently ignoring misspelled branch fields.

## Condition semantics

Comparisons are case-insensitive scalar comparisons.

```text
equals      -> actual == expected
not_equals  -> actual != expected
```

`==` and `!=` are also accepted by the semantic parser for programmatic profiles.

If the requested query has no cached value, the condition evaluates **false**, including `not_equals`. Missing host state therefore cannot accidentally select a negative condition merely because the value is unavailable.

An omitted else branch is a no-op when the condition is false.

## Hot-path rule

Host/system APIs are never called from condition evaluation.

Windows builds a `WindowsSystemQueryCache` from the exact query keys referenced by the compiled profile. The default refresh period is 100 ms. A refresh happens outside the keyboard callback and atomically publishes a fresh immutable snapshot.

The keyboard path performs only:

```text
current snapshot reference
  -> dictionary lookup
  -> scalar comparison
  -> emit already-compiled BehaviorAction
```

A transient provider failure retains the previous cached value when one exists.

## Repeat behavior

`WHEN` evaluates on the initial physical key-down. Repeated Windows key-down events for the held source key do not re-run the condition or re-emit the selected branch.

This prevents a held key from repeatedly launching `exec`/`shell` actions and keeps condition-driven state transitions deterministic.

## Query discovery

The compiled profile collects required query keys from:

- direct `QUERY` behaviors
- each `WHEN` condition
- nested `WHEN` conditions
- `QUERY` actions inside conditional branches

Only those keys are refreshed by the Windows query cache.

## Relationship to shared runtime state

#135 extends the same condition/value-source direction with declared profile/runtime state such as `state.mode` and `state.nav_locked`.

The goal is one typed condition model with multiple explicit value sources, not separate condition engines for cached host state and runtime-local state.
