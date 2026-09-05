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

On Windows, close any already-running copy of `hotkeySKG.exe`, make sure a Japanese IME is installed, then run:

```powershell
$env:IKEYD_LEGACY_EXE = 'C:\path\to\hotkeySKG.exe'
dotnet test tests/iKeyd.Windows.Tests/iKeyd.Windows.Tests.csproj --filter 'Category=LegacyExeE2E'
```

The runner creates a focused IME-capable test window, launches the real legacy process, feeds it the same realtime-safe compatibility scenarios, captures keyboard output emitted by the executable, and compares that output with the shared scenario expectation.

When intentionally testing a different legacy binary, pin its expected hash explicitly:

```powershell
$env:IKEYD_LEGACY_EXE_SHA256 = '<sha256>'
```

Normal CI does not require or download the legacy binary; `LegacyExeE2E` tests are opt-in when `IKEYD_LEGACY_EXE` is available.

Run all normal tests with:

```bash
dotnet test iKeyd.sln
```
