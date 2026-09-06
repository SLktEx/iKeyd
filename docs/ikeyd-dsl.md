# iKeyd authoring DSL

`.ikeyd` is the canonical human-authored source for iKeyd keyboard behavior.

The normal Windows build does **not** compile `.ikeyd` to JSON and then read that JSON at runtime. The current path is:

```text
config/hotkeySKG.ikeyd
  -> typed DSL document
  -> semantic/profile + Behavior representation
  -> build-time static generators
       -> GeneratedProfile.g.cs
       -> GeneratedMouseProfile.g.cs
  -> iKeyd.exe
```

JSON remains available for compatibility fixtures, migration/debug tooling, and historical differential checks. It is not a required normal-build or runtime intermediate.

The application project invokes `tools/iKeyd.DslCompiler` automatically during `dotnet build`. The compiler's direct low-level interface is:

```text
iKeyd.DslCompiler <profile.ikeyd> <GeneratedProfile.g.cs> <GeneratedMouseProfile.g.cs>
```

The older `ikeyd check` / `ikeyd import` CLI currently belongs to the AHK-v1 migration/importer path; `ikeyd build` is still reserved there. Do not confuse that legacy migration CLI with the canonical `.ikeyd` build pipeline.

## Profile block

A document contains one profile block. `chord_window` is required by the current parser; `startup_mode` defaults to `S` when omitted.

```ikeyd
profile hotkeySKG {
    chord_window = 40ms
    startup_mode = S
}
```

The Windows reference profile currently expects the normal S/K mode family used by hotkeySKG.

## Physical layouts and position references

A `layout` gives stable row/column coordinates to physical keys:

```ikeyd
layout BASE {
    row Q W E R T Y U I O P
    row A S D F G H J K L SColon
    row Z X C V B N M Comma Dot Slash
}
```

Rows and columns are 1-based. `BASE[1,1]` is the physical `Q` position in this example.

Position references can be used anywhere the current keymap grammar accepts an input key:

```ikeyd
keymap S {
    BASE[1,1] = "-"
    combo BASE[2,8] + BASE[1,1] = "fa"
}
```

The compiler resolves authoring coordinates to canonical physical key identities before static profile generation. Changing the visible output assigned to a position therefore does not require rewriting combos that refer to that physical position.

### `POS[row,column]`

`POS[...]` is the canonical physical-position spelling. If no explicit `layout POS` exists, it aliases `layout BASE`:

```ikeyd
layout BASE {
    row Q W E
    row A S D
}

keymap S {
    combo POS[1,1] + POS[2,2] = "escape"
}
```

An explicit `layout POS` may be declared when the canonical physical geometry should be independent from another named authoring layout.

The Windows key surface is no longer limited to the historical compact 54-key set. The JIS109 registry and Windows `(VK, scan code, extended)` identity rules are documented in `jis109-key-surface-audit.md`.

## Keymaps

A keymap contains ordinary mappings, behavior mappings, and two-key combos.

```ikeyd
keymap S {
    Q = "q"
    W = "w"
    combo Q + W = "escape"
}
```

A physical key may not have both an ordinary string mapping and a behavior mapping in the same keymap.

Quoted string mappings are retained for the existing hotkeySKG-compatible profile/output path. When the intended meaning is explicitly "one Unicode scalar" or "arbitrary direct text", use the first-class `UNICODE` / `TEXT` behaviors described below rather than relying on a legacy string's interpretation.

## Standard behavior invocations

A key may invoke a first-class behavior instead of producing a legacy string output:

```ikeyd
keymap S {
    A = LT(NUM, Z)
    X = MT(Ctrl, X)
    Space = MO(NAV)
    Muhenkan = MOD(Ctrl)
}
```

Behavior invocations are represented as typed/profile semantic data and executed through the generic Behavior runtime. The platform event loop does not add separate LT/MT/MO/MOD state machines.

### `LT(layer, tap_key)`

Layer-tap sends `tap_key` on a tap and owns `layer` while held.

