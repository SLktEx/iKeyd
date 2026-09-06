# Portable `.ikeyd` compiler architecture

## Purpose

`.ikeyd` is the canonical source language for keyboard behavior, not a serialization format for the current Windows runtime.

The intended compiler model is:

```text
.ikeyd source
    -> parser / typed document
    -> semantic analysis
    -> portable + target-aware Behavior IR
       -> iKeyd C# static representation
       -> iKeyd Rust representation
       -> QMK backend
       -> ZMK backend
       -> optional JSON/debug projection
```

JSON is an optional projection useful for snapshots, diffs, migration, visualization, debugging, and external tooling. It is not the canonical IR and is no longer a mandatory normal-build hop.

The current C# Windows build already consumes `.ikeyd` directly and generates static C# profile data under `obj/` (#150).

## Design rules

### 1. Meaning lives in semantic data / Behavior IR

The semantic model describes keyboard behavior rather than target source syntax. Portable semantics must not depend on C# expressions, Rust syntax, QMK macros, ZMK Devicetree nodes, or a JSON schema.

Examples include:

- canonical physical key identity / position
- key output
- Unicode scalar output
- text output
- layers and modifiers
- hold-tap / mod-tap / layer-tap
- combos
- bounded behavior actions
- pointer behavior
- source location
- explicit target requirements/extensions

### 2. Physical identity is first-class

`POS[row,col]` is a physical reference. Authoring aliases such as `BASE[row,col]` are resolved before target code generation.

On Windows, the canonical JIS109 work under #174 preserves the physical `(virtual key, scan code, extended flag)` identity at the backend boundary instead of reducing everything to a virtual key alone.

Backends receive canonical physical identity/position and must not independently reinterpret authoring aliases.

### 3. Portable core and target extensions are separate

Portable source describes requested semantics. Host-only concepts such as process inspection, active-window conditions, clipboard access, command execution, and other operating-system integration are explicit capabilities rather than assumptions of firmware targets.

Target-specific configuration uses the #151 extension model. Target blocks are additive metadata and must not silently redefine portable bindings.

### 4. Unsupported behavior must never disappear silently

Each backend classifies a requested semantic construct as one of:

1. **direct** — semantics can be preserved natively;
2. **target extension** — source explicitly requests a supported target-specific facility;
3. **adaptation/hook** — the backend has a defined adaptation contract;
4. **unsupported** — compilation fails with a precise diagnostic.

A backend must never silently omit requested behavior.

This is intentionally more precise than the old blanket rule "unsupported means hard error".

#### QMK

QMK may lower host-only semantics to deterministic generated hooks under #161. Export succeeds with safe default hook implementations and reports which hooks need customization. Constructs outside the direct/hook/extension model still fail explicitly.

#### ZMK

ZMK currently has no equivalent general host-hook policy. Unsupported host-only semantics therefore remain explicit capability failures unless a concrete adaptation model is designed later.

### 5. Backends consume validated semantics, not source syntax

A backend does not parse `.ikeyd`, resolve `BASE[...]`, or invent its own meaning for authoring constructs. Parsing/name resolution/semantic validation belong before backend generation.

This keeps iKeyd C#, iKeyd Rust, QMK, and ZMK aligned on one language rather than becoming four loosely related parsers.

## Current implementation status

Already established:

- `.ikeyd` is the normal build input (#150);
- no mandatory JSON hop exists in the Windows build;
- target-neutral Behavior IR/capability foundation exists (#145);
- target selectors plus `require`, `option`, and `native` extensions exist (#151);
- physical positions remain first-class;
- the Windows/JIS109 key registry covers the full canonical physical surface (#174 repository-side work);
- key / Unicode scalar / text output are distinct semantic actions (#67);
- target/source diagnostics retain useful source context where implemented.

Remaining backend work is tracked by #146/#147/#148/#149/#161. Language/runtime completion continues under #64/#99/#135/#80.

## Backend responsibilities

### iKeyd C#

Compile canonical `.ikeyd` into deterministic static profile/mouse representation consumed by the current .NET Windows runtime. This is the current reference/oracle path during Rust migration.

### iKeyd Rust

Consume the same settled semantics through a deterministic Rust-oriented representation. It must not require a JSON intermediate or preserve C# implementation shapes merely for compatibility.

### QMK

Lower portable firmware-capable semantics directly. Host-only semantics covered by #161 become explicit generated hooks with conservative defaults and a separate customization area. Anything outside the defined direct/hook/extension policy fails diagnostically.

### ZMK

Lower the supported portable intersection and explicit ZMK extensions. Unsupported host-only behavior currently fails clearly rather than being silently erased.

### JSON/debug

Optional compatibility/debug projection only. JSON text is not the in-memory semantic contract and is not required by normal runtime startup.

## Compilation phases

Conceptually:

```text
source text
  |
  v
parser / typed document
  |
  v
semantic analysis
  |  - resolve physical aliases/positions
  |  - resolve symbols/layers/behaviors
  |  - validate source-level invariants
  v
portable + target-aware Behavior semantics
  |
  v
target capability / adaptation validation
  |\
  | +--> diagnostics
  v
backend/static representation generation
```

The exact internal class boundaries may evolve. The invariant is that meaning is settled before target source generation and is not reconstructed from generated text.

## Normal Windows build today

The historical migration sequence is complete enough that this is no longer the normal path:

```text
.ikeyd -> JSON -> generated C# -> iKeyd.exe
```

The current normal path is:

```text
.ikeyd
  -> typed DSL/profile + Behavior semantics
  -> GeneratedProfile.g.cs / GeneratedMouseProfile.g.cs
  -> iKeyd.exe
```

Historical JSON compilers/fixtures remain for compatibility and differential verification only.

## Conformance strategy

Correctness is defined semantically before generated-source snapshots.

For a portable fixture, prefer:

```text
same .ikeyd fixture
  -> parsed/typed assertions
  -> semantic / Behavior IR assertions
  -> backend capability/adaptation assertions
  -> deterministic target snapshot/build check
```

QMK hook cases assert preservation of an explicit adaptation contract, not false equivalence to native firmware behavior. ZMK fixtures assert both the supported intersection and the unsupported boundary. Rust fixtures should share the same semantic cases used by the reference path.

This cross-backend suite is tracked by #149.

## Non-goals

- forcing every host feature directly into embedded firmware;
- defining portability as the smallest intersection of every target forever;
- using JSON as the compiler object model;
- allowing unsupported behavior to disappear silently;
- allowing target-specific raw syntax to leak into portable semantics;
- preserving obsolete C#-specific internal architecture in the Rust migration.

## Related issues

- #64 `.ikeyd` authoring language/tooling
- #80 hardcoded Windows/hotkeySKG binding migration
- #99 generic/custom Behavior semantics
- #135 typed runtime state
- #144 portable DSL/backends umbrella
- #145 Behavior IR/capability foundation
- #146 QMK backend
- #147 ZMK backend
- #148 Rust profile/backend path
- #149 cross-backend conformance suite
- #150 canonical `.ikeyd` build input
- #151 target extensions/capabilities
- #161 QMK generated host hooks
