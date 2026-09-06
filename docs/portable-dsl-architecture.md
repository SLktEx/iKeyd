# Portable `.ikeyd` compiler architecture

## Purpose

`.ikeyd` is the source language for keyboard behavior, not a serialization format for the current Windows runtime.

The compiler must preserve one semantic model across multiple targets:

```text
.ikeyd source
    -> parser
    -> AST
    -> semantic analysis
    -> Behavior IR
       -> iKeyd C# backend
       -> iKeyd Rust backend
       -> QMK backend
       -> ZMK backend
       -> JSON/debug backend
```

JSON is an optional projection of the compiler-owned IR. It is useful for snapshots, diffs, migration, visualization, debugging, and external tooling, but it is not the canonical IR and should not remain a mandatory build hop.

## Design rules

### 1. Meaning lives in the IR

The IR describes keyboard semantics rather than target syntax. It must not contain C# expressions, Rust syntax, QMK macros, ZMK Devicetree nodes, or JSON-schema-specific structures.

Examples of target-neutral concepts include:

- physical key position
- key output
- layer activation
- modifier tap
- layer tap / hold tap
- combo
- macro
- Unicode output
- pointer behavior
- source location

### 2. Physical position is first-class

`POS[row,col]` is a canonical physical reference.

Authoring aliases such as `BASE[row,col]` and `LAYER[row,col]` are resolved during semantic analysis. Backends receive resolved positions and must not reinterpret layout aliases independently.

This lets combo definitions and other physical relationships survive changes to the output characters assigned to a layer.

### 3. Portable core and target extensions are separate

Portable source describes behavior that can be represented by the shared semantic model.

Host-only concepts such as process inspection, active-window conditions, clipboard access, command execution, and other operating-system integration are target capabilities rather than assumptions of the language core.

Target-specific escape hatches may exist, but they must be explicit. They must not silently alter the meaning of portable source on other backends.

### 4. Backends declare capabilities

Each backend declares the capabilities it can preserve. Example capability names:

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

Capabilities describe semantic behavior, not implementation mechanisms.

Before code generation, target validation walks the IR and rejects required capabilities that the selected backend does not implement.

Unsupported behavior is a compile error. A backend must never silently discard or approximate behavior unless the source explicitly requests an approximation mode in the future.

Example diagnostic:

```text
error IKYD2041: `clipboard` is not supported by target `qmk`
  --> keymap.ikeyd:143:9
```

### 5. Backends consume IR, never source syntax

A backend receives validated Behavior IR. It does not parse `.ikeyd`, resolve `BASE[...]`, or depend on the authoring grammar.

This keeps all targets aligned on one definition of meaning and prevents QMK/ZMK/iKeyd from gradually becoming separate languages.

## Backend responsibilities

### iKeyd C#

Generate the static profile representation consumed by the current .NET runtime. During migration this replaces the mandatory `JSON -> GeneratedProfile.g.cs` path with `IR -> GeneratedProfile.g.cs`.

### iKeyd Rust

Generate a deterministic Rust profile/module, or another Rust build-time representation owned by the compiler. JSON should not be required between IR and the Rust runtime.

### QMK

Generate QMK source/configuration for the supported portable subset. Map semantics rather than spelling; for example, a portable layer-tap behavior maps to the appropriate QMK representation only when its semantics can be preserved.

### ZMK

Generate ZMK keymap/configuration for the supported portable subset. Position-based combo semantics should map naturally from resolved physical positions.

### JSON/debug

Serialize a stable, documented projection useful for tests and tooling. This output is diagnostic/interchange data, not the in-memory IR contract.

## Compilation phases

```text
source text
  |
  v
parser
  |
  v
AST
  |  - names still exist
  |  - authoring aliases still exist
  v
semantic analysis
  |  - resolve layouts and positions
  |  - resolve symbols/layers/modifiers
  |  - validate source-level invariants
  v
Behavior IR
  |  - target-neutral semantics
  |  - source locations retained
  v
target capability validation
  |
  +--> diagnostics
  |
  v
backend code generation
```

## Migration from the current JSON pipeline

Current path:

```text
.ikeyd -> JSON -> GeneratedProfile.g.cs -> iKeyd.exe
```

Target path:

```text
.ikeyd -> AST -> Behavior IR -> GeneratedProfile.g.cs -> iKeyd.exe
                         |
                         +-> JSON (optional)
```

Migration should be incremental:

1. Introduce Behavior IR and capability primitives without removing the existing JSON path.
2. Lower current DSL constructs into IR.
3. Make the existing static C# profile generator consume IR.
4. Keep JSON emission as a compatibility/debug backend and compare it with current fixtures.
5. Make `.ikeyd` the normal build input.
6. Add QMK and ZMK backends against the same conformance fixtures.
7. Add the Rust backend before or alongside the runtime migration.

## Conformance strategy

Correctness is defined at the semantic level first.

A portable fixture should:

1. parse and lower to a known Behavior IR snapshot;
2. validate successfully for each target that claims the required capabilities;
3. generate deterministic target output;
4. fail with a precise diagnostic for targets that cannot preserve its semantics.

Generated QMK/ZMK/C#/Rust text snapshots are useful regressions, but text equality alone must not become the definition of semantic correctness.

## Non-goals

- forcing every iKeyd host feature onto embedded firmware;
- designing the portable core as the intersection of every target forever;
- using JSON as the compiler's object model;
- allowing backends to silently ignore unsupported behavior;
- allowing target-specific raw syntax to leak into portable IR.

## Related issues

- #144 portable DSL/backends umbrella
- #145 Behavior IR and capability validation
- #146 QMK backend
- #147 ZMK backend
- #148 Rust backend
- #149 cross-backend conformance suite
- #150 canonical `.ikeyd` build input
- #151 target extension syntax and capability semantics
