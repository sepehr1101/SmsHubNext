[CmdletBinding()]
param(
    [ValidateRange(1, 100000)]
    [int]$MessageCount = 100000,

    [ValidateRange(1, 100)]
    [int]$BatchSize = 100,

    [ValidateRange(100, 60000)]
    [int]$RequestIntervalMs = 1000,

    [string]$SenderLine = $env:SMSHUBNEXT_SMSIR_SANDBOX_SENDER_LINE,

    [string]$SandboxRecipient = $env:SMSHUBNEXT_SMSIR_SANDBOX_RECIPIENT,

    [switch]$KeepEnvironment,

    [Parameter(Mandatory = $true)]
    [switch]$IUnderstandThisContactsSmsIrSandbox
)

$ErrorActionPreference = 'Stop'
$testRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$baseComposeFile = Join-Path $testRoot 'compose.load.yml'
$sandboxComposeFile = Join-Path $testRoot 'compose.smsir-sandbox.yml'
$resultsPath = Join-Path $testRoot 'results'
$publishPath = [System.IO.Path]::GetFullPath((Join-Path $testRoot '.publish'))
$projectPath = [System.IO.Path]::GetFullPath((Join-Path $testRoot '../../src/SmsHubNext/SmsHubNext.csproj'))
$sandboxApiKey = $env:SMSHUBNEXT_SMSIR_SANDBOX_API_KEY
$customerApiKey = 'smshub-smsir-sandbox-customer-key-2026'
$jwtKey = 'SmsHubNext_smsir_sandbox_JWT_key_2026_change_only_in_tests_123456789'
$jwtIssuer = 'smsir-sandbox-tests'
$jwtAudience = 'smsir-sandbox-tests'
$runId = '{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMddHHmmss'), [Guid]::NewGuid().ToString('N').Substring(0, 8)
$expectedBatchCount = $MessageCount / $BatchSize
$requestInterval = [TimeSpan]::FromMilliseconds($RequestIntervalMs)
$verifyTimeoutMinutes = [Math]::Max(
    10,
    [Math]::Ceiling($expectedBatchCount * $RequestIntervalMs / 60000) + 10)
$k6SummaryPath = Join-Path $resultsPath 'smsir-sandbox-k6.json'
$databaseSummaryPath = Join-Path $resultsPath 'smsir-sandbox-db.json'
$reportPath = Join-Path $resultsPath "smsir-sandbox-$MessageCount.json"

if ($MessageCount % $BatchSize -ne 0) {
    throw 'MessageCount must be divisible by BatchSize so the run has an exact expected batch count.'
}
if ([string]::IsNullOrWhiteSpace($sandboxApiKey)) {
    throw 'Set SMSHUBNEXT_SMSIR_SANDBOX_API_KEY to a Sandbox-type key before running this test.'
}
if ([string]::IsNullOrWhiteSpace($SenderLine) -or $SenderLine -notmatch '^\d+$') {
    throw 'SenderLine must be a numeric sender line enabled for the sms.ir Sandbox key.'
}
if ([string]::IsNullOrWhiteSpace($SandboxRecipient) -or $SandboxRecipient -notmatch '^09\d{9}$') {
    throw 'SandboxRecipient must be one controlled Iranian mobile number in 09xxxxxxxxx form.'
}
if (-not $publishPath.StartsWith($testRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The sandbox publish path resolved outside its test directory.'
}

New-Item -ItemType Directory -Path $resultsPath -Force | Out-Null

if (Test-Path -LiteralPath $publishPath) {
    Remove-Item -LiteralPath $publishPath -Recurse -Force
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker compose --file $baseComposeFile --file $sandboxComposeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE."
    }
}

function ConvertTo-Base64Url {
    param([byte[]]$Bytes)

    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-TestJwt {
    param(
        [string]$Key,
        [string]$Issuer,
        [string]$Audience
    )

    [DateTimeOffset]$now = [DateTimeOffset]::UtcNow
    [string]$headerJson = '{"alg":"HS256","typ":"JWT"}'
    [string]$payloadJson = @{
        iss = $Issuer
        aud = $Audience
        sub = 'smsir-sandbox-runner'
        iat = $now.ToUnixTimeSeconds()
        nbf = $now.AddMinutes(-1).ToUnixTimeSeconds()
        exp = $now.AddHours(4).ToUnixTimeSeconds()
    } | ConvertTo-Json -Compress

    [string]$header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($headerJson))
    [string]$payload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payloadJson))
    [string]$unsignedToken = "$header.$payload"
    [Security.Cryptography.HMACSHA256]$hmac =
        [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($Key))

    try {
        [string]$signature = ConvertTo-Base64Url (
            $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsignedToken)))
        return "$unsignedToken.$signature"
    }
    finally {
        $hmac.Dispose()
    }
}

