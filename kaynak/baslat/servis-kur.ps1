# Atölye servisini kurar ve başlatır. Yönetici izni istemez. Menüden (BASLAT.bat > S) ya da doğrudan çalışır.
# Depo yolunu bilmiyorsa sorar, parola yoksa kurar, servisin ayağa kalktığını doğrular.
# Kullanım: powershell -ExecutionPolicy Bypass -File servis-kur.ps1 [-Depo \\sunucu\Sarnic-Veri] [-Port 8765]
param(
    [string]$Depo = '',
    [int]$Port = 8765
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$kok = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $kok 'servis\SarnicServis.exe'
$isKlasoru = Join-Path $kok 'is'
$ayarYolu = Join-Path $isKlasoru 'servis_ayar.json'
$parolaYolu = Join-Path $isKlasoru 'yonetim_parola.json'
$gorevAdi = 'SarnicAtolyeServisi'
if (-not (Test-Path $exe)) { throw "Servis bulunamadı: $exe" }

# Önceki kurulumdan depo ve port hatırlanır; ikinci çalıştırmada soru sorulmaz.
$kayitli = $null
if (Test-Path $ayarYolu) { try { $kayitli = Get-Content $ayarYolu -Raw -Encoding UTF8 | ConvertFrom-Json } catch { } }
if (-not $Depo -and $kayitli -and $kayitli.depo) { $Depo = $kayitli.depo }
if (-not $PSBoundParameters.ContainsKey('Port') -and $kayitli -and $kayitli.port) { $Port = [int]$kayitli.port }
if (-not $Depo) {
    Write-Host 'Depo: Sarnıç ve Atölye''nin ortak kullandığı klasör (örn. \\sunucu\Sarnic-Veri). Sarnıç''taki "dizin" ayarıyla aynı olmalı.'
    $Depo = (Read-Host 'Depo klasörü').Trim().Trim('"')
}
if (-not $Depo) { throw 'Depo klasörü gerekli.' }
if (-not (Test-Path $Depo -PathType Container)) {
    $c = Read-Host "Klasör yok: $Depo. Oluşturulsun mu? (e/h) [h]"
    if ($c.Trim().ToLower().StartsWith('e')) { New-Item -ItemType Directory -Force $Depo | Out-Null }
    else { throw "Depo klasörü bulunamadı: $Depo" }
}

# Parola yoksa şimdi kurulur (kuyruk temizleme için gerekli).
if (-not (Test-Path $parolaYolu)) {
    Write-Host 'Yönetim parolası henüz yok. Şimdi belirleyin (kuyruk temizleme bunu ister).' -ForegroundColor Yellow
    & $exe --parola-ayarla --kok $kok
    if ($LASTEXITCODE -ne 0) { throw 'Parola kurulamadı.' }
}

# Görev: oturum açılınca başlar, düşerse yeniden başlar.
$eylem = New-ScheduledTaskAction -Execute $exe -Argument "--depo `"$Depo`" --port $Port" -WorkingDirectory (Split-Path $exe)
$tetik = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
$ayar = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $gorevAdi -Action $eylem -Trigger $tetik -Settings $ayar -Force | Out-Null
Stop-ScheduledTask -TaskName $gorevAdi -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Start-ScheduledTask -TaskName $gorevAdi
Write-Host "Görev kuruldu ve başlatıldı: $gorevAdi"

# Doğrulama: servis kendi bilgi dosyasını depoya yazar.
$bilgi = Join-Path $Depo 'yonetim\servis.json'
$sinir = (Get-Date).AddSeconds(45)
$yazildi = $false
while ((Get-Date) -lt $sinir) {
    if ((Test-Path $bilgi) -and ((Get-Item $bilgi).LastWriteTime -gt (Get-Date).AddMinutes(-2))) { $yazildi = $true; break }
    Start-Sleep -Seconds 1
}
Write-Host ''
if ($yazildi) {
    Write-Host 'SERVİS ÇALIŞIYOR.' -ForegroundColor Green
    Write-Host "  Depo : $Depo"
    Write-Host '  Sarnıç tarafı: portal paketindeki ayar.json içinde "dizin" şu değerde olmalı:'
    Write-Host "    $Depo" -ForegroundColor Cyan
    Write-Host '  (Portal paketini paketle-portal.ps1 -Depo ile üretirseniz bu değer içine kendiliğinden yazılır.)'
    Write-Host '  Bilgisayar yeniden başlarsa servis, biri oturum açınca çalışır.'
    exit 0
}
Write-Host 'Servis 45 saniyede bilgi dosyasını yazmadı.' -ForegroundColor Red
Write-Host "  Beklenen dosya: $bilgi"
Write-Host "  Elle deneyin: servis.bat --depo `"$Depo`" (hata metni pencerede görünür)"
exit 1
