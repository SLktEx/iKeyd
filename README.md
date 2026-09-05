# iKeyd

iKeyd is a cross-platform rewrite of the existing `hotkeySKG` AutoHotkey v1 setup.

The first target is Windows without an AutoHotkey runtime. After the Windows implementation is stable, the shared core will be extended to Linux in this order:

1. Wayland
2. X11

Current roadmap: #1.

## Windows v1 quick start

The Windows application is a tray-resident `iKeyd.exe`. AutoHotkey is not required.

### Build locally

```powershell
dotnet restore iKeyd.sln
dotnet build iKeyd.sln --configuration Release
dotnet run --project src/iKeyd.App/iKeyd.App.csproj --configuration Release
```

The application loads `iKeyd.json` from the executable directory by default. The repository's canonical default configuration is:

```text
config/hotkeySKG.behavior.json
```

The build copies that file beside the executable as `iKeyd.json`.

See [JSON configuration schema](docs/json-configuration.md) for the complete field layout, validation rules, key/chord semantics, and the distinction between runtime configuration and legacy snapshot metadata.

### Download an Actions artifact

Run **Windows package** from the GitHub Actions tab, or push a `v*` tag. The workflow creates a self-contained `win-x64` package named:

```text
iKeyd-win-x64
```

The artifact contains the self-contained Windows executable and its default `iKeyd.json`, so a separate .NET or AutoHotkey installation is not required.

### Tray controls

Right-click the tray icon to use:

- **Mode → S / K / T / R** — switch the input mode.
- **Clipboard History...** — choose from the 20-entry clipboard history and paste the selection.
- **Macro...** — edit and execute a legacy-style macro.
- **Cancel Macro** — cancel a running macro, including a macro waiting in `{wait ...}`.
- **Exit** — stop the keyboard hook and exit iKeyd.

Double-clicking the tray icon also opens Clipboard History.

Only one iKeyd process is allowed per Windows user session.

### Command-line options

Use a different keymap/config file:

```powershell
iKeyd.exe --config C:\path\to\my-iKeyd.json
```

Override the startup mode:

```powershell
iKeyd.exe --mode K
```

Supported startup modes are `S`, `K`, `T`, and `R`.

## Windows v1 behavior

The Windows runtime currently wires together the shared Core and Windows backends for:

- the legacy S/K single-stroke and chord maps with the 40 ms inclusive chord window,
- automatic single-stroke resolution after the chord window expires,
- IME-aware S/K routing and T/R modes,
- M/H/S/K/A layer state and the representative modifier/function actions captured by the regression spec,
- window minimize/maximize/half placement/topmost/opacity/title-bar operations,
- SM mouse movement/click/scroll and media controls,
- 20-entry clipboard history and picker UI,
- macro `{wait ...}`, `{calc ...}`, `{hk ...}`, repeat/increment behavior, editing, and cancellation,
- protection against iKeyd re-capturing its own `SendInput` events.

The runtime intentionally preserves compatibility quirks recorded by the regression spec instead of silently fixing them.

### Known Windows v1 limitations

- The regression-covered legacy behavior is the compatibility target. Long-tail `hotkeySKG` function branches that are not yet represented by the current runtime spec may still require additional wiring.
- The Windows v1 legacy Send interpreter supports the named keyboard tokens used by the current config/macro UI (`F1`-`F12`, arrows, Home/End, PageUp/PageDown, Tab, Backspace, Delete, Enter, Insert, Escape, AppsKey, Space) plus `^`/`!`/`+`/`#` modifier prefixes. Unknown AHK Send tokens are left as literal text rather than pretending to support the complete AHK v1 Send grammar.
- `T` mode intentionally inherits the previously active S/K map. Switching through `R` clears that active map, matching the captured legacy behavior.
- Wayland and X11 backends are not part of Windows v1. Wayland is the next Linux target, followed by X11.

## Migration strategy

The existing `hotkeySKG` behavior is treated as the compatibility baseline. Legacy behavior is captured as regression fixtures before it is reimplemented so that accidental behavior changes can be detected explicitly.

