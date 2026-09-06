<p align="center">
  <img src="docs/assets/brand/ikeyd-icon.png" alt="iKeyd official icon" width="160">
</p>

<h1 align="center">
  <img src="docs/assets/brand/ikeyd-logo.png" alt="iKeyd" width="420">
</h1>

<p align="center">
  <strong>Your keyboard, defined in code.</strong><br>
  Write keyboard behavior as configuration you can read, version, and run.
</p>

<p align="center">
  <a href="https://slktex.github.io/iKeyd/">Website</a>
  · <a href="docs/ikeyd-dsl.md">iKeyd DSL</a>
  · <a href="https://github.com/SLktEx/iKeyd/actions/workflows/windows-package.yml">Windows package</a>
  · <a href="docs/implementation-details.md">Implementation details</a>
</p>

---

iKeyd is an open-source keyboard customization runtime built around the `.ikeyd` authoring DSL. The active host target is **Windows without an AutoHotkey runtime**. The same keyboard semantics are being shaped for the planned Rust runtime and firmware backends rather than tied to one C# implementation.

## Keyboard behavior in code

The `.ikeyd` format keeps physical layout, key mappings, combos, tap/hold behavior, and other supported keyboard semantics in text you can review and version.

```text
layout BASE {
    row Q W E
    row A S D
}

keymap S {
    POS[1,1] = LT(NUM, Z)
    POS[1,2] = "w"
    combo POS[1,1] + POS[2,2] = "escape"
}
```

Normal builds consume `.ikeyd` directly at build time:

```text
config/hotkeySKG.ikeyd
  -> typed DSL/profile + Behavior semantics
  -> GeneratedProfile.g.cs + GeneratedMouseProfile.g.cs
  -> iKeyd.exe
```

There is no required `.ikeyd -> JSON -> runtime` hop. Historical JSON tooling remains available for compatibility, migration, and debugging.

The public CLI can validate or compile any `.ikeyd` profile with the same canonical parser/generators used by the normal build:

```bash
ikeyd check profile.ikeyd
ikeyd build profile.ikeyd
```

`build` writes `GeneratedProfile.g.cs` and `GeneratedMouseProfile.g.cs` under `build/<profile-name>/` by default. Pass an explicit output directory as the second argument when needed. The older AHK migration flow remains available through `ikeyd check source.ahk` and `ikeyd import source.ahk output.json`.

## What iKeyd does

- **Key remapping** — map physical input keys to the output you want.
- **Position-based combos** — bind combos to stable physical positions with `BASE[row,col]` or `POS[row,col]`.
- **Tap/hold behavior** — `LT(layer, tap_key)` and `MT(modifier, tap_key)` share the generic Behavior runtime.
- **Momentary layers/modifiers** — `MO(layer)` and `MOD(modifier)` own resources for the physical hold duration with deterministic cleanup.
- **Shared runtime state** — declared bool/string state can be set, toggled, and read by bounded conditions and custom behaviors.
- **Unicode/text output** — first-class one-scalar Unicode and arbitrary text semantics with explicit repeat policy.
- **IME-aware input** — preserve the Japanese-input behavior required by the existing workflow.
- **Keyboard mouse** — continuous keyboard-driven pointer movement, clicks, scrolling, and related controls.
- **Clipboard history** — tray-accessible clipboard history with text/image support and authenticated encrypted persistence.

## Quick start — Windows

The application is a tray-resident `iKeyd.exe`. AutoHotkey is not required.

```powershell
dotnet restore iKeyd.sln
dotnet build iKeyd.sln --configuration Release
dotnet run --project src/iKeyd.App/iKeyd.App.csproj --configuration Release
```

The normal build compiles the repository's canonical profile:

```text
config/hotkeySKG.ikeyd
```

into static generated data inside the application build. The default executable therefore starts without reading or parsing a profile JSON file.

For compatibility/debugging, the Windows app still accepts an explicit JSON profile override:

```powershell
iKeyd.exe --config C:\path\to\compat-profile.json
```

That override is optional and is not the normal authoring/build path.

To override the startup mode:

```powershell
iKeyd.exe --mode K
```

Supported startup modes are `S`, `K`, `T`, and `R`.

A concurrent second iKeyd launch is rejected before it installs another keyboard hook.

## Download

Run **Windows package** from [GitHub Actions](https://github.com/SLktEx/iKeyd/actions/workflows/windows-package.yml), or use a published release.

The workflow produces a self-contained `win-x64` package named:

```text
iKeyd-win-x64
```

The normal package contains the executable with its compiled default profile. The package verification explicitly rejects an unexpected `iKeyd.json`; a separate .NET or AutoHotkey installation is not required.

## Configuration

- [iKeyd DSL](docs/ikeyd-dsl.md) — current human-authored language and normal build path.
- [`config/hotkeySKG.ikeyd`](config/hotkeySKG.ikeyd) — canonical compatibility/reference profile.
- [Shared runtime state](docs/runtime-state.md) — typed process-local state, mutation, conditions, reset, and hot-path rules.
- [Unicode/text output](docs/unicode-text-output.md) — direct Unicode/text semantics and repeat behavior.
- [Target extensions](docs/target-extensions.md) — explicit backend requirements/options/native extensions.
- [JSON compatibility format](docs/json-configuration.md) — compatibility/debug override format, not canonical authoring input.

Additional references:

- [Behavior DSL](docs/behavior-dsl.md)
- [Portable DSL architecture](docs/portable-dsl-architecture.md)
- [JIS109 key-surface audit](docs/jis109-key-surface-audit.md)
- [AHK v1 importer](docs/ahk-v1-importer.md)
- [Compatibility inventory](docs/compatibility-inventory.md)
- [Windows keyboard backend](docs/windows-keyboard-backend.md)
- [Wayland notes](docs/wayland.md)

## Compatibility & testing

iKeyd treats the existing `hotkeySKG` behavior as a compatibility baseline for the current Windows reference implementation. Regression fixtures, deterministic core tests, Windows keyboard-hook tests, and hosted differential tests are used to keep the rewrite honest.

For the full Windows behavior notes, legacy-oracle setup, differential test commands, known limitations, and migration details, see **[Implementation & compatibility details](docs/implementation-details.md)**.

Run the normal test suite with:

```bash
dotnet test iKeyd.sln
```

Real Windows/Japanese-IME acceptance remains separate from what hosted CI can prove.

## Active roadmap

The current execution order is broadly:

1. finish real Windows / Japanese-IME reference verification;
2. finish `.ikeyd` / Behavior semantics and remove remaining hardcoded profile bindings;
3. migrate the Windows runtime from .NET/C# to Rust;
4. add/finish ZMK and QMK backends with cross-backend conformance coverage.

Linux runtime expansion is currently deferred. Existing Wayland work remains in the repository, but new Linux runtime work is not the active roadmap until it is explicitly resumed against the then-current Rust architecture.

## Brand

The public name is **iKeyd**. The product brand idea is **“Your keyboard, defined in code.”** The naming line **“I Key'd / I keyed it my way.”** remains part of the project's identity.

The canonical icon is `docs/assets/brand/ikeyd-icon.png` and the canonical combined logo/wordmark is `docs/assets/brand/ikeyd-logo.png`. Brand rules and the website visual direction are documented in [`docs/brand.md`](docs/brand.md).
