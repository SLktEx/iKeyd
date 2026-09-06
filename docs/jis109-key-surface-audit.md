# JIS109 key-surface audit

Issue #174 separates **physical key-surface completeness** from the legacy compatibility inventory.

`missing=0` in the legacy compatibility report means that every discovered legacy feature fragment is classified or covered. It does **not** prove that every physical key can travel through the iKeyd input/compiler/runtime pipeline. Treating those two questions as equivalent is the blind spot that allowed missing JIS/Windows keys to look complete.

## Canonical surface

`Jis109PhysicalKeyRegistry` is the target-neutral machine-readable source of truth for the 109 physical positions. The Windows mapping must cover that registry exactly once and preserve the identity required to distinguish positions that share a virtual key.

The Windows identity used by the runtime is:

- virtual key (`VK`)
- scan code
- extended-key bit

The scan/extended fields are not optional metadata for Japanese IME keys, sided modifiers, navigation keys, or numpad keys; they are part of the physical identity.

## Independent CI gates

Physical-key completeness is enforced independently of the legacy compatibility matrix by tests that verify:

1. the canonical registry contains exactly 109 distinct positions/names;
2. every canonical JIS109 key name compiles through the static `.ikeyd` profile path;
3. the Windows registry is symmetric across physical input -> `KeyCode` -> output -> physical input;
4. JIS punctuation uses the correct positions (`;`/`+` at scan `0x27`, `:`/`*` at scan `0x28`);
5. scan-only Japanese-key identities survive routing and diagnostics;
6. unknown ordinary keys are passed through instead of being silently swallowed;
7. the hot input/output path remains allocation-free where existing performance tests require it.

A future key added to the canonical compact universe must therefore update the compiler/runtime mappings or fail CI instead of being hidden behind a legacy `missing=0` result.

## Legacy migration boundary

Hard-coded compatibility actions for layers, mouse, media, window operations, and similar legacy behavior remain a separate migration concern tracked by #80. Their existence must not be used as evidence that the physical key surface is incomplete, and conversely their compatibility coverage must not be used as evidence that the physical key surface is complete.

## Real-Windows closeout

Automated tests cannot prove the firmware/driver identity reported by a real Japanese keyboard. Before #174 is closed, run the real-Windows smoke/probe on JIS106/109 hardware and confirm at least the following high-risk positions:

- 半角/全角
- カタカナ/ひらがな/ローマ字
- 無変換
- 変換
- `ろ`
- `¥`
- `;` and `:`
- left/right Ctrl, Alt, Shift
- numpad Enter and navigation-vs-numpad duplicates

Record the observed `VK`, scan code, and extended bit, then rerun the #59 real-Windows verification set. This hardware smoke is the only remaining evidence that cannot be supplied by repository CI.
