# iKeyd authoring DSL

The `.ikeyd` format is an authoring language that compiles to the existing iKeyd JSON profile. Runtime code does not parse the DSL and does not carry authoring-only layout metadata.

## Position-based key references

A layout gives stable coordinates to physical input keys:

```text
layout BASE {
    row Q W E R T Y U I O P
    row A S D F G H J K L SColon
    row Z X C V B N M Comma Dot Slash
}
```

Coordinates are 1-based, so `BASE[1,1]` is the physical `Q` position and `BASE[2,8]` is the physical `K` position.

Position references can be used anywhere a key input is accepted in a keymap:

```text
keymap S {
    BASE[1,1] = "-"
    combo BASE[2,8] + BASE[1,1] = "fa"
}
```

The compiler resolves those references before emitting JSON. The example above emits the same runtime keys as:

```text
keymap S {
    Q = "-"
    combo K + Q = "fa"
}
```

This keeps combos attached to finger positions rather than to the current character/output assignment. Changing what the BASE keys output therefore does not require rewriting the combo definitions.

### `POS[row,column]`

`POS[...]` is the canonical physical-position spelling. If no explicit `layout POS` exists, `POS[...]` aliases `layout BASE`:

```text
layout BASE {
    row Q W E
    row A S D
}

keymap S {
    combo POS[1,1] + POS[2,2] = "escape"
}
```

This resolves to the physical pair `Q + S`.

An explicit `layout POS` can be declared when the canonical physical geometry should be separate from a named authoring layout.

## Behavior invocations

A key mapping may invoke a first-class behavior instead of producing a string directly:

```text
layout BASE {
    row Q W E
}

keymap BASE {
    POS[1,1] = LT(NUM, Z)
    POS[1,2] = "w"
}
```

The authoring compiler keeps these two mapping kinds separate. The example above lowers approximately to:

```json
{
  "singleStroke": {
    "BASE": {
      "W": "w"
    }
  },
  "behaviors": {
    "BASE": {
      "Q": {
        "name": "LT",
        "arguments": ["NUM", "Z"]
      }
    }
  }
}
```

`LT` is not a special execution path in the runtime. The profile representation is compiled into a normal `BehaviorDefinition`, and standard `LT` uses the same generic Behavior runtime that future user-defined behaviors use.

In the current first syntax slice, behavior arguments are identifier-like tokens. Per-instance options, typed values and user-defined `behavior` bodies are specified in `behavior-dsl.md` but are intentionally added in later slices.

A physical key cannot simultaneously have a string mapping and a behavior mapping in the same keymap.

## Rules

- Rows and columns are 1-based.
- Layouts must be declared before a keymap uses their coordinates in the current prototype.
- A physical key identifier may appear only once inside a layout.
- Direct key identifiers such as `Q`, `K`, and `SColon` remain valid.
- `layout` blocks are compile-time-only and do not appear in generated JSON.
- Position references are resolved before behavior mappings are emitted.
- Out-of-range coordinates and unknown layouts are compile errors with source line numbers.

## Why position references exist

For a combo or behavior binding, the important thing is often "this physical finger position", not the letter currently assigned there. Position references let the BASE mapping evolve without coupling behavior or combo definitions to the current visible character layout.
