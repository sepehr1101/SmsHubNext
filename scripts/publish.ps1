param(
    [string] $Configuration = "Release",
    [string] $OutputDir = "artifacts\publish\SmsHubNext",
    [string] $ZipPath = "artifacts\publish\SmsHubNext.zip",
    [string] $Runtime = "",
    [bool] $SelfContained = $false,
    [string] $ProductionSettingsPath = "",
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
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
$zipFile = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ZipPath))
$artifactsPrefix = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts")) + [System.IO.Path]::DirectorySeparatorChar
foreach ($artifactPath in @($publishDir, $zipFile)) {
    if (-not $artifactPath.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish output paths must be inside the repository artifacts directory: $artifactPath"
    }
}
if ($zipFile.StartsWith($publishDir + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
    $zipFile.Equals($publishDir, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ZipPath must be outside OutputDir."
}

$productionSettings = $null
if (-not [string]::IsNullOrWhiteSpace($ProductionSettingsPath)) {
    $settingsFile = (Resolve-Path -LiteralPath $ProductionSettingsPath).Path
    if ($settingsFile.StartsWith($publishDir + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "ProductionSettingsPath must be outside OutputDir so it survives cleanup."
    }
    $productionSettings = Get-Content -LiteralPath $settingsFile -Raw
    $null = $productionSettings | ConvertFrom-Json
}

Write-Host "Restoring packages..."
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw "Package restore failed." }

Write-Host "Building $Configuration..."
dotnet build $solution --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if (-not $SkipUnitTests) {
    Write-Host "Running unit tests..."
    dotnet test $unitTests --configuration $Configuration --no-restore --no-build
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}

if ($IncludeIntegrationTests) {
    Write-Host "Running integration tests. Docker/Testcontainers must be available..."
    dotnet test $integrationTests --configuration $Configuration --no-restore --no-build
    if ($LASTEXITCODE -ne 0) { throw "Integration tests failed." }
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
    # Runtime-specific assets may not exist in the solution's earlier restore.
    $publishArgs = @($publishArgs | Where-Object { $_ -ne "--no-restore" })
    $publishArgs += @("--runtime", $Runtime)
}
elseif (-not $SelfContained) {
    $publishArgs += "/p:UseAppHost=false"
}

Write-Host "Publishing to $publishDir..."
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Server configuration stays on the server; never ship development/local secrets.
Get-ChildItem -LiteralPath $publishDir -Filter "appsettings.*.json" -File |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

if ($null -ne $productionSettings) {
    # Explicit opt-in: this private deployment package contains real host settings.
    # Include both names so an older Production file cannot override these values.
    foreach ($settingsName in @("appsettings.json", "appsettings.Production.json")) {
        Set-Content -LiteralPath (Join-Path $publishDir $settingsName) -Value $productionSettings -Encoding UTF8
    }

    $webConfigPath = Join-Path $publishDir "web.config"
    [xml] $webConfig = Get-Content -LiteralPath $webConfigPath -Raw
    $aspNetCore = $webConfig.SelectSingleNode("/configuration/location/system.webServer/aspNetCore")
    $environmentVariables = $aspNetCore.SelectSingleNode("environmentVariables")
    if ($null -eq $environmentVariables) {
        $environmentVariables = $webConfig.CreateElement("environmentVariables")
        $null = $aspNetCore.AppendChild($environmentVariables)
    }
    foreach ($variableName in @("ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT")) {
        $variable = $environmentVariables.SelectSingleNode("environmentVariable[@name='$variableName']")
        if ($null -eq $variable) {
            $variable = $webConfig.CreateElement("environmentVariable")
            $variable.SetAttribute("name", $variableName)
            $null = $environmentVariables.AppendChild($variable)
        }
        $variable.SetAttribute("value", "Production")
    }
    $webConfig.Save($webConfigPath)
}

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
