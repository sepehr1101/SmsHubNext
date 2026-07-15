[CmdletBinding()]
param(
    [string] $Version,
    [string] $DestinationDirectory,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ([string]::IsNullOrWhiteSpace($DestinationDirectory)) {
    $DestinationDirectory = Join-Path $PSScriptRoot 'redist'
}

$settings = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'installer.settings.psd1')
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$settings.HostingBundleVersion
}

$metadataUrl = 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json'
$metadata = Invoke-RestMethod -Uri $metadataUrl -UseBasicParsing
$release = $metadata.releases | Where-Object {
    $_.'aspnetcore-runtime'.version -eq $Version
} | Select-Object -First 1

if ($null -eq $release) {
    throw ".NET ASP.NET Core release '$Version' was not found in official release metadata."
}

$hostingBundle = $release.'aspnetcore-runtime'.files | Where-Object {
    $_.name -eq 'dotnet-hosting-win.exe'
} | Select-Object -First 1

if ($null -eq $hostingBundle) {
    throw ".NET Hosting Bundle '$Version' was not found in official release metadata."
}

$destination = [System.IO.Path]::GetFullPath(
    (Join-Path $DestinationDirectory "dotnet-hosting-$Version-win.exe"))
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null

if (-not $Force -and (Test-Path -LiteralPath $destination -PathType Leaf)) {
    $cachedHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA512).Hash
    if (-not [string]::Equals(
        $cachedHash,
        [string]$hostingBundle.hash,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $destination -Force
    }
}

if ($Force -or -not (Test-Path -LiteralPath $destination -PathType Leaf)) {
    $temporary = "$destination.download"
    try {
        $downloadUrls = @([string]$hostingBundle.url)
        if ([string]$hostingBundle.url -like 'https://builds.dotnet.microsoft.com/*') {
            $downloadUrls += ([string]$hostingBundle.url).Replace(
                'https://builds.dotnet.microsoft.com/dotnet/',
                'https://dotnetcli.blob.core.windows.net/dotnet/')
        }

        $downloaded = $false
        foreach ($downloadUrl in $downloadUrls) {
            try {
                Invoke-WebRequest -Uri $downloadUrl -OutFile $temporary -UseBasicParsing
                $downloaded = $true
                break
            }
            catch {
                Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
            }
        }
        if (-not $downloaded) {
            throw 'The .NET Hosting Bundle could not be downloaded from official Microsoft endpoints.'
        }
        Move-Item -LiteralPath $temporary -Destination $destination -Force
    }
    finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}

$actualHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA512).Hash
if (-not [string]::Equals(
    $actualHash,
    [string]$hostingBundle.hash,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    Remove-Item -LiteralPath $destination -Force
    throw 'Downloaded .NET Hosting Bundle failed the official SHA-512 verification.'
}

Write-Output $destination
