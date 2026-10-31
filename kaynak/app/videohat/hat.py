"""VideoHat tek video işleme hattı."""
import logging
import shutil
import subprocess
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from . import cihaz, cikti, goruntu, ses, terimler as terimler_mod, yollar
from .ayar import Ayar
from .hatalar import UpscaleHatasi


@dataclass
class Bayraklar:
    agir_ses: bool = False
    goruntu: bool = False
    upscale: bool = False
    model: str | None = None
    cihaz: str | None = None


@dataclass
class Sonuc:
    ad: str
    basarili: bool
    hata: str | None
    sureler: dict[str, float] = field(default_factory=dict)
    cikti_dosyalari: list[Path] = field(default_factory=list)
    cihaz: str = ""


def _penceresiz() -> int:
    return getattr(subprocess, "CREATE_NO_WINDOW", 0)


def _ses_akisi_var(video: Path) -> bool:
    try:
        sonuc = subprocess.run(
            [
                str(yollar.ffprobe()),
                "-v", "error",
                "-select_streams", "a:0",
                "-show_entries", "stream=codec_type",
                "-of", "default=noprint_wrappers=1:nokey=1",
                str(video),
            ],
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
        return bool(sonuc.stdout.strip())
    except subprocess.CalledProcessError:
        return False


def _video_akisi_var(video: Path) -> bool:
    try:
        sonuc = subprocess.run(
            [str(yollar.ffprobe()), "-v", "error", "-select_streams", "v:0",
             "-show_entries", "stream=codec_type", "-of", "default=noprint_wrappers=1:nokey=1",
             str(video)],
            check=True, capture_output=True, text=True, encoding="utf-8",
            errors="replace", creationflags=_penceresiz())
        return bool(sonuc.stdout.strip())
    except subprocess.CalledProcessError:
        return False


def _hata_metni(adim: str, hata: Exception) -> str:
    ayrinti = str(hata).strip()
    return f"{adim} adımında hata" + (f": {ayrinti}" if ayrinti else ".")


def _ffmpeg_calistir(komut: list[str]) -> None:
    try:
        subprocess.run(
            komut,
            check=True,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            creationflags=_penceresiz(),
        )
    except subprocess.CalledProcessError as hata:
        satirlar = (hata.stderr or "").splitlines()[-5:]
        ayrinti = "\n".join(satirlar)
        raise RuntimeError(f"ffmpeg işlemi başarısız oldu.\n{ayrinti}".rstrip()) from hata


def isle_tek(
    video: Path,
    cikis_dir: Path,
    ayar: Ayar,
    bayraklar: Bayraklar,
    cevirmen: Any = None,
) -> Sonuc:
    video = Path(video)
    cikis_dir = Path(cikis_dir)
    ad = video.stem
    sonuc = Sonuc(ad=ad, basarili=False, hata=None)
    gunluk = logging.getLogger("videohat")

    if not _ses_akisi_var(video):
        sonuc.hata = "ses akışı yok"
        return sonuc

    ara = cikis_dir / "_ara"
    cikti_dosyalari: list[Path] = []
    try:
        cikis_dir.mkdir(parents=True, exist_ok=True)
        ara.mkdir(parents=True, exist_ok=True)
        ham48 = ara / "ham48.wav"
        temiz48 = ara / "temiz48.wav"
        temiz16 = ara / "temiz16.wav"
        ham16 = ara / "ham16.wav"

        def olc(adim: str, islem):
            baslangic = time.perf_counter()
            try:
                return islem()
            finally:
                sonuc.sureler[adim] = time.perf_counter() - baslangic

        olc("ses_cikar", lambda: ses.cikar(video, ham48))
        olc("ses_temizle", lambda: ses.temizle(ham48, temiz48, bayraklar.agir_ses, ayar))
        olc("ses_16k", lambda: ses.stt_icin(temiz48, temiz16))
        olc("ses_16k_ham", lambda: ses.stt_icin(ham48, ham16))

        secim = olc(
            "cihaz_sec",
            lambda: cihaz.sec(ayar, zorla_cihaz=bayraklar.cihaz, zorla_model=bayraklar.model),
        )
        sonuc.cihaz = secim.device
        tm = terimler_mod.yukle(ayar.kok / ayar.stt.terimler_dosyasi)
        terimler = tm.istem

        def stt_calistir():
            nonlocal secim
            try:
                return stt_yaz(temiz16, secim, ayar, terimler, wav16_ham=ham16)
            except (RuntimeError, OSError) as hata:
                mesaj = str(hata).lower()
                anahtarlar = ("cuda", "cublas", "cudnn", "out of memory", "memory")
                if secim.device != "cuda" or not any(k in mesaj for k in anahtarlar):
                    raise
                gunluk.warning("CUDA belleği/çalıştırması yetersiz; CPU'ya düşüldü: %s", hata)
                cihaz.cuda_kapat()
                secim = cihaz.sec(ayar, zorla_cihaz="cpu", zorla_model=bayraklar.model)
                sonuc.cihaz = secim.device
                return stt_yaz(temiz16, secim, ayar, terimler, wav16_ham=ham16)

        from . import stt as stt_mod
        stt_yaz = stt_mod.yaz
        bolumler = olc("stt", stt_calistir)
        # Terim listesi Türkçedir; başka dildeki kayıtta bulanık eşleştirme doğru sözcüğü bozar.
        duzeltme_sayisi = olc("terim_duzeltme", lambda: terimler_mod.uygula(bolumler, tm)
                              if stt_mod.SON_DIL in (None, "tr") else 0)
        if duzeltme_sayisi:
            gunluk.info("Terim düzeltme: %d değişiklik", duzeltme_sayisi)

        uzantilar = {
            ".srt": cikti.srt,
            ".vtt": cikti.vtt,
            ".txt": cikti.txt,
        }
        cikti_yollari = {uzanti: cikis_dir / f"{ad}{uzanti}" for uzanti in uzantilar}
        json_yolu = cikis_dir / f"{ad}.json"

        def ciktilari_yaz():
            for uzanti, yazici in uzantilar.items():
                yazici(bolumler, cikti_yollari[uzanti])

        olc("cikti", ciktilari_yaz)
        cikti_dosyalari.extend(cikti_yollari.values())
        cikti_dosyalari.append(json_yolu)

        if not _video_akisi_var(video):
            gunluk.info("Girdide görüntü yok; video çıktısı üretilmedi (yalnız transkript).")
        else:
            # Tek nihai video: <ad>_islenmis.mp4. Ara adımlar _ara\ içinde kalır ve iş bitince silinir.
            goruntu_etkin = bayraklar.goruntu or ayar.goruntu.etkin
            son_video = ara / "video.mp4"
            if goruntu_etkin:
                # Orijinal video yeniden kodlanır (hafif filtreler) + temiz ses
                olc(
                    "goruntu",
                    lambda: goruntu.iyilestir(video, temiz48, son_video, ayar, ayar.goruntu.nvenc),
                )
            else:
                # Görüntü aynen kopyalanır, yalnız ses temizi ile değiştirilir
                ffmpeg = str(yollar.ffmpeg())
                olc(
                    "mux",
                    lambda: _ffmpeg_calistir(
                        [
                            ffmpeg, "-y", "-i", str(video), "-i", str(temiz48),
                            "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy",
                            "-c:a", "aac", "-b:a", "192k", "-shortest", str(son_video),
                        ]
                    ),
                )
            if bayraklar.upscale:
                buyuk = ara / "video_buyuk.mp4"
                try:
                    olc("upscale", lambda: goruntu.upscale(son_video, buyuk, ayar))
                    son_video = buyuk
                except UpscaleHatasi as hata:
                    sonuc.sureler.setdefault("upscale", 0.0)
                    gunluk.warning("Upscale atlandı: %s", hata)
            nihai = cikis_dir / f"{ad}_islenmis.mp4"
            shutil.move(str(son_video), str(nihai))
            cikti_dosyalari.append(nihai)

        # Çeviri adımı
        ceviriler = None
        ceviri_model_ad = None
        dil = stt_mod.SON_DIL
        if dil and dil != "tr" and bolumler and cevirmen is None:
            # Model yalnız Türkçe dışı kayıtta, STT'den sonra yüklenir (Whisper ile VRAM paylaşımı az).
            from .ceviri import cevirmen_olustur
            cevirmen = cevirmen_olustur(ayar)
        izinli = getattr(cevirmen, "diller", None)
        if (cevirmen is not None and dil and dil != "tr" and bolumler
                and (izinli is None or dil in izinli)):
            try:
                metinler = [s.metin for s in bolumler]
                def _cevir_islem():
                    return cevirmen.cevir(metinler, kaynak_dil=dil)
                ceviriler = olc("ceviri", _cevir_islem)
                ceviri_model_ad = getattr(cevirmen, "model_ad", None)
                if ceviriler:
                    for s, c in zip(bolumler, ceviriler):
                        if c and c.strip():
                            s.ceviri = c.strip()
            except Exception as hata:
                sonuc.sureler.setdefault("ceviri", 0.0)
                gunluk.warning("Çeviri yapılamadı: %s", hata)
                ceviriler = None
                ceviri_model_ad = None

        cikti.json_meta(
            bolumler,
            secim,
            sonuc.sureler,
            json_yolu,
            dil=dil,
            ceviriler=ceviriler,
            ceviri_model=ceviri_model_ad,
        )
        sonuc.basarili = True
        sonuc.cikti_dosyalari = cikti_dosyalari
        sonuc.hata = None
        if not ayar.genel.ara_dosya_sakla:
            shutil.rmtree(ara, ignore_errors=True)
        return sonuc
    except Exception as hata:
        sonuc.hata = _hata_metni("Video işleme", hata)
        sonuc.cikti_dosyalari = cikti_dosyalari
        gunluk.error("%s: %s", ad, sonuc.hata)
        return sonuc
