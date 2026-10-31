# Aktarım paketi: dist\SarnicAtolye-Paketi (SarnicAtolye + Kdenlive + BASLAT.bat) ve 7z arşivi üretir.
# Önce hazirla.ps1 çalışmış, _indir\kdenlive-*_standalone.exe indirilmiş olmalı.
param(
    [string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe',
    [int]$Sikistirma = 5,
    [switch]$SadeceKlasor,   # klasörü kur, sıkıştırma
    [switch]$KlasorHazir,    # klasör zaten kurulu: yalnız sıkıştır
    [string]$ParcaBoyutu = ''    # örn. 3900m (FAT32 USB için bölünmüş arşiv); boş = tek dosya
)
$ErrorActionPreference = 'Stop'
$proje = $PSScriptRoot
$gpu = Join-Path $proje 'dist\SarnicAtolye-GPU'
$cikis = Join-Path $proje 'dist\SarnicAtolye-Paketi'
$kdExe = Get-ChildItem (Join-Path $proje '_indir') -Filter 'kdenlive-*_standalone.exe' | Sort-Object Name | Select-Object -Last 1
if (-not (Test-Path $gpu)) { throw "GPU paketi yok: $gpu (önce hazirla.ps1)" }
if (-not $kdExe) { throw "Kdenlive standalone .exe _indir\ içinde yok." }
if (-not (Test-Path $SevenZip)) { throw "7z bulunamadı: $SevenZip" }

if (-not $KlasorHazir) {
Write-Host '[1/5] Klasör hazırlanıyor...'
if (Test-Path $cikis) { Remove-Item $cikis -Recurse -Force }
New-Item -ItemType Directory $cikis | Out-Null
$vh = Join-Path $cikis 'SarnicAtolye'
robocopy $gpu $vh /E /NFL /NDL /NJH /NJS /NP /XD (Join-Path $gpu 'is') | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy hatası (kod $LASTEXITCODE)" }
foreach ($d in 'gelen', 'isleniyor', 'bitti', 'hata', 'gunluk') { New-Item -ItemType Directory -Force (Join-Path $vh "is\$d") | Out-Null }
Copy-Item (Join-Path $proje 'kaynak\KULLANIM.txt') $vh -Force
New-Item -ItemType Directory -Force (Join-Path $vh 'araclar') | Out-Null
Copy-Item (Join-Path $proje 'kaynak\baslat\baslat.ps1') (Join-Path $vh 'araclar') -Force
Copy-Item (Join-Path $proje 'kaynak\baslat\BASLAT.bat') $cikis -Force
Copy-Item (Join-Path $proje 'kaynak\baslat\OKU-BENI.txt') $cikis -Force

# SURUMLER.txt: gerçek dosyalardan (dist-info, commit_sha.txt) yeniden üretilir; hazirla.ps1'in eski çıktısı atılır
$sp = Join-Path $vh 'python\Lib\site-packages'
$pk = Get-ChildItem $sp -Directory -Filter '*.dist-info' | ForEach-Object {
    $m = Get-Content (Join-Path $_.FullName 'METADATA') -TotalCount 12 -Encoding UTF8
    $ad = ($m | Where-Object { $_ -like 'Name: *' } | Select-Object -First 1) -replace '^Name:\s*', ''
    $sr = ($m | Where-Object { $_ -like 'Version: *' } | Select-Object -First 1) -replace '^Version:\s*', ''
    "{0}=={1}" -f $ad, $sr
} | Sort-Object
$mod = foreach ($a in 'large-v3', 'medium', 'turbo') { "{0,-9}: {1}" -f $a, (Get-Content (Join-Path $vh "modeller\$a\commit_sha.txt") -Raw).Trim() }
$artcraftKlasor = Join-Path $vh 'artcraft'
$artcraftListesi = @()
if (Test-Path $artcraftKlasor) {
    $artcraftGosterim = @{
        'wordcraft'  = 'WordCraft'
        'gridcraft'  = 'GridCraft'
        'deckcraft'  = 'DeckCraft'
        'pdfcraft'   = 'PdfCraft'
        'photocraft' = 'PhotoCraft'
        'figcraft'   = 'FigCraft'
        'cadcraft'   = 'CADCraft'
    }
    Get-ChildItem $artcraftKlasor -Directory | ForEach-Object {
        $acAd = $_.Name.ToLower()
        $acGoster = if ($artcraftGosterim.ContainsKey($acAd)) { $artcraftGosterim[$acAd] } else { $_.Name }
        $acExe = Get-ChildItem $_.FullName -Recurse -Filter "*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
        $surum = ""
        if ($acExe) {
            try { $surum = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($acExe.FullName).ProductVersion } catch {}
        }
        if (-not $surum) {
            $zip = Get-ChildItem (Join-Path $proje '_indir\artcraft') -Filter "$acAd-*-windows-x64-portable.zip" -ErrorAction SilentlyContinue | Select-Object -Last 1
            if ($zip -and $zip.Name -match "^$acAd-(.+)-windows-x64-portable\.zip$") {
                $surum = $Matches[1]
            }
        }
        if (-not $surum) { $surum = "1.0.0" }
        $artcraftListesi += "$acGoster $surum (Apache-2.0, github.com/storytold/$acAd)"
    }
}
$araclarListesi = @()
$loExe = Join-Path $vh 'araclar\libreoffice\program\soffice.exe'
if (Test-Path $loExe) {
    $loSurum = ""
    try { $loSurum = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($loExe).ProductVersion } catch {}
    if ($loSurum) {
        $araclarListesi += ("{0,-26}: documentfoundation.org (MPL-2.0), kirpilmis tasinabilir kopya" -f "LibreOffice $($loSurum.Trim())")
    }
}
$yediExe = Join-Path $vh 'araclar\7z\7z.exe'
if (Test-Path $yediExe) {
    $yediSurum = ""
    try { $yediSurum = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($yediExe).ProductVersion } catch {}
    if ($yediSurum) {
        $araclarListesi += ("{0,-26}: github.com/ip7z/7zip (LGPL-2.1 + unRAR), yalniz 7z.exe/7z.dll" -f "7-Zip $($yediSurum.Trim())")
    }
}
$djvuTxtExe = Join-Path $vh 'araclar\djvulibre\djvutxt.exe'
if (Test-Path $djvuTxtExe) {
    $djvuSurum = ""
    $djvuSetup = Get-ChildItem (Join-Path $proje '_indir\djvulibre') -Filter 'DjVuLibre-*_DjView*' -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($djvuSetup -and $djvuSetup.Name -match 'DjVuLibre-(.+)_DjView') {
        $djvuSurum = $Matches[1]
    }
    if (-not $djvuSurum) { $djvuSurum = '3.5.29' }
    $araclarListesi += ("{0,-26}: djvu.sourceforge.net (GPL-2.0), djvutxt/djvused/ddjvu" -f "DjVuLibre $djvuSurum")
}
$sv = @"
Sarnic Atolye paketi - bilesen listesi ($(Get-Date -Format 'yyyy-MM-dd'))
Bu dosya paket olusturulurken gercek dosyalardan uretilmistir.

YAZILIM KAYNAKLARI (tumu resmi yayinci kanallarindan indirilmistir)
Python 3.12.10 embeddable : python.org (python-3.12.10-embed-amd64.zip)
FFmpeg 9.0.2 essentials   : gyan.dev (ffmpeg-release-essentials.zip), GPLv3 derlemesi
DeepFilterNet v0.5.6      : github.com/Rikorose/DeepFilterNet (deep-filter-0.5.6-x86_64-pc-windows-msvc.exe) [istege bagli: --agir-ses]
Real-ESRGAN ncnn-vulkan   : github.com/xinntao/Real-ESRGAN (v0.2.5.0, realesrgan-ncnn-vulkan-20220424-windows.zip) [istege bagli: --upscale]
Kdenlive 26.08.1          : download.kde.org (kdenlive-26.08.1_standalone.exe), GPLv3
Whisper CT2 modelleri     : huggingface.co - Systran/faster-whisper-large-v3, Systran/faster-whisper-medium, mobiuslabsgmbh/faster-whisper-large-v3-turbo
OCR modelleri             : PP-OCRv6 (Apache-2.0, PaddleOCR/RapidOCR)
Sozluk                    : FrequencyWords tr_50k (CC BY-SA 4.0, hermitdave)
PyMuPDF                   : PyMuPDF (AGPL-3.0)
$($araclarListesi -join "`r`n")
$($artcraftListesi -join "`r`n")

MODEL COMMIT SHA
$($mod -join "`r`n")

PYTHON PAKETLERI (site-packages)
$($pk -join "`r`n")
"@
Set-Content -Path (Join-Path $vh 'SURUMLER.txt') -Value $sv -Encoding UTF8

Write-Host '[2/5] Kdenlive açılıyor...'
$kd = Join-Path $cikis 'Kdenlive'
& $SevenZip x $kdExe.FullName "-o$kd" -y -bso0 -bsp0
if ($LASTEXITCODE -ne 0) { throw 'Kdenlive açılamadı' }

Write-Host '[3/5] Doğrulama (dosya sayısı/boyut)...'
$boyut = (Get-ChildItem $cikis -Recurse -File | Measure-Object Length -Sum).Sum / 1GB
"{0:N2} GB, {1} dosya" -f $boyut, (Get-ChildItem $cikis -Recurse -File).Count

}
if ($SadeceKlasor) { Write-Host "Klasör hazır: $cikis"; return }
Write-Host '[4/5] 7z sıkıştırılıyor (birkaç dakika-yarım saat)...'
$arsiv = Join-Path $proje 'dist\SarnicAtolye-Paketi.7z'
Get-ChildItem (Join-Path $proje 'dist') -Filter 'SarnicAtolye-Paketi.7z*' | Remove-Item -Force
$a = @('a', '-t7z', "-mx=$Sikistirma", '-mmt=on', '-bso0', '-bsp0', $arsiv, $cikis)
if ($ParcaBoyutu) { $a += "-v$ParcaBoyutu" }
& $SevenZip @a
if ($LASTEXITCODE -ne 0) { throw '7z başarısız' }

Write-Host '[5/5] Arşiv bütünlüğü sınanıyor...'
$ilk = if ($ParcaBoyutu) { "$arsiv.001" } else { $arsiv }
& $SevenZip t $ilk -bso0 -bsp0
if ($LASTEXITCODE -ne 0) { throw '7z test başarısız' }
$h = (Get-FileHash $ilk -Algorithm SHA256).Hash
Get-ChildItem (Join-Path $proje 'dist') -Filter 'SarnicAtolye-Paketi.7z*' | ForEach-Object { '{0}  {1:N2} GB' -f $_.Name, ($_.Length / 1GB) }
"SHA256 ($(Split-Path $ilk -Leaf)): $h"
Set-Content (Join-Path $proje 'dist\SarnicAtolye-Paketi.sha256.txt') "$h  $(Split-Path $ilk -Leaf)" -Encoding ASCII