```ikeyd
A = LT(NUM, Z) {
    tapping_term = 170ms
    hold_on_other_key_press = false
}
```

Current options:

- `tapping_term = <duration>` — milliseconds such as `170ms`; default `200ms`.
- `hold_on_other_key_press = true|false` — whether another physical key-down resolves the pending behavior as a hold immediately.

### `MT(modifier, tap_key)`

Mod-tap uses the same tap/hold resolver, but owns a modifier while held:

```ikeyd
X = MT(Ctrl, X)
C = MT(Shift, C) {
    tapping_term = 150ms
}
```

Release/cancellation cleanup guarantees that an owned modifier is released.

### `MO(layer)`

`MO` activates a named layer for exactly the physical hold duration:

```ikeyd
Space = MO(NAV)
```

The first physical key-down activates the layer. Keyboard auto-repeat does not activate it again. Key-up or runtime cancellation releases the owned layer.

### `MOD(modifier)`

`MOD` owns a modifier for the physical hold duration:

```ikeyd
Muhenkan = MOD(Ctrl)
```

Accepted aliases in the current standard helper include:

- `Ctrl` / `Control`
- `Shift`
- `Alt`
- `Gui` / `Win` / `Super`

Repeated physical key-downs do not replay modifier-down; key-up/cancellation emits the matching modifier-up.

## First-class Unicode and text output

Key output, one Unicode scalar, and arbitrary text are distinct semantics.

The current literal-friendly authoring form uses a behavior option block because the general behavior argument grammar remains identifier-oriented:

```ikeyd
keymap SYMBOL {
    J = UNICODE() {
        value = "→"
    }

    K = UNICODE() {
        value = "🦀"
    }

    L = TEXT() {
        value = "hello 世界"
    }
}
```

Semantics:

- `UNICODE` contains exactly one Unicode scalar and follows physical keyboard repeat.
- `TEXT` contains a non-empty Unicode string and is emitted once; repeated key-down does not implicitly replay the whole string.
- layer/modifier ownership transitions remain non-repeatable.
- Windows lowers direct Unicode/text output through `SendInput` + `KEYEVENTF_UNICODE`; UTF-16 is a backend detail rather than part of portable Behavior semantics.

See `unicode-text-output.md` for the detailed repeat/validation/backend contract.

## User-defined behaviors

The current generic Behavior DSL supports a bounded first slice of user-defined behavior logic, including:

- top-level `behavior NAME(args) { ... }`
- behavior-local boolean variables
- `on_press`
- `on_interrupt(key)`
- `on_release`
- bounded `if/else`
- `send`
- `layer.on/off`
- `modifier.down/up`
- boolean assignment
- deterministic owned layer/modifier cleanup

Example:

```ikeyd
behavior SMART(layer, tap) {
    var active: bool = false

    on_press {
        active = true
        layer.on(layer)
    }

    on_release {
        if active {
            layer.off(layer)
        }
        send tap
    }
}

keymap S {
    Q = SMART(NUM, Z)
}
```

This is intentionally not a general-purpose scripting language. Unbounded loops, recursion, blocking I/O, and arbitrary asynchronous work do not belong on the keyboard event path.

The remaining behavior-language work (`on_hold` / `on_tap`, bounded timer semantics, richer local values, composition/reuse, one-shot helpers and tap dance) is tracked by #99. See `behavior-dsl.md` for the Behavior-specific design/status.

## Clipboard history settings

The optional top-level `clipboard` block configures iKeyd's own history. It does not change or encrypt the normal system clipboard itself.

```ikeyd
clipboard {
    history = true
    max_items = 100
    persist = true
    images = true
    encryption = user
    cipher = auto

    // Optional Windows persistence directory.
    // directory = "%LOCALAPPDATA%\\iKeyd"
}
```

Current settings:

- `history = true|false` — history collection/picker; default `true`.
- `max_items = <positive integer>` — retained item count; default `20`.
- `persist = true|false` — encrypted persistence vs memory-only; default `true`.
- `images = true|false` — include image payloads; default `true`.
- `encryption = user` — current supported user-scoped key-protection policy.
- `cipher = auto|chacha20_poly1305` — authenticated cipher selection. `auto` lets a future runtime choose its preferred implementation without changing the DSL.
- `directory = "..."` — optional persistence directory.

