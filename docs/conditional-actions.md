# Conditional actions

`when(...)` selects one configured output action from cached system context without querying Win32, processes, IME, or other platform APIs on the low-level keyboard callback.

## Syntax

```text
when(<query> == <value>, <then-action>, <else-action>)
when(<query> != <value>, <then-action>)
```

The first slice supports `==` and `!=`, quoted string values, and the boolean literals `true` and `false`.

Examples:

```text
layer APP {
    POS.Q = when(foreground.process == "Code.exe", key(Escape), key(F1))
    POS.W = when(ime.kana_active == true, text("かな"), text("kana"))
    POS.E = when(keyboard.capslock == true, exec("tool.exe", "--caps"), key(E))
}
```

Conditional actions may be nested. Commas inside quoted strings or nested actions do not split the outer `when(...)` arguments:

```text
POS.R = when(
    foreground.process != "Code.exe",
    when(keyboard.numlock == true, text("one,two"), key(F3)),
    key(F4)
)
```

The DSL remains statically compiled. The condition and both branches are represented in canonical JSON and generated C#; the runtime does not parse DSL text.

## Query values

Conditions use the same system-query registry as `query(...)`, including:

```text
system.os
system.architecture
system.hostname
system.username
foreground.process
foreground.pid
foreground.title
ime.kana_active
keyboard.capslock
keyboard.numlock
keyboard.scrolllock
```

Unknown query names are compile errors.

Comparisons are case-insensitive scalar comparisons. Boolean query values are represented as `true` or `false` strings internally, so boolean literals in the DSL use the same stable representation.

## Missing and stale data

A condition whose query value is missing evaluates to false. If the `else` branch is omitted, false is a no-op.

On Windows, only query keys referenced by the compiled profile are refreshed. A failed refresh keeps the previous value for that key when one exists. If a value has never been acquired successfully, it remains missing.

## Keyboard hot path

The keyboard callback does not call the platform query provider.

```text
Windows query backend
    -> periodic refresh outside keyboard callback
    -> current immutable snapshot
    -> keyboard callback reads snapshot only
    -> evaluate condition
    -> dispatch selected action
```

The default Windows refresh interval is 100 ms. Snapshot publication swaps the current dictionary atomically; condition evaluation only performs an in-memory lookup and comparison.

`query(...)` uses the same snapshot once conditional actions are enabled, so direct query output also avoids per-key platform queries.

## Action dispatch

The selected branch keeps the normal semantics of the action it contains:

- `key(...)` participates in configured held modifiers.
- `text(...)` emits literal text.
- mouse, media, and window actions use their existing desktop paths.
- `clipboard(...)`, `macro(...)`, `exec(...)`, and `shell(...)` keep using the host-action boundary and do not run directly on the keyboard callback.

`layer(...)` and `modifier(...)` are hold actions and are not valid branches of `when(...)` in this first model.

## Non-goals

This syntax is intentionally not a general-purpose expression language. The first slice does not add arithmetic, regex, loops, user-defined functions, implicit shell evaluation, or arbitrary code execution in conditions.
