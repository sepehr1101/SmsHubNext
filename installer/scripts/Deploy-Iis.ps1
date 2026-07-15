[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Backup', 'Restore', 'DeleteBackup', 'Ensure', 'EnsureServices', 'Start', 'Stop', 'Remove', 'TestBinding', 'Validate')]
    [string] $Action,

    [Parameter(Mandatory = $true)]
    [string] $SiteName,

    [Parameter(Mandatory = $true)]
    [string] $AppPoolName,

    [string] $PhysicalPath,

    [string] $KeyRingPath,

    [string] $LogsPath,

    [string] $BackupPath,

    [ValidateRange(1, 65535)]
    [int] $Port = 8080,

    [string] $HostName = '',

    [bool] $OpenFirewall = $true,

    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

function Import-IisAdministration {
    Import-Module WebAdministration -ErrorAction Stop
}

function Ensure-IisServiceRunning {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $service = Get-Service -Name $Name -ErrorAction Stop
    if ($service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
        return
    }

    try {
        Start-Service -Name $Name -ErrorAction Stop
        $service.WaitForStatus(
            [System.ServiceProcess.ServiceControllerStatus]::Running,
            [TimeSpan]::FromSeconds(30))
    }
    catch {
        throw "Required IIS service '$Name' is stopped and could not be started. $($_.Exception.Message)"
    }
}

function Ensure-IisServicesRunning {
    Ensure-IisServiceRunning -Name 'WAS'
    Ensure-IisServiceRunning -Name 'W3SVC'
}

function Stop-SmsHubNextSite {
    Import-IisAdministration
    if (Test-Path -LiteralPath "IIS:\Sites\$SiteName") {
        Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath "IIS:\AppPools\$AppPoolName") {
        Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    }
}

function Start-SmsHubNextSite {
    Ensure-IisServicesRunning
    Import-IisAdministration
    if (-not (Test-Path -LiteralPath "IIS:\AppPools\$AppPoolName")) {
        throw "IIS application pool '$AppPoolName' does not exist."
    }
    if (-not (Test-Path -LiteralPath "IIS:\Sites\$SiteName")) {
        throw "IIS site '$SiteName' does not exist."
    }

    Start-WebAppPool -Name $AppPoolName
    Start-Website -Name $SiteName
}

function Invoke-Robocopy {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Destination,

        [switch] $Mirror
    )

    [System.IO.Directory]::CreateDirectory($Destination) | Out-Null
    $arguments = @($Source, $Destination, '/E', '/COPY:DAT', '/DCOPY:DAT', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
    if ($Mirror) {
        $arguments[2] = '/MIR'
    }

    $robocopyPath = Join-Path $env:SystemRoot 'System32\robocopy.exe'
    [void](Invoke-InstallerProcess `
        -FilePath $robocopyPath `
        -ArgumentList $arguments `
        -SuccessfulExitCodes @(0, 1, 2, 3, 4, 5, 6, 7))
}

function Backup-SmsHubNext {
    $applicationDirectory = Assert-InstallerSafeDirectory -Path $PhysicalPath -Purpose 'application'
    $backupDirectory = Assert-InstallerSafeDirectory -Path $BackupPath -Purpose 'backup'
    Stop-SmsHubNextSite

    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $applicationDirectory) {
        Invoke-Robocopy -Source $applicationDirectory -Destination $backupDirectory
    }
}

function Restore-SmsHubNext {
    $applicationDirectory = Assert-InstallerSafeDirectory -Path $PhysicalPath -Purpose 'application'
    $backupDirectory = Assert-InstallerSafeDirectory -Path $BackupPath -Purpose 'backup'
    if (-not (Test-Path -LiteralPath $backupDirectory -PathType Container)) {
        throw "Deployment backup '$backupDirectory' does not exist."
    }

    Stop-SmsHubNextSite
    Invoke-Robocopy -Source $backupDirectory -Destination $applicationDirectory -Mirror
}

function Remove-DeploymentBackup {
    $backupDirectory = Assert-InstallerSafeDirectory -Path $BackupPath -Purpose 'backup'
    if (Test-Path -LiteralPath $backupDirectory) {
        Remove-Item -LiteralPath $backupDirectory -Recurse -Force
    }
}

function Assert-BindingAvailable {
    $activeListener = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Port -eq $Port } |
        Select-Object -First 1
    $webAdministrationModule = Get-Module -ListAvailable -Name WebAdministration | Select-Object -First 1
    if ($null -eq $webAdministrationModule) {
        if ($null -ne $activeListener) {
            throw "TCP port '$Port' is already in use by another process."
        }
        return
    }

    Import-IisAdministration
    $expectedBinding = "*:${Port}:${HostName}"
    $httpBindings = @(Get-WebBinding -Protocol 'http')
    $conflict = $httpBindings | Where-Object {
        $_.bindingInformation -eq $expectedBinding -and
        $_.ItemXPath -notmatch ([regex]::Escape("site[@name='$SiteName']"))
    } | Select-Object -First 1

    if ($null -ne $conflict) {
        throw "HTTP binding '$expectedBinding' is already used by another IIS site."
    }

    $iisOwnsPort = $null -ne ($httpBindings | Where-Object {
        $_.bindingInformation -match ":${Port}:"
    } | Select-Object -First 1)
    if (-not $iisOwnsPort -and $null -ne $activeListener) {
        throw "TCP port '$Port' is already in use by another process."
    }
}

function Grant-IisPermissions {
    $applicationDirectory = Assert-InstallerSafeDirectory -Path $PhysicalPath -Purpose 'application'
    $keyDirectory = Assert-InstallerSafeDirectory -Path $KeyRingPath -Purpose 'Data Protection key-ring'
    $logDirectory = Assert-InstallerSafeDirectory -Path $LogsPath -Purpose 'log'

    [System.IO.Directory]::CreateDirectory($applicationDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($keyDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null

    $principal = "IIS AppPool\$AppPoolName"
    $icaclsPath = Join-Path $env:SystemRoot 'System32\icacls.exe'
    [void](Invoke-InstallerProcess `
        -FilePath $icaclsPath `
        -ArgumentList @($applicationDirectory, '/grant:r', "${principal}:(OI)(CI)(RX)", '/T', '/C'))
    [void](Invoke-InstallerProcess `
        -FilePath $icaclsPath `
        -ArgumentList @($keyDirectory, '/grant:r', "${principal}:(OI)(CI)(M)", '/T', '/C'))
    [void](Invoke-InstallerProcess `
        -FilePath $icaclsPath `
        -ArgumentList @($logDirectory, '/grant:r', "${principal}:(OI)(CI)(M)", '/T', '/C'))

    $productionSettingsPath = Join-Path $applicationDirectory 'appsettings.Production.json'
    if (Test-Path -LiteralPath $productionSettingsPath -PathType Leaf) {
        [void](Invoke-InstallerProcess `
            -FilePath $icaclsPath `
            -ArgumentList @(
                $productionSettingsPath,
                '/inheritance:r',
                '/grant:r',
                '*S-1-5-18:(F)',
                '*S-1-5-32-544:(F)',
                "${principal}:(R)"))
    }
}

function Ensure-SmsHubNextSite {
    $applicationDirectory = Assert-InstallerSafeDirectory -Path $PhysicalPath -Purpose 'application'
    Assert-BindingAvailable
    Import-IisAdministration

    if (-not (Test-Path -LiteralPath "IIS:\AppPools\$AppPoolName")) {
        New-WebAppPool -Name $AppPoolName | Out-Null
    }
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'

    if (-not (Test-Path -LiteralPath "IIS:\Sites\$SiteName")) {
        New-Website `
            -Name $SiteName `
            -Port $Port `
            -HostHeader $HostName `
            -PhysicalPath $applicationDirectory `
            -ApplicationPool $AppPoolName | Out-Null
    }
    else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $applicationDirectory
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName

        Get-WebBinding -Name $SiteName -Protocol 'http' | ForEach-Object {
            Remove-WebBinding `
                -Name $SiteName `
                -Protocol 'http' `
                -BindingInformation $_.bindingInformation
        }
        New-WebBinding -Name $SiteName -Protocol 'http' -Port $Port -HostHeader $HostName
    }

    Grant-IisPermissions

    $firewallRuleName = "SmsHubNext HTTP $Port"
    $firewallRuleGroup = 'SmsHubNext'
    $getFirewallRule = Get-Command 'Get-NetFirewallRule' -ErrorAction SilentlyContinue
    if ($OpenFirewall -and $null -ne $getFirewallRule) {
        Get-NetFirewallRule -Group $firewallRuleGroup -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Group $firewallRuleGroup `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $Port | Out-Null
    }
}

function Remove-SmsHubNextSite {
    Import-IisAdministration
    if (Test-Path -LiteralPath "IIS:\Sites\$SiteName") {
        Remove-Website -Name $SiteName
    }
    if (Test-Path -LiteralPath "IIS:\AppPools\$AppPoolName") {
        Remove-WebAppPool -Name $AppPoolName
    }

    if (Get-Command 'Get-NetFirewallRule' -ErrorAction SilentlyContinue) {
        Get-NetFirewallRule -Group 'SmsHubNext' -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue
    }
}

try {
    switch ($Action) {
        'Backup' { Backup-SmsHubNext }
        'Restore' { Restore-SmsHubNext }
        'DeleteBackup' { Remove-DeploymentBackup }
        'Ensure' { Ensure-SmsHubNextSite }
        'EnsureServices' { Ensure-IisServicesRunning }
        'Start' { Start-SmsHubNextSite }
        'Stop' { Stop-SmsHubNextSite }
        'Remove' { Remove-SmsHubNextSite }
        'TestBinding' { Assert-BindingAvailable }
        'Validate' {
            [void](Assert-InstallerSafeDirectory -Path $PhysicalPath -Purpose 'application')
            [void](Assert-InstallerSafeDirectory -Path $KeyRingPath -Purpose 'Data Protection key-ring')
            [void](Assert-InstallerSafeDirectory -Path $LogsPath -Purpose 'log')
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
        Write-InstallerResult `
            -Path $ResultPath `
            -Success $true `
            -Code "iis.$($Action.ToLowerInvariant())_succeeded" `
            -Message "IIS deployment action '$Action' succeeded."
    }
    exit 0
}
catch {
    if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
        Write-InstallerResult `
            -Path $ResultPath `
            -Success $false `
            -Code "iis.$($Action.ToLowerInvariant())_failed" `
            -Message (Get-InstallerSafeErrorMessage -ErrorRecord $_)
    }
    exit 22
}
