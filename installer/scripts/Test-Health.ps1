[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 65535)]
    [int] $Port,

    [string] $HostName = '',

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [ValidateRange(1, 120)]
    [int] $Attempts = 30,

    [ValidateRange(1, 10)]
    [int] $DelaySeconds = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

$healthUri = "http://127.0.0.1:$Port/health"
$headers = @{}
if (-not [string]::IsNullOrWhiteSpace($HostName)) {
    $headers['Host'] = $HostName
}

for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    try {
        $response = Invoke-WebRequest `
            -Uri $healthUri `
            -Headers $headers `
            -UseBasicParsing `
            -TimeoutSec 10
        if ($response.StatusCode -eq 200) {
            Write-InstallerResult `
                -Path $ResultPath `
                -Success $true `
                -Code 'health.succeeded' `
                -Message 'SmsHubNext health check succeeded.'
            exit 0
        }
    }
    catch {
        if ($attempt -eq $Attempts) {
            Write-InstallerResult `
                -Path $ResultPath `
                -Success $false `
                -Code 'health.failed' `
                -Message (Get-InstallerSafeErrorMessage -ErrorRecord $_)
            exit 23
        }
    }

    if ($attempt -lt $Attempts) {
        Start-Sleep -Seconds $DelaySeconds
    }
}

Write-InstallerResult `
    -Path $ResultPath `
    -Success $false `
    -Code 'health.failed' `
    -Message 'SmsHubNext did not become healthy before the timeout.'
exit 23
