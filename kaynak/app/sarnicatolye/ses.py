"""Sarnıç Atölye ses çıkarma, filtreleme ve normalizasyon modülü."""
import logging
import re
import subprocess
import tempfile
from pathlib import Path

from . import yollar
from .ayar import Ayar
from .hatalar import IsHatasi


def _penceresiz() -> int:
    return getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _ffmpeg_calistir(komut: list[str]) -> subprocess.CompletedProcess:
    try:
        return subprocess.run(
            komut,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
    except subprocess.CalledProcessError as hata:
        satirlar = (hata.stderr or "").strip().splitlines()[-5:]
        ayrinti = "\n".join(satirlar)
        mesaj = f"FFmpeg çalıştırma hatası:\n{ayrinti}" if ayrinti else "FFmpeg çalıştırma hatası."
        raise IsHatasi(mesaj) from hata
    except OSError as hata:
        raise IsHatasi(f"FFmpeg başlatılamadı: {hata}") from hata


def cikar_komutu(video: Path | str, hedef_wav: Path | str, sr: int = 48000) -> list[str]:
    """Videodan ses çıkarma ffmpeg komutunu oluşturur."""
    return [
        str(yollar.ffmpeg()),
        "-y",
        "-i", str(video),
        "-vn",
        "-ac", "1",
        "-ar", str(sr),
        "-c:a", "pcm_s16le",
        str(hedef_wav),
    ]


def cikar(video: Path, hedef_wav: Path, sr: int = 48000) -> None:
    """Videodan sesi mono PCM 16-bit WAV olarak çıkarır."""
    video = Path(video)
    hedef_wav = Path(hedef_wav)
    hedef_wav.parent.mkdir(parents=True, exist_ok=True)
    komut = cikar_komutu(video, hedef_wav, sr=sr)
    _ffmpeg_calistir(komut)


def stt_icin(wav48: Path, wav16: Path) -> None:
    """Whisper STT girişi için 16 kHz mono PCM 16-bit WAV üretir."""
    wav48 = Path(wav48)
    wav16 = Path(wav16)
    wav16.parent.mkdir(parents=True, exist_ok=True)
    komut = [
        str(yollar.ffmpeg()),
        "-y",
        "-i", str(wav48),
        "-ac", "1",
        "-ar", "16000",
        "-c:a", "pcm_s16le",
        str(wav16),
    ]
    _ffmpeg_calistir(komut)


def olc_lufs(wav: Path) -> float:
    """WAV dosyasının ebur128 Integrated LUFS değerini ölçer."""
    wav = Path(wav)
    komut = [
        str(yollar.ffmpeg()),
        "-nostats",
        "-i", str(wav),
        "-af", "ebur128",
        "-f", "null",
        "-",
    ]
    proc = _ffmpeg_calistir(komut)
    metin = proc.stderr or ""
    eslesme = re.search(
        r"Integrated loudness:.*?I:\s*(-?[\d\.]+)\s*LUFS",
        metin,
        re.DOTALL | re.IGNORECASE,
    )
    if not eslesme:
        tum = re.findall(r"I:\s*(-?[\d\.]+)\s*LUFS", metin, re.IGNORECASE)
        if tum:
            try:
                return float(tum[-1])
            except ValueError as e:
                raise IsHatasi(f"LUFS değeri çözümlenemedi: {e}") from e
        raise IsHatasi("FFmpeg çıktısından LUFS (Integrated loudness) değeri okunamadı.")
    try:
        return float(eslesme.group(1))
    except ValueError as e:
        raise IsHatasi(f"LUFS değeri sayıya dönüştürülemedi: {e}") from e


def temizle(wav_in: Path, wav_out: Path, agir: bool, ayar: Ayar) -> None:
    """Ses dosyasını gürültüden arındırır ve ses seviyesini normalleştirir."""
    wav_in = Path(wav_in)
    wav_out = Path(wav_out)
    wav_out.parent.mkdir(parents=True, exist_ok=True)

    if agir:
        try:
            df_exe = yollar.deep_filter()
            with tempfile.TemporaryDirectory() as gecici_dizin_str:
                gecici_dizin = Path(gecici_dizin_str)
                subprocess.run(
                    [str(df_exe), str(wav_in), "-o", str(gecici_dizin)],
                    check=True,
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    creationflags=_penceresiz(),
                )
                ciktilar = sorted(gecici_dizin.glob("*.wav"))
                if not ciktilar:
                    ciktilar = [p for p in gecici_dizin.iterdir() if p.is_file()]
                if not ciktilar:
                    raise RuntimeError("deep-filter çıktı dosyası üretmedi.")
                df_cikti = ciktilar[0]

                oran = ayar.ses.agir_kuru_oran
                filtre = (
                    f"[0]volume={1.0 - oran:.4g}[a];"
                    f"[1]volume={oran:.4g}[b];"
                    f"[a][b]amix=inputs=2:normalize=0,"
                    f"highpass=f={ayar.ses.highpass_hz},"
                    f"loudnorm=I={ayar.ses.lufs}:TP={ayar.ses.tp}:LRA={ayar.ses.lra}"
                )
                komut = [
                    str(yollar.ffmpeg()),
                    "-y",
                    "-i", str(df_cikti),
                    "-i", str(wav_in),
                    "-filter_complex", filtre,
                    "-ar", "48000",
                    "-ac", "1",
                    "-c:a", "pcm_s16le",
                    str(wav_out),
                ]
                _ffmpeg_calistir(komut)
                return
        except Exception as hata:
            gunluk = logging.getLogger("sarnicatolye")
            gunluk.warning(
                "deep-filter kullanılamadı veya başarısız oldu, normal temizlemeye geçiliyor: %s",
                hata,
            )

    filtre = (
        f"highpass=f={ayar.ses.highpass_hz},"
        f"afftdn=nf={ayar.ses.afftdn_nf},"
        f"loudnorm=I={ayar.ses.lufs}:TP={ayar.ses.tp}:LRA={ayar.ses.lra}"
    )
    komut = [
        str(yollar.ffmpeg()),
        "-y",
        "-i", str(wav_in),
        "-af", filtre,
        "-ar", "48000",
        "-ac", "1",
        "-c:a", "pcm_s16le",
        str(wav_out),
    ]
    _ffmpeg_calistir(komut)
