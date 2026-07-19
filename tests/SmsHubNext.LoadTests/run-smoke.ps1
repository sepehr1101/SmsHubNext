[CmdletBinding()]
param(
    [switch]$KeepEnvironment
)

$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$composeFile = Join-Path $testRoot 'compose.load.yml'
$resultsPath = Join-Path $testRoot 'results'
$publishPath = [System.IO.Path]::GetFullPath((Join-Path $testRoot '.publish'))
$projectPath = [System.IO.Path]::GetFullPath((Join-Path $testRoot '../../src/SmsHubNext/SmsHubNext.csproj'))

New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

if (-not $publishPath.StartsWith($testRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The load-test publish path resolved outside its test directory.'
}

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

& dotnet publish $projectPath --configuration Release --output $publishPath /p:UseAppHost=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker compose --file $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE."
    }
}

Invoke-Compose down --volumes --remove-orphans

try {
    Invoke-Compose up --detach --build sqlserver app

    $live = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri 'http://localhost:5081/health/live' -TimeoutSec 2 -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                $live = $true
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $live) {
        throw 'SmsHubNext did not become live within 120 seconds.'
    }

    Invoke-Compose run --rm seed
    $k6Failure = $null
    try {
        Invoke-Compose run --rm k6
    }
    catch {
        $k6Failure = $_
    }

    Invoke-Compose run --rm verify

    if ($null -ne $k6Failure) {
        throw $k6Failure
    }
}
finally {
    if (-not $KeepEnvironment) {
        Invoke-Compose down --volumes --remove-orphans
    }
}