`config/hotkeySKG.behavior.json` is both the production default keymap and the effective regression snapshot for S/K single-stroke/chord behavior. Tests link the same file into their fixture directory instead of maintaining a separate copy.

The snapshot intentionally preserves odd or conflicting legacy behavior. Fixing those behaviors is a separate decision from reproducing them.

To regenerate the snapshot from an AHK v1 source file:

```bash
python tools/extract-legacy-spec.py path/to/hotkeySKG.ahk \
  config/hotkeySKG.behavior.json
```

## Compatibility scenarios

`tests/iKeyd.Compatibility.Tests/Scenarios` contains implementation-independent input scenarios for differential and Windows E2E testing.

Each scenario records:

- initial mode / IME / modifier state
- timestamped `keyDown` / `keyUp` input events
- expected externally observable text and key events
- tags that select E2E suites

The same scenario contract is consumed by separate runners for:

- `hotkeySKG.ahk` on AutoHotkey v1
- the compiled legacy `hotkeySKG.exe`
- iKeyd

AHK source and compiled EXE results are always kept as separate observations because they may not behave identically.

The Windows scenario runner uses the real `WH_KEYBOARD_LL` hook and the real `SendInput` path. Real-time Windows tests use scenarios with wide timing margins; exact 39/40/41 ms boundary behavior stays in deterministic Core tests.

Scenarios tagged `hosted-legacy` are exercised against the real legacy oracles on GitHub-hosted Windows. Adding or changing compatibility scenarios automatically triggers the hosted differential workflow.

### Compiled legacy executable differential

The current reference `hotkeySKG.exe` is pinned by SHA-256:

```text
5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea
```

On Windows, the local one-command runner is:

```powershell
.\tools\run-legacy-differential.ps1 -LegacyExe 'C:\path\to\hotkeySKG.exe'
```

This command verifies the executable hash, runs each realtime-safe scenario through both the real iKeyd Windows path and the real legacy process, compares the two observations directly, and writes JSON reports.

### GitHub-hosted legacy oracle workflow

`.github/workflows/legacy-differential.yml` runs the compatibility oracles on GitHub-hosted `windows-latest`; no self-hosted runner is required.

The user-provided legacy artifacts are stored only in encrypted form:

```text
tests/legacy-binary/hotkeySKG.exe.gpg
tests/legacy-binary/hotkeySKG.ahk.gpg.asc
```

Both are decrypted with the repository Actions secret:

```text
LEGACY_EXE_GPG_PASSPHRASE
```

The workflow uses the GnuPG shipped with Git for Windows, then:

1. decrypts the compiled `hotkeySKG.exe` and verifies its pinned SHA-256,
2. decrypts the original `hotkeySKG.ahk` and verifies its independently pinned SHA-256,
3. downloads the AutoHotkey v1.1.16.05 interpreter from the commit behind `v1.1.16.05-dev.1` and verifies the exact Git blob,
4. runs the tagged scenarios through iKeyd and the compiled legacy EXE,
5. separately runs the same scenarios through iKeyd and the original AHK source,
6. uploads machine-readable reports under `legacy-differential-reports`.

Compiled-EXE reports and AHK-source reports live in separate directories so the two legacy oracles are never collapsed into one result.

For GitHub-hosted execution, the legacy process is switched to its existing T mode with the legacy `M + 3` control chord. T mode bypasses `IME_IfRomaKana()` while retaining the S chord table, which makes S-map keyboard compatibility testable on an ephemeral hosted runner without installing Japanese IME. IME-specific compatibility remains a separate responsibility of the interactive S/K-mode runner.

To invoke only the compiled legacy runner locally:

```powershell
$env:IKEYD_LEGACY_EXE = 'C:\path\to\hotkeySKG.exe'
dotnet test tests/iKeyd.Windows.Tests/iKeyd.Windows.Tests.csproj --filter 'Category=LegacyExeE2E'
```

When intentionally testing a different compiled legacy binary, pass its expected hash to the script or set `IKEYD_LEGACY_EXE_SHA256` explicitly.

Run all normal tests with:

```bash
dotnet test iKeyd.sln
```
