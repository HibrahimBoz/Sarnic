# Sarnıç Atölye başlatıcı menüsü (Windows 10/11, yerleşik PowerShell 5.1; kurulum gerektirmez).
# Etkileşimli: BASLAT.bat çift tıklanır. Otomatik test: -Dosya <yol> [-Agir e|h] [-Goruntu e|h]
param(
    [string]$Dosya = '',
    [string]$Agir = 'h',
    [string]$Goruntu = 'h'
)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$kok = Split-Path -Parent $PSScriptRoot          # ...\SarnicAtolye  (bu dosya araclar\ altında)
$paketKok = Split-Path -Parent $kok              # ...\SarnicAtolye-Paketi
$kdenlive = Get-ChildItem (Join-Path $paketKok 'Kdenlive') -Recurse -Filter kdenlive.exe -ErrorAction SilentlyContinue | Select-Object -First 1
$env:SARNICATOLYE_PAUSE = '0'

function Baslik {
    Clear-Host
    Write-Host '      ___________________________________' -ForegroundColor Cyan
    Write-Host '     |   _     _     _     _     _     _   |' -ForegroundColor Cyan
    Write-Host '     |  / \   / \   / \   / \   / \   / \  |' -ForegroundColor Cyan
    Write-Host '     | |   | |   | |   | |   | |   | |   | |' -ForegroundColor Cyan
    Write-Host '     | |   | |   | |   | |   | |   | |   | |' -ForegroundColor Cyan
    Write-Host '     |~|~~~|~|~~~|~|~~~|~|~~~|~|~~~|~|~~~|~|' -ForegroundColor DarkCyan
    Write-Host '     |~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~|' -ForegroundColor DarkCyan
    Write-Host ''
    Write-Host '  Sarnıç Atölye  -  video transkript, belge, OCR, dizin' -ForegroundColor Cyan
    Write-Host '  ----------------------------------------------------' -ForegroundColor Cyan
    Write-Host ''
}

function Surucu-Surumu {
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if (-not $smi) { return $null }
    $v = & nvidia-smi --query-gpu=driver_version --format=csv,noheader 2>$null | Select-Object -First 1
    if ($v) { try { return [version]($v.Trim()) } catch { return $null } }
    return $null
}

function Cihaz-Ozeti {
    $smi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
    if ($smi) {
        $o = & nvidia-smi --query-gpu=name,memory.total,driver_version --format=csv,noheader 2>$null
        if ($o) {
            Write-Host "  Ekran kartı: $($o | Select-Object -First 1)" -ForegroundColor Gray
            $sv = Surucu-Surumu
            if ($sv -and $sv -lt [version]'528.33') {
                Write-Host "  UYARI: NVIDIA sürücüsü eski ($sv). GPU için en az 528.33 gerekir -> sürücüyü güncelleyin (BT izni gerekebilir)." -ForegroundColor Yellow
            }
            return
        }
    }
    Write-Host '  Ekran kartı: NVIDIA bulunamadı -> işlemci (CPU) ile çalışır, daha yavaş olur.' -ForegroundColor Yellow
}

function Evet-Mi([string]$soru, [string]$varsayilan = 'h') {
    $c = Read-Host "$soru (e/h) [$varsayilan]"
    if ([string]::IsNullOrWhiteSpace($c)) { $c = $varsayilan }
    return $c.Trim().ToLower().StartsWith('e')
}

function Dosya-Sec {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Title = 'İşlenecek video/ses dosyalarını seçin (birden fazla seçilebilir)'
    $d.Filter = 'Video/ses|*.mp4;*.mkv;*.mov;*.avi;*.m4a;*.wav;*.mp3|Tüm dosyalar|*.*'
    $d.Multiselect = $true
    if ($d.ShowDialog() -eq 'OK') { return $d.FileNames } else { return @() }
}

function Klasor-Sec {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'İçindeki tüm videoların işleneceği klasörü seçin'
    if ($d.ShowDialog() -eq 'OK') { return @($d.SelectedPath) } else { return @() }
}

function Belge-Sec {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Title = 'İşlenecek belge/görüntü dosyalarını seçin (birden fazla seçilebilir)'
    $d.Filter = 'Belge/görüntü|*.pdf;*.docx;*.pptx;*.xlsx;*.odt;*.rtf;*.txt;*.html;*.eml;*.msg;*.doc;*.xls;*.ppt;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|Tüm dosyalar|*.*'
    $d.Multiselect = $true
    if ($d.ShowDialog() -eq 'OK') { return $d.FileNames } else { return @() }
}

