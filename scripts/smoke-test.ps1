param(
    [Parameter(Mandatory = $true)]
    [string] $BaseUrl,

    [Parameter(Mandatory = $true)]
    [string] $JwtToken,

    [string] $ApiKey = "",
    [string] $SenderLine = "",
    [string] $Recipient = "",
    [byte] $ProviderId = 1,
    [byte] $MessageTypeId = 1,
    [switch] $SkipRealSend
)

$ErrorActionPreference = "Stop"

function Join-Url([string] $Root, [string] $Path) {
    return $Root.TrimEnd("/") + "/" + $Path.TrimStart("/")
}

function Invoke-SmokeRequest(
    [string] $Name,
    [string] $Method,
    [string] $Url,
    [hashtable] $Headers,
    $Body = $null,
    [int[]] $ExpectedStatusCodes = @(200)
) {
    Write-Host "Checking $Name..."

    try {
        $parameters = @{
            Method = $Method
            Uri = $Url
            Headers = $Headers
            UseBasicParsing = $true
        }

        if ($null -ne $Body) {
            $parameters["ContentType"] = "application/json"
            $parameters["Body"] = ($Body | ConvertTo-Json -Depth 10)
        }

        $response = Invoke-WebRequest @parameters
        if ($ExpectedStatusCodes -notcontains [int] $response.StatusCode) {
            throw "$Name returned HTTP $($response.StatusCode), expected $($ExpectedStatusCodes -join ', ')"
        }

        if ([string]::IsNullOrWhiteSpace($response.Content)) {
            return $null
        }

        return $response.Content | ConvertFrom-Json
    }
    catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int] $_.Exception.Response.StatusCode
            if ($ExpectedStatusCodes -contains $statusCode) {
                return $null
            }
        }

        throw
    }
}

$base = $BaseUrl.TrimEnd("/")
$authHeaders = @{ Authorization = "Bearer $JwtToken" }

Invoke-SmokeRequest `
    -Name "health endpoint" `
    -Method "GET" `
    -Url (Join-Url $base "/health") `
    -Headers @{} `
    -ExpectedStatusCodes @(200) | Out-Null

Invoke-SmokeRequest `
    -Name "provider reference data with JWT" `
    -Method "GET" `
    -Url (Join-Url $base "/reference-data/providers") `
    -Headers $authHeaders `
    -ExpectedStatusCodes @(200) | Out-Null

Invoke-SmokeRequest `
    -Name "tariff quote" `
    -Method "POST" `
    -Url (Join-Url $base "/tariffs/quote") `
    -Headers $authHeaders `
    -Body @{
        providerId = $ProviderId
        messageTypeId = $MessageTypeId
        text = "Hello"
    } `
    -ExpectedStatusCodes @(200) | Out-Null

if ($SkipRealSend) {
    Write-Host "Skipping real send because -SkipRealSend was supplied."
    Write-Host "Smoke test passed."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "ApiKey is required unless -SkipRealSend is supplied."
}

if ([string]::IsNullOrWhiteSpace($SenderLine)) {
    throw "SenderLine is required unless -SkipRealSend is supplied."
}

if ([string]::IsNullOrWhiteSpace($Recipient)) {
    throw "Recipient is required unless -SkipRealSend is supplied."
}

$sendHeaders = @{ "X-Api-Key" = $ApiKey }
$clientBatchId = "deploy-smoke-" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()

$sendResponse = Invoke-SmokeRequest `
    -Name "real send endpoint" `
    -Method "POST" `
    -Url (Join-Url $base "/messages") `
    -Headers $sendHeaders `
    -Body @{
        senderLine = $SenderLine
        messageTypeId = $MessageTypeId
        clientBatchId = $clientBatchId
        messages = @(
            @{
                recipient = $Recipient
                text = "Smoke test"
            }
        )
    } `
    -ExpectedStatusCodes @(200, 202)

if ($null -eq $sendResponse -or $null -eq $sendResponse.batchId) {
    throw "Send endpoint did not return a batchId."
}

Invoke-SmokeRequest `
    -Name "created batch can be read with JWT" `
    -Method "GET" `
    -Url (Join-Url $base "/batches/$($sendResponse.batchId)") `
    -Headers $authHeaders `
    -ExpectedStatusCodes @(200) | Out-Null

Write-Host "Smoke test passed."
Write-Host "BatchId: $($sendResponse.batchId)"
