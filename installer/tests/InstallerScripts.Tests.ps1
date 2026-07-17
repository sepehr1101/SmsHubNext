[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$installerRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $installerRoot '..'))
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "SmsHubNext.InstallerTests.$([Guid]::NewGuid().ToString('N'))"

function Assert-InstallerTest {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

try {
    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null

    $parseFailures = [System.Collections.Generic.List[string]]::new()
    Get-ChildItem (Join-Path $installerRoot 'scripts\*.ps1') | ForEach-Object {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile(
            $_.FullName,
            [ref]$tokens,
            [ref]$errors)
        foreach ($parseError in $errors) {
            $parseFailures.Add("$($_.Name):$($parseError.Extent.StartLineNumber): $($parseError.Message)")
        }
    }
    Assert-InstallerTest `
        -Condition ($parseFailures.Count -eq 0) `
        -Message ("PowerShell parse failures: " + ($parseFailures -join '; '))

    $powerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    . (Join-Path $installerRoot 'scripts\Installer.Common.ps1')

    $argumentProbeScript = Join-Path $testRoot 'argument probe.ps1'
    $argumentProbeResult = Join-Path $testRoot 'argument probe result.txt'
    $argumentProbeValue = 'value with "quotes" and a trailing slash\'
    [System.IO.File]::WriteAllText(
        $argumentProbeScript,
        "param([string] `$Value, [string] `$OutputPath)`r`n[System.IO.File]::WriteAllText(`$OutputPath, `$Value)`r`n",
        [System.Text.UTF8Encoding]::new($false))
    [void](Invoke-InstallerProcess `
        -FilePath $powerShellPath `
        -ArgumentList @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
            '-File', $argumentProbeScript,
            '-Value', $argumentProbeValue,
            '-OutputPath', $argumentProbeResult))
    Assert-InstallerTest `
        -Condition ([System.IO.File]::ReadAllText($argumentProbeResult) -eq $argumentProbeValue) `
        -Message 'Installer process argument quoting corrupted a value containing spaces, quotes, or a trailing slash.'

    $validateResult = Join-Path $testRoot 'validate-result.json'
    $validateProcess = Start-Process `
        -FilePath $powerShellPath `
        -ArgumentList @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $installerRoot 'scripts\Deploy-Iis.ps1'),
            '-Action', 'Validate',
            '-SiteName', 'SmsHubNext',
            '-AppPoolName', 'SmsHubNext',
            '-PhysicalPath', (Join-Path $testRoot 'application'),
            '-KeyRingPath', (Join-Path $testRoot 'keys'),
            '-LogsPath', (Join-Path $testRoot 'logs'),
            '-ResultPath', $validateResult
        ) `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    Assert-InstallerTest -Condition ($validateProcess.ExitCode -eq 0) -Message 'Deployment parameter validation failed.'

    $secret = 'never-log-this;password=value'
    $databaseRequest = Join-Path $testRoot 'invalid-database-request.json'
    $databaseResult = Join-Path $testRoot 'database-result.json'
    $requestPayload = [ordered]@{
        server = ''
        database = 'SmsHubNext'
        authentication = 'SqlServer'
        username = 'sa'
        password = $secret
        connectTimeoutSeconds = 1
        trustServerCertificate = $true
    } | ConvertTo-Json
    [System.IO.File]::WriteAllText(
        $databaseRequest,
        $requestPayload,
        [System.Text.UTF8Encoding]::new($false))

    $databaseProcess = Start-Process `
        -FilePath $powerShellPath `
        -ArgumentList @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $installerRoot 'scripts\Test-Database.ps1'),
            '-RequestPath', $databaseRequest,
            '-ResultPath', $databaseResult
        ) `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    Assert-InstallerTest -Condition ($databaseProcess.ExitCode -ne 0) -Message 'Invalid database request unexpectedly succeeded.'
    $databaseResultContent = [System.IO.File]::ReadAllText($databaseResult)
    Assert-InstallerTest `
        -Condition (-not $databaseResultContent.Contains($secret)) `
        -Message 'Database probe leaked the password into its result.'

    $connectionProbeRequest = Join-Path $testRoot 'connection-probe-request.json'
    $connectionProbeResult = Join-Path $testRoot 'connection-probe-result.json'
    $connectionProbePayload = [ordered]@{
        server = '127.0.0.1,1'
        database = 'SmsHubNext'
        authentication = 'SqlServer'
        username = 'sa'
        password = $secret
        connectTimeoutSeconds = 1
        trustServerCertificate = $true
    } | ConvertTo-Json
    [System.IO.File]::WriteAllText(
        $connectionProbeRequest,
        $connectionProbePayload,
        [System.Text.UTF8Encoding]::new($false))
    $connectionProbeProcess = Start-Process `
        -FilePath $powerShellPath `
        -ArgumentList @(
            '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
            '-File', (Join-Path $installerRoot 'scripts\Test-Database.ps1'),
            '-RequestPath', $connectionProbeRequest,
            '-ResultPath', $connectionProbeResult
        ) `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    Assert-InstallerTest `
        -Condition ($connectionProbeProcess.ExitCode -ne 0) `
        -Message 'The unreachable SQL endpoint unexpectedly succeeded.'
    $connectionProbeResultContent = [System.IO.File]::ReadAllText($connectionProbeResult)
    Assert-InstallerTest `
        -Condition (-not $connectionProbeResultContent.Contains('Keyword not supported')) `
        -Message 'PowerShell generated a connection string with unsupported SQL keywords.'
    Assert-InstallerTest `
        -Condition (-not $connectionProbeResultContent.Contains($secret)) `
        -Message 'Database connection failure leaked the password into its result.'

    $innoScript = [System.IO.File]::ReadAllText((Join-Path $installerRoot 'SmsHubNext.iss'))
    Assert-InstallerTest `
        -Condition $innoScript.Contains('D4C4D455-64E5-4AA1-99AA-B41BD58B7891') `
        -Message 'Installer AppId must remain stable for upgrades.'
    Assert-InstallerTest `
        -Condition (-not ($innoScript -match '(?i)-Password\s')) `
        -Message 'SQL password must never be passed on a process command line.'
    Assert-InstallerTest `
        -Condition (-not ($innoScript -match '(?i)SQLEXPR|SQLServer.*\.exe')) `
        -Message 'Installer must not bundle or search for a SQL Server installer.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('--request')) `
        -Message 'SmsHubNext setup commands must receive credentials through a response file.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('dotnet-hosting-win.exe')) `
        -Message 'Offline installer must embed the .NET Hosting Bundle.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('appsettings.Development*.json,*.pdb')) `
        -Message 'Installer must exclude development settings and symbols from the production payload.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('WizardSilent and DatabaseConfigurationRequired')) `
        -Message 'Silent fresh installs must require an explicit database response file.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('RemoveFailedFreshDeployment')) `
        -Message 'A failed fresh installation must clean up newly-created IIS resources.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('RestorePreviousDeployment')) `
        -Message 'A failed upgrade must restore the previous deployment.'
    Assert-InstallerTest `
        -Condition (-not $innoScript.Contains('setup migrate')) `
        -Message 'Installer must let the web application own startup database migrations.'

    $setupCommandSource = [System.IO.File]::ReadAllText(
        (Join-Path $repoRoot 'src\SmsHubNext\Deployment\SetupCommandRunner.cs'))
    Assert-InstallerTest `
        -Condition (-not $setupCommandSource.Contains('"migrate"')) `
        -Message 'Setup command mode must not create a second migration execution path.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('IisPage.Edits[0].Enabled := not UpgradeInstallation')) `
        -Message 'Upgrade must not allow an IIS site rename that would orphan the previous site.'
    $initializeWizardStart = $innoScript.IndexOf('procedure InitializeWizard;', [System.StringComparison]::Ordinal)
    $initializeWizardEnd = $innoScript.IndexOf('procedure CurPageChanged', $initializeWizardStart, [System.StringComparison]::Ordinal)
    Assert-InstallerTest `
        -Condition (($initializeWizardStart -ge 0) -and ($initializeWizardEnd -gt $initializeWizardStart)) `
        -Message 'The InitializeWizard block could not be located.'
    $initializeWizardBlock = $innoScript.Substring(
        $initializeWizardStart,
        $initializeWizardEnd - $initializeWizardStart)
    Assert-InstallerTest `
        -Condition (-not $initializeWizardBlock.Contains("ExpandConstant('{app}")) `
        -Message 'InitializeWizard must use WizardDirValue because the {app} constant is not initialized yet.'
    Assert-InstallerTest `
        -Condition ($initializeWizardBlock.Contains('UpdateExistingInstallationState')) `
        -Message 'InitializeWizard must detect existing deployments using the wizard directory value.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('AuthenticationPage.Values[SqlServerAuthenticationOption] := True')) `
        -Message 'SQL Server Authentication must be the explicit default installer option.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('SqlAuthentication := AuthenticationPage.Values[SqlServerAuthenticationOption]')) `
        -Message 'SQL credential controls must follow the SQL Server Authentication option.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('TestPortButton.OnClick := @TestPortButtonClick')) `
        -Message 'The IIS page must provide an explicit port availability test button.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains('Result := TestIisBindingAvailability(False)')) `
        -Message 'Leaving the IIS page must require a successful binding availability check.'

    $deployIisScript = [System.IO.File]::ReadAllText((Join-Path $installerRoot 'scripts\Deploy-Iis.ps1'))
    Assert-InstallerTest `
        -Condition ($deployIisScript.Contains("'TestBinding' { Assert-BindingAvailable }")) `
        -Message 'The IIS deployment script must expose a non-mutating binding availability check.'
    Assert-InstallerTest `
        -Condition ($deployIisScript.Contains("'EnsureServices' { Ensure-IisServicesRunning }")) `
        -Message 'The IIS deployment script must expose an IIS service recovery action.'
    Assert-InstallerTest `
        -Condition ($deployIisScript -match 'function Start-SmsHubNextSite \{\r?\n\s+Ensure-IisServicesRunning') `
        -Message 'Starting the site must recover stopped IIS services first.'
    Assert-InstallerTest `
        -Condition ($innoScript.Contains("'-Action EnsureServices'")) `
        -Message 'Installer preparation must recover stopped IIS services before files are deployed.'
    Assert-InstallerTest `
        -Condition ($deployIisScript.Contains("processModel.idleTimeout -Value ([TimeSpan]::Zero)")) `
        -Message 'The application pool must not idle while SQL-backed background workers are active.'
    Assert-InstallerTest `
        -Condition ($deployIisScript.Contains("applicationDefaults.preloadEnabled -Value `$true")) `
        -Message 'The IIS site must preload after startup or recycle so background workers resume promptly.'
    $enableIisScript = [System.IO.File]::ReadAllText((Join-Path $installerRoot 'scripts\Enable-Iis.ps1'))
    Assert-InstallerTest `
        -Condition ($enableIisScript.Contains("'Web-AppInit'") -and $enableIisScript.Contains("'IIS-ApplicationInit'")) `
        -Message 'The installer must enable IIS Application Initialization on server and client Windows editions.'

    $buildScript = [System.IO.File]::ReadAllText((Join-Path $installerRoot 'build-installer.ps1'))
    Assert-InstallerTest `
        -Condition ($buildScript.Contains('Algorithm SHA256')) `
        -Message 'Installer builds must emit a SHA-256 checksum for unsigned release artifacts.'
    Assert-InstallerTest `
        -Condition ($buildScript.Contains('-p:Version=$Version')) `
        -Message 'Published application and installer versions must stay aligned.'

    $desktopProjects = @(Get-ChildItem $repoRoot -Recurse -Filter '*.csproj' | Where-Object {
        $content = [System.IO.File]::ReadAllText($_.FullName)
        $content -match 'UseWindowsForms|UseWPF'
    })
    Assert-InstallerTest `
        -Condition ($desktopProjects.Count -eq 0) `
        -Message 'Installer must not introduce a WinForms or WPF project.'

    Write-Output 'Installer script and contract tests passed.'
}
finally {
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        [System.IO.Path]::GetFileName($resolvedTestRoot).StartsWith('SmsHubNext.InstallerTests.', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
