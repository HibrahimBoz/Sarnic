# Atölye servisini kullanıcı oturumu açılınca başlatır. Yönetici izni istemez.
# Kullanım: pwsh -File servis-kur.ps1 -Depo \\sunucu\Sarnic-Veri
param(
    [Parameter(Mandatory)][string]$Depo,
    [int]$Port = 8765
)
$ErrorActionPreference = 'Stop'
$kok = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $kok 'servis\SarnicServis.exe'
if (-not (Test-Path $exe)) { throw "Servis bulunamadı: $exe" }

$eylem = New-ScheduledTaskAction -Execute $exe -Argument "--depo `"$Depo`" --port $Port" -WorkingDirectory (Split-Path $exe)
$tetik = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$ayar = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'SarnicAtolyeServisi' -Action $eylem -Trigger $tetik -Settings $ayar -Force | Out-Null
Start-ScheduledTask -TaskName 'SarnicAtolyeServisi'
Write-Host 'Görev kuruldu ve başlatıldı: SarnicAtolyeServisi'
Write-Host 'Bilgisayar yeniden başlarsa servis oturum açılınca çalışır.'
