# hotkeySKG compatibility matrix

This document tracks Windows compatibility against the pinned legacy `hotkeySKG.exe` and its original AutoHotkey v1 source.

The compatibility rule for issue #46 is:

1. Prefer the behavior of the pinned compiled `hotkeySKG.exe` used in practice.
2. Record the original `hotkeySKG.ahk` behavior independently.
3. Preserve legacy quirks unless a difference is explicitly documented as intentional.

`Implemented` means the iKeyd runtime has an implementation. `Automated` means the behavior is covered by an automated regression or differential test. `Real Windows` is reserved for interactive verification on a normal Windows installation, including Japanese IME where relevant.

| Area | Legacy behavior covered | Implemented | Automated | Real Windows | Notes |
| --- | --- | :---: | :---: | :---: | --- |
| S/K single strokes | production legacy map | yes | yes | pending | shared production/regression keymap |
| 40 ms chords | inclusive legacy chord window | yes | yes | partial | exact boundary remains deterministic Core coverage |
| S/K/T/R routing | legacy mode transitions | yes | yes | pending | T inherits the previous S/K map; R clears it |
| M/H/S/K/A layer state | ordered legacy layer state | yes | yes | pending | order-sensitive states are preserved; Alt+Kana menu-mask Ctrl tap is preserved |
| M/MH/HM/MS function layer | Q-W-E-R-T-Y-U-I-O-P, symbols, navigation, window, macro and clipboard functions | yes | expanding | pending | long-tail functions covered by runtime differential cases |
| KM/KMH/KHM/KMS | Ctrl-prefixed function-layer derivatives | yes | yes | pending | compiled EXE and original AHK confirm `^` applies to the next Send item, not the complete expanded sequence |
| AM/AMH/AHM/AMS | Alt-prefixed function-layer derivatives | yes | yes | pending | compiled EXE and original AHK confirm `!` applies to the next Send item, not the complete expanded sequence |
| SH/KSH/ASH | number/function-key layer | yes | yes | pending | representative runtime oracle cases cover SH, KSH and ASH; KSH intentionally means Ctrl+SHKey |
| SM mouse movement | normal, D/E fine movement, C quarter-screen movement | yes | partial | pending | C uses screen dimensions in the legacy script |
| SM mouse buttons | left/right/middle plus left toggle | yes | partial | pending | right-button branch preserves the legacy `s tate` typo behavior |
| SM wheel/media | wheel, Ctrl-wheel, volume and media transport | yes | partial | pending | backend/unit coverage present; broader oracle coverage remains |
| SM window-corner mouse moves | N top-left / M bottom-right | yes | partial | pending | legacy ignores windows whose X position is negative |
| Window operations | minimize/maximize/halves/topmost/opacity/caption | yes | yes | pending | modern Windows half placement uses Win+arrow like legacy |
| Window groups | add/remove/activate/reset group | yes | expanding | pending | stateful ordering/rebuild behavior remains under long-tail coverage expansion |
| Clipboard history | 20-entry history/picker/capture/paste | yes | yes | pending | preserves duplicate entries and the legacy selected-index (`clipNTmp`) promotion semantics |
| Macro H/Y slots | separate H/Y templates, shared repeat, increment/wait/calc/hk | yes | expanding | pending | execution is kept off the low-level hook thread |
| Send named keys | F1-F12, arrows, editing/navigation, AppsKey etc. | yes | yes | pending | |
| Send repeat tokens | e.g. `{LEFT 3}`, `{ENTER 2}` | yes | yes | pending | added under #46 |
| Send explicit key state | e.g. `{SHIFT DOWN}` / `{SHIFT UP}` | yes | yes | pending | added under #46 |
| Send vk/sc tokens | e.g. `{vkF3sc029}` | yes | partial | pending | parser preserves both fields; real SendInput low-level VK/scan identity differential is being validated |
| Send literal braces/specials | `{{}`, `{}}`, `{!}`, `{#}`, `{^}` | yes | yes | pending | |
| Macro Click token | coordinates/button/down/up/wheel forms used by legacy macro help | yes | expanding | pending | |
| ConsoleWindowClass Ctrl+V / Ctrl+X | legacy console menu paste/cut sequence | yes | yes | pending | context-specific hotkey is wired in the full Windows runtime |
| gsview Alt+E | WM_COMMAND 105 | yes | pending | pending | context-specific hotkey is implemented; dedicated automated message verification remains |
| Ctrl+Esc Suspend toggle | suspend all normal hotkeys and toggle back | yes | yes | pending | full runtime regression verifies suspend and resume |
| Japanese IME routing | IME on/off and Roma/Kana behavior | yes | partial | pending | hosted runner cannot replace interactive Japanese-IME verification |
| high-load hook/chord behavior | no unexpected missed/late chords under host load | implementation present | partial | pending | requires real-machine soak/latency verification |

## Confirmed compatibility quirks

- Alt+Kana (`!sc070`) causes AutoHotkey to emit a Ctrl menu-mask tap before entering the A layer; iKeyd preserves it.
- KSH dispatches Ctrl+SHKey rather than Win+SHKey.
- The SM right-button source typo (`if s tate = U`) prevents the right-button-down branch while still allowing release when the physical button is down.
- Function-layer K/A derivatives use an AHK Send prefix (`^` / `!`) whose lifetime is one following Send item. For example, `^{HOME}+{END}` releases Ctrl before Shift+End.
- Clipboard history is selected-index-sensitive and may contain duplicate strings; it is not a value-deduplicating MRU list.

## Completion rule

Issue #46 can be closed only when:

- every compatibility-target branch in the legacy source is represented here,
- every non-UI deterministic behavior has regression/differential coverage where practical,
- the pinned compiled EXE has no known unintended behavioral difference,
- Japanese IME and normal desktop usage have been checked on a real Windows machine,
- any remaining difference is explicitly marked intentional with its impact documented.
