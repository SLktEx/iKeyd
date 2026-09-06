# Legacy compatibility inventory

Issue #54 makes the remaining work for #46 measurable. The legacy AHK source is scanned into a stable inventory and overlaid with conservative evidence-based coverage rules. The generated JSON is the machine-readable source for follow-up work; the Markdown rendering is the human review view.

The scanner is intentionally **not** a general AutoHotkey parser. It recognizes the behavior surfaces used by `hotkeySKG`: S/K single-stroke and chord declarations, hotkeys and key-up handlers, functions and labels, mode/layer/IME branches, Send expressions, window/mouse/media operations, clipboard/macro operations, process-specific contexts, and input-state/timer behavior.

## Run locally

With a plaintext legacy source available locally:

```powershell
python tools/analyze-legacy-compatibility.py C:\path\to\hotkeySKG.ahk `
  --coverage tests\compatibility\coverage-rules.json `
  --profile config\hotkeySKG.behavior.json `
  --scenarios tests\iKeyd.Compatibility.Tests\Scenarios `
  --json TestResults\compatibility-inventory\compatibility-matrix.json `
  --markdown TestResults\compatibility-inventory\compatibility-matrix.md
```

The repository does not need to contain a plaintext copy of the pinned AHK source. The `Legacy differential` workflow decrypts the pinned source in the runner's temporary directory, verifies its SHA-256, runs the scanner there, and uploads the two matrix files as an Actions artifact.

## Matrix fields

Every inventory entry has a stable content-derived `id`, source line, owner (`function:...`, `label:...`, `hotkey:...`, or `top-level`), optional `#If`/`#IfWin...` context, semantic tags, and these coverage dimensions:

- `implementation`: iKeyd implementation status
- `unit`: deterministic/unit regression evidence
- `scenario`: shared compatibility scenario evidence or an explicitly named deterministic regression layer
- `exeDiff`: compiled `hotkeySKG.exe` differential evidence
- `ahkDiff`: original AHK source differential evidence
- `realWindows`: real Windows / Japanese IME verification requirement or result
- `intentionalDifference`: whether a remaining difference is explicitly accepted

`scenario: "regression"` means the inventory entry is covered by an explicit deterministic regression layer identified in `evidence`, but is not being claimed as a shared legacy-oracle scenario. This is intentionally distinct from `scenario: "yes"`: it removes a false `scenario-missing` signal without pretending that compiled-EXE/AHK differential evidence exists. `exeDiff`, `ahkDiff`, and `realWindows` remain independent and continue to show the remaining oracle work.

The derived `classification` is one of the categories used by #46/#54, including `unknown`, `implementation-missing`, `scenario-missing`, `implemented-but-untested`, `real-windows-verification-required`, `partially-verified`, and `implemented-and-verified`.

## Coverage policy

`tests/compatibility/coverage-rules.json` is deliberately conservative. A broad feature area being implemented does not imply every legacy branch is verified. Rules may mark a category `partial`, but exact entries should only be upgraded when there is concrete evidence from tests, differential observations, or real-Windows verification.

A category-level `regression` status must be narrower than the user-facing feature shell. For example, `macro-operation` and `clipboard-operation` entries can point at deterministic parser/executor/history/controller regressions, while interactive `InputBox` UI and the `OnClipboardChange` label remain separate inventory work instead of being swept into the same status.

Later issues should link scenarios back to matrix entries using an `inventoryIds` array in scenario JSON. The scanner already indexes that field when present, so #55 can incrementally make scenario coverage explicit without redesigning the matrix format.

`unknown` entries are not failures of the scanner; they are the work queue. The completion condition for #46 is that relevant unknown/unintended-mismatch entries are eliminated or explicitly documented as intentional differences.

## Source-change detection

The report records the source SHA-256 and uses content/context-derived IDs rather than line numbers as identity. If the pinned source changes, regenerated JSON can be diffed to identify added/removed behavior without every ID changing merely because lines moved.
