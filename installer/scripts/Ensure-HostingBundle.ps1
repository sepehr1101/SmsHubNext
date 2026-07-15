[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath,

    [string] $RequiredMajorVersion = '10'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

function Test-HostingBundleInstalled {
    $ancmPath = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
    if (-not (Test-Path -LiteralPath $ancmPath -PathType Leaf)) {
        return $false
    }

    $dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
        return $false
    }

    $runtimes = & $dotnetPath --list-runtimes 2>$null
    return [bool]($runtimes | Where-Object {
        $_ -match "^Microsoft\.AspNetCore\.App $([regex]::Escape($RequiredMajorVersion))\."
    })
}

try {
    if (Test-HostingBundleInstalled) {
        Write-InstallerResult `
            -Path $ResultPath `
            -Success $true `
            -Code 'hosting_bundle.already_installed' `
            -Message '.NET Hosting Bundle is already installed.'
        exit 0
    }

    $fullInstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
    if (-not (Test-Path -LiteralPath $fullInstallerPath -PathType Leaf)) {
        throw 'The bundled .NET Hosting Bundle installer is missing.'
    }

    $exitCode = Invoke-InstallerProcess `
        -FilePath $fullInstallerPath `
        -ArgumentList @('/install', '/quiet', '/norestart') `
        -SuccessfulExitCodes @(0, 1641, 3010)

    if (-not (Test-HostingBundleInstalled)) {
        throw '.NET Hosting Bundle installation completed but ASP.NET Core Module or runtime was not detected.'
    }

    $restartRequired = $exitCode -in @(1641, 3010)
    Write-InstallerResult `
        -Path $ResultPath `
        -Success $true `
        -Code 'hosting_bundle.installed' `
        -Message '.NET Hosting Bundle was installed.' `
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
        -Code 'hosting_bundle.install_failed' `
        -Message (Get-InstallerSafeErrorMessage -ErrorRecord $_)
    exit 21
}