Clipboard settings become typed profile data/static configuration in the canonical build. They do not require a generated JSON file.

## Mouse motion settings

The optional top-level `mouse` block controls the continuous keyboard-driven virtual-stick pointer engine independently from the bindings that select mouse actions.

```ikeyd
mouse {
    engine = virtual_stick
    update = 8ms

    response {
        press = 45ms
        release = 2ms
        curve = smoothstep
    }

    speed {
        normal = 1000
        precision = 800
        fine = 240
        fast = 4400
    }

    socd = neutral
    tap_nudge = 1px
    max_catchup = 32ms
}
```

Current settings:

- `engine = virtual_stick` — current engine.
- `update = <duration>` — motion-loop cadence; default `8ms`.
- `response.press` / `response.release` — virtual-stick rise/release timing.
- `response.curve = linear|smoothstep` — response curve.
- `speed.normal`, `speed.precision`, `speed.fine`, `speed.fast` — pointer velocity bands.
- `socd = neutral` — opposite directions cancel.
- `tap_nudge = <pixels>` — deterministic short-tap movement.
- `max_catchup = <duration>` — caps delayed integration after scheduler stalls.

The mouse block is parsed into typed mouse configuration and then emitted as static generated C# for the Windows build. It is not a mandatory JSON runtime configuration.

## Target extensions

Portable behavior and target-specific configuration are deliberately separate.

The current target-extension syntax supports explicit target blocks such as:

- `target ikeyd`
- `target ikeyd-csharp`
- `target ikeyd-rust`
- `target qmk`
- `target zmk`

Target blocks may carry the supported `require`, `option`, and `native` declarations. They are additive target metadata; they may not silently redefine portable key bindings.

Unsupported target requirements must produce diagnostics rather than disappearing. See `target-extensions.md` and `portable-dsl-architecture.md` for the capability/backend model.

## Current tooling boundary

There are currently two different tool surfaces with different jobs:

1. **Canonical application build** — `dotnet build` runs `iKeyd.DslCompiler` against `config/hotkeySKG.ikeyd` and generates static C# under `obj/`.
2. **Legacy migration CLI** — `ikeyd check` / `ikeyd import` operate on the supported AHK-v1 importer subset; its `build` command is still reserved.

A friendly public `ikeyd check profile.ikeyd` / `ikeyd build profile.ikeyd` surface is still #64 work. Until that lands, documentation should not claim the AHK migration CLI already validates/builds arbitrary `.ikeyd` files.

Historical Python `.ikeyd -> JSON` compilers may remain useful for compatibility/migration tests. They are not the source of truth for normal builds.

## Grammar and validation rules

Current important rules include:

- rows and columns are 1-based.
- layouts must be declared before coordinates that use them in the current parser.
- a physical key identifier may appear only once inside a layout.
- direct canonical key names remain valid alongside position references.
- position aliases are resolved before static profile generation.
- a physical key may not have both ordinary and behavior mappings in the same keymap.
- behavior option names may not be duplicated.
- behavior arguments in the current general invocation grammar are identifier-like tokens; arbitrary direct Unicode/text literals use the `value = "..."` option form.
- the document may contain at most one top-level `clipboard` block and one top-level `mouse` block.
- invalid positions, unsupported clipboard/mouse values, malformed target requirements, and invalid first-class Unicode/text values produce compile/parser diagnostics instead of being silently ignored.
- the Windows static compiler preserves the canonical JIS109 physical-key universe rather than truncating mappings to the historical compact key set.

## Architecture invariant

Normal authoring/build flow is:

```text
.ikeyd
  -> parser / typed document
  -> semantic analysis / Behavior representation
  -> selected target/static representation
  -> runtime/backend
```

Do not introduce a required JSON hop to add new language features. Do not add a second runtime parser for source `.ikeyd`. JSON is optional compatibility/debug output only.
