# Host automation actions

`.ikeyd` can request a small set of host-side actions without turning the keyboard hot path into a scripting runtime.

The current actions are:

- `EXEC` — launch one executable with literal argv boundaries
- `SHELL` — explicitly run one command through the Windows command interpreter
- `QUERY` — read one registered host/system scalar and emit it as direct text

These actions are target/host capabilities. They are not firmware-portable key semantics and must never be silently discarded by another backend.

## Authoring

The general behavior argument grammar is still identifier-oriented, so arbitrary host strings use the existing behavior option block.

### Exec

```ikeyd
keymap TOOLS {
    T = EXEC() {
        executable = "wt.exe"
        arg0 = "-d"
        arg1 = "C:\\work tree"
    }
}
```

`arg0` through `argN` must be contiguous. The compiler rejects missing indexes such as `arg1` without `arg0`.

Programmatic profile data may also use ordinary invocation arguments, where the first argument is the executable and the rest are argv values.

### Shell

```ikeyd
keymap TOOLS {
    P = SHELL() {
        command = "Get-ChildItem | Select-Object -First 10"
    }
}
```

`SHELL` is intentionally explicit because the command string is interpreted by the platform shell. On the current Windows backend this uses the configured command interpreter (`ComSpec`, normally `cmd.exe`) with `/d /s /c`.

### Query

```ikeyd
keymap TOOLS {
    F = QUERY() {
        key = foreground.process
    }
    H = QUERY() {
        key = system.hostname
    }
}
```

Supported query keys are:

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

Query names are case-insensitive at validation and normalized to the canonical spelling.

## Keyboard hot-path boundary

The low-level keyboard path never calls `Process.Start`, foreground-window APIs, IME APIs, or other system-query APIs directly.

The path is:

```text
physical key
  -> generic BehaviorRuntime
  -> Exec / Shell / Query BehaviorAction
  -> BehaviorWindowsInputRouter
  -> non-blocking host-action post
       -> bounded command queue worker
       -> query worker
```

`Exec`, `Shell`, and `Query` are one-shot actions with `RepeatPolicy.Never`. Windows physical auto-repeat therefore cannot repeatedly launch a process or replay a query merely because the source key is held.

The command queue uses `TryWrite` on a bounded channel. When full, enqueue returns `false`; keyboard input does not wait for queue space.

## Exec security contract

`EXEC` is the preferred command action.

Windows uses `ProcessStartInfo.ArgumentList` and `UseShellExecute = false`. Arguments are never concatenated into one shell command string. This preserves spaces, punctuation, and shell metacharacters as literal argv values.

No host action requests elevation.

Use `SHELL` only when shell parsing, pipelines, redirection, expansion, or other command-interpreter behavior is intentionally required.

## Command results

Command execution produces a typed result containing:

- the original request
- exit code when a process started and exited
- stdout
- stderr
- launch/startup error when applicable

The resident app currently records failed command results through diagnostics/trace output. The typed result contract is retained so future UI, state, macros, or tooling can consume it without redesigning process execution.

## Query semantics and #116

`QUERY` currently runs outside the keyboard callback and emits the resulting scalar as direct text. Missing foreground data resolves to an empty scalar where the Windows provider cannot identify a foreground process/title.

#116 extends this query registry with a cached immutable snapshot for `when(...)` conditions. Condition evaluation must read that snapshot only; it must not turn each key event back into a Win32/process/IME query.

## Portability

The command/query contracts live in Core, while Windows process and Win32 implementation details live in the Windows backend.

Firmware or other runtimes must either:

- implement an equivalent capability,
- use an explicit adaptation/hook model (for example the QMK hook policy), or
- reject the unsupported requirement with a diagnostic.

Requested host behavior must not disappear silently.
