[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$LegacyExe,

    [ValidateScript({ -not $_ -or (Test-Path -LiteralPath $_ -PathType Leaf) })]
    [string]$IKeydExe,

    [string]$PlanPath = (Join-Path $PSScriptRoot "..\tests\compatibility\real-windows-verification-plan.json"),

    [string]$ReportDirectory = (Join-Path $PWD "TestResults\real-windows"),

    [switch]$Interactive,
    [switch]$SkipDifferential,
    [switch]$SkipBackendE2E
)

$ErrorActionPreference = "Stop"

if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
    throw "#59 real-Windows verification must run in an interactive Windows session."
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-SafeValue([scriptblock]$Action) {
    try { return & $Action } catch { return $null }
}

function Get-RepositoryState {
    $commit = Get-SafeValue { (& git rev-parse HEAD 2>$null).Trim() }
    $status = Get-SafeValue { @(& git status --porcelain 2>$null) }
    return [ordered]@{
        commit = $commit
        dirty = if ($null -eq $status) { $null } else { $status.Count -gt 0 }
    }
}

function Get-ActiveKeyboardLayout {
    try {
        if (-not ("IKeydRealWindowsNative" -as [type])) {
            Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class IKeydRealWindowsNative {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
}
"@
        }
        $window = [IKeydRealWindowsNative]::GetForegroundWindow()
        $thread = [IKeydRealWindowsNative]::GetWindowThreadProcessId($window, [IntPtr]::Zero)
        $layout = [IKeydRealWindowsNative]::GetKeyboardLayout($thread).ToInt64() -band 0xffffffffL
        return ("0x{0:X8}" -f $layout)
    }
    catch {
        return $null
    }
}

$resolvedPlan = (Resolve-Path -LiteralPath $PlanPath).Path
$plan = Get-Content -LiteralPath $resolvedPlan -Raw | ConvertFrom-Json -Depth 32
if ($plan.schemaVersion -ne 1) {
    throw "Unsupported real-Windows verification plan schema version '$($plan.schemaVersion)'."
}

$resolvedLegacy = (Resolve-Path -LiteralPath $LegacyExe).Path
$legacySha = Get-Sha256 $resolvedLegacy
$expectedLegacySha = $plan.pinnedLegacyExeSha256.ToLowerInvariant()
if ($legacySha -ne $expectedLegacySha) {
    throw "Legacy executable SHA-256 mismatch. Expected $expectedLegacySha, actual $legacySha."
}

$resolvedIKeyd = $null
$iKeydSha = $null
if (-not [string]::IsNullOrWhiteSpace($IKeydExe)) {
    $resolvedIKeyd = (Resolve-Path -LiteralPath $IKeydExe).Path
    $iKeydSha = Get-Sha256 $resolvedIKeyd
}

