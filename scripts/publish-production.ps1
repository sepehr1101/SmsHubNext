<#
.SYNOPSIS
Build the standard private Windows x64 Production package.
.EXAMPLE
.\scripts\publish-production.ps1
.EXAMPLE
.\scripts\publish-production.ps1 -IncludeIntegrationTests
#>
[CmdletBinding()]
param(
    [string] $ProductionSettingsPath = "artifacts\host-settings\appsettings.Production.json",
    [switch] $IncludeIntegrationTests
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$settingsPath = if ([System.IO.Path]::IsPathRooted($ProductionSettingsPath)) {
    $ProductionSettingsPath
} else {
    Join-Path $repoRoot $ProductionSettingsPath
}

# Fail before restore/build rather than silently changing the deployment format.
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "Production settings are missing: $settingsPath. Restore this private file or supply -ProductionSettingsPath; no generic package will be substituted."
}
$settingsJson = Get-Content -LiteralPath $settingsPath -Raw
$null = $settingsJson | ConvertFrom-Json

# Use the user's Dubai time, independently of the build computer's time zone.
$stamp = [DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(4)).ToString("yyyyMMdd-HHmmss", [System.Globalization.CultureInfo]::InvariantCulture)
$packageName = "SmsHubNext-Production-$stamp"
$outputRelative = "artifacts\publish\$packageName"
$zipRelative = "$outputRelative.zip"
$outputPath = Join-Path $repoRoot $outputRelative
$zipPath = Join-Path $repoRoot $zipRelative
if ((Test-Path -LiteralPath $outputPath) -or (Test-Path -LiteralPath $zipPath)) {
    throw "A package with this timestamp already exists. Run again in a new second; existing packages will not be overwritten."
}

$publishParameters = @{
    Configuration = "Release"
    Runtime = "win-x64"
    SelfContained = $true
    ProductionSettingsPath = $settingsPath
    OutputDir = $outputRelative
    ZipPath = $zipRelative
    IncludeIntegrationTests = $IncludeIntegrationTests.IsPresent
}
& (Join-Path $PSScriptRoot "publish.ps1") @publishParameters

# Check the deployment contract, without printing configuration values or secrets.
foreach ($requiredFile in @("SmsHubNext.exe", "SmsHubNext.dll", "coreclr.dll", "hostfxr.dll", "web.config", "appsettings.json", "appsettings.Production.json")) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputPath $requiredFile) -PathType Leaf)) {
        throw "Incomplete Production package: $requiredFile is missing."
    }
}
$runtimeConfig = Get-Content -LiteralPath (Join-Path $outputPath "SmsHubNext.runtimeconfig.json") -Raw | ConvertFrom-Json
$frameworkNames = @($runtimeConfig.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
if ($frameworkNames -notcontains "Microsoft.NETCore.App" -or $frameworkNames -notcontains "Microsoft.AspNetCore.App") {
    throw "The package must include both .NET and ASP.NET Core runtimes."
}
foreach ($settingsName in @("appsettings.json", "appsettings.Production.json")) {
    $packagedSettings = Get-Content -LiteralPath (Join-Path $outputPath $settingsName) -Raw
    if ($packagedSettings.Trim() -cne $settingsJson.Trim()) {
        throw "Packaged settings do not match the supplied Production settings."
    }
}
[xml] $webConfig = Get-Content -LiteralPath (Join-Path $outputPath "web.config") -Raw
$aspNetCore = $webConfig.SelectSingleNode("/configuration/location/system.webServer/aspNetCore")
if ($aspNetCore.GetAttribute("processPath") -ne ".\SmsHubNext.exe") {
    throw "IIS must launch the packaged Windows executable."
}
foreach ($environmentName in @("ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT")) {
    $variable = $aspNetCore.SelectSingleNode("environmentVariables/environmentVariable[@name='$environmentName']")
    if ($null -eq $variable -or $variable.GetAttribute("value") -ne "Production") {
        throw "IIS environment $environmentName must be Production."
    }
}
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $names = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredEntry in @("SmsHubNext.exe", "coreclr.dll", "web.config", "appsettings.Production.json")) {
        if ($names -notcontains $requiredEntry) { throw "ZIP is incomplete: $requiredEntry is missing." }
    }
    if (@($names | Where-Object { $_ -match '(^|[\\/])appsettings\.(Development|.*\.local)\.json$' }).Count -gt 0) {
        throw "ZIP must not contain Development or local settings."
    }
} finally {
    $archive.Dispose()
}

$sizeMb = [Math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 2)
Write-Host "Verified: Release / win-x64 / self-contained / Production settings / IIS Production."
Write-Host "Production ZIP ($sizeMb MB): $zipPath"
