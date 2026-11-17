<#
Starts OpenRGB as a local SDK server at every logon, which RGB Studio uses for memory (and for hidapi).

  powershell -ExecutionPolicy Bypass -File scripts\register_openrgb_server.ps1 -OpenRGB "C:\Program Files\OpenRGB\OpenRGB.exe"

Install OpenRGB 1.0 or newer first (https://openrgb.org). Run this from an elevated PowerShell:
OpenRGB needs administrator rights to reach the memory sticks over SMBus.
Remove with:  Unregister-ScheduledTask -TaskName "RGB Studio OpenRGB Server"
#>
param(
    [Parameter(Mandatory = $true)][string]$OpenRGB,
    [int]$Port = 6743,
    [string]$TaskName = "RGB Studio OpenRGB Server"
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path $OpenRGB)) { throw "OpenRGB.exe not found: $OpenRGB" }
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Run this from an elevated PowerShell (OpenRGB needs admin rights for SMBus)." }

$dir = Split-Path $OpenRGB
$config = Join-Path $dir "config"
New-Item -ItemType Directory -Force $config | Out-Null
$arguments = "--config `"$config`" --server --server-host 127.0.0.1 --server-port $Port --noautoconnect"
$action = New-ScheduledTaskAction -Execute $OpenRGB -Argument $arguments -WorkingDirectory $dir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $TaskName
Start-Sleep -Seconds 4
$ok = Test-NetConnection 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
if ($ok) { "OpenRGB server is listening on 127.0.0.1:$Port. RGB Studio will find it." }
else { "Task registered, but nothing listens on port $Port yet. Open OpenRGB once by hand to finish its first-run setup, then log off and on." }
