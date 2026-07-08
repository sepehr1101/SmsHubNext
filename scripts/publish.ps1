param(
    [string] $Configuration = "Release",
    [string] $OutputDir = "artifacts\publish\SmsHubNext",
    [string] $ZipPath = "artifacts\publish\SmsHubNext.zip",
    [string] $Runtime = "",
    [bool] $SelfContained = $false,
    [switch] $SkipUnitTests,
    [switch] $IncludeIntegrationTests,
    [switch] $NoZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$solution = Join-Path $repoRoot "SmsHubNext.slnx"
$project = Join-Path $repoRoot "src\SmsHubNext\SmsHubNext.csproj"
$unitTests = Join-Path $repoRoot "tests\SmsHubNext.UnitTests\SmsHubNext.UnitTests.csproj"
$integrationTests = Join-Path $repoRoot "tests\SmsHubNext.IntegrationTests\SmsHubNext.IntegrationTests.csproj"
$publishDir = Join-Path $repoRoot $OutputDir
$zipFile = Join-Path $repoRoot $ZipPath

Write-Host "Restoring packages..."
dotnet restore $solution

Write-Host "Building $Configuration..."
dotnet build $solution --configuration $Configuration --no-restore

if (-not $SkipUnitTests) {
    Write-Host "Running unit tests..."
    dotnet test $unitTests --configuration $Configuration --no-restore --no-build
}

if ($IncludeIntegrationTests) {
    Write-Host "Running integration tests. Docker/Testcontainers must be available..."
    dotnet test $integrationTests --configuration $Configuration --no-restore --no-build
}

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

$publishArgs = @(
    "publish",
    $project,
    "--configuration",
    $Configuration,
    "--no-restore",
    "--output",
    $publishDir,
    "/p:SelfContained=$SelfContained"
)

if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
    $publishArgs += @("--runtime", $Runtime)
}

Write-Host "Publishing to $publishDir..."
dotnet @publishArgs

if (-not $NoZip) {
    $zipParent = Split-Path -Parent $zipFile
    if (-not (Test-Path $zipParent)) {
        New-Item -ItemType Directory -Path $zipParent | Out-Null
    }

    if (Test-Path $zipFile) {
        Remove-Item -LiteralPath $zipFile -Force
    }

    Write-Host "Creating $zipFile..."
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipFile
}

Write-Host "Publish artifact is ready:"
Write-Host "  Folder: $publishDir"
if (-not $NoZip) {
    Write-Host "  Zip:    $zipFile"
}
