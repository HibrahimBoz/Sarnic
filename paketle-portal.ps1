# Video Portalı'nı ayrı paketler: self-contained klasör (libvlc yanında) + 7z. ClickOnce kullanmaz.
param(
    [string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe',
    [string]$Kok = ''          # ayar.json "kok" değeri (örn. D:\Dersler); boşsa portal klasör sorar
)
$ErrorActionPreference = 'Stop'
$proje = $PSScriptRoot
$csproj = Join-Path $proje 'kaynak\portal\Portal.Uygulama\Portal.Uygulama.csproj'
$cikis = Join-Path $proje 'dist\VideoPortali'
if (Test-Path $cikis) { Remove-Item $cikis -Recurse -Force }

Write-Host '[1/4] Yayın...'
dotnet publish $csproj -p:PublishProfile=Klasor
if ($LASTEXITCODE -ne 0) { throw "dotnet publish hata kodu $LASTEXITCODE" }

Write-Host '[2/4] libvlc doğrulama...'
$vlc = Join-Path $cikis 'libvlc\win-x64'
$dll = Join-Path $vlc 'libvlc.dll'
$eklenti = @(Get-ChildItem (Join-Path $vlc 'plugins') -Recurse -Filter '*.dll' -ErrorAction SilentlyContinue).Count
if (-not (Test-Path $dll) -or $eklenti -lt 100) { throw "libvlc eksik: libvlc.dll var mı=$(Test-Path $dll), eklenti dll=$eklenti" }
Write-Host "  libvlc.dll var, eklenti dll: $eklenti"

# İlk açılışta tüm eklentileri taramaması için önbelleği yayının kendi LibVLC'siyle üret.
$onbellekIslemi = Start-Process -FilePath (Join-Path $cikis 'Portal.Uygulama.exe') -ArgumentList '--vlc-onbellek' -WindowStyle Hidden -PassThru
if (-not $onbellekIslemi.WaitForExit(60000)) {
    $onbellekIslemi.Kill()
    throw 'LibVLC eklenti önbelleği üretimi zaman aşımına uğradı.'
}
$onbellek = Join-Path $vlc 'plugins\plugins.dat'
if ($onbellekIslemi.ExitCode -ne 0 -or -not (Test-Path $onbellek) -or (Get-Item $onbellek).Length -eq 0) {
    throw 'LibVLC plugins.dat üretilemedi; eksik önbellekle paket hazırlanmadı.'
}

Write-Host '[3/4] ayar.json ve OKU-BENI...'
if ($Kok) { Set-Content (Join-Path $cikis 'ayar.json') (@{ kok = $Kok } | ConvertTo-Json) -Encoding UTF8 }
@"
Video Portalı (kapalı ağ)
KURULUM YOK. Bu klasörü yerel diske açın (örn. D:\VideoPortali). Portal.Uygulama.exe çalıştırın.
- .NET ve libvlc klasörün içindedir. İnternet, yönetici yetkisi gerekmez.
- ayar.json: "kok" alanına işlenmiş derslerin klasörünü yazın (boşsa portal sorar).
- Yeni çıktı klasörleri _islenmis ekiyle bulunur; eski _videoperisi klasörleri de okunur.
- Ağ paylaşımından çalıştırmayın (Windows güvenlik uyarısı). Yerel diskten çalıştırın.
- Video açılmazsa: libvlc\win-x64\libvlc.dll ve libvlc\win-x64\plugins\ klasörü yerinde mi bakın.
"@ | Set-Content (Join-Path $cikis 'OKU-BENI.txt') -Encoding UTF8

Write-Host '[4/4] 7z...'
$arsiv = Join-Path $proje 'dist\VideoPortali.7z'
if (Test-Path $arsiv) { Remove-Item $arsiv -Force }
& $SevenZip a -t7z -mx=5 $arsiv $cikis | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "7z hata kodu $LASTEXITCODE" }
$h = (Get-FileHash $arsiv -Algorithm SHA256).Hash
Write-Host "Arşiv: $arsiv ($([math]::Round((Get-Item $arsiv).Length/1MB)) MB) SHA256: $h"
