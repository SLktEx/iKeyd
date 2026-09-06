<p align="center">
  <img src="docs/assets/brand/ikeyd-icon.png" alt="iKeyd official icon" width="180">
</p>

<h1 align="center">iKeyd</h1>

<p align="center">
  <strong>I Key'd — I keyed it my way.</strong><br>
  Your keyboard, your workflow.
</p>

<p align="center">
  <a href="https://github.com/SLktEx/iKeyd/actions/workflows/windows-package.yml">Windows package</a>
  · <a href="docs/ikeyd-dsl.md">iKeyd DSL</a>
  · <a href="docs/json-configuration.md">JSON schema</a>
  · <a href="docs/implementation-details.md">Implementation details</a>
</p>

<p align="center">
  <img src="docs/assets/brand/readme-hero.jpg" alt="iKeyd — I keyed it my way." width="100%">
</p>

---

iKeyd is an open-source keyboard customization runtime built around the idea that the keyboard should adapt to you — not the other way around.

It is the compiled successor to the existing `hotkeySKG` AutoHotkey v1 setup. The first production target is **Windows without an AutoHotkey runtime**. The shared core is designed to expand to Linux, with **Wayland first, then X11**.

## What iKeyd does

- **Key remapping** — make ordinary keys behave the way you want.
- **Layers & modifiers** — compose richer behavior without needing more physical keys.
- **Combos / simultaneous presses** — use multiple keys as one expressive input.
- **Tap / hold behavior** — one physical key can do different things depending on how it is used.
- **Macros & commands** — automate text, window operations, app launches, mouse operations, and more.
- **IME-aware input** — preserve the Japanese-input behavior required by the existing workflow.
- **Keyboard mouse** — mouse movement, clicks, scrolling, and media controls from the keyboard.
- **Clipboard history** — tray-accessible clipboard history and paste flow.

The Windows runtime intentionally keeps compatibility quirks that are part of the captured `hotkeySKG` behavior instead of silently changing them.

## Quick start — Windows

The application is a tray-resident `iKeyd.exe`. AutoHotkey is not required.

```powershell
dotnet restore iKeyd.sln
dotnet build iKeyd.sln --configuration Release
dotnet run --project src/iKeyd.App/iKeyd.App.csproj --configuration Release
```

By default, the executable loads `iKeyd.json` from its own directory. The repository's canonical default configuration is:

```text
config/hotkeySKG.behavior.json
```

To use another configuration:

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

Two configuration paths are documented while the project evolves:

- [iKeyd DSL](docs/ikeyd-dsl.md) — the human-oriented configuration language.
- [JSON configuration schema](docs/json-configuration.md) — the complete runtime configuration model and validation rules.

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

The public name is **iKeyd** and the naming line is **“I Key'd / I keyed it my way.”**

The cyan `K` mark shown at the top of this README is the official iKeyd application and tray icon.

Brand assets and the visual direction live under [`docs/assets/brand/`](docs/assets/brand/) and are summarized in [`docs/brand.md`](docs/brand.md).

---

<p align="center"><sub>Good Keys. Better Developers.</sub></p>
