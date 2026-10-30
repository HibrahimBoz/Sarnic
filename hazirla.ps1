# ==============================================================================
# VideoHat Dağıtım Paketi Hazırlama Betiği (hazirla.ps1)
# Windows PowerShell 5.1+ ve PowerShell 7 uyumludur.
# ==============================================================================

[CmdletBinding()]
param(
    [ValidateSet('GPU', 'CPU', 'Ikisi')]
    [string]$Paket = 'Ikisi',

    [switch]$ModelAtla,

    [string[]]$ArtCraft = @('wordcraft', 'gridcraft', 'deckcraft', 'pdfcraft', 'photocraft')
)

# Hata durumunda hemen durdur ve indirme çubuğunu gizle
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    # --------------------------------------------------------------------------
    # Dizin Tanımlamaları
    # --------------------------------------------------------------------------
    $ProjeKoku = $PSScriptRoot
    $IndirKlasoru = Join-Path $ProjeKoku '_indir'
    $KaynakKlasoru = Join-Path $ProjeKoku 'kaynak'
    $DistKlasoru = Join-Path $ProjeKoku 'dist'

    $GpuPaketYolu = Join-Path $DistKlasoru 'VideoHat-GPU'
    $CpuPaketYolu = Join-Path $DistKlasoru 'VideoHat-CPU'

    if (-not (Test-Path $IndirKlasoru)) {
        New-Item -ItemType Directory -Path $IndirKlasoru -Force | Out-Null
    }
    if (-not (Test-Path $DistKlasoru)) {
        New-Item -ItemType Directory -Path $DistKlasoru -Force | Out-Null
    }

    # --------------------------------------------------------------------------
    # Yardımcı Fonksiyonlar
    # --------------------------------------------------------------------------
    function Invoke-RobocopyMirror {
        param(
            [string]$Kaynak,
            [string]$Hedef,
            [string[]]$EkArgumanlar = @()
        )
        if (-not (Test-Path $Kaynak)) {
            New-Item -ItemType Directory -Path $Kaynak -Force | Out-Null
        }
        if (-not (Test-Path $Hedef)) {
            New-Item -ItemType Directory -Path $Hedef -Force | Out-Null
        }
        $robocopyArgs = @($Kaynak, $Hedef, '/MIR', '/NP', '/NDL', '/NFL', '/NJH', '/NJS') + $EkArgumanlar
        & robocopy.exe $robocopyArgs
        # Robocopy 0-7 arası kodlar başarılı kabul edilir.
        if ($LASTEXITCODE -ge 8) {
            throw "Robocopy kopyalama başarısız oldu (Hata kodu: $LASTEXITCODE). Kaynak: $Kaynak -> Hedef: $Hedef"
        }
    }

    function Get-DosyaHashVeBoyutListesi {
        param([string]$KokDizin)
        $dosyalar = Get-ChildItem -Path $KokDizin -Recurse -File | Where-Object {
            $_.Extension -in @('.exe', '.dll', '.bin') -or $_.Length -ge 10MB
        }
        $sonuclar = @()
        foreach ($d in $dosyalar) {
            $hashObj = Get-FileHash -Path $d.FullName -Algorithm SHA256
            $rel = $d.FullName.Substring($KokDizin.Length).TrimStart('\', '/')
            $boyutMB = [math]::Round($d.Length / 1MB, 2)
            $sonuclar += [PSCustomObject]@{
                GoreliYol = $rel
                BoyutMB   = $boyutMB
                SHA256    = $hashObj.Hash
            }
        }
        return $sonuclar
    }

    # --------------------------------------------------------------------------
    # [1/10] Ön Kontrol: Python ve İnternet Erişimi
    # --------------------------------------------------------------------------
    Write-Host "[1/10] Ön kontroller yapılıyor (Python ve internet erişimi)..." -ForegroundColor Cyan

    $hostPythonCmd = $null
    $hostPythonPrefixArgs = @()

    # Öncelikli olarak 'py -3.13' kontrol edilir
    try {
        $null = & py -3.13 --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            $hostPythonCmd = 'py'
            $hostPythonPrefixArgs = @('-3.13')
        }
    } catch {}

    # py -3.13 bulunamazsa standart 'python' komutu kontrol edilir
    if (-not $hostPythonCmd) {
        try {
            $null = & python --version 2>&1
            if ($LASTEXITCODE -eq 0) {
                $hostPythonCmd = 'python'
                $hostPythonPrefixArgs = @()
            }
        } catch {}
    }

    if (-not $hostPythonCmd) {
        throw "Ön kontrol hatası: Geliştirme makinesinde ne 'py -3.13' ne de 'python' komutu bulunamadı. Lütfen Python'un kurulu ve PATH ortam değişkenine ekli olduğundan emin olun."
    }

    function Invoke-GelistiriciPython {
        param([string[]]$PythonArgumanlari)
        $tumArgumanlar = $hostPythonPrefixArgs + $PythonArgumanlari
        & $hostPythonCmd $tumArgumanlar
        if ($LASTEXITCODE -ne 0) {
            throw "Python komutu hata verdi (Çıkış kodu: $LASTEXITCODE): $hostPythonCmd $($tumArgumanlar -join ' ')"
        }
    }

    # İnternet erişimi kontrolü (python.org'a HEAD isteği)
    try {
        $headReq = Invoke-WebRequest -Uri "https://www.python.org" -Method Head -UseBasicParsing -TimeoutSec 15
        if ($headReq.StatusCode -lt 200 -or $headReq.StatusCode -ge 400) {
            throw "HTTP durum kodu: $($headReq.StatusCode)"
        }
    } catch {
        throw "Ön kontrol hatası: İnternet erişimi doğrulanamadı (https://www.python.org): $($_.Exception.Message)"
    }

    # --------------------------------------------------------------------------
    # [2/10] Embeddable Python 3.12.10 İndirme ve Açma
    # --------------------------------------------------------------------------
    Write-Host "[2/10] Embeddable Python 3.12.10 indiriliyor ve hazırlanıyor..." -ForegroundColor Cyan

    $pythonZipUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip"
    $pythonZipCache = Join-Path $IndirKlasoru "python-3.12.10-embed-amd64.zip"
    $pythonEmbedCache = Join-Path $IndirKlasoru "python312_embed"

    if (-not (Test-Path $pythonZipCache)) {
        Write-Host "  -> Python embed zip indiriliyor: $pythonZipUrl"
        Invoke-WebRequest -Uri $pythonZipUrl -OutFile $pythonZipCache -UseBasicParsing
    } else {
        Write-Host "  -> Python embed zip önbellekte mevcut." -ForegroundColor Gray
    }

    if (-not (Test-Path (Join-Path $pythonEmbedCache "python.exe"))) {
        Write-Host "  -> Python embed zip önbellek dizinine açılıyor..."
        if (Test-Path $pythonEmbedCache) { Remove-Item -Path $pythonEmbedCache -Recurse -Force }
        New-Item -ItemType Directory -Path $pythonEmbedCache -Force | Out-Null
        Expand-Archive -Path $pythonZipCache -DestinationPath $pythonEmbedCache -Force
    }

    # python312._pth dosyasını kaynak klasöründen kopyala
    $pthKaynak = Join-Path $KaynakKlasoru "python312._pth"
    if (-not (Test-Path $pthKaynak)) {
        throw "Gerekli kaynak dosyası bulunamadı: $pthKaynak"
    }
    Copy-Item -Path $pthKaynak -Destination (Join-Path $pythonEmbedCache "python312._pth") -Force

    # --------------------------------------------------------------------------
    # [3/10] Pip Paketleri Hazırlığı (faster-whisper, ctranslate2, nvidia-*)
    # --------------------------------------------------------------------------
    Write-Host "[3/10] Python bağımlılık paketleri (pip) hedefe kuruluyor..." -ForegroundColor Cyan

    $pipCacheDir = Join-Path $IndirKlasoru "pip_cache"
    if (-not (Test-Path $pipCacheDir)) {
        New-Item -ItemType Directory -Path $pipCacheDir -Force | Out-Null
    }

    # Paket kurulum hedeflerini belirle
    # GPU gerekiyorsa doğrudan GPU klasörüne veya önbelleğe kurulur
    $ortakSitePackagesGPU = Join-Path $IndirKlasoru "site-packages-gpu"
    $ortakSitePackagesCPU = Join-Path $IndirKlasoru "site-packages-cpu"
    $pipFreezeGpuCikti = ""
    $pipFreezeCpuCikti = ""

    if ($Paket -in @('GPU', 'Ikisi')) {
        if (-not (Test-Path (Join-Path $ortakSitePackagesGPU "ctranslate2")) -or -not (Test-Path (Join-Path $ortakSitePackagesGPU "pymupdf")) -or -not (Test-Path (Join-Path $ortakSitePackagesGPU "rapidocr"))) {
            Write-Host "  -> GPU paketleri indiriliyor ve kuruluyor..."
            New-Item -ItemType Directory -Path $ortakSitePackagesGPU -Force | Out-Null
            $gpuPipArgs = @(
                '-m', 'pip', 'install',
                '--cache-dir', $pipCacheDir,
                '--target', $ortakSitePackagesGPU,
                '--python-version', '3.12',
                '--platform', 'win_amd64',
                '--implementation', 'cp',
                '--only-binary=:all:',
                'faster-whisper==1.2.1',
                'ctranslate2==4.8.2',
                'av<17',
                'nvidia-cublas-cu12',
                'nvidia-cudnn-cu12==9.*',
                'pymupdf==2.0',
                'rapidocr==3.10.0',
                'onnxruntime==1.31.1',
                'opencv-python-headless==5.0.0.93',
                'numpy==2.5.4',
                'pillow==13.0.0',
                'python-docx==1.2.0',
                'python-pptx==1.0.2',
                'openpyxl==3.1.5',
                'olefile==0.47',
                'extract-msg==0.30.4'
            )
            Invoke-GelistiriciPython -PythonArgumanlari $gpuPipArgs
        } else {
            Write-Host "  -> GPU paketleri önbellekte mevcut." -ForegroundColor Gray
        }

        # pip freeze çıktısını al
        $pipFreezeGpuCikti = (& $hostPythonCmd ($hostPythonPrefixArgs + @('-m', 'pip', 'freeze', '--path', $ortakSitePackagesGPU)) 2>&1) -join "`r`n"
    }

    if ($Paket -eq 'CPU') {
        if (-not (Test-Path (Join-Path $ortakSitePackagesCPU "ctranslate2")) -or -not (Test-Path (Join-Path $ortakSitePackagesCPU "pymupdf")) -or -not (Test-Path (Join-Path $ortakSitePackagesCPU "rapidocr"))) {
            Write-Host "  -> CPU paketleri indiriliyor ve kuruluyor (nvidia-* hariç)..."
            New-Item -ItemType Directory -Path $ortakSitePackagesCPU -Force | Out-Null
            $cpuPipArgs = @(
                '-m', 'pip', 'install',
                '--cache-dir', $pipCacheDir,
                '--target', $ortakSitePackagesCPU,
                '--python-version', '3.12',
                '--platform', 'win_amd64',
                '--implementation', 'cp',
                '--only-binary=:all:',
                'faster-whisper==1.2.1',
                'ctranslate2==4.8.2',
                'av<17',
                'pymupdf==2.0',
                'rapidocr==3.10.0',
                'onnxruntime==1.31.1',
                'opencv-python-headless==5.0.0.93',
                'numpy==2.5.4',
                'pillow==13.0.0',
                'python-docx==1.2.0',
                'python-pptx==1.0.2',
                'openpyxl==3.1.5',
                'olefile==0.47',
                'extract-msg==0.30.4'
            )
            Invoke-GelistiriciPython -PythonArgumanlari $cpuPipArgs
        } else {
            Write-Host "  -> CPU paketleri önbellekte mevcut." -ForegroundColor Gray
        }
        $pipFreezeCpuCikti = (& $hostPythonCmd ($hostPythonPrefixArgs + @('-m', 'pip', 'freeze', '--path', $ortakSitePackagesCPU)) 2>&1) -join "`r`n"
    }

    # --------------------------------------------------------------------------
    # [4/10] GPU CUDA DLL'leri (cublas + cudnn -> cuda\; nvidia sil)
    # --------------------------------------------------------------------------
    Write-Host "[4/10] GPU paketi için CUDA DLL'leri düzenleniyor..." -ForegroundColor Cyan

    $cudaDllCache = Join-Path $IndirKlasoru "cuda_dlls"
    if ($Paket -in @('GPU', 'Ikisi')) {
        if (-not (Test-Path $cudaDllCache)) {
            New-Item -ItemType Directory -Path $cudaDllCache -Force | Out-Null
            
            $cublasDlls = Get-ChildItem -Path (Join-Path $ortakSitePackagesGPU "nvidia\cublas\bin\*.dll") -ErrorAction SilentlyContinue
            $cudnnDlls = Get-ChildItem -Path (Join-Path $ortakSitePackagesGPU "nvidia\cudnn\bin\*.dll") -ErrorAction SilentlyContinue

            if (-not $cublasDlls -or -not $cudnnDlls) {
                throw "CUDA DLL'leri bulunamadı! nvidia-cublas veya nvidia-cudnn kurulumu eksik."
            }

            foreach ($dll in ($cublasDlls + $cudnnDlls)) {
                Copy-Item -Path $dll.FullName -Destination $cudaDllCache -Force
            }
            Write-Host "  -> CUDA DLL'leri cuda önbelleğine kopyalandı."
        } else {
            Write-Host "  -> CUDA DLL'leri önbellekte mevcut." -ForegroundColor Gray
        }

        # site-packages\nvidia klasörünü temizle
        $nvidiaSitePkg = Join-Path $ortakSitePackagesGPU "nvidia"
        if (Test-Path $nvidiaSitePkg) {
            Remove-Item -Path $nvidiaSitePkg -Recurse -Force
        }
    } else {
        Write-Host "  -> Yalnız CPU paketi seçildi, CUDA adımı atlandı." -ForegroundColor Gray
    }

    # --------------------------------------------------------------------------
    # [5/10] MSVC Çalışma Zamanı DLL'leri
    # --------------------------------------------------------------------------
    Write-Host "[5/10] MSVC çalışma zamanı kütüphaneleri toplanıyor..." -ForegroundColor Cyan

    $msvcCacheDir = Join-Path $IndirKlasoru "msvc_dlls"
    if (-not (Test-Path $msvcCacheDir)) {
        New-Item -ItemType Directory -Path $msvcCacheDir -Force | Out-Null
    }

    $msvcDlls = @('msvcp140.dll', 'msvcp140_1.dll', 'msvcp140_2.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll', 'concrt140.dll')
    $system32Dir = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::System)
    if (-not $system32Dir) { $system32Dir = "$env:SystemRoot\System32" }

    foreach ($dll in $msvcDlls) {
        $kaynakDll = Join-Path $system32Dir $dll
        $hedefDll = Join-Path $msvcCacheDir $dll
        if (Test-Path $kaynakDll) {
            Copy-Item -Path $kaynakDll -Destination $hedefDll -Force
        } else {
            Write-Warning "MSVC çalışma zamanı DLL'i System32 altında bulunamadı (atlandı): $dll"
        }
    }

    # --------------------------------------------------------------------------
    # [6/10] FFmpeg İndir / Aç (yalnız bin\*.exe)
    # --------------------------------------------------------------------------
    Write-Host "[6/10] FFmpeg ikili dosyaları indiriliyor ve ayıklanıyor..." -ForegroundColor Cyan

    $ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
    $ffmpegZip = Join-Path $IndirKlasoru "ffmpeg-release-essentials.zip"
    $ffmpegCacheBin = Join-Path $IndirKlasoru "ffmpeg_bin"

    if (-not (Test-Path (Join-Path $ffmpegCacheBin "ffmpeg.exe")) -or -not (Test-Path (Join-Path $ffmpegCacheBin "ffprobe.exe"))) {
        if (-not (Test-Path $ffmpegZip)) {
            Write-Host "  -> FFmpeg zip indiriliyor: $ffmpegUrl"
            Invoke-WebRequest -Uri $ffmpegUrl -OutFile $ffmpegZip -UseBasicParsing
        }

        Write-Host "  -> FFmpeg arşivinden ffmpeg.exe ve ffprobe.exe ayıklanıyor..."
        if (-not (Test-Path $ffmpegCacheBin)) {
            New-Item -ItemType Directory -Path $ffmpegCacheBin -Force | Out-Null
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ffmpegZip)
        try {
            foreach ($entry in $archive.Entries) {
                $normName = $entry.FullName -replace '\\', '/'
                if ($entry.Name -in @('ffmpeg.exe', 'ffprobe.exe') -and $normName -match '/bin/') {
                    $destExe = Join-Path $ffmpegCacheBin $entry.Name
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destExe, $true)
                }
            }
        } finally {
            $archive.Dispose()
        }

        if (-not (Test-Path (Join-Path $ffmpegCacheBin "ffmpeg.exe")) -or -not (Test-Path (Join-Path $ffmpegCacheBin "ffprobe.exe"))) {
            throw "FFmpeg arşivinden ffmpeg.exe veya ffprobe.exe ayıklanamadı!"
        }
    } else {
        Write-Host "  -> FFmpeg ikili dosyaları önbellekte mevcut." -ForegroundColor Gray
    }

    # --------------------------------------------------------------------------
    # [7/10] CT2 Modelleri İndirme (huggingface_hub.snapshot_download)
    # --------------------------------------------------------------------------
    Write-Host "[7/10] CT2 modelleri indiriliyor (snapshot_download)..." -ForegroundColor Cyan

    $modelShaBilgileri = @{}

    if ($ModelAtla) {
        Write-Host "  -> -ModelAtla anahtarı belirtildi, model indirme adımı atlandı." -ForegroundColor Yellow
    } else {
        # Geliştirme makinesinde huggingface_hub kontrolü
        $oncekiEAP = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $hfCheck = & $hostPythonCmd ($hostPythonPrefixArgs + @('-c', 'import huggingface_hub')) 2>&1
        $hfKod = $LASTEXITCODE
        $ErrorActionPreference = $oncekiEAP
        if ($hfKod -ne 0) {
            Write-Host "  -> huggingface_hub geliştirici makinesine kuruluyor (--user)..."
            & $hostPythonCmd ($hostPythonPrefixArgs + @('-m', 'pip', 'install', '--user', 'huggingface_hub'))
            if ($LASTEXITCODE -ne 0) {
                throw "huggingface_hub kurulumu başarısız oldu!"
            }
        }

        # Model indirme python betiğini hazırla
        $modelIndiriciPy = Join-Path $IndirKlasoru "hf_indirici.py"
        $modelIndiriciKod = @'
import sys
import os
import shutil

repo_id = sys.argv[1]
prefix_or_branch = sys.argv[2]
dest_dir = sys.argv[3]

from huggingface_hub import HfApi, snapshot_download

api = HfApi()
resolved_sha = None

if prefix_or_branch != "main":
    try:
        commits = api.list_repo_commits(repo_id)
        for c in commits:
            cid = getattr(c, 'commit_id', str(c))
            if cid.lower().startswith(prefix_or_branch.lower()):
                resolved_sha = cid
                break
    except Exception as e:
        sys.stderr.write(f"Commit listesi alinamadi ({repo_id}): {e}\n")

    if not resolved_sha:
        sys.stderr.write(f"Uyari: '{prefix_or_branch}' oneki {repo_id} reposunda bulunamadi, 'main' dalina dusuluyor.\n")
        try:
            info = api.model_info(repo_id)
            resolved_sha = info.sha
        except Exception:
            resolved_sha = "main"
else:
    try:
        info = api.model_info(repo_id)
        resolved_sha = info.sha
    except Exception:
        resolved_sha = "main"

print(f"Indiriliyor: {repo_id} (Revizyon: {resolved_sha}) -> {dest_dir}")
os.makedirs(dest_dir, exist_ok=True)
snapshot_download(repo_id=repo_id, revision=resolved_sha, local_dir=dest_dir)

cache_dir = os.path.join(dest_dir, ".cache")
if os.path.exists(cache_dir):
    shutil.rmtree(cache_dir, ignore_errors=True)

# Gerekli dosyalari dogrula
for f in ["model.bin", "tokenizer.json", "config.json"]:
    fp = os.path.join(dest_dir, f)
    if not os.path.isfile(fp):
        raise FileNotFoundError(f"Eksik model dosyasi: {fp}")

sha_file = os.path.join(dest_dir, "commit_sha.txt")
with open(sha_file, "w", encoding="utf-8") as sf:
    sf.write(str(resolved_sha).strip())

print(f"MODEL_TAMAMLANDI:{resolved_sha}")
'@
        [System.IO.File]::WriteAllText($modelIndiriciPy, $modelIndiriciKod, [System.Text.Encoding]::UTF8)

        $modellerKlasoru = Join-Path $IndirKlasoru "modeller"
        if (-not (Test-Path $modellerKlasoru)) {
            New-Item -ItemType Directory -Path $modellerKlasoru -Force | Out-Null
        }

        # İndirilecek modeller listesi
        $modelListesi = @(
            @{ Ad = 'medium'; Repo = 'Systran/faster-whisper-medium'; Rev = '08e178d4' },
            @{ Ad = 'large-v3'; Repo = 'Systran/faster-whisper-large-v3'; Rev = 'edaa852e' },
            @{ Ad = 'turbo'; Repo = 'mobiuslabsgmbh/faster-whisper-large-v3-turbo'; Rev = 'main' }
        )

        foreach ($m in $modelListesi) {
            $mHedef = Join-Path $modellerKlasoru $m.Ad
            $shaDosyasi = Join-Path $mHedef "commit_sha.txt"
            $modelBinDosyasi = Join-Path $mHedef "model.bin"

            if ((Test-Path $shaDosyasi) -and (Test-Path $modelBinDosyasi)) {
                $shaVal = (Get-Content -Path $shaDosyasi -Raw).Trim()
                Write-Host "  -> $($m.Ad) modeli önbellekte mevcut (SHA: $shaVal)." -ForegroundColor Gray
                $modelShaBilgileri[$m.Ad] = $shaVal
            } else {
                Write-Host "  -> $($m.Ad) modeli indiriliyor ($($m.Repo))..."
                Invoke-GelistiriciPython -PythonArgumanlari @($modelIndiriciPy, $m.Repo, $m.Rev, $mHedef)
                if (-not (Test-Path $shaDosyasi)) {
                    throw "$($m.Ad) model SHA dosyası üretilemedi!"
                }
                $shaVal = (Get-Content -Path $shaDosyasi -Raw).Trim()
                $modelShaBilgileri[$m.Ad] = $shaVal
            }
        }
    }

    # --------------------------------------------------------------------------
    # --------------------------------------------------------------------------
    # OCR modelleri ve Türkçe sözlük listesi (_indir\modeller-ocr, _indir\sozluk)
    # --------------------------------------------------------------------------
    Write-Host "  -> OCR modelleri indiriliyor ve kontrol ediliyor..." -ForegroundColor Cyan

    $ocrModellerKlasoru = Join-Path $IndirKlasoru "modeller-ocr"
    if (-not (Test-Path $ocrModellerKlasoru)) {
        New-Item -ItemType Directory -Path $ocrModellerKlasoru -Force | Out-Null
    }

    $ocrModelListesi = @(
        @{
            Ad     = 'ch_ppocr_mobile_v2.0_cls_mobile.onnx'
            Url    = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv4/cls/ch_ppocr_mobile_v2.0_cls_mobile.onnx'
            SHA256 = 'e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c'
        },
        @{
            Ad     = 'PP-OCRv6_det_small.onnx'
            Url    = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv6/det/PP-OCRv6_det_small.onnx'
            SHA256 = '090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f'
        },
        @{
            Ad     = 'PP-OCRv6_det_medium.onnx'
            Url    = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv6/det/PP-OCRv6_det_medium.onnx'
            SHA256 = '92078b7355007ccfffcd4c8cd441a3afd4538904d06881b29a155e1e679907c2'
        },
        @{
            Ad     = 'PP-OCRv6_rec_small.onnx'
            Url    = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv6/rec/PP-OCRv6_rec_small.onnx'
            SHA256 = '6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884'
        },
        @{
            Ad     = 'PP-OCRv6_rec_medium.onnx'
            Url    = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv6/rec/PP-OCRv6_rec_medium.onnx'
            SHA256 = 'eef444829dbbe18d7fea59a3f6eb75647518d2b3a9568d27c92e42940204894b'
        }
    )

    foreach ($om in $ocrModelListesi) {
        $omHedef = Join-Path $ocrModellerKlasoru $om.Ad
        $indirGerekli = $true
        if (Test-Path $omHedef) {
            $mevcutHash = (Get-FileHash -Path $omHedef -Algorithm SHA256).Hash
            if ($mevcutHash -eq $om.SHA256) {
                Write-Host "  -> $($om.Ad) önbellekte mevcut (SHA256 doğrulandı)." -ForegroundColor Gray
                $indirGerekli = $false
            } else {
                Write-Host "  -> $($om.Ad) SHA256 uyuşmazlığı, yeniden indirilecek..." -ForegroundColor Yellow
                Remove-Item -Path $omHedef -Force
            }
        }
        if ($indirGerekli) {
            Write-Host "  -> $($om.Ad) indiriliyor..."
            Invoke-WebRequest -Uri $om.Url -OutFile $omHedef -UseBasicParsing
            $inenHash = (Get-FileHash -Path $omHedef -Algorithm SHA256).Hash
            if ($inenHash -ne $om.SHA256) {
                Remove-Item -Path $omHedef -Force
                throw "SHA256 doğrulama hatası ($($om.Ad))! Beklenen: $($om.SHA256), Hesaplanan: $inenHash"
            }
            Write-Host "  -> $($om.Ad) indirildi ve SHA256 doğrulandı." -ForegroundColor Gray
        }
    }

    # Türkçe sözcük sıklık listesi
    $sozlukKlasoru = Join-Path $IndirKlasoru "sozluk"
    if (-not (Test-Path $sozlukKlasoru)) {
        New-Item -ItemType Directory -Path $sozlukKlasoru -Force | Out-Null
    }
    $sozlukHedef = Join-Path $sozlukKlasoru "tr_kelime.txt"
    $sozlukUrl = "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/tr/tr_50k.txt"
    if (-not (Test-Path $sozlukHedef)) {
        try {
            Write-Host "  -> Türkçe sözcük sıklık listesi indiriliyor: $sozlukUrl"
            Invoke-WebRequest -Uri $sozlukUrl -OutFile $sozlukHedef -UseBasicParsing
            Write-Host "  -> tr_kelime.txt indirildi." -ForegroundColor Gray
        } catch {
            Write-Warning "Türkçe sözcük sıklık listesi indirilemedi (isteğe bağlı, paketleme sürüyor): $($_.Exception.Message)"
        }
    } else {
        Write-Host "  -> tr_kelime.txt önbellekte mevcut." -ForegroundColor Gray
    }

    # --------------------------------------------------------------------------
    # [8/10] Araçlar (DeepFilterNet ve Real-ESRGAN)
    # --------------------------------------------------------------------------
    Write-Host "[8/10] Yardımcı araçlar indiriliyor (DeepFilterNet ve Real-ESRGAN)..." -ForegroundColor Cyan

    $araclarCache = Join-Path $IndirKlasoru "araclar"
    if (-not (Test-Path $araclarCache)) {
        New-Item -ItemType Directory -Path $araclarCache -Force | Out-Null
    }

    # DeepFilterNet
    $deepFilterUrl = "https://github.com/Rikorose/DeepFilterNet/releases/download/v0.5.6/deep-filter-0.5.6-x86_64-pc-windows-msvc.exe"
    $deepFilterExe = Join-Path $araclarCache "deep-filter.exe"
    if (-not (Test-Path $deepFilterExe)) {
        Write-Host "  -> deep-filter.exe indiriliyor..."
        Invoke-WebRequest -Uri $deepFilterUrl -OutFile $deepFilterExe -UseBasicParsing
    } else {
        Write-Host "  -> deep-filter.exe önbellekte mevcut." -ForegroundColor Gray
    }

    $deepFilterHelp = ""
    try {
        $deepFilterHelp = (& $deepFilterExe --help 2>&1) -join "`r`n"
    } catch {
        $deepFilterHelp = "deep-filter.exe yardım çıktısı alınamadı: $($_.Exception.Message)"
    }

    # Real-ESRGAN (hata olursa atla)
    $realesrganUrl = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-windows.zip"
    $realesrganZip = Join-Path $IndirKlasoru "realesrgan-ncnn-vulkan-20220424-windows.zip"
    $realesrganDir = Join-Path $araclarCache "realesrgan"

    try {
        if (-not (Test-Path (Join-Path $realesrganDir "realesrgan-ncnn-vulkan.exe"))) {
            if (-not (Test-Path $realesrganZip)) {
                Write-Host "  -> Real-ESRGAN zip indiriliyor..."
                Invoke-WebRequest -Uri $realesrganUrl -OutFile $realesrganZip -UseBasicParsing
            }
            Write-Host "  -> Real-ESRGAN zip açılıyor..."
            if (Test-Path $realesrganDir) { Remove-Item -Path $realesrganDir -Recurse -Force }
            New-Item -ItemType Directory -Path $realesrganDir -Force | Out-Null
            Expand-Archive -Path $realesrganZip -DestinationPath $realesrganDir -Force
        } else {
            Write-Host "  -> Real-ESRGAN önbellekte mevcut." -ForegroundColor Gray
        }
    } catch {
        Write-Warning "Real-ESRGAN indirilemedi veya açılamadı (kritik değil, atlandı): $($_.Exception.Message)"
    }

    # --------------------------------------------------------------------------
    # --------------------------------------------------------------------------
    # ArtCraft Taşınabilir Uygulamaları (wordcraft, gridcraft, deckcraft, ...)
    # --------------------------------------------------------------------------
    Write-Host "  -> ArtCraft taşınabilir uygulamaları kontrol ediliyor..." -ForegroundColor Cyan

    $artcraftIndirKlasoru = Join-Path $IndirKlasoru "artcraft"
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

    $artcraftZipleri = @{}
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
                    } else {
                        Write-Warning "${adKucuk}: GitHub özeti (digest) yok, doğrulanmadı"
                    }
                } else {
                    Write-Warning "${adKucuk}: '$assetAdi' varlığı (asset) bulunamadı"
                }
            }
        } catch {
            Write-Warning "ArtCraft ($adKucuk) GitHub API hatası: $($_.Exception.Message)"
        }

        # Eğer API'den sürüm alınamadıysa önbellekte mevcut zip ara
        if (-not $surum) {
            $onbellekZip = Get-ChildItem -Path $artcraftIndirKlasoru -Filter "$adKucuk-*-windows-x64-portable.zip" -ErrorAction SilentlyContinue |
                Where-Object {
                    $raw = $_.Name -replace "^$adKucuk-","" -replace "-windows-x64-portable\.zip$",""
                    $v = $null
                    [version]::TryParse($raw, [ref]$v)
                } |
                Sort-Object { [version](($_.Name -replace "^$adKucuk-","" -replace "-windows-x64-portable\.zip$","")) } |
                Select-Object -Last 1
            if ($onbellekZip -and $onbellekZip.Name -match "^$adKucuk-(.+)-windows-x64-portable\.zip$") {
                $surum = $Matches[1]
                $assetAdi = $onbellekZip.Name
            }
        }

        if ($assetAdi) {
            $hedefZip = Join-Path $artcraftIndirKlasoru $assetAdi
            $hedefZipTmp = "$hedefZip.tmp"

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
                    Invoke-WebRequest -Uri $downloadUrl -OutFile $hedefZipTmp -UseBasicParsing -TimeoutSec 60
                    if ($beklenenDigest) {
                        $inenHash = (Get-FileHash -Path $hedefZipTmp -Algorithm SHA256).Hash
                        if (-not $inenHash.Equals($beklenenDigest, [System.StringComparison]::OrdinalIgnoreCase)) {
                            Remove-Item -Path $hedefZipTmp -Force -ErrorAction SilentlyContinue
                            Write-Warning "ArtCraft ($adKucuk) SHA256 uyuşmazlığı! Dosya silindi. Beklenen: $beklenenDigest, Alınan: $inenHash"
                        } else {
                            Move-Item -Path $hedefZipTmp -Destination $hedefZip -Force
                            $zipGecerli = $true
                        }
                    } else {
                        Move-Item -Path $hedefZipTmp -Destination $hedefZip -Force
                        $zipGecerli = $true
                    }
                } catch {
                    Remove-Item -Path $hedefZipTmp -Force -ErrorAction SilentlyContinue
                    Write-Warning "ArtCraft ($adKucuk) indirme hatası: $($_.Exception.Message)"
                }
            }

            if ($zipGecerli -and (Test-Path $hedefZip)) {
                $artcraftZipleri[$adKucuk] = @{
                    Ad    = $adGoster
                    Surum = $surum
                    Zip   = $hedefZip
                }
                $artcraftSurumListesi += "$adGoster $surum (Apache-2.0, github.com/storytold/$adKucuk)"
            }
        }
    }

    # --------------------------------------------------------------------------
    # [9/10] Uygulama Dosyaları, Yapılandırmalar ve Dizin Düzeni
    # --------------------------------------------------------------------------
    Write-Host "[9/10] Uygulama yapılandırmaları ve kaynak dosyaları hazırlanıyor..." -ForegroundColor Cyan

    $ayarKaynak = Join-Path $KaynakKlasoru "ayar.toml"
    $terimlerKaynak = Join-Path $KaynakKlasoru "terimler.txt"
    $batKaynak = Join-Path $KaynakKlasoru "bat"
    $appKaynak = Join-Path $KaynakKlasoru "app"

    if (-not (Test-Path $ayarKaynak)) { throw "Gerekli kaynak dosyası yok: $ayarKaynak" }
    if (-not (Test-Path $terimlerKaynak)) { throw "Gerekli kaynak dosyası yok: $terimlerKaynak" }
    if (-not (Test-Path $batKaynak)) { throw "Gerekli bat klasörü yok: $batKaynak" }
    if (-not (Test-Path $appKaynak)) {
        New-Item -ItemType Directory -Path $appKaynak -Force | Out-Null
    }

    # Yardımcı: Paket temel yapısını inşa eden fonksiyon
    function Build-TemelPaket {
        param(
            [string]$PaketHedefYolu,
            [string]$PaketTuru # 'GPU' veya 'CPU'
        )
        Write-Host "  -> [$PaketTuru] Temel paket yapısı kuruluyor: $PaketHedefYolu"

        if (-not (Test-Path $PaketHedefYolu)) {
            New-Item -ItemType Directory -Path $PaketHedefYolu -Force | Out-Null
        }

        # 1. python/ embed kopyala
        $hedefPython = Join-Path $PaketHedefYolu "python"
        Invoke-RobocopyMirror -Kaynak $pythonEmbedCache -Hedef $hedefPython

        # 2. MSVC DLL'lerini python/ içine kopyala
        Get-ChildItem -Path (Join-Path $msvcCacheDir "*.dll") -ErrorAction SilentlyContinue | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $hedefPython -Force
        }

        # 3. site-packages kopyala
        $hedefSitePackages = Join-Path $hedefPython "Lib\site-packages"
        $kaynakSitePackages = if ($PaketTuru -eq 'GPU') { $ortakSitePackagesGPU } else {
            if (Test-Path $ortakSitePackagesCPU) { $ortakSitePackagesCPU } else { $ortakSitePackagesGPU }
        }
        Invoke-RobocopyMirror -Kaynak $kaynakSitePackages -Hedef $hedefSitePackages

        # 4. ffmpeg\bin\ kopyala
        $hedefFfmpegBin = Join-Path $PaketHedefYolu "ffmpeg\bin"
        Invoke-RobocopyMirror -Kaynak $ffmpegCacheBin -Hedef $hedefFfmpegBin

        # 5. araclar kopyala
        $hedefAraclar = Join-Path $PaketHedefYolu "araclar"
        if (-not (Test-Path $hedefAraclar)) { New-Item -ItemType Directory -Path $hedefAraclar -Force | Out-Null }
        Copy-Item -Path $deepFilterExe -Destination $hedefAraclar -Force
        if (Test-Path $realesrganDir) {
            Invoke-RobocopyMirror -Kaynak $realesrganDir -Hedef (Join-Path $hedefAraclar "realesrgan")
            # "genel" model (realesr-general-x4v3, ncnn'e dönüştürülmüş; BSD-3, github.com/xinntao/Real-ESRGAN): upscale_model = "genel"
            $genelModel = Join-Path $KaynakKlasoru "modeller-ncnn\genel"
            if (Test-Path $genelModel) {
                Invoke-RobocopyMirror -Kaynak $genelModel -Hedef (Join-Path $hedefAraclar "realesrgan\genel")
            }
        }

        # 6. app\ kopyala (robocopy /MIR)
        $hedefApp = Join-Path $PaketHedefYolu "app"
        Invoke-RobocopyMirror -Kaynak $appKaynak -Hedef $hedefApp

        # 7. ayar.toml ve terimler.txt
        Copy-Item -Path $ayarKaynak -Destination $PaketHedefYolu -Force
        Copy-Item -Path $terimlerKaynak -Destination $PaketHedefYolu -Force
        $kilavuz = Join-Path $KaynakKlasoru "KULLANIM.txt"
        if (Test-Path $kilavuz) { Copy-Item -Path $kilavuz -Destination $PaketHedefYolu -Force }

        # 8. calistir.bat, izle.bat, kontrol.bat köke kopyala
        Get-ChildItem -Path (Join-Path $batKaynak "*.bat") | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $PaketHedefYolu -Force
        }

        # 9. modeller-ocr\ (önbellekten)
        $ocrCache = Join-Path $IndirKlasoru "modeller-ocr"
        if (Test-Path $ocrCache) {
            $hedefOcr = Join-Path $PaketHedefYolu "modeller-ocr"
            Invoke-RobocopyMirror -Kaynak $ocrCache -Hedef $hedefOcr
        }

        # 10. sozluk\tr_kelime.txt (varsa)
        $sozlukKaynak = Join-Path $IndirKlasoru "sozluk\tr_kelime.txt"
        if (Test-Path $sozlukKaynak) {
            $hedefSozluk = Join-Path $PaketHedefYolu "sozluk"
            if (-not (Test-Path $hedefSozluk)) { New-Item -ItemType Directory -Path $hedefSozluk -Force | Out-Null }
            Copy-Item -Path $sozlukKaynak -Destination (Join-Path $hedefSozluk "tr_kelime.txt") -Force
        }

        # 11. ortak\ (kaynak\ortak klasörünün tamamı)
        $ortakKaynak = Join-Path $KaynakKlasoru "ortak"
        if (Test-Path $ortakKaynak) {
            $hedefOrtak = Join-Path $PaketHedefYolu "ortak"
            Invoke-RobocopyMirror -Kaynak $ortakKaynak -Hedef $hedefOrtak
        }

        # 13. artcraft\ klasörünü pakete aç
        foreach ($acKey in $artcraftZipleri.Keys) {
            $acItem = $artcraftZipleri[$acKey]
            $hedefArtcraft = Join-Path $PaketHedefYolu "artcraft\$acKey"
            if (Test-Path $hedefArtcraft) {
                Remove-Item -Path $hedefArtcraft -Recurse -Force
            }
            New-Item -ItemType Directory -Path $hedefArtcraft -Force | Out-Null
            try {
                Expand-Archive -Path $acItem.Zip -DestinationPath $hedefArtcraft -Force
            } catch {
                Write-Warning "ArtCraft ($acKey) arşiv açma hatası: $($_.Exception.Message)"
            }
        }

        # 12. Boş is\ klasörleri
        $isKlasorleri = @('gelen', 'isleniyor', 'bitti', 'hata', 'gunluk', 'dizin')
        foreach ($k in $isKlasorleri) {
            $isYol = Join-Path $PaketHedefYolu "is\$k"
            if (-not (Test-Path $isYol)) {
                New-Item -ItemType Directory -Path $isYol -Force | Out-Null
            }
        }
    }

    # --------------------------------------------------------------------------
    # [10/10] Paketleme, Robocopy, SURUMLER.txt ve SHA256 Kontrol Listesi
    # --------------------------------------------------------------------------
    Write-Host "[10/10] Paketleme tamamlanıyor ve SURUMLER.txt üretiliyor..." -ForegroundColor Cyan

    $uretilenPaketler = @()

    # GPU Paketini Kur
    if ($Paket -in @('GPU', 'Ikisi')) {
        Build-TemelPaket -PaketHedefYolu $GpuPaketYolu -PaketTuru 'GPU'

        # cuda\ klasörünü kopyala
        $gpuCuda = Join-Path $GpuPaketYolu "cuda"
        Invoke-RobocopyMirror -Kaynak $cudaDllCache -Hedef $gpuCuda

        # Modelleri ekle (large-v3, medium, turbo)
        if (-not $ModelAtla) {
            $gpuModeller = Join-Path $GpuPaketYolu "modeller"
            foreach ($mAd in @('large-v3', 'medium', 'turbo')) {
                $kaynakModel = Join-Path $IndirKlasoru "modeller\$mAd"
                $hedefModel = Join-Path $gpuModeller $mAd
                if (Test-Path $kaynakModel) {
                    Invoke-RobocopyMirror -Kaynak $kaynakModel -Hedef $hedefModel
                }
            }
        }

        # GPU SURUMLER.txt
        $gpuBuyukler = Get-DosyaHashVeBoyutListesi -KokDizin $GpuPaketYolu
        $surumSb = [System.Text.StringBuilder]::new()
        [void]$surumSb.AppendLine("================================================================================")
        [void]$surumSb.AppendLine("VideoHat Dagitim Paketi: dist\VideoHat-GPU")
        [void]$surumSb.AppendLine("Olusturma Tarihi: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
        [void]$surumSb.AppendLine("================================================================================")
        [void]$surumSb.AppendLine("")
        [void]$surumSb.AppendLine("1. BILESEN SURUMLERI")
        [void]$surumSb.AppendLine("--------------------------------------------------------------------------------")
        [void]$surumSb.AppendLine("Python: 3.12.10 (Embeddable AMD64)")
        [void]$surumSb.AppendLine("FFmpeg: Gyan.dev Release Essentials")
        [void]$surumSb.AppendLine("DeepFilterNet: v0.5.6")
        [void]$surumSb.AppendLine("Real-ESRGAN: v0.2.5.0-ncnn-vulkan")
        [void]$surumSb.AppendLine("OCR Modelleri: PP-OCRv6 (Apache-2.0, PaddleOCR/RapidOCR)")
        [void]$surumSb.AppendLine("Sozluk: FrequencyWords tr_50k (CC BY-SA 4.0, hermitdave)")
        [void]$surumSb.AppendLine("PDF Kutuphanesi: PyMuPDF (AGPL-3.0)")
        foreach ($acSatir in $artcraftSurumListesi) {
            [void]$surumSb.AppendLine($acSatir)
        }
        [void]$surumSb.AppendLine("")
        [void]$surumSb.AppendLine("2. MODEL COMMIT SHA BILGILERI")
        [void]$surumSb.AppendLine("--------------------------------------------------------------------------------")
        foreach ($k in @('large-v3', 'medium', 'turbo')) {
            $shaGoster = if ($modelShaBilgileri.ContainsKey($k)) { $modelShaBilgileri[$k] } else { 'ModelAtla ile atlandi' }
            [void]$surumSb.AppendLine("$($k.PadRight(10)) : $shaGoster")
        }
        [void]$surumSb.AppendLine("")
        [void]$surumSb.AppendLine("3. PYTHON PAKETLERI (pip freeze)")
        [void]$surumSb.AppendLine("--------------------------------------------------------------------------------")
        [void]$surumSb.AppendLine($pipFreezeGpuCikti)
        [void]$surumSb.AppendLine("")
        [void]$surumSb.AppendLine("4. DEEP-FILTER YARDIM CIKTISI")
        [void]$surumSb.AppendLine("--------------------------------------------------------------------------------")
        [void]$surumSb.AppendLine($deepFilterHelp)
        [void]$surumSb.AppendLine("")
        [void]$surumSb.AppendLine("5. BUYUK DOSYALAR SHA256 KONTROL LISTESI")
        [void]$surumSb.AppendLine("--------------------------------------------------------------------------------")
        foreach ($bd in $gpuBuyukler) {
            [void]$surumSb.AppendLine("$($bd.SHA256)  $($bd.BoyutMB.ToString().PadLeft(8)) MB  $($bd.GoreliYol)")
        }

        [System.IO.File]::WriteAllText((Join-Path $GpuPaketYolu "SURUMLER.txt"), $surumSb.ToString(), [System.Text.Encoding]::UTF8)
        $uretilenPaketler += @{ Ad = "dist\VideoHat-GPU"; Yol = $GpuPaketYolu; Buyukler = $gpuBuyukler }
    }

    # CPU Paketini Kur
    if ($Paket -in @('CPU', 'Ikisi')) {
        if ($Paket -eq 'Ikisi') {
            Write-Host "  -> CPU paketi GPU paketinden robocopy ile kopyalanıyor..."
            Invoke-RobocopyMirror -Kaynak $GpuPaketYolu -Hedef $CpuPaketYolu

            # CPU paketinden cuda\ kaldırılır
            $cpuCuda = Join-Path $CpuPaketYolu "cuda"
            if (Test-Path $cpuCuda) {
                Remove-Item -Path $cpuCuda -Recurse -Force
            }

            # CPU paketinden large-v3 ve turbo modelleri silinir, medium kalır
            $cpuLarge = Join-Path $CpuPaketYolu "modeller\large-v3"
            $cpuTurbo = Join-Path $CpuPaketYolu "modeller\turbo"
            if (Test-Path $cpuLarge) { Remove-Item -Path $cpuLarge -Recurse -Force }
            if (Test-Path $cpuTurbo) { Remove-Item -Path $cpuTurbo -Recurse -Force }
        } else {
            # Yalnız CPU seçilmişse temel paket kurulur
            Build-TemelPaket -PaketHedefYolu $CpuPaketYolu -PaketTuru 'CPU'
            if (-not $ModelAtla) {
                $cpuModeller = Join-Path $CpuPaketYolu "modeller"
                $kaynakMedium = Join-Path $IndirKlasoru "modeller\medium"
                $hedefMedium = Join-Path $cpuModeller "medium"
                if (Test-Path $kaynakMedium) {
                    Invoke-RobocopyMirror -Kaynak $kaynakMedium -Hedef $hedefMedium
                }
            }
        }

        # CPU SURUMLER.txt
        $cpuBuyukler = Get-DosyaHashVeBoyutListesi -KokDizin $CpuPaketYolu
        $surumSbCpu = [System.Text.StringBuilder]::new()
        [void]$surumSbCpu.AppendLine("================================================================================")
        [void]$surumSbCpu.AppendLine("VideoHat Dagitim Paketi: dist\VideoHat-CPU")
        [void]$surumSbCpu.AppendLine("Olusturma Tarihi: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
        [void]$surumSbCpu.AppendLine("================================================================================")
        [void]$surumSbCpu.AppendLine("")
        [void]$surumSbCpu.AppendLine("1. BILESEN SURUMLERI")
        [void]$surumSbCpu.AppendLine("--------------------------------------------------------------------------------")
        [void]$surumSbCpu.AppendLine("Python: 3.12.10 (Embeddable AMD64)")
        [void]$surumSbCpu.AppendLine("FFmpeg: Gyan.dev Release Essentials")
        [void]$surumSbCpu.AppendLine("DeepFilterNet: v0.5.6")
        [void]$surumSbCpu.AppendLine("Real-ESRGAN: v0.2.5.0-ncnn-vulkan")
        [void]$surumSbCpu.AppendLine("OCR Modelleri: PP-OCRv6 (Apache-2.0, PaddleOCR/RapidOCR)")
        [void]$surumSbCpu.AppendLine("Sozluk: FrequencyWords tr_50k (CC BY-SA 4.0, hermitdave)")
        [void]$surumSbCpu.AppendLine("PDF Kutuphanesi: PyMuPDF (AGPL-3.0)")
        foreach ($acSatir in $artcraftSurumListesi) {
            [void]$surumSbCpu.AppendLine($acSatir)
        }
        [void]$surumSbCpu.AppendLine("")
        [void]$surumSbCpu.AppendLine("2. MODEL COMMIT SHA BILGILERI")
        [void]$surumSbCpu.AppendLine("--------------------------------------------------------------------------------")
        $shaMedium = if ($modelShaBilgileri.ContainsKey('medium')) { $modelShaBilgileri['medium'] } else { 'ModelAtla ile atlandi' }
        [void]$surumSbCpu.AppendLine("medium     : $shaMedium")
        [void]$surumSbCpu.AppendLine("large-v3   : CPU paketinde bulunmaz")
        [void]$surumSbCpu.AppendLine("turbo      : CPU paketinde bulunmaz")
        [void]$surumSbCpu.AppendLine("")
        [void]$surumSbCpu.AppendLine("3. PYTHON PAKETLERI (pip freeze)")
        [void]$surumSbCpu.AppendLine("--------------------------------------------------------------------------------")
        $cpuFreezeKullan = if ($pipFreezeCpuCikti) { $pipFreezeCpuCikti } else { $pipFreezeGpuCikti }
        [void]$surumSbCpu.AppendLine($cpuFreezeKullan)
        [void]$surumSbCpu.AppendLine("")
        [void]$surumSbCpu.AppendLine("4. DEEP-FILTER YARDIM CIKTISI")
        [void]$surumSbCpu.AppendLine("--------------------------------------------------------------------------------")
        [void]$surumSbCpu.AppendLine($deepFilterHelp)
        [void]$surumSbCpu.AppendLine("")
        [void]$surumSbCpu.AppendLine("5. BUYUK DOSYALAR SHA256 KONTROL LISTESI")
        [void]$surumSbCpu.AppendLine("--------------------------------------------------------------------------------")
        foreach ($bd in $cpuBuyukler) {
            [void]$surumSbCpu.AppendLine("$($bd.SHA256)  $($bd.BoyutMB.ToString().PadLeft(8)) MB  $($bd.GoreliYol)")
        }

        [System.IO.File]::WriteAllText((Join-Path $CpuPaketYolu "SURUMLER.txt"), $surumSbCpu.ToString(), [System.Text.Encoding]::UTF8)
        $uretilenPaketler += @{ Ad = "dist\VideoHat-CPU"; Yol = $CpuPaketYolu; Buyukler = $cpuBuyukler }
    }

    # --------------------------------------------------------------------------
    # Konsola Özet ve SHA256 Listesi Yazdırma
    # --------------------------------------------------------------------------
    Write-Host "`n================================================================================" -ForegroundColor Green
    Write-Host "HAZIRLAMA ISLEMI TAMAMLANDI" -ForegroundColor Green
    Write-Host "================================================================================" -ForegroundColor Green

    foreach ($p in $uretilenPaketler) {
        $toplamBoyutByte = (Get-ChildItem -Path $p.Yol -Recurse -File | Measure-Object -Property Length -Sum).Sum
        $toplamBoyutGB = [math]::Round($toplamBoyutByte / 1GB, 2)
        Write-Host "`nPaket: $($p.Ad) (Toplam Boyut: $toplamBoyutGB GB)" -ForegroundColor Cyan
        Write-Host "Büyük Dosyalar SHA256 Listesi:" -ForegroundColor Yellow
        foreach ($bd in $p.Buyukler) {
            Write-Host "  $($bd.SHA256)  $($bd.BoyutMB.ToString().PadLeft(8)) MB  $($bd.GoreliYol)"
        }
    }

    $hazirMetni = ($uretilenPaketler | ForEach-Object { $_.Ad }) -join ", "
    Write-Host "`nHazır: $hazirMetni" -ForegroundColor Green

} catch {
    Write-Host "`n[HATA] Hazırlık işlemi sırasında hata oluştu: $($_.Exception.Message)" -ForegroundColor Red
    Write-Error $_
    exit 1
}
