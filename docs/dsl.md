# iKeyd DSL

The `.ikeyd` format is the human-authored configuration for iKeyd. The normal app build now treats the DSL as the production profile source, compiles it to canonical JSON under `obj/`, and then feeds that JSON into the existing static profile compiler.

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

Named physical references are also supported:

```text
BASE.Q
BASE.SColon
```

`POS` is the canonical spelling for the physical keyboard. Resolution order is:

1. an explicitly declared `layout POS`
2. the selected built-in physical keyboard such as `keyboard JIS109`
3. `BASE` for legacy DSL files that do not declare a keyboard preset

This means existing files keep their old `POS -> BASE` behavior, while a JIS109 profile can use `POS.Ro` or `POS.Muhenkan` without coupling physical combos to a small logical `BASE` layout.

### Built-in JIS109 keyboard

A standard Japanese 109-key physical keyboard does not need to be written out by hand. Declare the built-in preset once:

```text
keyboard JIS109
```

The declaration registers the complete 109-key physical layout, including the function row, JIS-specific keys, left/right modifiers, navigation cluster, arrows and numeric keypad. Physical keys can then be referenced either through the preset name or through `POS`:

```text
JIS109.Ro
JIS109.Yen
JIS109.Henkan
JIS109.Muhenkan
JIS109.KatakanaHiragana
JIS109.ZenkakuHankaku
JIS109.NumpadEnter

POS.Ro
POS.Muhenkan
POS.NumpadEnter
```

For example, a compact logical layout can coexist with the full physical keyboard:

```text
profile myProfile {
    chord_window = 40ms
}

keyboard JIS109

layout BASE {
    row Q W E R T Y U I O P
    row A S D F G H J K L SColon
    row Z X C V B N M Comma Dot Slash
}

keymap S {
    POS.Ro = backslash
    combo POS.Muhenkan + POS.Ro = Escape

    // BASE still refers to the compact logical/typing geometry.
    combo BASE[1,1] + BASE[1,2] = something
}
```

The `JIS109` preset contains exactly 109 unique physical keys. `NumpadComma` remains a supported compact iKeyd key for hardware that provides it, but it is not part of the standard 109-key preset.

You can still declare custom `layout` blocks alongside a keyboard preset. A deliberately declared `layout POS` takes precedence over the preset when custom physical hardware needs its own geometry.

### JIS physical keys

iKeyd treats JIS-specific keys as compact physical key identities rather than migration-only string names. They can therefore participate in normal single mappings and combos without falling back to dictionary lookup.

Canonical names include:

```text
Ro
Yen
Henkan
Muhenkan
KatakanaHiragana
ZenkakuHankaku
Caret
LeftBracket
RightBracket
```

QMK/HID-style aliases are accepted at the key-ID boundary where useful, for example `INT1` for `Ro`, `INT3` for `Yen`, `INT4` for `Henkan`, `INT5` for `Muhenkan`, and `LANG5` for `ZenkakuHankaku`.

Full-size physical keys are also compact identities, including left/right modifiers, navigation keys, and the numeric keypad. Examples:

```text
LShift RShift
LCtrl RCtrl
LAlt RAlt
LGui RGui
Enter NumpadEnter
Home Numpad7
NumpadComma
```

On Windows, physical keyboard events are resolved from scan code plus the extended-key bit when available. This keeps distinctions such as `Enter` vs `NumpadEnter`, left vs right modifiers, and JIS-specific physical keys independent of the active logical keyboard layout. Virtual-key mapping remains a fallback for events without a usable scan code.

## Keymaps

For a keymap that follows a declared physical layout, use `using` and `map`:

```text
keymap S using BASE {
    map {
        row -  ni ha `, ti  gu ba ko ga hi ge
        row no to ka nn xtu ku u  i  si na /
        row su ma ki ru tu  te ta de .  bu
    }
}
```

The values are whitespace-separated output tokens. Quoting is optional for ordinary outputs and required only when the output itself contains whitespace or otherwise needs escaping:

```text
Q = fa
W = "hello world"
F1 = {F1}
```

The number of `map` rows and the number of outputs in each row must exactly match the referenced layout. A mismatch is a compile error with filename and line number.

Keys outside the visual layout can stay explicit:

```text
1 = 1
F1 = {F1}
Ro = Backslash
Yen = layer_symbol
```

Direct physical-position references are also valid:

```text
BASE[1,1] = q
POS[2,4] = x
JIS109.Ro = backslash
```

## Combos

A one-off combo can be written directly:

```text
combo K + Q = fa
combo POS[2,8] + BASE[1,1] = fa
combo POS.Muhenkan + POS.Ro = Escape
```

When many combos share one key, group them:

```text
combos K {
    Q = fa
    W = go
    E = hu
    R = fi
}
```

Grouped combos are only syntax sugar. Declaration order is preserved exactly.

Legacy hotkeySKG intentionally contains duplicate unordered chord pairs. iKeyd preserves those declarations and keeps the legacy first-declaration-wins behavior instead of silently deduplicating the source.

## Comments

`//` comments are supported outside quoted strings.

```text
combo F + U = she
combo F + U = je // intentional legacy duplicate
```

If the literal output itself contains `//`, quote it so it is not parsed as a comment. A literal comma should also be quoted as `","`; an unquoted comma is treated as a row separator.

## Production build

A normal build needs only the .NET SDK:

```text
dotnet build iKeyd.sln --configuration Release
```

The app build performs this pipeline automatically:

```text
config/hotkeySKG.ikeyd
  -> iKeyd.ProfileCompiler DSL parser
  -> obj/.../hotkeySKG.behavior.generated.json
  -> existing JSON profile validation/compiler
  -> obj/.../GeneratedProfile.g.cs
  -> iKeyd.exe
```

During the migration period, the generated JSON is also compared semantically with `config/hotkeySKG.behavior.json`. The build fails if the DSL changes legacy behavior accidentally. Once the DSL becomes the sole source of truth, that compatibility snapshot can be retired separately.

The profile compiler can also be invoked directly:

```text
dotnet tools/iKeyd.ProfileCompiler/bin/Release/net8.0/iKeyd.ProfileCompiler.dll \
  config/hotkeySKG.ikeyd \
  /tmp/GeneratedProfile.g.cs \
  --emit-json /tmp/hotkeySKG.behavior.generated.json \
  --check-against config/hotkeySKG.behavior.json
```

The Python `tools/compile-ikeyd-dsl.py` implementation remains useful as an independent authoring/reference compiler while the DSL is being stabilized; the production build does not depend on Python.

## Design boundary

The DSL is an authoring format, not a runtime scripting language. Runtime startup does not parse DSL or JSON; the release binary uses generated static profile data.

The current language focuses on the existing hotkeySKG S/K single-stroke and chord profile. Layer behaviors, tap/hold, mouse, media, window, clipboard and macro syntax remain follow-up language work.