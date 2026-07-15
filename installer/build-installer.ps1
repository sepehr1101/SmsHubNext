[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Version,
    [string] $HostingBundlePath,
    [string] $InnoCompilerPath,
    [switch] $SkipTests,
    [switch] $IncludeIntegrationTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$settings = Import-PowerShellDataFile (Join-Path $PSScriptRoot 'installer.settings.psd1')
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$settings.AppVersion
}

if ([string]::IsNullOrWhiteSpace($HostingBundlePath)) {
    $HostingBundlePath = & (Join-Path $PSScriptRoot 'download-hosting-bundle.ps1') `
        -Version ([string]$settings.HostingBundleVersion)
}
$HostingBundlePath = [System.IO.Path]::GetFullPath($HostingBundlePath)
if (-not (Test-Path -LiteralPath $HostingBundlePath -PathType Leaf)) {
    throw "Hosting Bundle '$HostingBundlePath' does not exist."
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $InnoCompilerPath = & (Join-Path $PSScriptRoot 'bootstrap-inno-setup.ps1') `
        -Version ([string]$settings.InnoSetupVersion)
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or
    -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw 'The pinned Inno Setup compiler (ISCC.exe) could not be bootstrapped or found.'
}

$solution = Join-Path $repoRoot 'SmsHubNext.slnx'
$project = Join-Path $repoRoot 'src\SmsHubNext\SmsHubNext.csproj'
$publishDirectory = Join-Path $repoRoot 'artifacts\installer\publish'
$outputDirectory = Join-Path $repoRoot 'artifacts\installer'
$unitTests = Join-Path $repoRoot 'tests\SmsHubNext.UnitTests\SmsHubNext.UnitTests.csproj'
$integrationTests = Join-Path $repoRoot 'tests\SmsHubNext.IntegrationTests\SmsHubNext.IntegrationTests.csproj'
$scriptTests = Join-Path $PSScriptRoot 'tests\InstallerScripts.Tests.ps1'

dotnet restore $solution
if ($LASTEXITCODE -ne 0) {
    throw 'Solution restore failed.'
}

dotnet restore $project --runtime win-x64
if ($LASTEXITCODE -ne 0) {
    throw 'Windows x64 publish restore failed.'
}

dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    throw 'Solution build failed.'
}

if (-not $SkipTests) {
    dotnet test $unitTests --configuration $Configuration --no-restore --no-build
    if ($LASTEXITCODE -ne 0) {
        throw 'Unit tests failed.'
    }

    & $scriptTests
    if ($LASTEXITCODE -ne 0) {
        throw 'Installer script tests failed.'
    }
}

if ($IncludeIntegrationTests) {
    dotnet test $integrationTests --configuration $Configuration --no-restore --no-build
    if ($LASTEXITCODE -ne 0) {
        throw 'Integration tests failed.'
    }
}

if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
    if (-not $resolvedPublishDirectory.StartsWith(
        [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts')),
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected publish directory '$resolvedPublishDirectory'."
    }
    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}

dotnet publish $project `
    --configuration $Configuration `
    --no-restore `
    --runtime win-x64 `
    --self-contained false `
    -p:Version=$Version `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Application publish failed.'
}

[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$innoScript = Join-Path $PSScriptRoot 'SmsHubNext.iss'
& $InnoCompilerPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDirectory" `
    "/DHostingBundlePath=$HostingBundlePath" `
    "/DOutputDir=$outputDirectory" `
    $innoScript
if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup compilation failed.'
}

$installerPath = Join-Path $outputDirectory "SmsHubNext-Setup-$Version-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer '$installerPath' was not created."
}

$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
$checksumPath = "$installerPath.sha256"
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$installerHash *$([System.IO.Path]::GetFileName($installerPath))$([Environment]::NewLine)",
    [System.Text.UTF8Encoding]::new($false))

Write-Output $installerPath
Write-Output $checksumPath