$resolvedReportDirectory = [System.IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Force -Path $resolvedReportDirectory | Out-Null
$differentialDirectory = Join-Path $resolvedReportDirectory "legacy-differential"
$backendDirectory = Join-Path $resolvedReportDirectory "win32-backend"

$userLanguages = @()
try {
    $userLanguages = @(Get-WinUserLanguageList | ForEach-Object {
        [ordered]@{
            languageTag = $_.LanguageTag
            localizedName = $_.LocalizedName
            inputMethodTips = @($_.InputMethodTips)
        }
    })
}
catch {
    $userLanguages = @()
}

$japaneseImeConfigured = @($userLanguages | Where-Object {
    $_.languageTag -eq "ja-JP" -and $_.inputMethodTips.Count -gt 0
}).Count -gt 0

$os = Get-SafeValue { Get-CimInstance Win32_OperatingSystem }
$computer = Get-SafeValue { Get-CimInstance Win32_ComputerSystem }
$repoState = Get-RepositoryState
$environment = [ordered]@{
    capturedAtUtc = [DateTime]::UtcNow.ToString("o")
    osCaption = if ($os) { $os.Caption } else { $null }
    osVersion = if ($os) { $os.Version } else { [Environment]::OSVersion.VersionString }
    osBuildNumber = if ($os) { $os.BuildNumber } else { $null }
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    machineModel = if ($computer) { $computer.Model } else { $null }
    culture = (Get-Culture).Name
    uiCulture = (Get-UICulture).Name
    systemLocale = Get-SafeValue { (Get-WinSystemLocale).Name }
    userLanguages = $userLanguages
    japaneseImeConfigured = $japaneseImeConfigured
    activeKeyboardLayout = Get-ActiveKeyboardLayout
    powershellVersion = $PSVersionTable.PSVersion.ToString()
}

$automated = [ordered]@{
    legacyDifferential = [ordered]@{
        status = "not-run"
        reportDirectory = $differentialDirectory
        message = $null
    }
    backendCompatibility = [ordered]@{
        status = "not-run"
        reportDirectory = $backendDirectory
        message = $null
    }
}

if (-not $SkipDifferential) {
    Write-Host "Running real-IME legacy differential..."
    try {
        & (Join-Path $PSScriptRoot "run-legacy-differential.ps1") `
            -LegacyExe $resolvedLegacy `
            -ReportDirectory $differentialDirectory `
            -ExpectedSha256 $expectedLegacySha
        $automated.legacyDifferential.status = "pass"
    }
    catch {
        $automated.legacyDifferential.status = "fail"
        $automated.legacyDifferential.message = $_.Exception.Message
        Write-Warning "Automated legacy differential failed: $($_.Exception.Message)"
    }
}

if (-not $SkipBackendE2E) {
    Write-Host "Running safe real-Win32 backend E2E..."
    New-Item -ItemType Directory -Force -Path $backendDirectory | Out-Null
    $previousRealWindowsE2E = $env:IKEYD_REAL_WINDOWS_E2E
    try {
        $env:IKEYD_REAL_WINDOWS_E2E = "1"
        & dotnet test tests/iKeyd.Windows.Tests/iKeyd.Windows.Tests.csproj `
            --configuration Release `
            --filter "Category=RealWindowsCompatibilityE2E" `
            --results-directory $backendDirectory `
            --logger "trx;LogFileName=real-windows-backend.trx"
        if ($LASTEXITCODE -ne 0) {
            throw "Real-Win32 backend E2E exited with code $LASTEXITCODE."
        }
        $automated.backendCompatibility.status = "pass"
    }
    catch {
        $automated.backendCompatibility.status = "fail"
        $automated.backendCompatibility.message = $_.Exception.Message
        Write-Warning "Real-Win32 backend E2E failed: $($_.Exception.Message)"
    }
    finally {
        $env:IKEYD_REAL_WINDOWS_E2E = $previousRealWindowsE2E
    }
}

$checkResults = @()
$allChecks = @($plan.checks) + @($plan.supplementalChecks)
foreach ($check in $allChecks) {
    $status = "pending"
    $notes = ""

    if ($Interactive) {
        Write-Host ""
        Write-Host "=== $($check.title) [$($check.id)] ==="
        if ($check.requiredEnvironment) {
            Write-Host "Required: $(@($check.requiredEnvironment) -join ', ')"
        }
        foreach ($instruction in @($check.instructions)) {
            Write-Host " - $instruction"
        }
        if ($check.inventoryIds) {
            Write-Host "Inventory entries: $(@($check.inventoryIds).Count)"
        }

        do {
            $answer = (Read-Host "Result: [p]ass / [f]ail / [s]kip").Trim().ToLowerInvariant()
        } while ($answer -notin @("p", "pass", "f", "fail", "s", "skip"))

        $status = switch ($answer) {
            { $_ -in @("p", "pass") } { "pass"; break }
            { $_ -in @("f", "fail") } { "fail"; break }
            default { "skipped" }
        }
        $notes = Read-Host "Notes (optional)"
    }

    $checkResults += [ordered]@{
        id = $check.id
        title = $check.title
        mode = if ($check.mode) { $check.mode } else { "manual" }
        status = $status
        notes = $notes
        inventoryIds = @($check.inventoryIds)
    }
}

$inventoryIds = @($plan.checks | ForEach-Object { @($_.inventoryIds) })
$uniqueInventoryIds = @($inventoryIds | Sort-Object -Unique)
$statuses = @($checkResults | ForEach-Object { $_.status })
$manualComplete = $statuses.Count -gt 0 -and @($statuses | Where-Object { $_ -ne "pass" }).Count -eq 0
$complete = (
    $automated.legacyDifferential.status -eq "pass" -and
    $automated.backendCompatibility.status -eq "pass" -and
    $manualComplete -and
    -not [string]::IsNullOrWhiteSpace($iKeydSha) -and
    $japaneseImeConfigured -and
    $uniqueInventoryIds.Count -eq [int]$plan.expectedRealWindowsInventoryCount
)

$report = [ordered]@{
    schemaVersion = 1
    planId = $plan.planId
    issue = 59
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    repository = $repoState
    baseline = [ordered]@{
        compatibilityCommit = $plan.baselineCompatibilityCommit
        legacyDifferentialRun = $plan.baselineLegacyDifferentialRun
        pinnedLegacySourceSha256 = $plan.pinnedLegacySourceSha256
    }
    binaries = [ordered]@{
        legacy = [ordered]@{ path = $resolvedLegacy; sha256 = $legacySha }
        ikeyd = [ordered]@{ path = $resolvedIKeyd; sha256 = $iKeydSha }
    }
    environment = $environment
    automated = $automated
    checks = $checkResults
    summary = [ordered]@{
        expectedRealWindowsInventoryCount = [int]$plan.expectedRealWindowsInventoryCount
        plannedInventoryCount = $uniqueInventoryIds.Count
        passedChecks = @($statuses | Where-Object { $_ -eq "pass" }).Count
        failedChecks = @($statuses | Where-Object { $_ -eq "fail" }).Count
        skippedChecks = @($statuses | Where-Object { $_ -eq "skipped" }).Count
        pendingChecks = @($statuses | Where-Object { $_ -eq "pending" }).Count
        japaneseImeConfigured = $japaneseImeConfigured
        complete = $complete
    }
}

$reportPath = Join-Path $resolvedReportDirectory "verification-report.json"
$report | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $reportPath -Encoding utf8

Write-Host ""
Write-Host "Real-Windows verification report: $reportPath"
Write-Host "Inventory covered by plan: $($uniqueInventoryIds.Count)/$($plan.expectedRealWindowsInventoryCount)"
Write-Host "Automated differential: $($automated.legacyDifferential.status)"
Write-Host "Real-Win32 backend E2E: $($automated.backendCompatibility.status)"
Write-Host "Manual checks: pass=$($report.summary.passedChecks), fail=$($report.summary.failedChecks), skipped=$($report.summary.skippedChecks), pending=$($report.summary.pendingChecks)"
Write-Host "Complete: $complete"

if ($Interactive -and -not $complete) {
    Write-Warning "#59 verification is not complete. Keep the report and resolve failed/skipped/pending checks before closing the issue."
}
