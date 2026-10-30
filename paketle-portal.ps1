# Veri Dedektifi'ni ayrı paketler: self-contained klasör (libvlc yanında) + 7z. ClickOnce kullanmaz.
param(
    [string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe',
    [string]$Kok = '',          # ayar.json "kok" değeri (örn. D:\Dersler); boşsa portal klasör sorar
    [string[]]$ArtCraft = @('wordcraft', 'gridcraft', 'deckcraft', 'pdfcraft', 'photocraft')
)
$ErrorActionPreference = 'Stop'
$proje = $PSScriptRoot
$csproj = Join-Path $proje 'kaynak\portal\Portal.Uygulama\Portal.Uygulama.csproj'
$cikis = Join-Path $proje 'dist\VeriDedektifi'
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

Write-Host '[3/5] ArtCraft uygulamaları hazırlanıyor...'
$artcraftIndirKlasoru = Join-Path $proje '_indir\artcraft'
if (-not (Test-Path $artcraftIndirKlasoru)) {
    New-Item -ItemType Directory -Path $artcraftIndirKlasoru -Force | Out-Null
}

$artcraftGosterim = @{
    'wordcraft'  = 'WordCraft'
    'gridcraft'  = 'GridCraft'
    'deckcraft'  = 'DeckCraft'
    'pdfcraft'   = 'PdfCraft'
    'photocraft' = 'PhotoCraft'
    'figcraft'   = 'FigCraft'
    'cadcraft'   = 'CADCraft'
}

$artcraftSurumListesi = @()

foreach ($ad in $ArtCraft) {
    $adKucuk = $ad.ToLower().Trim()
    $adGoster = if ($artcraftGosterim.ContainsKey($adKucuk)) { $artcraftGosterim[$adKucuk] } else { $ad }
    $apiUrl = "https://api.github.com/repos/storytold/$adKucuk/releases/latest"

    $surum = $null
    $assetAdi = $null
    $beklenenDigest = $null
    $downloadUrl = $null
    $zipGecerli = $false

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers @{ 'User-Agent' = 'VeriFirini-Paketleyici' } -TimeoutSec 15
        if ($release -and $release.tag_name) {
            $surum = $release.tag_name -replace '^v', ''
            $assetAdi = "$adKucuk-$surum-windows-x64-portable.zip"
            $asset = $release.assets | Where-Object { $_.name -eq $assetAdi } | Select-Object -First 1
            if ($asset) {
                $downloadUrl = $asset.browser_download_url
                if ($asset.digest) {
                    $beklenenDigest = ($asset.digest -replace '^sha256:', '').Trim()
                }
            }
        }
    } catch {
        Write-Warning "ArtCraft ($adKucuk) GitHub API hatası: $($_.Exception.Message)"
    }

    if (-not $surum) {
        $onbellekZip = Get-ChildItem -Path $artcraftIndirKlasoru -Filter "$adKucuk-*-windows-x64-portable.zip" -ErrorAction SilentlyContinue | Select-Object -Last 1
        if ($onbellekZip -and $onbellekZip.Name -match "^$adKucuk-(.+)-windows-x64-portable\.zip$") {
            $surum = $Matches[1]
            $assetAdi = $onbellekZip.Name
        }
    }

    if ($assetAdi) {
        $hedefZip = Join-Path $artcraftIndirKlasoru $assetAdi

        if (Test-Path $hedefZip) {
            if ($beklenenDigest) {
                $mevcutHash = (Get-FileHash -Path $hedefZip -Algorithm SHA256).Hash
                if ($mevcutHash.Equals($beklenenDigest, [System.StringComparison]::OrdinalIgnoreCase)) {
                    Write-Host "  -> ArtCraft $adKucuk önbellekte mevcut (doğru özet)." -ForegroundColor Gray
                    $zipGecerli = $true
                } else {
                    Write-Host "  -> ArtCraft $adKucuk önbellek özeti uyuşmuyor, yeniden indirilecek..." -ForegroundColor Yellow
                    Remove-Item -Path $hedefZip -Force -ErrorAction SilentlyContinue
                }
            } else {
                Write-Host "  -> ArtCraft $adKucuk önbellekte mevcut." -ForegroundColor Gray
                $zipGecerli = $true
            }
        }

        if (-not $zipGecerli -and $downloadUrl) {
            try {
                Write-Host "  -> ArtCraft $adKucuk indiriliyor: $downloadUrl"
                Invoke-WebRequest -Uri $downloadUrl -OutFile $hedefZip -UseBasicParsing -TimeoutSec 60
                if ($beklenenDigest) {
                    $inenHash = (Get-FileHash -Path $hedefZip -Algorithm SHA256).Hash
                    if (-not $inenHash.Equals($beklenenDigest, [System.StringComparison]::OrdinalIgnoreCase)) {
                        Remove-Item -Path $hedefZip -Force -ErrorAction SilentlyContinue
                        Write-Warning "ArtCraft ($adKucuk) SHA256 uyuşmazlığı! Dosya silindi. Beklenen: $beklenenDigest, Alınan: $inenHash"
                    } else {
                        $zipGecerli = $true
                    }
                } else {
                    $zipGecerli = $true
                }
            } catch {
                Write-Warning "ArtCraft ($adKucuk) indirme hatası: $($_.Exception.Message)"
            }
        }

        if ($zipGecerli -and (Test-Path $hedefZip)) {
            $hedefKlasor = Join-Path $cikis "artcraft\$adKucuk"
            if (-not (Test-Path $hedefKlasor)) {
                New-Item -ItemType Directory -Path $hedefKlasor -Force | Out-Null
            }
            try {
                Expand-Archive -Path $hedefZip -DestinationPath $hedefKlasor -Force
                $artcraftSurumListesi += "$adGoster $surum (Apache-2.0, github.com/storytold/$adKucuk)"
            } catch {
                Write-Warning "ArtCraft ($adKucuk) arşiv açma hatası: $($_.Exception.Message)"
            }
        }
    }
}

