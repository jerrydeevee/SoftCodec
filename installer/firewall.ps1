#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$AppPath,[int]$SipPort=5060,[int]$RtpPort=5004,[int]$RtcpPort=5005)
$ErrorActionPreference='Stop'
if (!(Test-Path -LiteralPath $AppPath -PathType Leaf)) { throw 'SoftCodec executable niet gevonden.' }
foreach ($port in @($SipPort,$RtpPort,$RtcpPort)) { if ($port -lt 1 -or $port -gt 65535) { throw 'Ongeldige poort.' } }
Get-NetFirewallRule -Group SoftCodec -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'SoftCodec SIP/RTP/RTCP' -Group SoftCodec -Direction Inbound -Action Allow -Protocol UDP -LocalPort $SipPort,$RtpPort,$RtcpPort -Program (Resolve-Path -LiteralPath $AppPath).Path -Profile Domain,Private
