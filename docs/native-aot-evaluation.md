# Native AOT evaluation

## Status

Native AOT is **experimental for iKeyd**. The release build remains the normal .NET 8 Windows self-contained single-file publish until this evaluation produces a viable candidate and the candidate passes runtime compatibility and performance checks.

The Windows app currently targets `net8.0-windows` and uses WinForms. Native AOT supports Windows as a platform, but WinForms has trimming/AOT compatibility constraints, so iKeyd must not assume that `PublishAot=true` is a supported release configuration.

## Reproduce

From a Windows development environment with the .NET 8 SDK:

```powershell
./tools/evaluate-native-aot.ps1
```

The script writes results to `artifacts/native-aot-evaluation/`:

- `report.json` — machine-readable result and decision
- `normal-publish.log` — normal release publish log
- `native-aot-publish.log` — Native AOT probe log
- `normal/` — normal release candidate
- `native-aot/` — Native AOT output when one is produced

The same experiment runs in `.github/workflows/native-aot-evaluation.yml` on Windows.

## Decision policy

The evaluation has two stages.

### Stage 1: build viability

Compare the supported configurations without private framework suppression switches:

1. normal self-contained single-file `win-x64` release publish;
2. `win-x64` publish with `PublishAot=true`.

If the AOT publish fails or does not produce `iKeyd.exe`, the decision is **defer Native AOT**. A failure here is an evaluation result rather than a CI failure; the full compiler/linker/trimmer output is preserved in the artifact.

Do not enable private WinForms trimming/AOT suppression properties merely to make the release build pass. They can hide unsupported code paths and would make the result unsuitable as a production compatibility guarantee.

### Stage 2: runtime and performance qualification

Only run this stage if Stage 1 produces a viable AOT executable. Before adopting Native AOT, compare it with the normal release on:

- application startup;
- first-key latency;
- steady-state key-processing latency;
- memory usage;
- binary size;
- keyboard hook/input behavior;
- diagnostics and crash behavior;
- behavior under CPU contention.

An AOT binary is adopted only if it remains behaviorally compatible and provides a meaningful improvement for iKeyd's latency-sensitive workload without unacceptable maintenance or debugging cost.

If no viable AOT binary is produced, startup/first-key/steady-state/memory comparisons are marked `not-applicable-no-viable-aot-binary`; they must not be inferred or fabricated.

## Revisit conditions

Re-run the evaluation when any of these materially change:

- iKeyd moves to a newer .NET/WinForms version with improved Native AOT support;
- WinForms gains official trimming/Native AOT support appropriate for this application;
- the latency-sensitive runtime is split from the WinForms UI into a separate executable that can be AOT-published independently;
- iKeyd replaces the Windows UI/runtime boundary with an AOT-compatible application model.

## Recorded result

The authoritative result for the current branch is the `report.json` produced by the Native AOT evaluation workflow. This section should be updated with the concrete CI result before issue #51 is closed.
