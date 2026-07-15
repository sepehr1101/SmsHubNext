# SmsHubNext Windows installer

The installer is an Inno Setup orchestrator around the existing ASP.NET Core executable and small Windows deployment scripts. It deliberately does not introduce a second application, WinForms/WPF project, or business-configuration UI.

## Build

Requirements on the build machine:

- .NET 10 SDK
- network access only when downloading the pinned official Inno compiler and .NET Hosting Bundle for the first time

Run from the repository root:

```powershell
.\installer\build-installer.ps1
```

To include Docker/Testcontainers integration tests:

```powershell
.\installer\build-installer.ps1 -IncludeIntegrationTests
```

The pinned versions live in `installer.settings.psd1`. If Inno is absent, the build bootstraps it into the gitignored `artifacts\tools` directory only after verifying the official Pyrsys B.V. Authenticode signature. The Hosting Bundle download script resolves the official Microsoft release metadata and verifies the published SHA-512 before accepting the file. The offline redistributable is cached in the gitignored `installer\redist` directory.

Output:

```text
artifacts\installer\SmsHubNext-Setup-<version>-x64.exe
```

## Test layers

- Deployment unit tests cover validation, connection-string escaping, atomic settings merge/rollback and command-mode exit/result contracts.
- The setup integration test starts SQL Server and proves that configuration succeeds for a missing target database without creating it. The existing application integration suite owns verification of startup database creation and DbUp migrations.
- `tests\InstallerScripts.Tests.ps1` parses every PowerShell file, exercises safe path validation and process quoting, verifies secret non-disclosure contracts, and guards the stable Inno AppId/design constraints.
- Compiling `SmsHubNext.iss` with the pinned compiler is itself a required build check.
- Real IIS/Windows Feature/restart/upgrade behavior must additionally pass the disposable-VM matrix in `docs\operations\windows-installer-fa.md`; those operations intentionally are not run on a developer workstation by unit tests.

On an elevated disposable VM, the fresh-install/uninstall smoke path can be automated with:

```powershell
.\installer\tests\Invoke-InstallerAcceptance.ps1 `
  -InstallerPath .\artifacts\installer\SmsHubNext-Setup-0.1.6-x64.exe `
  -SetupRequestPath C:\SecureTemp\smshub-setup.json `
  -UninstallAfterVerification
```

## Release notes

The generated setup is not Authenticode-signed unless a signing step is provided by the release environment. Publish a SHA-256 checksum alongside every unsigned artifact. Do not commit the Hosting Bundle, setup executable, response files, production settings, or installer logs containing environment details.
