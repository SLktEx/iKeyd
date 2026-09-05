# JSON configuration schema

iKeyd loads its automation profile from JSON. The Windows application reads `iKeyd.json` from the executable directory by default, or a different file passed with `--config`.

The repository's canonical default profile is `config/hotkeySKG.behavior.json`; normal builds copy it beside the executable as `iKeyd.json`.

## Minimal shape

```json
{
  "source": {
    "chordWindowMs": 40
  },
  "startupMode": "S",
  "singleStroke": {
    "S": {
      "Q": "-"
    },
    "K": {
      "Q": "o"
    }
  },
  "chords": {
    "S": [
      ["K", "Q", "fa"]
    ],
    "K": [
      ["K", "Q", "ti"]
    ]
  },
  "hotkeys": [
    {
      "trigger": "^j",
      "action": "Send, hello"
    }
  ]
}
```

`singleStroke` and `chords` are the only required top-level properties. `source`, `startupMode`, and `hotkeys` are optional.

## Top-level fields

| Field | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `source` | object | no | — | Source/profile metadata. Only `source.chordWindowMs` currently affects runtime behavior. |
| `startupMode` | string | no | `"S"` | Initial input mode. Core accepts any non-empty name; the current Windows app accepts only `S`, `K`, `T`, or `R`. |
| `singleStroke` | object | yes | — | Named keymaps containing single-key mappings. |
| `chords` | object | yes | — | Named keymaps containing two-key chord mappings. |
| `hotkeys` | array | no | `[]` | Imported/general hotkey bindings represented as `trigger` + `action` strings. |

Unknown top-level properties are currently ignored by `AutomationProfileJson.Parse`.

## `source`

```json
{
  "source": {
    "chordWindowMs": 40
  }
}
```

### `source.chordWindowMs`

- Type: integer
- Required: no
- Default: `40`
- Constraint: must be `>= 0`

This is the maximum interval used by the chord engine when deciding whether two key presses form a chord. The hotkeySKG compatibility profile uses `40` ms.

Other properties under `source` are ignored by the runtime parser. The canonical compatibility snapshot currently includes metadata such as `runtime` and `executableLines`; those fields document where the snapshot came from but do not configure iKeyd runtime behavior.

## `startupMode`

```json
{
  "startupMode": "S"
}
```

The Core profile model accepts any non-empty string so future profiles can define their own mode names.

The current Windows tray application is hotkeySKG-compatible and restricts this value to:

- `S`
- `K`
- `T`
- `R`

The Windows application also requires both the `S` and `K` keymaps to exist, even when another startup mode is selected.

`T` and `R` are routing modes; only `S` and `K` are keymap names in the current Windows profile.

## `singleStroke`

`singleStroke` is an object whose property names are keymap names. Each keymap is another object mapping a key ID to an output string.

```json
{
  "singleStroke": {
    "S": {
      "Q": "-",
      "W": "ni",
      "F1": "{F1}"
    },
    "K": {
      "Q": "o",
      "W": "sa"
    }
  }
}
```

Conceptually:

```text
singleStroke.<mode>.<key> = <output string>
```

Rules:

- every keymap present in `singleStroke` must also be present in `chords`, and vice versa;
- keymap names are matched case-insensitively;
- key IDs are trimmed and normalized to uppercase internally, so key lookup is case-insensitive;
- key IDs must not be empty or whitespace;
- output values must be JSON strings; an empty string is allowed;
- when a profile is built programmatically with duplicate single-key declarations, the last declaration wins. JSON objects themselves should not rely on duplicate property names.

## `chords`

`chords` is an object with the same keymap names as `singleStroke`. Each keymap contains an array of three-element arrays:

```json
{
  "chords": {
    "S": [
      ["K", "Q", "fa"],
      ["D", "Y", "wi"]
    ],
    "K": [
      ["K", "Q", "ti"]
    ]
  }
}
```

Each entry is:

```text
[first key, second key, output string]
```

For example:

```json
["K", "Q", "fa"]
```

means pressing `K` and `Q` inside the chord window resolves to `fa`.

Rules:

- each chord entry must contain exactly three elements;
- the first two elements are non-empty key IDs;
- the third element is an output string; an empty string is allowed;
- key IDs are case-insensitive;
- chord pairs are unordered: `["K", "Q", ...]` and `["Q", "K", ...]` represent the same pair;
- chord declaration order is significant when the same unordered pair appears more than once: the first declaration wins.

The first-wins behavior intentionally matches the legacy hotkeySKG lookup behavior and is why duplicate chords remain visible in the compatibility snapshot.

## `hotkeys`

`hotkeys` is optional.

```json
{
  "hotkeys": [
    {
      "trigger": "^j",
      "action": "Send, hello"
    }
  ]
}
```

Each entry must contain:

| Field | Type | Description |
| --- | --- | --- |
| `trigger` | string | Trigger expression imported/stored for the binding. |
| `action` | string | Action text imported/stored for the binding. |

The generic Core profile model preserves these bindings and the AHK v1 importer can emit them. They are not part of the S/K single-stroke/chord lookup itself.

## Keymap name matching

The parser builds the set of mode names from the union of the properties under `singleStroke` and `chords`. The two sections must define the same set of names, compared case-insensitively.

Valid:

```json
{
  "singleStroke": {
    "S": {},
    "Nav": {}
  },
  "chords": {
    "s": [],
    "nav": []
  }
}
```

Invalid because `Nav` has no matching `chords` section:

```json
{
  "singleStroke": {
    "S": {},
    "Nav": {}
  },
  "chords": {
    "S": []
  }
}
```

## Canonical hotkeySKG snapshot metadata

`config/hotkeySKG.behavior.json` serves two roles:

1. the default production profile copied to `iKeyd.json`;
2. the compatibility/regression snapshot generated from the legacy hotkeySKG AHK source.

Because of the second role, it contains additional documentation-only fields that are not part of the runtime profile contract:

```json
{
  "source": {
    "runtime": "AutoHotkey v1.1.16.05",
    "executableLines": 1714,
    "chordWindowMs": 40
  },
  "knownQuirks": {
    "duplicateChordPatterns": {},
    "duplicateFlagDefinitions": []
  }
}
```

`runtime`, `executableLines`, and `knownQuirks` are intentionally preserved by the legacy extraction snapshot, but the normal runtime parser ignores them. Serializing an `AutomationProfile` with `AutomationProfileJson.Serialize` emits only runtime profile data and does not round-trip those snapshot-only fields.

## Current parser contract

In compact form, the runtime-facing contract is:

```text
root := {
  source?: {
    chordWindowMs?: integer >= 0,
    ...ignored metadata
  },
  startupMode?: non-empty string,
  singleStroke: {
    <mode>: {
      <non-empty key id>: string
    }
  },
  chords: {
    <same mode>: [
      [<non-empty key id>, <non-empty key id>, string],
      ...
    ]
  },
  hotkeys?: [
    {
      trigger: string,
      action: string
    },
    ...
  ],
  ...ignored fields
}
```

For Windows v1, add these application-level requirements:

```text
startupMode ∈ { S, K, T, R }
keymaps include S and K
```

## Related implementation

The schema is implemented by:

- `src/iKeyd.Core/Configuration/AutomationProfileJson.cs`
- `src/iKeyd.Core/Configuration/AutomationProfile.cs`
- `src/iKeyd.Core/Chords/ChordKey.cs`
- `src/iKeyd.Core/Keymaps/Keymap.cs`

The Windows-specific startup-mode constraints are applied in `src/iKeyd.App/IKeydConfiguration.cs`.
