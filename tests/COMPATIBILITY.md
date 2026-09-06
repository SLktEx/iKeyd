# Compatibility test strategy

Issue #17 deliberately uses multiple test layers instead of forcing every legacy behavior through one flaky realtime harness.

## Oracle identities

The legacy implementations are independent oracles:

- compiled `hotkeySKG.exe`, pinned by plaintext SHA-256,
- original `hotkeySKG.ahk`, pinned separately and executed with AutoHotkey v1.1.16.05,
- iKeyd on the real Windows `WH_KEYBOARD_LL` -> Core -> `SendInput` path.

Compiled-EXE and AHK-source reports are stored separately. A source/compiled difference is therefore observable instead of being silently collapsed.

## Coverage matrix

| Behavior | Primary test layer | Notes |
| --- | --- | --- |
| S/K single strokes | hosted three-oracle differential | `hosted-legacy` scenarios |
| S/K chords | hosted three-oracle differential | includes multiple chord groups |
| legacy duplicate chord quirks | hosted three-oracle differential | K `SColon+V -> nya`, `F+U -> she` verify first-match behavior |
| undefined chord fallback | hosted three-oracle differential | verifies observable fallback text |
| long continuous input | hosted three-oracle differential | multi-chord continuous sequence |
| high-speed input | hosted three-oracle differential | short inter-chord burst with real Windows timing |
| 39/40/41 ms boundary | deterministic Core compatibility tests | exact timing boundary is intentionally not delegated to scheduler-sensitive realtime sleeps |
| KeyDown / KeyUp handling | shared scenarios + real Windows keyboard E2E | scenarios contain explicit down/up timelines |
| M/H/S/K/A layer state and held-layer semantics | `LayerStateMachineTests` against the captured legacy runtime fixture | typed state preserves press order, release, tap/hold consumption, and representative layer actions |
| S/R/T/K mode routing | `InputModeStateTests` against the captured legacy runtime fixture | T preserves the previously selected S/K keymap like the legacy runtime |
| IME ON/OFF routing | `InputModeStateTests` + `WindowsInputMethodTests` | hosted legacy oracle uses T mode to bypass ephemeral-runner IME activation; the interactive legacy runner retains the real IME probe path |
| synthetic input recapture prevention | `WindowsKeyboardE2ETests` | real `WH_KEYBOARD_LL` receives foreign injected input and ignores iKeyd-marked `SendInput` |
| clipboard history | Core + Windows clipboard controller tests | feature compatibility is owned by #7 rather than keyboard timing scenarios |
| macro parsing/execution | Core macro parser/executor tests | feature compatibility is owned by #8 rather than keyboard timing scenarios |
| desktop/window/mouse operations | Core desktop semantics + Windows backend tests | feature compatibility is owned by #6 |

## Hosted T-mode adapter

GitHub-hosted Windows sessions do not reliably activate a newly installed Japanese IME without a login/reboot cycle. The hosted legacy adapter therefore uses control chords that already exist in the legacy implementation:

- S keymap: `M+3` -> T mode while retaining `gimode="S"`.
- K keymap: `M+4` -> K mode / `gimode="K"`, then `M+3` -> T mode while retaining `gimode="K"`.

This exercises the real compiled and source implementations while avoiding an artificial hosted-runner IME dependency. It does **not** claim to replace IME-specific tests.

## Real Windows final verification

Issue #59 is tracked separately from hosted CI. `tests/compatibility/real-windows-verification-plan.json` pins the 162 compatibility-matrix entries whose final state requires a real interactive Windows machine and groups them into seven verification areas. Physical-keyboard and CPU-load checks are supplemental completion requirements.

Run `tools/run-real-windows-verification.ps1` on the target Windows machine. It records OS/locale/input-method metadata, verifies binary hashes, runs the real-IME `LegacyDifferentialE2E` smoke comparison, collects manual group results, and writes `TestResults/real-windows/verification-report.json`.

A final report is accepted only when `tools/validate-real-windows-verification.py --require-complete` succeeds. Deterministic or hosted evidence must never be used to mark a #59 group as passed without the corresponding real-machine observation. See `docs/real-windows-verification.md` for the operator flow and cleanup requirements.

## Failure artifacts

Each differential JSON report contains:

- scenario id,
- complete initial state, including declared modifiers and IME state,
- complete timestamped input event timeline,
- expected observations,
- iKeyd observation and runner metadata,
- legacy observation and runner metadata,
- iKeyd-vs-expected, legacy-vs-expected, and direct iKeyd-vs-legacy differences,
- Windows execution diagnostics captured before and after each runner, including foreground window and physical modifier state.

The GitHub-hosted workflow uploads both compiled-EXE and AHK-source reports even when the comparison step fails.

## Execution policy

Normal CI always runs deterministic Core and backend tests. The `Legacy differential` workflow is separate, runs on GitHub-hosted `windows-latest`, decrypts the pinned legacy artifacts at runtime, verifies their identities, and executes the tagged realtime-safe scenarios against both legacy oracles.
