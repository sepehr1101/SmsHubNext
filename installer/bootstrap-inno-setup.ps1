[CmdletBinding()]
param(
    [string] $Version,
    [string] $DestinationDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$settings = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'installer.settings.psd1')
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$settings.InnoSetupVersion
}
if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $repoRoot 'artifacts\tools\InnoSetup6'
}

$destination = [System.IO.Path]::GetFullPath($DestinationDirectory)
$compilerPath = Join-Path $destination 'ISCC.exe'
if (Test-Path -LiteralPath $compilerPath -PathType Leaf) {
    $compilerSignature = Get-AuthenticodeSignature -FilePath $compilerPath
    if ($compilerSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $compilerSignature.SignerCertificate -or
        $compilerSignature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
        throw "Existing Inno compiler '$compilerPath' does not have the expected valid Pyrsys B.V. signature."
    }
    Write-Output $compilerPath
    exit 0
}

$toolsDirectory = [System.IO.Path]::GetDirectoryName($destination)
[System.IO.Directory]::CreateDirectory($toolsDirectory) | Out-Null
$installerPath = Join-Path $toolsDirectory "innosetup-$Version.exe"
$downloadUri = "https://files.jrsoftware.org/is/6/innosetup-$Version.exe"

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    Invoke-WebRequest -Uri $downloadUri -OutFile $installerPath -UseBasicParsing
}

$signature = Get-AuthenticodeSignature -FilePath $installerPath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $null -eq $signature.SignerCertificate -or
    $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
    Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue
    throw 'The downloaded Inno Setup package does not have the expected valid Pyrsys B.V. Authenticode signature.'
}

$arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="' + $destination + '"'
$process = Start-Process `
    -FilePath $installerPath `
    -ArgumentList $arguments `
    -Wait `
    -PassThru `
    -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Inno Setup bootstrap failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw "Inno Setup bootstrap completed but '$compilerPath' was not created."
}

Write-Output $compilerPath