Write-Host '[4/5] ayar.json ve OKU-BENI...'
if ($Kok) { Set-Content (Join-Path $cikis 'ayar.json') (@{ kok = $Kok } | ConvertTo-Json) -Encoding UTF8 }
@"
Veri Dedektifi (kapalı ağ)
KURULUM YOK. Bu klasörü yerel diske açın (örn. D:\VeriDedektifi). Portal.Uygulama.exe dosyasını çalıştırın.
- .NET ve libvlc klasörün içindedir. İnternet, yönetici yetkisi gerekmez.
- ayar.json: "kok" alanına işlenmiş derslerin klasörünü yazın (boşsa portal sorar).
- ayar.json: "dizin" alanına arama dizini klasörünü yazın (boşsa <kok>\_veridedektifi kullanılır).
- Yeni çıktı klasörleri _islenmis ekiyle bulunur; eski _videoperisi klasörleri de okunur.
- Ağ paylaşımından çalıştırmayın (Windows güvenlik uyarısı). Yerel diskten çalıştırın.
- Video açılmazsa: libvlc\win-x64\libvlc.dll ve libvlc\win-x64\plugins\ klasörü yerinde mi bakın.
"@ | Set-Content (Join-Path $cikis 'OKU-BENI.txt') -Encoding UTF8

if ($artcraftSurumListesi.Count -gt 0) {
    Add-Content (Join-Path $cikis 'OKU-BENI.txt') "`r`nBileşenler (ArtCraft):`r`n$($artcraftSurumListesi -join "`r`n")" -Encoding UTF8
    Set-Content (Join-Path $cikis 'SURUMLER.txt') ($artcraftSurumListesi -join "`r`n") -Encoding UTF8
}

Write-Host '[5/5] 7z...'
$arsiv = Join-Path $proje 'dist\VeriDedektifi.7z'
if (Test-Path $arsiv) { Remove-Item $arsiv -Force }
& $SevenZip a -t7z -mx=5 $arsiv $cikis | Select-Object -Last 3
if ($LASTEXITCODE -ne 0) { throw "7z hata kodu $LASTEXITCODE" }
$h = (Get-FileHash $arsiv -Algorithm SHA256).Hash
Write-Host "Arşiv: $arsiv ($([math]::Round((Get-Item $arsiv).Length/1MB)) MB) SHA256: $h"
