# Legacy hotkeySKG behavior baseline

This document records compatibility-sensitive behavior discovered while reading the AutoHotkey v1 implementation used as the iKeyd migration source.

## Input modes

The legacy runtime has two pieces of mode state: `gmode` and `gimode`.

- `process1`: `SMODE`, `gimode = S`
- `process2`: `RMODE`, `gimode = ""`
- `process3`: `TMODE`, **does not change `gimode`**
- `process4`: `KMODE`, `gimode = K`

`TMODE` always routes input through the chord engine, so it inherits whichever `gimode` was active before entering it. Entering T from R therefore leaves an empty keymap name. This is a legacy quirk, not a design recommendation for the new core.

S and K use the chord engine only when the Windows IME is open and its conversion mode is one of `9, 19, 25, 27, 16`. R passes normal keys through.

## Layer state

The legacy `fstate` is an order-sensitive string assembled from `M`, `H`, `S`, `K`, and `A`. Examples include `MH`, `HM`, `KMS`, and `AMS`. Order matters in several branches.

Important tap/hold behavior includes:

- `H` tap sends Ctrl.
- `S` tap sends Space.
- `MH` then H-up sends Tab.
- `HM` then M-up sends Shift+Tab.
- `MS` then Space-up sends Enter.
- `HS` then Space-up sends Ctrl+Space.
- `KS` then Space-up sends Shift+Space.
- `KMS` then Space-up sends Ctrl+Enter.
- `AMS` then Space-up sends Alt+Enter.

Kana is also overloaded: from an empty state it toggles K, from K it clears K, but while M/H/S is active it emits Muhenkan/Henkan/Ctrl+Esc instead.

## Modified key dispatch

A non-empty `fstate` bypasses the normal S/K chord path. Notable mappings are captured in `hotkeySKG.runtime.json`, including the odd legacy `KSH -> Ctrl+SHKey` behavior.

## Desktop and mouse/media operations

Representative window actions, mouse movement/clicks, wheel actions, volume, and media controls are captured as platform behavior. The new implementation should keep these outside the OS-independent chord/mode core.

Older Windows versions use explicit `WinMove` branches for some snap operations, while newer versions send the corresponding Windows shortcuts.

### Intentional mouse differences after v0.4

The pinned legacy source contains `if s tate = U` in the SM+H right-button hold branch. Because that condition can never match the intended `state` variable, legacy cannot press the right button from the up state through this branch; it can only reach release-side behavior if the button is already held.

iKeyd intentionally does **not** reproduce that typo. SM+H remains a functional right-button hold toggle. Reproducing the typo would deliberately break an already usable control without providing a useful compatibility benefit.

Mouse movement is also intentionally no longer a fixed-distance copy of the legacy implementation. Issue #134 replaced stepped key-repeat-driven movement with the virtual-stick relative-pointer engine used by v0.4.0 and later. The key placement remains compatible, but movement timing and distance are deliberately smoother and therefore not byte-for-byte legacy behavior.

User-visible impact: only mouse behavior differs here; keyboard/chord/mode compatibility is unaffected. These are accepted product improvements rather than unresolved hosted compatibility failures. See #134 and parent compatibility tracking in #46.

## Clipboard history

The history has a maximum of 20 entries, prepends new clipboard contents at index 0, and pastes with Shift+Insert. The insertion algorithm is affected by `clipNTmp`, which is changed when a history row is selected. This makes the next clipboard insertion selection-sensitive; the fixture preserves that behavior so we can decide compatibility intentionally later.

## Macro behavior

The legacy macro language supports plain text, `{wait N}`, `{calc EXPR}`, `{hk STATEkey}`, looping, incrementing backtick-separated fields, and Escape cancellation.

`{hk ...}` only accepts M/S/H state letters in its regex even though other state letters exist elsewhere in the runtime. Arithmetic uses integer division for `/`.

## Known source inconsistencies

The regression fixtures intentionally record source oddities rather than silently repairing them. Examples include duplicate chord declarations, the duplicate `flag_Colon` assignment, T-mode map inheritance, and the `if s tate = U` typo in the right-button toggle branch.