function Dizin-Klasor-Sec {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'Dizinlenecek kök klasörü seçin'
    if ($d.ShowDialog() -eq 'OK') { return @($d.SelectedPath) } else { return @() }
}

function Calistir-Python([string[]]$PythonArgs) {
    $pythonExe = Join-Path $kok 'python\python.exe'
    if (-not (Test-Path $pythonExe)) { $pythonExe = 'python' }
    $env:HF_HUB_OFFLINE = '1'
    $env:TRANSFORMERS_OFFLINE = '1'
    $env:HF_HUB_DISABLE_TELEMETRY = '1'
    $env:PYTHONNOUSERSITE = '1'
    $env:PYTHONUTF8 = '1'
    $env:SARNICATOLYE_KOK = $kok
    $env:PATH = "$kok\ffmpeg\bin;$kok\cuda;$kok\python;$env:PATH"
    # Çıktı ekrana gider; işlev yalnız çıkış kodunu döndürür
    & $pythonExe @PythonArgs | Out-Host
    return $LASTEXITCODE
}

function Isle([string[]]$girdiler, [bool]$agir, [bool]$goruntu) {
    if (-not $girdiler -or $girdiler.Count -eq 0) { Write-Host 'Seçim yapılmadı.' -ForegroundColor Yellow; return }
    $args2 = @($girdiler)
    if ($agir) { $args2 += '--agir-ses' }
    if ($goruntu) { $args2 += '--goruntu' }
    Write-Host ''
    Write-Host 'İşleniyor... (pencereyi kapatmayın; uzun videolar dakikalar sürebilir)' -ForegroundColor Green
    & (Join-Path $kok 'calistir.bat') @args2
    $kod = $LASTEXITCODE
    Write-Host ''
    if ($kod -eq 0) { Write-Host 'BİTTİ. Çıktılar her videonun yanındaki "<ad>_islenmis" klasöründe (tek video: <ad>_islenmis.mp4).' -ForegroundColor Green }
    else { Write-Host "Hata oluştu (kod $kod). Ayrıntı: $kok\is\gunluk" -ForegroundColor Red }
    $ilk = $girdiler[0]
    $klasor = if (Test-Path $ilk -PathType Container) { $ilk } else { Split-Path -Parent $ilk }
    if ($kod -eq 0 -and $klasor -and -not $Dosya) { Start-Process explorer.exe -ArgumentList ('"' + $klasor + '"') }
}

# Otomatik mod (test)
if ($Dosya) {
    Isle @($Dosya) ($Agir.ToLower().StartsWith('e')) ($Goruntu.ToLower().StartsWith('e'))
    exit $LASTEXITCODE
}

