# Unicode and text output

`.ikeyd` distinguishes keyboard-key output from direct Unicode/text output.

These are different semantics:

```text
key output       -> a virtual/physical keyboard key such as A, Enter, Ctrl
Unicode output   -> exactly one Unicode scalar value
text output      -> one non-empty sequence of Unicode scalar values
```

Direct Unicode/text output is not a replacement for shortcuts. Use key/modifier behavior for `Ctrl+C`, `Alt+Enter`, and similar keyboard semantics.

## Authoring

The current literal-friendly syntax uses the existing behavior option block so arbitrary Unicode, whitespace, punctuation, and commas do not need a second argument grammar:

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

For identifier-only values, the compiler/runtime IR also accepts the compact programmatic form `UNICODE(A)` / `TEXT(hello)`, but option-backed literals are the normal authored form for arbitrary text.

The exact surface sugar can evolve under #64 without changing the semantic actions below.

## Semantic / Behavior IR

The shared behavior action vocabulary contains separate primitives:

- `SendKey`
- `SendUnicode`
- `SendText`

`SendUnicode` carries exactly one validated Unicode scalar. `SendText` carries a validated non-empty Unicode string. Windows UTF-16 code units do not leak into this semantic layer.

The semantic action constructors reject unpaired UTF-16 or otherwise invalid scalar/string values. Canonical `.ikeyd` literals are decoded by the existing JSON-string grammar first and the resulting `UNICODE` / `TEXT` values are validated during document compilation; malformed string syntax fails before normal runtime dispatch.

## Repeat policy

Actions carry explicit repeat metadata:

- `SendKey` -> `PhysicalKeyDown`
- `SendUnicode` -> `PhysicalKeyDown`
- `SendText` -> `Never`
- layer/modifier ownership transitions -> `Never`

The behavior runtime does not invent a repeat timer. It receives the repeated physical key-down events already produced by the platform keyboard path.

A held `UNICODE` mapping therefore emits once on initial key-down and again for each repeated physical down until key-up. A held multi-character `TEXT` mapping emits only once. Repeated downs do not replay `MO`, `MOD`, LT/MT layer/modifier transitions, or other non-repeatable state changes.

Supplementary characters such as `🦀` remain one logical Unicode action even though the Windows backend emits the required UTF-16 surrogate pair.

## Windows backend

Both direct Unicode and text actions lower to the existing `IKeyboardOutput.SendText` boundary. The native Windows implementation uses `SendInput` with `KEYEVENTF_UNICODE` and emits each UTF-16 code unit as a down/up pair in order.

This keeps keyboard-layout-independent direct text separate from the IME-oriented legacy S/K romaji path, which intentionally emits real key presses for Japanese composition.

## Portability

`SendUnicode` and `SendText` are target-neutral semantic requirements. Backends must either preserve them directly, use an explicit adaptation/capability mechanism, or reject them explicitly. They must not silently disappear.

The planned Rust Windows runtime can consume the same scalar/string + repeat-policy contract without inheriting C# string/Win32 implementation details. QMK/ZMK capability handling remains part of their backend work.