function Wait-ForLiveApplication {
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try {
            $response = Invoke-WebRequest `
                -Uri 'http://localhost:5081/health/live' `
                -TimeoutSec 2 `
                -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    throw 'SmsHubNext did not become live within 120 seconds.'
}

function Initialize-SmsIrAccount {
    param([string]$Jwt)

    $headers = @{ Authorization = "Bearer $Jwt" }
    $accountBody = @{
        providerCode = 'smsir'
        displayName = "sms.ir Sandbox $runId"
        authType = 'ApiKey'
        settings = @{}
        secret = $sandboxApiKey
        isActive = $true
    } | ConvertTo-Json -Depth 5 -Compress

    $accountResponse = Invoke-RestMethod `
        -Uri 'http://localhost:5081/provider-accounts' `
        -Method Post `
        -Headers $headers `
        -ContentType 'application/json' `
        -Body $accountBody

    if (-not $accountResponse.success -or $accountResponse.data.id -le 0) {
        throw 'SmsHubNext did not create the encrypted sms.ir Sandbox provider account.'
    }

    $lineBody = @{
        providerId = 3
        lineNumber = $SenderLine
        isSharedLine = $true
        customerId = $null
        providerAccountId = [int]$accountResponse.data.id
        isActive = $true
    } | ConvertTo-Json -Compress

    $lineResponse = Invoke-RestMethod `
        -Uri 'http://localhost:5081/reference-data/sender-lines' `
        -Method Post `
        -Headers $headers `
        -ContentType 'application/json' `
        -Body $lineBody

    if (-not $lineResponse.success -or $lineResponse.data.id -le 0) {
        throw 'SmsHubNext did not associate the Sandbox sender line with its encrypted account.'
    }
}

function Invoke-ContractProbe {
    param([string]$Jwt)

    $probeClientBatchId = "smsir-sandbox-probe-$runId"
    $probeBody = @{
        senderLine = $SenderLine
        messageTypeId = 250
        clientBatchId = $probeClientBatchId
        messages = @(
            @{
                recipient = $SandboxRecipient
                text = "SmsHubNext sms.ir Sandbox contract probe $runId"
                clientCorrelatedId = "probe-$runId"
            }
        )
    } | ConvertTo-Json -Depth 6 -Compress

    $probeResponse = Invoke-RestMethod `
        -Uri 'http://localhost:5081/messages' `
        -Method Post `
        -Headers @{ 'X-Api-Key' = $customerApiKey } `
        -ContentType 'application/json' `
        -Body $probeBody

    if (-not $probeResponse.success -or $probeResponse.data.acceptedCount -ne 1) {
        throw 'The one-message sms.ir Sandbox contract probe was not accepted by SmsHubNext.'
    }

    [long]$batchId = $probeResponse.data.batchId
    $headers = @{ Authorization = "Bearer $Jwt" }
    for ($attempt = 0; $attempt -lt 180; $attempt++) {
        $batchResponse = Invoke-RestMethod `
            -Uri "http://localhost:5081/batches/$batchId" `
            -Method Get `
            -Headers $headers

        [int]$status = $batchResponse.data.status
        if ($status -eq 3) {
            return
        }
        if ($status -in @(4, 6, 7)) {
            [string]$serialized = $batchResponse | ConvertTo-Json -Depth 10 -Compress
            throw "The sms.ir Sandbox contract probe failed: $serialized"
        }

        Start-Sleep -Seconds 1
    }

    throw 'The sms.ir Sandbox contract probe did not complete within 180 seconds.'
}

function Read-DatabaseSummary {
    [string[]]$jsonLines = Get-Content -LiteralPath $databaseSummaryPath |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    [string]$json = ($jsonLines -join '').Trim()
    if (-not $json.StartsWith('{', [System.StringComparison]::Ordinal)) {
        throw "The SQL verifier did not produce a JSON object: $json"
    }

    return $json | ConvertFrom-Json
}

$environmentNames = @(
    'SMSHUBNEXT_SMSIR_SANDBOX_SENDER_LINE',
    'SMSHUBNEXT_SMSIR_SANDBOX_RECIPIENT',
    'SMSHUBNEXT_SMSIR_SANDBOX_RUN_ID',
    'SMSHUBNEXT_SMSIR_SANDBOX_MESSAGE_COUNT',
    'SMSHUBNEXT_SMSIR_SANDBOX_BATCH_SIZE',
    'SMSHUBNEXT_SMSIR_SANDBOX_EXPECTED_BATCH_COUNT',
    'SMSHUBNEXT_SMSIR_SANDBOX_REQUEST_INTERVAL',
    'SMSHUBNEXT_SMSIR_SANDBOX_REQUEST_INTERVAL_MS',
    'SMSHUBNEXT_SMSIR_SANDBOX_VERIFY_TIMEOUT_MINUTES'
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

& dotnet publish $projectPath --configuration Release --output $publishPath /p:UseAppHost=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$env:SMSHUBNEXT_SMSIR_SANDBOX_SENDER_LINE = $SenderLine
$env:SMSHUBNEXT_SMSIR_SANDBOX_RECIPIENT = $SandboxRecipient
$env:SMSHUBNEXT_SMSIR_SANDBOX_RUN_ID = $runId
$env:SMSHUBNEXT_SMSIR_SANDBOX_MESSAGE_COUNT = $MessageCount.ToString()
$env:SMSHUBNEXT_SMSIR_SANDBOX_BATCH_SIZE = $BatchSize.ToString()
$env:SMSHUBNEXT_SMSIR_SANDBOX_EXPECTED_BATCH_COUNT = $expectedBatchCount.ToString()
$env:SMSHUBNEXT_SMSIR_SANDBOX_REQUEST_INTERVAL =
    $requestInterval.ToString('c', [Globalization.CultureInfo]::InvariantCulture)
$env:SMSHUBNEXT_SMSIR_SANDBOX_REQUEST_INTERVAL_MS = $RequestIntervalMs.ToString()
$env:SMSHUBNEXT_SMSIR_SANDBOX_VERIFY_TIMEOUT_MINUTES = $verifyTimeoutMinutes.ToString()

try {
    Invoke-Compose down --volumes --remove-orphans
    [DateTime]$startedAtUtc = [DateTime]::UtcNow
    Invoke-Compose up --detach --build sqlserver app
    Wait-ForLiveApplication
    Invoke-Compose run --rm smsir-seed

    [string]$jwt = New-TestJwt -Key $jwtKey -Issuer $jwtIssuer -Audience $jwtAudience
    Initialize-SmsIrAccount -Jwt $jwt
    Invoke-ContractProbe -Jwt $jwt

    Write-Host "Sandbox contract probe passed. Starting $MessageCount messages in $expectedBatchCount batches."
    Invoke-Compose run --rm smsir-k6
    Invoke-Compose run --rm smsir-verify

    $k6Summary = Get-Content -LiteralPath $k6SummaryPath -Raw | ConvertFrom-Json
    $databaseSummary = Read-DatabaseSummary
    [DateTime]$finishedAtUtc = [DateTime]::UtcNow

    $report = [ordered]@{
        provider = 'smsir'
        environment = 'Sandbox'
        runId = $runId
        startedAtUtc = $startedAtUtc.ToString('O')
        finishedAtUtc = $finishedAtUtc.ToString('O')
        elapsedSeconds = ($finishedAtUtc - $startedAtUtc).TotalSeconds
        requestedMessageCount = $MessageCount
        batchSize = $BatchSize
        expectedBatchCount = $expectedBatchCount
        requestIntervalMilliseconds = $RequestIntervalMs
        senderLine = $SenderLine
        recipientSuffix = $SandboxRecipient.Substring($SandboxRecipient.Length - 4)
        contractProbePassed = $true
        database = $databaseSummary
        k6 = $k6Summary
    }

    $report | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $reportPath -Encoding utf8
    Write-Host "sms.ir Sandbox report: $reportPath"
}
finally {
    if (-not $KeepEnvironment) {
        Invoke-Compose down --volumes --remove-orphans
    }

    foreach ($name in $environmentNames) {
        [object]$previousValue = $previousEnvironment[$name]
        if ($null -eq $previousValue) {
            Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item -Path "Env:$name" -Value $previousValue
        }
    }
}
