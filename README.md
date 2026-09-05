# iKeyd

iKeyd is a cross-platform rewrite of the existing `hotkeySKG` AutoHotkey v1 setup.

The first target is Windows without an AutoHotkey runtime. After the Windows implementation is stable, the shared core will be extended to Linux in this order:

1. Wayland
2. X11

## Migration strategy

The existing `hotkeySKG` behavior is treated as the compatibility baseline. Legacy behavior is captured as regression fixtures before it is reimplemented so that accidental behavior changes can be detected explicitly.

Current roadmap: #1.

## Regression spec

`tests/iKeyd.LegacySpec.Tests/Fixtures/hotkeySKG.behavior.json` records the effective S/K single-stroke maps, all declared S/K chord maps, the 40 ms chord window, and known legacy quirks.

The snapshot intentionally preserves odd or conflicting legacy behavior. Fixing those behaviors is a separate decision from reproducing them.

To regenerate the snapshot from an AHK v1 source file:

```bash
python tools/extract-legacy-spec.py path/to/hotkeySKG.ahk \
  tests/iKeyd.LegacySpec.Tests/Fixtures/hotkeySKG.behavior.json
```

## Compatibility scenarios

`tests/iKeyd.Compatibility.Tests/Scenarios` contains implementation-independent input scenarios for differential and Windows E2E testing.

Each scenario records:

- initial mode / IME / modifier state
- timestamped `keyDown` / `keyUp` input events
- expected externally observable text and key events
- tags that can later be used to select E2E suites

The same scenario contract is intended to be consumed by separate runners for:

- `hotkeySKG.ahk` on AutoHotkey v1
- the compiled legacy `hotkeySKG.exe`
- iKeyd

AHK source and compiled EXE results are kept as separate observations because they may not always behave identically.

The Windows scenario runner uses the real `WH_KEYBOARD_LL` hook and the real `SendInput` path. Real-time Windows tests use scenarios with wide timing margins; exact 39/40/41 ms boundary behavior stays in deterministic Core tests.

### Legacy executable differential test

The compiled legacy executable is intentionally not stored in this public repository. Tests discover it from a local path instead.

The current reference `hotkeySKG.exe` is pinned by SHA-256:

```text
5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea
```

On an interactive Windows session with a Japanese IME installed, the preferred command is:

```powershell
.\tools\run-legacy-differential.ps1 -LegacyExe 'C:\path\to\hotkeySKG.exe'
```

This command verifies the executable hash, runs each realtime-safe scenario through both the real iKeyd Windows path and the real legacy process, compares the two observations directly, and writes one JSON report per scenario to `TestResults\legacy-differential`.

A report separately records:

- iKeyd vs the shared expected result
- `hotkeySKG.exe` vs the shared expected result
- iKeyd vs `hotkeySKG.exe` directly
- runner metadata including the legacy executable SHA-256

To invoke only the legacy executable runner without the direct iKeyd-vs-EXE comparison:

```powershell
$env:IKEYD_LEGACY_EXE = 'C:\path\to\hotkeySKG.exe'
dotnet test tests/iKeyd.Windows.Tests/iKeyd.Windows.Tests.csproj --filter 'Category=LegacyExeE2E'
```

When intentionally testing a different legacy binary, pass its expected hash to the script or set `IKEYD_LEGACY_EXE_SHA256` explicitly.

Normal CI does not require or download the legacy binary. The executable and direct differential tests are opt-in when the reference binary is available on an interactive Windows machine.

Run all normal tests with:

```bash
dotnet test iKeyd.sln
```
