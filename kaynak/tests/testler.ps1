# Sarnıç Atölye kabul testleri a–h. Kullanım: pwsh -File testler.ps1 [-Paket dist\SarnicAtolye-GPU]
# Çıktı: tablo + testler_sonuc.json. Çıkış kodu: başarısız test sayısı.
param(
    [string[]]$Atla = @(),   # örn. -Atla cdeha,g  (c/d/e/h/a blokunu ve/veya g'yi atlar)
    [string]$Paket = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'dist\SarnicAtolye-GPU')
)
$ErrorActionPreference = 'Continue'
$proje = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$veri = Join-Path $proje 'dist\test_veri'
$cikisKok = Join-Path $proje 'dist\test_cikti'
$wer = Join-Path $PSScriptRoot 'wer.py'
$py = (Get-Command python).Source          # yalnız WER hesabı için geliştirme python'u
$ffmpeg = Join-Path $Paket 'ffmpeg\bin\ffmpeg.exe'
$sonuclar = [System.Collections.Generic.List[object]]::new()
if (Test-Path $cikisKok) { Remove-Item $cikisKok -Recurse -Force }
New-Item -ItemType Directory $cikisKok | Out-Null

function Kaydet($ad, $gecti, $ayrinti) {
    $sonuclar.Add([pscustomobject]@{ test = $ad; gecti = [bool]$gecti; ayrinti = $ayrinti })
    Write-Host ("[{0}] {1}: {2}" -f $(if ($gecti) { 'TAMAM' } else { 'HATA ' }), $ad, $ayrinti)
}
function Ortam([string]$paket, [hashtable]$ek = @{}) {
    $env:SARNICATOLYE_PAUSE = '0'; $env:SARNICATOLYE_AG_KILIDI = '1'
    $env:HTTP_PROXY = 'http://127.0.0.1:9'; $env:HTTPS_PROXY = 'http://127.0.0.1:9'
    foreach ($k in $ek.Keys) { Set-Item "env:$k" $ek[$k] }
}
function Isle([string]$paket, [string]$girdi, [string]$cikis, [string[]]$bayrak = @()) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $log = & cmd /c "`"$paket\calistir.bat`" `"$girdi`" --cikis `"$cikis`" $($bayrak -join ' ') 2>&1"
    [pscustomobject]@{ kod = $LASTEXITCODE; sure = $sw.Elapsed.TotalSeconds; log = ($log -join "`n") }
}
function Wer([string]$ref, [string]$hyp) {
    $o = & $py $wer $ref $hyp
    if ($o -match 'WER=([\d.]+)%') { [double]$Matches[1] } else { 999 }
}
function Sure([string]$dosya) {
    $o = & $ffmpeg -i $dosya 2>&1 | Select-String 'Duration: (\d+):(\d+):([\d.]+)'
    $m = $o.Matches[0].Groups
    [double]$m[1].Value * 3600 + [double]$m[2].Value * 60 + [double]$m[3].Value
}
function Lufs([string]$dosya) {
    $o = (& $ffmpeg -nostats -i $dosya -af 'ebur128=peak=true' -f null - 2>&1) -join "`n"
    $i = [regex]::Matches($o, 'I:\s+(-?[\d.]+) LUFS') | Select-Object -Last 1
    $p = [regex]::Matches($o, 'Peak:\s+(-?[\d.]+) dBFS') | Select-Object -Last 1
    @{ I = [double]$i.Groups[1].Value; TP = [double]$p.Groups[1].Value }
}

Ortam $Paket
$ref = Join-Path $veri 'metin_tr.txt'
$gunluk = Join-Path $Paket 'is\gunluk\ag_ihlal.log'
if (Test-Path $gunluk) { Remove-Item $gunluk -Force }

# b: veri
$b = @('temiz.mp4', 'gurultulu.mp4', 'yarim.mp4', 'ref.wav') | ForEach-Object { Test-Path (Join-Path $veri $_) }
Kaydet 'b sentetik veri' (-not ($b -contains $false)) "4 dosya: $($b -join ',')"
$sureTemiz = Sure (Join-Path $veri 'temiz.mp4')