while ($true) {
    Baslik
    Cihaz-Ozeti
    Write-Host ''
    Write-Host '  1) Video/ses dosyası seç ve işle'
    Write-Host '  2) Klasör seç ve içindeki tümünü işle'
    Write-Host '  3) Klasör izleme: "gelen" klasörüne atılan videolar otomatik işlensin'
    Write-Host '  4) Kdenlive''i aç (video düzenleme)'
    Write-Host '  5) Kendi kendine test (ilk çalıştırmada önerilir)'
    Write-Host '  6) Kullanım kılavuzunu aç'
    Write-Host '  7) Çok kameralı grup ders klasörü işle (senkron + ders.json)'
    Write-Host '  8) Belge/görüntü seç ve işle (metin + aranabilir PDF)'
    Write-Host '  9) Dizinlemeyi başlat (arama portalı için; pencere açık kalsın)'
    Write-Host '  0) Çıkış'
    Write-Host ''
    $s = Read-Host 'Seçiminiz'
    if ($null -eq $s -or ([Console]::IsInputRedirected -and [string]::IsNullOrEmpty($s))) { exit 0 }   # girdi akışı kapandı (EOF): sonsuz döngüye girme
    switch ($s) {
        '1' { $g = Dosya-Sec; if ($g.Count) { Isle $g (Evet-Mi 'Ağır gürültü giderme (çok gürültülü kayıt)?') (Evet-Mi 'Görüntü iyileştirme?') }; Read-Host 'Devam için Enter' | Out-Null }
        '2' { $g = Klasor-Sec; if ($g.Count) { Isle $g (Evet-Mi 'Ağır gürültü giderme (çok gürültülü kayıt)?') (Evet-Mi 'Görüntü iyileştirme?') }; Read-Host 'Devam için Enter' | Out-Null }
        '3' {
            $gelen = Join-Path $kok 'is\gelen'
            New-Item -ItemType Directory -Force $gelen | Out-Null
            Start-Process explorer.exe -ArgumentList ('"' + $gelen + '"')
            Write-Host "Videoları açılan 'gelen' klasörüne kopyalayın. İzleme penceresini kapatana kadar çalışır (Ctrl+C ile durur)." -ForegroundColor Green
            & (Join-Path $kok 'izle.bat')
        }
        '4' {
            if ($kdenlive) { Start-Process $kdenlive.FullName } else { Write-Host 'Kdenlive bulunamadı (SarnicAtolye-Paketi\Kdenlive).' -ForegroundColor Red; Read-Host 'Enter' | Out-Null }
        }
        '5' { & (Join-Path $kok 'kontrol.bat'); Read-Host 'Devam için Enter' | Out-Null }
        '6' { Start-Process notepad.exe -ArgumentList ('"' + (Join-Path $kok 'KULLANIM.txt') + '"') }
        '7' {
            $k = Klasor-Sec
            if ($k.Count) {
                $klasor = $k[0]
                $agir = Evet-Mi 'Ağır gürültü giderme (çok gürültülü kayıt)?'
                $goruntu = Evet-Mi 'Görüntü iyileştirme?'
                $args2 = @('"' + $klasor + '"')
                if ($agir) { $args2 += '--agir-ses' }
                if ($goruntu) { $args2 += '--goruntu' }
                Write-Host ''
                Write-Host 'Grup işleniyor... (pencereyi kapatmayın; uzun videolar dakikalar sürebilir)' -ForegroundColor Green
                $grupBat = Join-Path $kok 'grup.bat'
                if (-not (Test-Path $grupBat)) { $grupBat = Join-Path $kok 'bat\grup.bat' }
                & $grupBat @args2
                $kod = $LASTEXITCODE
                Write-Host ''
                if ($kod -eq 0) { Write-Host 'BİTTİ. Çıktılar ders klasöründe (<ad>_islenmis\ders.json).' -ForegroundColor Green }
                else { Write-Host "Hata oluştu (kod $kod). Ayrıntı: $kok\is\gunluk" -ForegroundColor Red }
                if ($kod -eq 0 -and $klasor -and -not $Dosya) { Start-Process explorer.exe -ArgumentList ('"' + $klasor + '"') }
            }
            Read-Host 'Devam için Enter' | Out-Null
        }
        '8' {
            $g = Belge-Sec
            if ($g.Count) {
                Write-Host ''
                Write-Host 'Belgeler işleniyor... (pencereyi kapatmayın)' -ForegroundColor Green
                $pyArgs = @('-m', 'sarnicatolye', 'belge') + $g
                $kod = Calistir-Python $pyArgs
                Write-Host ''
                if ($kod -eq 0) { Write-Host 'BİTTİ. Çıktılar her belgenin yanındaki "<ad>_belge" klasöründe.' -ForegroundColor Green }
                else { Write-Host "Hata oluştu (kod $kod). Ayrıntı: $kok\is\gunluk" -ForegroundColor Red }
                $ilk = $g[0]
                $klasor = if (Test-Path $ilk -PathType Container) { $ilk } else { Split-Path -Parent $ilk }
                if ($kod -eq 0 -and $klasor -and -not $Dosya) { Start-Process explorer.exe -ArgumentList ('"' + $klasor + '"') }
            }
            Read-Host 'Devam için Enter' | Out-Null
        }
        '9' {
            $k = Dizin-Klasor-Sec
            if ($k.Count) {
                $klasor = $k[0]
                Write-Host ''
                Write-Host "Dizinleme başlatılıyor: $klasor (Ctrl+C ile durur; pencere açık kalsın)..." -ForegroundColor Green
                Write-Host 'Dizin deposu: dizin dosyalarının yazılacağı klasör. Sunucu dosyalarına dokunulmaz.'
                $depo = Read-Host 'Depo klasörü (boş bırakın: kök klasörün içine _sarnic yazılır)'
                $pyArgs = @('-m', 'sarnicatolye', 'dizinle', '--kok', $klasor)
                if (-not [string]::IsNullOrWhiteSpace($depo)) { $pyArgs += @('--depo', $depo.Trim().Trim('"')) }
                $kod = Calistir-Python $pyArgs
                Write-Host ''
                if ($kod -eq 0) { Write-Host 'BİTTİ. Dizinleme tamamlandı.' -ForegroundColor Green }
                else { Write-Host "Hata oluştu (kod $kod). Ayrıntı: $kok\is\gunluk" -ForegroundColor Red }
            }
            Read-Host 'Devam için Enter' | Out-Null
        }
        '0' { exit 0 }
        default { }
    }
}
