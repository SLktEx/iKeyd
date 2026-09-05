[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$LegacyExe,

    [string]$ReportDirectory = (Join-Path $PWD "TestResults\legacy-differential"),

    [string]$ExpectedSha256 = "5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea"
)

$ErrorActionPreference = "Stop"

$resolvedExe = (Resolve-Path -LiteralPath $LegacyExe).Path
$actualSha256 = (Get-FileHash -LiteralPath $resolvedExe -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = $ExpectedSha256.Trim().ToLowerInvariant()

if ($actualSha256 -ne $expected) {
    throw "Legacy executable SHA-256 mismatch. Expected $expected, actual $actualSha256."
}

$resolvedReportDirectory = [System.IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Force -Path $resolvedReportDirectory | Out-Null

$previousExe = $env:IKEYD_LEGACY_EXE
$previousSha = $env:IKEYD_LEGACY_EXE_SHA256
$previousReport = $env:IKEYD_DIFFERENTIAL_REPORT_DIR

try {
    $env:IKEYD_LEGACY_EXE = $resolvedExe
    $env:IKEYD_LEGACY_EXE_SHA256 = $expected
    $env:IKEYD_DIFFERENTIAL_REPORT_DIR = $resolvedReportDirectory

    Write-Host "Legacy executable: $resolvedExe"
    Write-Host "SHA-256:          $actualSha256"
    Write-Host "Reports:          $resolvedReportDirectory"

    dotnet test tests/iKeyd.Windows.Tests/iKeyd.Windows.Tests.csproj `
        --configuration Release `
        --filter "Category=LegacyDifferentialE2E"

    if ($LASTEXITCODE -ne 0) {
        throw "Legacy differential tests failed with exit code $LASTEXITCODE. See JSON reports in $resolvedReportDirectory."
    }

    Write-Host "Legacy differential comparison passed."
}
finally {
    $env:IKEYD_LEGACY_EXE = $previousExe
    $env:IKEYD_LEGACY_EXE_SHA256 = $previousSha
    $env:IKEYD_DIFFERENTIAL_REPORT_DIR = $previousReport
}
