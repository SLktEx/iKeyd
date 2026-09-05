param(
    [string]$OutputDirectory = "artifacts/native-aot-evaluation"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$normalDir = Join-Path $outputRoot "normal"
$aotDir = Join-Path $outputRoot "native-aot"
$normalLog = Join-Path $outputRoot "normal-publish.log"
$aotLog = Join-Path $outputRoot "native-aot-publish.log"
$reportPath = Join-Path $outputRoot "report.json"

if (Test-Path $outputRoot) {
    Remove-Item $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $normalDir -Force | Out-Null
New-Item -ItemType Directory -Path $aotDir -Force | Out-Null

function Invoke-DotnetCapture {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$LogPath
    )

    $started = [System.Diagnostics.Stopwatch]::StartNew()
    $lines = @(& dotnet @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $started.Stop()
    $lines | Set-Content -Path $LogPath -Encoding utf8

    return [pscustomobject]@{
        ExitCode = $exitCode
        DurationMs = [math]::Round($started.Elapsed.TotalMilliseconds, 2)
        Lines = $lines
        WarningCount = @($lines | Where-Object { $_ -match '(?i)warning|IL[0-9]{4}|trim|AOT' }).Count
    }
}

function Get-ExecutableInfo {
    param([string]$Directory)

    $exe = Get-ChildItem -Path $Directory -Filter "iKeyd.exe" -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $exe) {
        return $null
    }

    return [pscustomobject]@{
        Path = $exe.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
        Bytes = $exe.Length
        MiB = [math]::Round($exe.Length / 1MB, 3)
        Sha256 = (Get-FileHash -Path $exe.FullName -Algorithm SHA256).Hash
    }
}

$common = @(
    "publish", "src/iKeyd.App/iKeyd.App.csproj",
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false"
)

Write-Host "== Normal self-contained single-file publish =="
$normalArgs = $common + @(
    "-p:PublishSingleFile=true",
    "--output", $normalDir
)
$normal = Invoke-DotnetCapture -Arguments $normalArgs -LogPath $normalLog
if ($normal.ExitCode -ne 0) {
    Get-Content $normalLog
    throw "Normal release publish failed with exit code $($normal.ExitCode)."
}
$normalExe = Get-ExecutableInfo -Directory $normalDir
if ($null -eq $normalExe) {
    throw "Normal release publish succeeded but iKeyd.exe was not produced."
}

Write-Host "== Native AOT publish probe =="
$aotArgs = $common + @(
    "-p:PublishAot=true",
    "-p:StripSymbols=true",
    "--output", $aotDir
)
$aot = Invoke-DotnetCapture -Arguments $aotArgs -LogPath $aotLog
$aotExe = if ($aot.ExitCode -eq 0) { Get-ExecutableInfo -Directory $aotDir } else { $null }
$aotViable = $aot.ExitCode -eq 0 -and $null -ne $aotExe

$decision = if ($aotViable) {
    "candidate-needs-runtime-benchmark"
} else {
    "defer-native-aot"
}

$reason = if ($aotViable) {
    "A Native AOT candidate binary was produced. Runtime compatibility and latency/memory benchmarks are required before adoption."
} else {
    "The supported PublishAot=true probe did not produce a viable iKeyd.exe. Do not use private WinForms trim/AOT suppression switches for release builds."
}

$report = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    dotnetVersion = (& dotnet --version).Trim()
    runtimeIdentifier = "win-x64"
    framework = "net8.0-windows"
    applicationModel = "WinForms"
    normalPublish = [ordered]@{
        success = $true
        exitCode = $normal.ExitCode
        durationMs = $normal.DurationMs
        warningLikeLineCount = $normal.WarningCount
        executable = $normalExe
        log = "normal-publish.log"
    }
    nativeAotPublish = [ordered]@{
        success = $aotViable
        exitCode = $aot.ExitCode
        durationMs = $aot.DurationMs
        warningLikeLineCount = $aot.WarningCount
        executable = $aotExe
        log = "native-aot-publish.log"
    }
    decision = $decision
    reason = $reason
    measurements = [ordered]@{
        startup = if ($aotViable) { "pending" } else { "not-applicable-no-viable-aot-binary" }
        firstKeyLatency = if ($aotViable) { "pending" } else { "not-applicable-no-viable-aot-binary" }
        steadyStateLatency = if ($aotViable) { "pending" } else { "not-applicable-no-viable-aot-binary" }
        memory = if ($aotViable) { "pending" } else { "not-applicable-no-viable-aot-binary" }
        binarySize = [ordered]@{
            normalBytes = $normalExe.Bytes
            nativeAotBytes = if ($null -ne $aotExe) { $aotExe.Bytes } else { $null }
        }
    }
}

$report | ConvertTo-Json -Depth 8 | Set-Content -Path $reportPath -Encoding utf8

Write-Host ""
Write-Host "Native AOT evaluation decision: $decision"
Write-Host $reason
Write-Host "Normal executable: $($normalExe.MiB) MiB"
if ($null -ne $aotExe) {
    Write-Host "AOT executable: $($aotExe.MiB) MiB"
} else {
    Write-Host "AOT executable: not produced"
}
Write-Host "Report: $reportPath"

if ($env:GITHUB_STEP_SUMMARY) {
    @"
## iKeyd Native AOT evaluation

- Decision: **$decision**
- Normal publish: success ($($normalExe.MiB) MiB)
- Native AOT publish: $(if ($aotViable) { "success ($($aotExe.MiB) MiB)" } else { "not viable (exit $($aot.ExitCode))" })
- Reason: $reason

See the uploaded `native-aot-evaluation` artifact for `report.json` and full publish logs.
"@ | Add-Content -Path $env:GITHUB_STEP_SUMMARY
}

# An unsupported AOT candidate is a valid evaluation result. Only failures in the
# evaluation machinery / normal release publish should fail this script.
exit 0
