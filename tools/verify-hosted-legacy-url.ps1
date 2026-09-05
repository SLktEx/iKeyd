[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,

    [string]$ExpectedSha256 = "5492198ce403d796c8588b17419bce82a0e6de3961bb40896a875ee5dee359ea",

    [string]$OutputPath = (Join-Path $env:RUNNER_TEMP "hotkeySKG.exe")
)

$ErrorActionPreference = "Stop"

Invoke-WebRequest -Uri $Url -OutFile $OutputPath -MaximumRedirection 10

$actual = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = $ExpectedSha256.Trim().ToLowerInvariant()

if ($actual -ne $expected) {
    Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
    throw "Legacy executable SHA-256 mismatch. Expected $expected, actual $actual."
}

Write-Host "Verified hotkeySKG.exe SHA-256: $actual"
Write-Output $OutputPath
