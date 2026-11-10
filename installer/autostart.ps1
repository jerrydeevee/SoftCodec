param([Parameter(Mandatory)][string]$AppPath,[switch]$Remove)
$ErrorActionPreference='Stop'
$name='SoftCodec Studio'
if ($Remove) { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue; exit }
if (!(Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'Executable niet gevonden.' }
$user=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$action=New-ScheduledTaskAction -Execute (Resolve-Path -LiteralPath $AppPath).Path -Argument '--minimized'
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $user
$principal=New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'SoftCodec in de interactieve studiosessie; herstart bij fouten.' -Force
