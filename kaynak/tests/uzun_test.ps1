# Uzun kayıt testi: süre + tepe bellek ölçer. Kullanım: pwsh -File uzun_test.ps1 -Girdi x.wav -Cikis dir [-Paket ...] [-Cihaz cpu]
param([string]$Girdi, [string]$Cikis, [string]$Paket = (Join-Path $PSScriptRoot '..\..\dist\SarnicAtolye-GPU'), [string]$Cihaz = '')
$env:SARNICATOLYE_PAUSE = '0'
if (Test-Path $Cikis) { Remove-Item $Cikis -Recurse -Force }
$arg = "/c `"`"$Paket\calistir.bat`" `"$Girdi`" --cikis `"$Cikis`" $(if($Cihaz){"--cihaz $Cihaz"})`""
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process cmd -ArgumentList $arg -PassThru -WindowStyle Hidden -RedirectStandardOutput "$Cikis.log"
$tepe = 0
while (-not $p.HasExited) {
    Start-Sleep 3
    foreach ($q in Get-Process python -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Paket*" }) {
        if ($q.PeakWorkingSet64 -gt $tepe) { $tepe = $q.PeakWorkingSet64 }
    }
}
"SURE_SN=$([int]$sw.Elapsed.TotalSeconds) TEPE_BELLEK_MB=$([int]($tepe/1MB)) KOD=$($p.ExitCode)"
