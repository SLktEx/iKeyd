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

Run tests with:

```bash
dotnet test iKeyd.sln
```
