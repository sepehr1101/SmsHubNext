Set-StrictMode -Version 3.0

function Write-InstallerResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [bool] $Success,

        [Parameter(Mandatory = $true)]
        [string] $Code,

        [Parameter(Mandatory = $true)]
        [string] $Message,

        [bool] $RestartRequired = $false
    )

    $directory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($Path))
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $payload = [ordered]@{
        success = $Success
        code = $Code
        message = $Message
        restartRequired = $RestartRequired
    }
    $json = $payload | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::GetFullPath($Path),
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-InstallerSafeErrorMessage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord] $ErrorRecord
    )

    $message = $ErrorRecord.Exception.Message -replace '[\r\n]+', ' '
    if ([string]::IsNullOrWhiteSpace($message)) {
        return 'عملیات ناموفق بود.'
    }

    return $message.Trim()
}

function Get-InstallerDisplayErrorMessage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord] $ErrorRecord,

        [Parameter(Mandatory = $true)]
        [string] $Fallback
    )

    $message = Get-InstallerSafeErrorMessage -ErrorRecord $ErrorRecord
    if ($message -match '[\u0600-\u06FF]') {
        return $message
    }

    return $Fallback
}

function Assert-InstallerSafeDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Purpose
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $root = [System.IO.Path]::GetPathRoot($fullPath).TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($fullPath) -or
        [string]::Equals($fullPath, $root, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.Length -le ($root.Length + 3)) {
        throw "استفاده از مسیر ناامن «$fullPath» برای $Purpose مجاز نیست."
    }

    return $fullPath
}

function Invoke-InstallerProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,

        [Parameter(Mandatory = $true)]
        [string[]] $ArgumentList,

        [int[]] $SuccessfulExitCodes = @(0)
    )

    $quotedArguments = $ArgumentList | ForEach-Object {
        $argument = [string]$_
        if ($argument -notmatch '[\s"]') {
            return $argument
        }

        $escaped = [regex]::Replace($argument, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        return '"' + $escaped + '"'
    }

    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList ($quotedArguments -join ' ') `
        -Wait `
        -PassThru `
        -WindowStyle Hidden

    if ($SuccessfulExitCodes -notcontains $process.ExitCode) {
        throw "اجرای پردازش «$FilePath» با کد خروج $($process.ExitCode) ناموفق بود."
    }

    return $process.ExitCode
}
