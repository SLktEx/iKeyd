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

## Rules

- Rows and columns are 1-based.
- Layouts must be declared before a keymap uses their coordinates in the current prototype.
- A physical key identifier may appear only once inside a layout.
- Direct key identifiers such as `Q`, `K`, and `SColon` remain valid.
- `layout` blocks are compile-time-only and do not appear in generated JSON.
- Out-of-range coordinates and unknown layouts are compile errors with source line numbers.

## Why position references exist

For a combo, the important thing is often "these two physical finger positions", not the letters currently assigned there. Position references let the BASE mapping evolve without coupling every combo definition to the current visible character layout.
