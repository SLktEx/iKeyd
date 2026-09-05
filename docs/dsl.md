# iKeyd DSL prototype

The `.ikeyd` format is the human-authored configuration for iKeyd. It compiles to the existing JSON profile schema today, and that JSON continues through the normal build-time profile compiler.

The language deliberately favors keyboard-specific constructs over a general-purpose scripting syntax.

## Profile

```text
profile hotkeySKG {
    runtime = "AutoHotkey v1.1.16.05"
    executable_lines = 1714
    chord_window = 40ms
}
```

## Physical layout

A layout gives stable names to physical positions and lets keymaps be written as rows instead of repetitive key/value declarations.

```text
layout BASE {
    row Q W E R T Y U I O P AT
    row A S D F G H J K L SColon Colon
    row Z X C V B N M Comma Dot Slash
}
```

Position references are 1-based:

```text
BASE[1,1]
BASE[2,4]
```

`POS[row,col]` aliases `BASE[row,col]` unless a dedicated `POS` layout is declared. This provides a physical-position spelling that stays useful when logical layouts change later.

## Keymaps

For a keymap that follows a declared physical layout, use `using` and `map`:

```text
keymap S using BASE {
    map {
        row "-", "ni", "ha", "`,", "ti", "gu", "ba", "ko", "ga", "hi", "ge"
        row "no", "to", "ka", "nn", "xtu", "ku", "u", "i", "si", "na", "/"
        row "su", "ma", "ki", "ru", "tu", "te", "ta", "de", ".", "bu"
    }
}
```

The number of `map` rows and the number of outputs in each row must exactly match the referenced layout. A mismatch is a compile error with filename and line number.

Keys outside the visual layout can stay explicit:

```text
1 = "1"
F1 = "{F1}"
```

Direct physical-position references are also valid:

```text
BASE[1,1] = "q"
POS[2,4] = "x"
```

## Combos

A one-off combo can be written directly:

```text
combo K + Q = "fa"
combo POS[2,8] + BASE[1,1] = "fa"
```

When many combos share one key, group them:

```text
combos K {
    Q = "fa"
    W = "go"
    E = "hu"
    R = "fi"
}
```

Grouped combos are only syntax sugar. Declaration order is preserved exactly.

Legacy hotkeySKG intentionally contains duplicate unordered chord pairs. iKeyd preserves those declarations and keeps the legacy first-declaration-wins behavior instead of silently deduplicating the source.

## Comments

`//` comments are supported outside quoted strings.

```text
combo F + U = "she"
combo F + U = "je" // intentional legacy duplicate
```

## Compile

```text
python tools/compile-ikeyd-dsl.py \
  config/hotkeySKG.ikeyd \
  config/hotkeySKG.behavior.generated.json
```

To prove compatibility with the current canonical JSON:

```text
python tools/compile-ikeyd-dsl.py \
  config/hotkeySKG.ikeyd \
  config/hotkeySKG.behavior.generated.json \
  --check-against config/hotkeySKG.behavior.json
```

## Current design boundary

The DSL is an authoring format, not a runtime scripting language. The intended pipeline is:

```text
.ikeyd
  -> parser / semantic validation
  -> canonical profile IR / JSON compatibility representation
  -> existing build-time profile compiler
  -> generated static tables / executable
```

The prototype currently focuses on the existing hotkeySKG S/K single-stroke and chord profile. Layer behaviors, tap/hold, mouse, media, window, clipboard and macro syntax remain follow-up language work.