# `.ikeyd` target extensions

`.ikeyd` has one portable semantic core. Backend-specific declarations live in explicit `target` blocks and never redefine that core.

## Syntax

```text
keymap BASE {
    Q = "q"
    W = LT(NAV, W)
    combo POS[1,1] + POS[1,2] = "escape"
}

target qmk {
    require combo
    require layer-tap
    option combo_term = 40ms
    native keymap.c = "#define COMBO_TERM 40\n"
}

target zmk {
    require combo
    require layer-tap
    option tapping-term-ms = 175
}
```

A target block currently accepts only three declarations:

- `require <capability>`: explicitly require a semantic capability from that backend.
- `option <name> = <scalar>`: pass backend-specific configuration metadata.
- `native <kind> = "<escaped text>"`: an explicit opaque backend escape hatch. Native fragments currently use a quoted string; `\n` can be used for multiline target text.

Portable bindings, layers, combos, behaviors, or macros cannot be declared or overridden inside a target block. If a target needs different portable behavior, that difference must be represented as a real semantic feature in the shared IR rather than hidden in target-specific source.

## Target selectors

Supported selectors are:

- `target ikeyd`: applies to both the current C# runtime and the future Rust runtime.
- `target ikeyd-csharp`: applies only to the C# backend.
- `target ikeyd-rust`: applies only to the Rust backend.
- `target qmk`: applies only to QMK.
- `target zmk`: applies only to ZMK.

`target windows` is intentionally not a backend selector. Windows/Linux are host platforms, while iKeyd/QMK/ZMK are compilation/runtime targets. Host platform conditions belong in host/platform semantics and must not be conflated with backend selection.

## Additive semantics

Target blocks are additive metadata only.

Given:

```text
Q = "q"

target qmk {
    option combo_term = 40ms
}
```

QMK still receives the portable `Q = "q"` binding. The target block cannot replace it.

This keeps one definition of keyboard behavior across backends and prevents `.ikeyd` from becoming several unrelated languages hidden behind one file extension.

## Selection

When compiling a target, only matching target blocks participate:

```text
target qmk { ... }   // selected for QMK only
target zmk { ... }   // selected for ZMK only
target ikeyd { ... } // selected for iKeyd C# and Rust
```

A foreign target block is ignored completely. In particular, a QMK native fragment does not make a ZMK or iKeyd build fail.

## Capabilities

The compiler owns stable semantic capability names:

```text
key-output
layer
combo
hold-tap
mod-tap
layer-tap
macro
unicode
pointer
host-command
clipboard
app-context
```

Portable IR nodes imply their required capabilities automatically. `require` is only for target-specific declarations that need an explicit backend contract.

Unsupported capability requirements use the same diagnostic as portable IR:

```text
error IKYD2041: `pointer` is not supported by target `zmk`.
```

Capability names describe meaning, not a QMK macro, ZMK behavior node, C# type, or Rust API. Capability-set evolution is compiler-versioned; source files do not pin implementation versions of individual capabilities.

## Options

Options are target-owned metadata. The portable compiler preserves their name, value, order, and source location, but does not assign portable meaning to them.

Multiple matching target blocks are allowed so configuration can stay close to related source, but the same option name may appear only once for a selected backend. Duplicate options fail with `IKYD2043` rather than relying on last-writer-wins behavior.

## Native escape hatches

Native fragments are intentionally narrow and explicit:

```text
target qmk {
    native keymap.c = "// QMK-only source\n"
}
```

```text
target zmk {
    native keymap.overlay = "// ZMK-only devicetree text\n"
}
```

Rules:

1. Native text is opaque to portable semantic analysis.
2. It is emitted only by the matching backend.
3. It cannot replace or mutate a portable binding in the IR.
4. QMK and ZMK allow native fragments because their generated projects may need firmware-specific declarations.
5. iKeyd C#/Rust does not currently allow native source injection; requesting it fails with `IKYD2042`.
6. Portable conformance tests must never depend on a native fragment to make the portable behavior correct.

Native fragments are an escape hatch, not the preferred extension mechanism. If the same need appears across targets, it should become a typed portable IR feature instead.

## Source locations and diagnostics

Every target block, requirement, option, and native fragment retains its `.ikeyd` source location. Errors point at the most specific declaration available.

Current target-extension diagnostics include:

- `IKYD2041`: required semantic capability is unsupported by the selected backend.
- `IKYD2042`: native fragments are unsupported by the selected backend.
- `IKYD2043`: a target option is declared more than once for the selected backend.

Unsupported behavior is never silently approximated or dropped.

## Relationship to JSON

Normal iKeyd builds compile `.ikeyd` directly to typed profile data and generated C#; JSON is not a required build intermediate. JSON remains useful as an optional compatibility/debug/tooling projection.

Target extensions follow the same rule: their canonical meaning is compiler-owned typed data, not a JSON schema.
