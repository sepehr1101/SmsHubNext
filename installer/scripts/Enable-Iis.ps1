[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

try {
    $restartRequired = $false
    $installWindowsFeature = Get-Command 'Install-WindowsFeature' -ErrorAction SilentlyContinue

    if ($null -ne $installWindowsFeature) {
        $serverFeatures = @(
            'Web-Server',
            'Web-Common-Http',
            'Web-Default-Doc',
            'Web-Static-Content',
            'Web-Http-Errors',
            'Web-Http-Logging',
            'Web-Request-Monitor',
            'Web-Filtering',
            'Web-Stat-Compression',
            'Web-Mgmt-Tools',
            'Web-Mgmt-Console'
        )
        $featureResult = Install-WindowsFeature `
            -Name $serverFeatures `
            -IncludeManagementTools `
            -ErrorAction Stop
        if (-not $featureResult.Success) {
            throw 'Windows Server could not enable all required IIS features.'
        }
        $restartRequired = [string]::Equals(
            [string]$featureResult.RestartNeeded,
            'Yes',
            [System.StringComparison]::OrdinalIgnoreCase)
    }
    else {
        $clientFeatures = @(
            'IIS-WebServerRole',
            'IIS-WebServer',
            'IIS-CommonHttpFeatures',
            'IIS-DefaultDocument',
            'IIS-StaticContent',
            'IIS-HttpErrors',
            'IIS-HealthAndDiagnostics',
            'IIS-HttpLogging',
            'IIS-RequestMonitor',
            'IIS-Security',
            'IIS-RequestFiltering',
            'IIS-Performance',
            'IIS-HttpCompressionStatic',
            'IIS-WebServerManagementTools',
            'IIS-ManagementConsole'
        )

        foreach ($featureName in $clientFeatures) {
            $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName -ErrorAction Stop
            if ($feature.State -ne 'Enabled') {
                $featureResult = Enable-WindowsOptionalFeature `
                    -Online `
                    -FeatureName $featureName `
                    -All `
                    -NoRestart `
                    -ErrorAction Stop
                if ($featureResult.RestartNeeded) {
                    $restartRequired = $true
                }
            }
        }
    }

    Write-InstallerResult `
        -Path $ResultPath `
        -Success $true `
        -Code 'iis.enabled' `
        -Message 'Required IIS features are enabled.' `
        -RestartRequired $restartRequired

    if ($restartRequired) {
        exit 3010
    }
    exit 0
}
catch {
    Write-InstallerResult `
        -Path $ResultPath `
        -Success $false `
        -Code 'iis.enable_failed' `
        -Message (Get-InstallerSafeErrorMessage -ErrorRecord $_)
    exit 20
}
