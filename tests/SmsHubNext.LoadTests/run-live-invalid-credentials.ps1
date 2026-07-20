[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('magfa', 'kavenegar')]
    [string]$Provider,

    [Parameter(Mandatory = $true)]
    [switch]$IUnderstandThisContactsLiveProvider
)

$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $testRoot '../..'))
$projectPath = Join-Path $repositoryRoot 'tests/SmsHubNext.IntegrationTests/SmsHubNext.IntegrationTests.csproj'
$resultsPath = Join-Path $testRoot 'results'
$reportPath = Join-Path $resultsPath "live-invalid-credentials-$Provider.json"

New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

$previousEnabled = $env:SMSHUBNEXT_RUN_LIVE_INVALID_CREDENTIALS
$previousProvider = $env:SMSHUBNEXT_LIVE_PROVIDER
$previousReportPath = $env:SMSHUBNEXT_LIVE_REPORT_PATH

try {
    $env:SMSHUBNEXT_RUN_LIVE_INVALID_CREDENTIALS = 'true'
    $env:SMSHUBNEXT_LIVE_PROVIDER = $Provider
    $env:SMSHUBNEXT_LIVE_REPORT_PATH = $reportPath

    & dotnet test $projectPath `
        --configuration Release `
        --filter 'FullyQualifiedName~InvalidCredentialsLiveTests' `
        --logger 'console;verbosity=normal'

    if ($LASTEXITCODE -ne 0) {
        throw "Live invalid-credential test failed with exit code $LASTEXITCODE."
    }

    Write-Host "Live probe report: $reportPath"
}
finally {
    $env:SMSHUBNEXT_RUN_LIVE_INVALID_CREDENTIALS = $previousEnabled
    $env:SMSHUBNEXT_LIVE_PROVIDER = $previousProvider
    $env:SMSHUBNEXT_LIVE_REPORT_PATH = $previousReportPath
}
