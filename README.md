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

iKeyd is an open-source keyboard customization runtime built around an authoring DSL that compiles to a static runtime profile. The first production target is **Windows without an AutoHotkey runtime**. The shared core is designed to expand to Linux, with **Wayland first, then X11**.

## Keyboard behavior in code

The `.ikeyd` authoring format keeps physical layout, key mappings, combos, and tap/hold behavior in text you can review and version.

```text
layout BASE {
    row Q W E
    row A S D
}

keymap BASE {
    POS[1,1] = LT(NUM, Z)
    POS[1,2] = "w"
    combo POS[1,1] + POS[2,2] = "escape"
}
```

Compile it to the runtime JSON profile with the public compiler entry point:

```bash
python tools/compile-ikeyd.py input.ikeyd output.json
```

The runtime does not parse authoring-only layout metadata; position references are resolved before the JSON profile is emitted.

## What iKeyd does

- **Key remapping** — map physical input keys to the output you want.
- **Position-based combos** — bind combos to stable physical positions with `BASE[row,col]` or `POS[row,col]`.
- **Layers and tap/hold behavior** — `LT(layer, tap_key)` uses one key for a tap key and a held layer.
- **Modifier tap** — `MT(modifier, tap_key)` uses one key for a tap key and a held modifier.
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

By default, the executable loads `iKeyd.json` from its own directory. The repository's canonical default runtime configuration is:

```text
config/hotkeySKG.behavior.json
```

To use another runtime configuration:

```powershell
iKeyd.exe --config C:\path\to\my-iKeyd.json
```

To override the startup mode:

```powershell
iKeyd.exe --mode K
```

Supported startup modes are `S`, `K`, `T`, and `R`.

## Download

Run **Windows package** from [GitHub Actions](https://github.com/SLktEx/iKeyd/actions/workflows/windows-package.yml), or push a `v*` tag. The workflow builds a self-contained `win-x64` package named:

```text
iKeyd-win-x64
```

The package includes the self-contained Windows executable and its default `iKeyd.json`, so a separate .NET or AutoHotkey installation is not required.

## Configuration

- [iKeyd DSL](docs/ikeyd-dsl.md) — the human-oriented authoring language.
- [JSON configuration schema](docs/json-configuration.md) — the runtime configuration model and validation rules.
- [`config/hotkeySKG.ikeyd`](config/hotkeySKG.ikeyd) — a real configuration used as the compatibility baseline.

Additional references:

- [Behavior DSL](docs/behavior-dsl.md)
- [AHK v1 importer](docs/ahk-v1-importer.md)
- [Compatibility inventory](docs/compatibility-inventory.md)
- [Windows keyboard backend](docs/windows-keyboard-backend.md)
- [Wayland notes](docs/wayland.md)

## Compatibility & testing

iKeyd treats the existing `hotkeySKG` behavior as a compatibility baseline. Regression fixtures, deterministic core tests, real Windows keyboard-hook tests, and hosted differential tests are used to keep the rewrite honest.

For the full Windows behavior notes, legacy-oracle setup, differential test commands, known limitations, and migration details, see **[Implementation & compatibility details](docs/implementation-details.md)**.

Run the normal test suite with:

```bash
dotnet test iKeyd.sln
```

## Platform roadmap

| Platform | Status |
| --- | --- |
| Windows | Primary target |
| Linux / Wayland | Next target |
| Linux / X11 | After Wayland |

## Brand

The public name is **iKeyd**. The product brand idea is **“Your keyboard, defined in code.”** The naming line **“I Key'd / I keyed it my way.”** remains part of the project's identity.

The canonical icon is `docs/assets/brand/ikeyd-icon.png` and the canonical combined logo/wordmark is `docs/assets/brand/ikeyd-logo.png`. Brand rules and the website visual direction are documented in [`docs/brand.md`](docs/brand.md).
