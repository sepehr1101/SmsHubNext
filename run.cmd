@echo off
rem Convenience launcher: run the web app from the repo root.
rem `dotnet run` needs a project; ours lives in src\SmsHubNext. Args are forwarded.
dotnet run --project "%~dp0src\SmsHubNext" %*