if ($Atla -notcontains 'cdeha') {
# c: WER (+ d: loudness)
$o1 = Join-Path $cikisKok 'temiz'
$r = Isle $Paket (Join-Path $veri 'temiz.mp4') $o1
$w = Wer $ref (Join-Path $o1 'temiz.txt')
Kaydet 'c WER temiz (<=10)' ($r.kod -eq 0 -and $w -le 10) "WER=$w% süre=$([math]::Round($r.sure,1))s kod=$($r.kod)"
$l = Lufs (Join-Path $o1 'temiz_islenmis.mp4')
Kaydet 'd loudness (-16±1, TP<=-1)' ([math]::Abs($l.I + 16) -le 1 -and $l.TP -le -1.0) "I=$($l.I) LUFS TP=$($l.TP) dBFS"

$o2 = Join-Path $cikisKok 'gur_normal'
$r = Isle $Paket (Join-Path $veri 'gurultulu.mp4') $o2
$wN = Wer $ref (Join-Path $o2 'gurultulu.txt')
Kaydet 'c WER gürültülü normal (<=15)' ($r.kod -eq 0 -and $wN -le 15) "WER=$wN%"
$o3 = Join-Path $cikisKok 'gur_agir'
$r = Isle $Paket (Join-Path $veri 'gurultulu.mp4') $o3 @('--agir-ses')
$wA = Wer $ref (Join-Path $o3 'gurultulu.txt')
Kaydet 'c WER gürültülü ağır (<=15, normal+2)' ($r.kod -eq 0 -and $wA -le 15 -and $wA -le $wN + 2) "WER=$wA% normal=$wN% kod=$($r.kod) $(if($r.kod){$r.log.Substring([math]::Max(0,$r.log.Length-300))})"

# e: CPU yolu
$o4 = Join-Path $cikisKok 'cpu'
$r = Isle $Paket (Join-Path $veri 'temiz.mp4') $o4 @('--cihaz', 'cpu')
$wC = Wer $ref (Join-Path $o4 'temiz.txt')
$cpuLog = $r.log -match "cpu"
Kaydet 'e CPU (WER<=18, süre<=1.5x)' ($r.kod -eq 0 -and $wC -le 18 -and $r.sure -le 1.5 * $sureTemiz) "WER=$wC% süre=$([math]::Round($r.sure,1))s video=$([math]::Round($sureTemiz,1))s"
$meta = Get-Content (Join-Path $o4 'temiz.json') -Raw -Encoding UTF8 | ConvertFrom-Json
Kaydet 'e CPU cihazı gerçekten cpu' ($meta.cihaz -eq 'cpu') "cihaz=$($meta.cihaz) model=$($meta.model) compute=$($meta.compute_type)"

# h: GPU yedeği (cublas DLL geçici adı değişir)
$dll = Join-Path $Paket 'cuda\cublas64_12.dll'
if (Test-Path $dll) {
    Rename-Item $dll 'cublas64_12.dll.gizli'
    try {
        $o5 = Join-Path $cikisKok 'yedek'
        $r = Isle $Paket (Join-Path $veri 'temiz.mp4') $o5
        $ok = $r.kod -eq 0 -and ($r.log -match "CPU'ya düşüldü" -or (Get-Content (Join-Path $o5 'temiz.json') -Raw -Encoding UTF8 | ConvertFrom-Json).cihaz -eq 'cpu')
        Kaydet 'h GPU yedeği' $ok "kod=$($r.kod) CPU'ya düşüldü logda: $($r.log -match "CPU'ya düşüldü")"
    } finally { Rename-Item "$dll.gizli" 'cublas64_12.dll' }
} else { Kaydet 'h GPU yedeği' $true 'atlandı (CPU paketi, cuda\ yok)' }

# a: ağ kilidi
$n = if (Test-Path $gunluk) { @(Get-Content $gunluk).Count } else { 0 }
$kontrol = & cmd /c "`"$Paket\python\python.exe`" -c `"import sys; sys.path.insert(0, r'$Paket\app'); import os; os.environ['SARNICATOLYE_KOK']=r'$Paket'; from sarnicatolye import ag_kilidi; ag_kilidi.kur(); import socket; socket.create_connection(('1.1.1.1',80),timeout=2)`" 2>&1"
$kancaCalisiyor = ($kontrol -join ' ') -match 'PermissionError'
if (Test-Path $gunluk) { Remove-Item $gunluk -Force }   # kontrol denemesinin kaydını sil
Kaydet 'a ağ kapalı (ihlal=0, kanca doğrulandı)' ($n -eq 0 -and $kancaCalisiyor) "ihlal=$n, kanca PermissionError verdi: $kancaCalisiyor"

}
# f: izleme
foreach ($d in 'gelen', 'isleniyor', 'bitti', 'hata') { Get-ChildItem (Join-Path $Paket "is\$d") -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue }
Remove-Item (Join-Path $Paket 'is\durum.jsonl') -Force -ErrorAction SilentlyContinue
$izle = Start-Process cmd -ArgumentList "/c `"$Paket\izle.bat`"" -PassThru -WindowStyle Hidden
Start-Sleep 5
$gelen = Join-Path $Paket 'is\gelen'
Copy-Item (Join-Path $veri 'temiz.mp4') (Join-Path $gelen 'ders1.mp4')
Copy-Item (Join-Path $veri 'gurultulu.mp4') (Join-Path $gelen 'ders2.mp4')
# yavaş yazılan dosya (30 sn'de tamamlanır); içerik ders1'den FARKLI olmalı (yinelenen sayılmasın)
$ucuncu = Join-Path $veri 'ucuncu.mp4'
& $ffmpeg -y -loglevel error -i (Join-Path $veri 'temiz.mp4') -af 'volume=0.7' -c:v copy -c:a aac $ucuncu
$kaynak = [System.IO.File]::ReadAllBytes($ucuncu)
$hedef = [System.IO.File]::Open((Join-Path $gelen 'ders3_yavas.mp4'), 'Create', 'Write', 'Read')
$parca = [math]::Ceiling($kaynak.Length / 6)
for ($i = 0; $i -lt 6; $i++) {
    $bas = $i * $parca; $say = [math]::Min($parca, $kaynak.Length - $bas)
    $hedef.Write($kaynak, $bas, $say); $hedef.Flush(); Start-Sleep 5
}
$hedef.Close()
# yinelenen: aynı içerik, farklı ad
Copy-Item (Join-Path $veri 'temiz.mp4') (Join-Path $gelen 'ders1_kopya.mp4')
$bitis = (Get-Date).AddMinutes(10)
while ((Get-Date) -lt $bitis) {
    Start-Sleep 10
    $bitti = @(Get-ChildItem (Join-Path $Paket 'is\bitti') -Directory -ErrorAction SilentlyContinue).Count
    $kalan = @(Get-ChildItem $gelen -File).Count + @(Get-ChildItem (Join-Path $Paket 'is\isleniyor') -File).Count
    Write-Host ("[izleme] {0:HH:mm:ss} gelen+isleniyor={1} bitti_dizin={2}" -f (Get-Date), $kalan, $bitti)
    if ($kalan -eq 0) { break }
}
Start-Sleep 5
Stop-Process -Id $izle.Id -Force -ErrorAction SilentlyContinue
Get-Process python -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Paket*" } | Stop-Process -Force
$bittiAd = @(Get-ChildItem (Join-Path $Paket 'is\bitti') | Select-Object -ExpandProperty Name)
$hataSay = @(Get-ChildItem (Join-Path $Paket 'is\hata') -ErrorAction SilentlyContinue).Count
$yinelenen = @($bittiAd | Where-Object { $_ -like '*yinelenen*' }).Count
$isDizinler = @(Get-ChildItem (Join-Path $Paket 'is\bitti') -Directory)
$isDizin = $isDizinler.Count
$islenmisEki = @($isDizinler | Where-Object { $_.Name -match '_islenmis(?:_\d+)?$' }).Count
Kaydet 'f izleme (3 iş, 1 yinelenen, 0 hata)' ($isDizin -eq 3 -and $islenmisEki -eq 3 -and $yinelenen -eq 1 -and $hataSay -eq 0) "bitti=[$($bittiAd -join ', ')] _islenmis_dizin=$islenmisEki hata=$hataSay"

if ($Atla -notcontains 'g') {
# g: taşınabilirlik
$hedefPaket = Join-Path $env:TEMP 'Deneme Ğüş\SarnicAtolye'
if (Test-Path (Split-Path $hedefPaket)) { Remove-Item (Split-Path $hedefPaket) -Recurse -Force }
robocopy $Paket $hedefPaket /E /NFL /NDL /NJH /NJS /NP | Out-Null
$ortam = & cmd /c "set PATH=%SystemRoot%\System32&& where python 2>&1 & where ffmpeg 2>&1 & where py 2>&1"
$kg = & cmd /c "set PATH=%SystemRoot%\System32&& set SARNICATOLYE_PAUSE=0&& call `"$hedefPaket\kontrol.bat`" 2>&1"
$kgKod = $LASTEXITCODE
$ortamTemiz = -not (($ortam -join ' ') -match 'python\.exe|ffmpeg\.exe|py\.exe')
Kaydet 'g taşınabilirlik (başka yol, Türkçe karakter, PATH temiz)' ($kgKod -eq 0 -and $ortamTemiz) "kontrol çıkış=$kgKod; sistem PATH'inde python/ffmpeg yok=$ortamTemiz"
Remove-Item (Split-Path $hedefPaket) -Recurse -Force -ErrorAction SilentlyContinue
}

$sonuclar | ConvertTo-Json | Set-Content (Join-Path $cikisKok 'testler_sonuc.json') -Encoding UTF8
$kalan = @($sonuclar | Where-Object { -not $_.gecti }).Count
Write-Host "`nSONUÇ: $($sonuclar.Count - $kalan)/$($sonuclar.Count) geçti"
exit $kalan
