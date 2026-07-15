[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $RequestPath,

    [Parameter(Mandatory = $true)]
    [string] $ResultPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

try {
    $requestJson = [System.IO.File]::ReadAllText(
        [System.IO.Path]::GetFullPath($RequestPath),
        [System.Text.Encoding]::UTF8)
    $request = $requestJson | ConvertFrom-Json

    if ([string]::IsNullOrWhiteSpace([string]$request.server)) {
        throw 'SQL Server address is required.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$request.database)) {
        throw 'Database name is required.'
    }

    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    # Windows PowerShell adapts DbConnectionStringBuilder as a dictionary. Use
    # canonical SQL keywords so assignments do not become invalid keys such as
    # "DataSource" or "InitialCatalog".
    $builder['Data Source'] = ([string]$request.server).Trim()
    $builder['Initial Catalog'] = ([string]$request.database).Trim()
    $builder['Application Name'] = 'SmsHubNext Setup Probe'
    $connectTimeoutSeconds = if ($request.PSObject.Properties.Name -notcontains 'connectTimeoutSeconds') {
        15
    }
    else {
        [int]$request.connectTimeoutSeconds
    }
    if ($connectTimeoutSeconds -lt 1 -or $connectTimeoutSeconds -gt 120) {
        throw 'SQL connection timeout must be between 1 and 120 seconds.'
    }

    $builder['Connect Timeout'] = $connectTimeoutSeconds
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = if ($request.PSObject.Properties.Name -contains 'trustServerCertificate') {
        [bool]$request.trustServerCertificate
    }
    else {
        $true
    }
    $builder['MultipleActiveResultSets'] = $true
    $builder['Persist Security Info'] = $false

    if ([string]::Equals(
        [string]$request.authentication,
        'Windows',
        [System.StringComparison]::OrdinalIgnoreCase)) {
        $builder['Integrated Security'] = $true
    }
    elseif ([string]::Equals(
        [string]$request.authentication,
        'SqlServer',
        [System.StringComparison]::OrdinalIgnoreCase)) {
        if ([string]::IsNullOrWhiteSpace([string]$request.username)) {
            throw 'SQL username is required.'
        }
        if ($null -eq $request.password -or [string]::IsNullOrEmpty([string]$request.password)) {
            throw 'SQL password is required.'
        }

        $builder['User ID'] = ([string]$request.username).Trim()
        $builder['Password'] = [string]$request.password
    }
    else {
        throw 'Authentication must be Windows or SqlServer.'
    }

    $targetConnectionString = $builder.ConnectionString
    $builder['Initial Catalog'] = 'master'
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'SELECT DB_ID(@databaseName);'
            $command.CommandTimeout = $connectTimeoutSeconds
            [void]$command.Parameters.AddWithValue('@databaseName', ([string]$request.database).Trim())
            $databaseId = $command.ExecuteScalar()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }

    if ($null -ne $databaseId -and $databaseId -isnot [System.DBNull]) {
        $targetConnection = [System.Data.SqlClient.SqlConnection]::new($targetConnectionString)
        try {
            $targetConnection.Open()
            $targetCommand = $targetConnection.CreateCommand()
            try {
                $targetCommand.CommandText = 'SELECT 1;'
                $targetCommand.CommandTimeout = $connectTimeoutSeconds
                [void]$targetCommand.ExecuteScalar()
            }
            finally {
                $targetCommand.Dispose()
            }
        }
        finally {
            $targetConnection.Dispose()
        }
    }

    Write-InstallerResult `
        -Path $ResultPath `
        -Success $true `
        -Code 'database.connection_succeeded' `
        -Message 'The SQL Server connection succeeded.'
    exit 0
}
catch {
    Write-InstallerResult `
        -Path $ResultPath `
        -Success $false `
        -Code 'database.connection_failed' `
        -Message (Get-InstallerSafeErrorMessage -ErrorRecord $_)
    exit 4
}
