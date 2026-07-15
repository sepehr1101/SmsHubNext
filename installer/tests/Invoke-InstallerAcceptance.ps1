[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,

    [Parameter(Mandatory = $true)]
    [string] $SetupRequestPath,

    [ValidateRange(1, 65535)]
    [int] $Port = 8080,

    [string] $SiteName = 'SmsHubNext',

    [string] $InstallPath = "$env:ProgramFiles\SmsHubNext",

    [string] $LogDirectory = "$env:SystemDrive\SmsHubNext-Acceptance",

    [switch] $UninstallAfterVerification
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Installer acceptance tests must run as Administrator on a disposable Windows VM.'
}

$installer = [System.IO.Path]::GetFullPath($InstallerPath)
$request = [System.IO.Path]::GetFullPath($SetupRequestPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer '$installer' does not exist."
}
if (-not (Test-Path -LiteralPath $request -PathType Leaf)) {
    throw "Setup response file '$request' does not exist."
}

[System.IO.Directory]::CreateDirectory($LogDirectory) | Out-Null
$installLog = Join-Path $LogDirectory 'fresh-install.log'
$arguments = @(
    '/VERYSILENT',
    '/SUPPRESSMSGBOXES',
    '/NORESTART',
    "/SETUPREQUEST=`"$request`"",
    "/DIR=`"$InstallPath`"",
    "/LOG=`"$installLog`""
) -join ' '

$setup = Start-Process `
    -FilePath $installer `
    -ArgumentList $arguments `
    -Wait `
    -PassThru `
    -WindowStyle Hidden
if ($setup.ExitCode -ne 0) {
    throw "Fresh installation failed with exit code $($setup.ExitCode). See '$installLog'."
}

$settingsPath = Join-Path $InstallPath 'appsettings.Production.json'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw 'Production settings were not created.'
}

Import-Module WebAdministration -ErrorAction Stop
if (-not (Test-Path -LiteralPath "IIS:\Sites\$SiteName")) {
    throw "IIS site '$SiteName' was not created."
}

$health = Invoke-WebRequest `
    -Uri "http://127.0.0.1:$Port/health" `
    -UseBasicParsing `
    -TimeoutSec 15
if ($health.StatusCode -ne 200) {
    throw "Health endpoint returned HTTP $($health.StatusCode)."
}

Write-Output 'Fresh-install acceptance checks passed.'

if ($UninstallAfterVerification) {
    $uninstaller = Get-ChildItem -LiteralPath $InstallPath -Filter 'unins*.exe' |
        Sort-Object Name |
        Select-Object -First 1
    if ($null -eq $uninstaller) {
        throw 'Inno uninstaller was not found.'
    }

    $uninstallLog = Join-Path $LogDirectory 'uninstall.log'
    $uninstall = Start-Process `
        -FilePath $uninstaller.FullName `
        -ArgumentList "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$uninstallLog`"" `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($uninstall.ExitCode -ne 0) {
        throw "Uninstall failed with exit code $($uninstall.ExitCode). See '$uninstallLog'."
    }
    if (Test-Path -LiteralPath "IIS:\Sites\$SiteName") {
        throw "IIS site '$SiteName' remained after uninstall."
    }
    if (-not (Test-Path -LiteralPath "$env:ProgramData\SmsHubNext\DataProtection-Keys")) {
        throw 'Uninstall unexpectedly removed the persistent Data Protection key directory.'
    }

    Write-Output 'Uninstall acceptance checks passed; persistent data was retained.'
}
