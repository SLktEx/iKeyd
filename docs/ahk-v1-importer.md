# AHK v1 importer

Issue #10 adds a deliberately small AutoHotkey v1 importer for migrating configuration into iKeyd's existing platform-neutral `AutomationProfile`. It is **not** an AutoHotkey interpreter or compiler.

## Core/profile boundary

```text
iKeyd.Core
  Configuration/AutomationProfile + AutomationProfileJson
  Runtime/ConfiguredChordRuntime
  Input / Chords / Keymaps / Macro / Clipboard / Desktop

        ↑ reusable APIs

iKeyd.Profiles.HotkeySkg
  S/K/T/R mode policy
  M/H/S/K/A layer state machine

        ↑ Windows v1 compatibility profile

iKeyd.App + iKeyd.Windows
```

The Core assembly no longer owns the `hotkeySKG`-specific S/K/T/R or M/H/S/K/A policy. The legacy policy is isolated in the `iKeyd.Profiles.HotkeySkg` project, while `ConfiguredChordRuntime` can execute arbitrary named keymaps from an `AutomationProfile`.

## Initial supported AHK v1 subset

The importer recognizes the declaration patterns used heavily by `hotkeySKG.ahk`:

- `singleStroke<mode>_<key>=<output>` single-stroke mappings
- `kCmb<mode><n>:=flag_<key1>|flag_<key2>` plus `resultOfKCmb<mode><n>=<output>` chord mappings
- single-line `hotkey::action` declarations

`Send, ...` and other simple single-line hotkey actions are preserved as action text in the imported profile instead of being executed by the importer.

Compatibility semantics intentionally preserved:

- repeated single-stroke assignments are last-write-wins
- repeated unordered chord pairs remain declaration ordered, so the first mapping is effective when compiled by `Keymap<T>`
- duplicate declarations produce warnings instead of being silently discarded

## Diagnostics

`ikeyd check` and `ikeyd import` report diagnostics with source line numbers.

Important codes include:

- `AHK1001` malformed single-stroke mapping
- `AHK1002` malformed chord declaration/result
- `AHK1003` chord without a matching result assignment
- `AHK1004` multi-line hotkey not imported
- `AHK2001` repeated single-stroke assignment
- `AHK2002` repeated chord pair
- `AHK2003` repeated chord result assignment
- `AHK2004` repeated hotkey trigger
- `AHK9000` statement outside the initial importer subset

`AHK9000` is informational: unsupported syntax is made visible rather than guessed. Error diagnostics cause `ikeyd import` to abort without writing output.

## CLI

Build the solution first:

```bash
dotnet build iKeyd.sln --configuration Release
```

Check a source file and print diagnostics:

```bash
dotnet run --project src/iKeyd.Cli/iKeyd.Cli.csproj -- check path/to/hotkeySKG.ahk
```

Import the supported subset to the same `AutomationProfile` JSON format used by Core:

```bash
dotnet run --project src/iKeyd.Cli/iKeyd.Cli.csproj -- import path/to/hotkeySKG.ahk imported.json
```

The resulting JSON can be loaded by `AutomationProfileJson`. The Windows v1 tray application additionally requires the hotkeySKG compatibility profile conventions (notably S/K keymaps and an S/K/T/R startup mode), while future backends can consume other named keymaps through generic Core APIs.

The command name below is reserved but intentionally not implemented in #10:

```text
ikeyd build ...
```

A future issue can make `build` turn imported profiles into complete application/packages without expanding the initial importer into a full AHK compiler.

## Non-goals

The initial importer does not translate arbitrary AHK control flow, functions, labels, GUI code, COM calls, process/window automation, or the complete `Send` grammar. Those statements are reported as unsupported and can be migrated explicitly or covered by future importer extensions.
