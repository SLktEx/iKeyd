# Real Windows compatibility verification (#59)

This is the final compatibility pass after hosted/deterministic #57 work. It must run in a normal interactive Windows session with a Japanese IME configured.

## What is pinned

`tests/compatibility/real-windows-verification-plan.json` is the acceptance inventory for this pass. It pins all 162 compatibility-matrix entries whose `realWindows` state is `required` and groups them into:

- Japanese IME routing/composition
- mode selectors / processF
- application-context hotkeys
- macro H/Y slots and editor
- clipboard history/picker
- window/desktop operations
- mouse/media operations

Two supplemental checks cover the physical keyboard path and behavior under CPU load. They are part of completion even though they are not individual matrix inventory rows.

## Before running

Use the real machine/session that represents normal iKeyd usage. Record/install as needed:

- Japanese IME with `ja-JP` input method configured
- the production `hotkeySKG.exe` pinned by SHA-256 in the plan
- the exact iKeyd executable being validated
- any application required for contextual checks (classic ConsoleWindowClass target and gsview-compatible target)

Close unrelated hotkey tools so they do not intercept the test input.

## Run

From the repository root in PowerShell 7 or Windows PowerShell:

```powershell
pwsh .\tools\run-real-windows-verification.ps1 `
  -LegacyExe C:\path\to\hotkeySKG.exe `
  -IKeydExe C:\path\to\iKeyd.exe `
  -Interactive
```

The runner:

1. verifies the legacy executable SHA-256,
2. records Windows build, locale, user input methods, active keyboard layout, repository commit and binary hashes,
3. runs the real-IME `LegacyDifferentialE2E` comparison,
4. walks each manual verification group and records pass/fail/skip plus notes,
5. writes `TestResults/real-windows/verification-report.json`.

Without `-Interactive`, manual checks remain `pending`; this is useful for collecting environment + automated differential evidence first. `-SkipDifferential` is available only when deliberately collecting manual evidence separately; such a report cannot be complete.

## Input diagnostics for #130 / #131 / #132

The most recent input diagnostics are persisted automatically to:

```text
%LOCALAPPDATA%\iKeyd\logs\input-diagnostics.log
```

The file is refreshed every two seconds from the bounded 256-entry in-memory ring buffer, so keyboard-hook processing never performs per-key disk I/O and the log does not grow without bound. On the next iKeyd start, the previous session is retained as:

```text
%LOCALAPPDATA%\iKeyd\logs\input-diagnostics.previous.log
```

The tray menu still contains `Save Input Diagnostics...` for making an explicit copy and `Reset Input State` for recovery.

The trace records physical VK/scan code, key down/up, physical held-key count and Shift/Ctrl/Alt/Win state, logical layer state, held layer triggers, suppressed keys, chord/timer state, output-path markers, reset markers and detected invariant violations. Logical S/K output text is not stored literally; only payload length and a fingerprint are retained.

When a mismatch occurs:

1. first preserve `%LOCALAPPDATA%\iKeyd\logs\input-diagnostics.log` or choose `Save Input Diagnostics...`,
2. record the matching verification group and visible symptom in the report notes,
3. if input remains stuck, choose `Reset Input State` to clear iKeyd's transient logical state,
4. preserve the diagnostic log with the verification report when reducing the mismatch to a regression.

For #130, the trace explicitly marks the `NonConvert + F` legacy `vkF3/sc029` path. For #131, it records whether 新下駄 output used ordinary keyboard-key injection (the IME composition path) or the legacy text/send fallback. For #132, compare the physical held-key/modifier summary with the logical layer/chord state around any reset or invariant-violation marker.

## Validate the report

A complete #59 report must pass:

```powershell
python .\tools\validate-real-windows-verification.py `
  --report .\TestResults\real-windows\verification-report.json `
  --require-complete
```

Completion requires all of the following:

- all 162 pinned real-Windows inventory IDs are present exactly once,
- every plan + supplemental check is `pass`,
- the real-IME legacy differential passed,
- Japanese IME was detected in the recorded user language configuration,
- the production legacy executable hash matches the pin,
- the tested iKeyd executable hash is present,
- the report itself marks `summary.complete=true`.

A failing check must include notes. Turn every reproducible mismatch into a minimal scenario/regression test before fixing it, then rerun the affected real-Windows group.

## Safety / cleanup

The automated differential uses a dedicated input sink. Manual groups can intentionally change clipboard contents, pointer position, CapsLock, media state and window state. Restore those states after each group. Prefer disposable test windows and non-critical media/clipboard content.

Do not mark a group `pass` from deterministic CI evidence alone; the purpose of this plan is the final real-machine observation.
