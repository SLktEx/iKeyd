# Native AOT evaluation

## Status

Native AOT is **deferred for the current iKeyd Windows application**. The release build remains the normal .NET 8 Windows self-contained single-file publish.

The Windows app targets `net8.0-windows` and uses WinForms. Native AOT supports Windows as a platform, but the current WinForms application model is blocked by trimming compatibility before iKeyd can produce a candidate AOT executable.

The repository now pins the .NET 8 SDK family through `global.json` so this result is reproducible against the framework generation iKeyd actually targets.

## Reproduce

From a Windows development environment with a compatible .NET 8 SDK:

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

## Recorded result

GitHub Actions Native AOT evaluation run `33995015824` produced the following result on Windows:

- SDK: `.NET SDK 8.0.424`
- target: `net8.0-windows`, `win-x64`, WinForms
- normal self-contained single-file publish: **success**
- normal `iKeyd.exe`: `153,617,154` bytes (`146.501 MiB`)
- Native AOT publish: **failed**, exit code `1`
- Native AOT executable: **not produced**
- decision: **`defer-native-aot`**

The Native AOT probe stops with:

```text
error NETSDK1175: Windows Forms is not supported or recommended with trimming enabled.
```

`PublishAot=true` requires trimming, so the current WinForms application is rejected by the .NET 8 SDK before a viable AOT iKeyd binary exists. This is an application-model/framework compatibility blocker rather than an iKeyd hot-path implementation failure.

Because no AOT candidate executable was produced, the following comparisons are intentionally recorded as **not applicable** rather than estimated:

- startup time;
- first-key latency;
- steady-state latency;
- memory use;
- AOT binary size.

The full `report.json` and both publish logs are retained by the evaluation workflow artifact.

## Decision

**Keep the normal .NET 8 self-contained release. Do not enable Native AOT for the current WinForms executable.**

Do not use private WinForms trim/AOT suppression properties in production simply to force an AOT binary to build. That would bypass the compatibility guard without establishing correctness.

## Revisit conditions

Re-run the evaluation when any of these materially change:

- iKeyd moves to a newer .NET/WinForms version with improved Native AOT support;
- WinForms gains official trimming/Native AOT support appropriate for this application;
- the latency-sensitive runtime is split from the WinForms UI into a separate executable that can be AOT-published independently;
- iKeyd replaces the Windows UI/runtime boundary with an AOT-compatible application model.
